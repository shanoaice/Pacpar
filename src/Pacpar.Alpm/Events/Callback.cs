#pragma warning disable SYSLIB1054
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>The phase a progress callback reports (libalpm's <c>_alpm_progress_t</c>).</summary>
public enum ProgressType : uint
{
  AddStart = 0,
  UpgradeStart = 1,
  DowngradeStart = 2,
  ReinstallStart = 3,
  RemoveStart = 4,
  ConflictsStart = 5,
  DiskSpaceStart = 6,
  IntegrityStart = 7,
  LoadStart = 8,
  KeyringStart = 9
}

/// <summary>The severity of a log message (libalpm's <c>_alpm_loglevel_t</c>).</summary>
/// <remarks>
/// A bitmask, not a scale: libalpm calls <c>alpm_cb_log</c> once per message with a single flag, and
/// consumers that want a subset test the flags (pacman and paru both filter on their own side, since
/// libalpm emits DEBUG and FUNCTION messages regardless).
/// </remarks>
[Flags]
public enum LogLevel : uint
{
  Error = 1,
  Warning = 2,
  Debug = 4,
  Function = 8
}

/// <summary>The outcome a fetch handler reports back to libalpm.</summary>
/// <remarks>
/// Replaces the bare <c>int</c> of <c>alpm_cb_fetch</c>, whose contract is "0 on success, 1 if the
/// file exists and is identical, -1 on error" (alpm.h). Any other value is not something libalpm
/// defines, so returning an arbitrary number should not be expressible.
/// </remarks>
public enum FetchResult
{
  /// <summary>The file was fetched (0).</summary>
  Success = 0,

  /// <summary>The local file already exists and is identical, so nothing was transferred (1).</summary>
  UpToDate = 1,

  /// <summary>The fetch failed (-1).</summary>
  Error = -1
}


/// <summary>
/// Holds the native callback context (ctx) and the user-facing handlers of an <see cref="Alpm"/>
/// instance.
/// </summary>
/// <remarks>
/// Registration of native callbacks with libalpm is presence-driven: setting a non-null handler
/// registers the corresponding <c>[UnmanagedCallersOnly]</c> native thunk, while setting a handler
/// to <see langword="null"/> unregisters it from the native handle. When a callback is unregistered,
/// libalpm's own default behavior applies directly:
/// <list type="bullet">
///   <item>
///     <description>
///       Leaving <see cref="FetchHandler"/> unset allows libalpm to use its internal libcurl-based
///       downloader.
///     </description>
///   </item>
///   <item>
///     <description>
///       Leaving <see cref="QuestionHandler"/> unset lets libalpm proceed with its built-in default answers.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="DownloadHandler"/> receives progress events emitted only by libalpm's internal
///       downloader, and is therefore inert whenever an external <see cref="FetchHandler"/> is active.
///     </description>
///   </item>
/// </list>
/// Handlers are configuration, not control flow. Mutating a handler from within an active callback
/// invocation throws <see cref="InvalidOperationException"/> to prevent native use-after-free and NULL
/// dereference crashes in libalpm's download and dispatch loops. Mutating a handler after the owning
/// <see cref="Alpm"/> handle has been disposed throws <see cref="ObjectDisposedException"/>.
/// <para>
/// Handlers run synchronously on the thread libalpm invokes them from - the thread already running
/// the transaction, never a thread chosen by this library - and they block libalpm for as long as
/// they run.
/// </para>
/// <para>
/// <see cref="QuestionHandler"/> is stricter still: libalpm reads the answer as soon as the callback
/// returns, so a question must be answered inline, before returning. Answering from a continuation,
/// a task, or another thread leaves libalpm reading an answer that has not been written yet.
/// </para>
/// <para>
/// Every handler invocation is routed through <c>SafeInvoke</c>, which swallows handler exceptions
/// at the FFI boundary and reports them through <see cref="HandlerException"/> when that observer is
/// set. The ctx handle is owned by <see cref="Alpm"/>, which releases it only once <c>alpm_release</c>
/// has returned successfully.
/// </para>
/// </remarks>
public sealed class Callback
{
  // The ALPM context, guarded behind a SafeHandle so it is not disposed while
  // callbacks are still active.
  private readonly SafeAlpmHandle _handle;
  private int _invokeDepth;
  private bool _detached;

  // The ALPM handle's root lifetime token
  private readonly Lifetime _lifetime;

  // The handle's binding policy; the payload factories read it while a callback runs.
  private readonly AlpmBindingConfig _binding;

  // do not Dispose this before the callback class has been disposed
  // otherwise it will screw up the callbacks
  private WeakGCHandle<Callback> _ctxHandle;

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void EventAgent(void* ctx, _alpm_event_t* eventT)
  {
    if (ctx == null) return;
    try
    {
      var success = WeakGCHandle<Callback>.FromIntPtr((nint)ctx).TryGetTarget(out var callback);
      if (!success || callback == null) return;
      callback._invokeDepth++;
      try
      {
        SafeInvoke(() => callback.EventHandler?.Invoke(AlpmEvent.FromUnion(eventT, callback._lifetime)), callback.HandlerException);
      }
      finally
      {
        callback._invokeDepth--;
      }
    }
    catch
    {
      // This catch is to guard against a race condition where the callback is disposed while the native code is still calling back. In that case, the WeakGCHandle will throw an exception, which we want to ignore.
    }
  }

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe int FetchAgent(void* ctx, byte* url, byte* localPath, int force)
  {
    // fails without a valid ctx
    if (ctx == null) return -1;
    try
    {
      var success = WeakGCHandle<Callback>.FromIntPtr((nint)ctx).TryGetTarget(out var callback);
      if (!success || callback == null) return -1;
      callback._invokeDepth++;
      try
      {
        return SafeInvoke(() =>
        {
          var urlString = NativeString.FromNative((IntPtr)url) ?? "";
          var localPathString = NativeString.FromNative((IntPtr)localPath) ?? "";

          return (int)(callback.FetchHandler?.Invoke(urlString, localPathString, force != 0) ?? FetchResult.Error);
        }, (int)FetchResult.Error, callback.HandlerException);
      }
      finally
      {
        callback._invokeDepth--;
      }
    }
    catch
    {
      // This catch is to guard against a race condition where the callback is disposed while the native code is still calling back. In that case, the WeakGCHandle will throw an exception, which we want to identify as a failure.
      return -1;
    }
  }

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void QuestionAgent(void* ctx, _alpm_question_t* questionT)
  {
    if (ctx == null) return;
    try
    {
      var success = WeakGCHandle<Callback>.FromIntPtr((nint)ctx).TryGetTarget(out var callback);
      if (!success || callback == null) return;
      callback._invokeDepth++;
      var question = AlpmQuestion.FromUnion(questionT, callback._binding);
      try
      {
        SafeInvoke(
          () => callback.QuestionHandler?.Invoke(question), callback.HandlerException);
      }
      finally
      {
        callback._invokeDepth--;
        question.Disarm();
      }
    }
    catch
    {
      // This catch is to guard against a race condition where the callback is disposed while the native code is still calling back. In that case, the WeakGCHandle will throw an exception, which we want to ignore.
    }
  }

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void ProgressAgent(void* ctx, _alpm_progress_t progress, byte* pkg, int percent, nuint howmany,
    nuint current)
  {
    if (ctx == null) return;
    try
    {
      var success = WeakGCHandle<Callback>.FromIntPtr((nint)ctx).TryGetTarget(out var callback);
      if (!success || callback == null) return;
      callback._invokeDepth++;
      try
      {
        SafeInvoke(
        () => callback.ProgressHandler?.Invoke((ProgressType)(uint)progress, NativeString.FromNative((nint)pkg) ?? "", percent,
          howmany, current), callback.HandlerException);
      }
      finally
      {
        callback._invokeDepth--;
      }
    }
    catch
    {
      // This catch is to guard against a race condition where the callback is disposed while the native code is still calling back. In that case, the WeakGCHandle will throw an exception, which we want to ignore.
    }
  }

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void DownloadAgent(void* ctx, byte* filename, _alpm_download_event_type_t eventType, void* data)
  {
    if (ctx == null) return;
    try
    {
      var success = WeakGCHandle<Callback>.FromIntPtr((nint)ctx).TryGetTarget(out var callback);
      if (!success || callback == null) return;
      callback._invokeDepth++;
      try
      {
        SafeInvoke(
          () => callback.DownloadHandler?.Invoke(NativeString.FromNative((nint)filename) ?? "",
            AlpmDownloadEvent.FromUnion(eventType, data)), callback.HandlerException);
      }
      finally
      {
        callback._invokeDepth--;
      }
    }
    catch
    {
      // This catch is to guard against a race condition where the callback is disposed while the native code is still calling back. In that case, the WeakGCHandle will throw an exception, which we want to ignore.
    }
  }

  /// <summary>
  /// Log thunk. This is the one libalpm callback that does not receive expanded arguments: it gets a
  /// <c>(fmt, va_list)</c> pair, so the message is expanded by <see cref="LogMessageFormatter"/>.
  /// The <c>va_list</c> is delivered as an opaque pointer, which is the only way it can reach C#.
  /// </summary>
  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void LogAgent(void* ctx, _alpm_loglevel_t level, byte* fmt, void* vaList)
  {
    if (ctx == null) return;
    try
    {
      var success = WeakGCHandle<Callback>.FromIntPtr((nint)ctx).TryGetTarget(out var callback);
    if (!success || callback == null) return;
    callback._invokeDepth++;
    try
    {
      SafeInvoke(
        () => callback.LogHandler?.Invoke((LogLevel)(uint)level, LogMessageFormatter.Format(fmt, vaList) ?? ""),
        callback.HandlerException);
    }
    finally
    {
      callback._invokeDepth--;
    }}
    catch
    {
      // This catch is to guard against a race condition where the callback is disposed while the native code is still calling back. In that case, the WeakGCHandle will throw an exception, which we want to ignore.
    }
  }

  internal Callback(SafeAlpmHandle alpmHandle, Lifetime lifetime, AlpmBindingConfig binding)
  {
    _handle = alpmHandle;
    _lifetime = lifetime;
    _binding = binding;
    _ctxHandle = new WeakGCHandle<Callback>(this);
  }

  private void ConfigureGuard()
  {
    ObjectDisposedException.ThrowIf(_detached, this);
    if (_invokeDepth != 0)
    {
      throw new InvalidOperationException(
        "Callback handlers are configuration, not control flow, and cannot be modified from within a handler.");
    }
  }

  private void ThrowIfError(int err)
  {
    if (err != 0)
    {
      var ex = ErrorHandler.ToException(NativeMethods.alpm_errno(_handle));
      GC.KeepAlive(this);
      throw ex;
    }
  }

  /// <summary>
  /// Receives libalpm event notifications. Setting a non-null delegate registers the native event
  /// callback; setting <see langword="null"/> unregisters it.
  /// </summary>
  public unsafe Action<AlpmEvent>? EventHandler
  {
    get;
    set
    {
      ConfigureGuard();
      if (value == null)
      {
        ThrowIfError(NativeMethods.alpm_option_set_eventcb(_handle, null, null));
        field = null;
        return;
      }

      field = value;
      var ctx = (void*)WeakGCHandle<Callback>.ToIntPtr(_ctxHandle);
      ThrowIfError(NativeMethods.alpm_option_set_eventcb(_handle, &EventAgent, ctx));
    }
  }

  /// <summary>
  /// Custom file retriever callback. Setting a non-null delegate registers an external download
  /// agent with libalpm; setting <see langword="null"/> restores libalpm's built-in libcurl downloader.
  /// </summary>
  public unsafe Func<string, string, bool, FetchResult>? FetchHandler
  {
    get;
    set
    {
      ConfigureGuard();
      if (value == null)
      {
        ThrowIfError(NativeMethods.alpm_option_set_fetchcb(_handle, null, null));
        field = null;
        return;
      }

      field = value;
      var ctx = (void*)WeakGCHandle<Callback>.ToIntPtr(_ctxHandle);
      ThrowIfError(NativeMethods.alpm_option_set_fetchcb(_handle, &FetchAgent, ctx));
    }
  }

  /// <summary>
  /// Receives interactive prompts from libalpm. Setting a non-null delegate registers the native
  /// question callback; setting <see langword="null"/> leaves libalpm to use its pre-seeded default answers.
  /// </summary>
  public unsafe Action<AlpmQuestion>? QuestionHandler
  {
    get;
    set
    {
      ConfigureGuard();
      if (value == null)
      {
        ThrowIfError(NativeMethods.alpm_option_set_questioncb(_handle, null, null));
        return;
      }

      field = value;
      var ctx = (void*)WeakGCHandle<Callback>.ToIntPtr(_ctxHandle);
      ThrowIfError(NativeMethods.alpm_option_set_questioncb(_handle, &QuestionAgent, ctx));
    }
  }

  /// <summary>
  /// Receives progress events during downloads handled by libalpm's internal downloader.
  /// Setting a non-null delegate registers the native download callback; setting <see langword="null"/>
  /// unregisters it. Note: this callback only fires when libalpm downloads files itself, and is inactive
  /// while <see cref="FetchHandler"/> is configured.
  /// </summary>
  public unsafe Action<string, AlpmDownloadEvent>? DownloadHandler
  {
    get;
    set
    {
      ConfigureGuard();
      if (value == null)
      {
        ThrowIfError(NativeMethods.alpm_option_set_dlcb(_handle, null, null));
        field = null;
        return;
      }

      field = value;
      var ctx = (void*)WeakGCHandle<Callback>.ToIntPtr(_ctxHandle);
      ThrowIfError(NativeMethods.alpm_option_set_dlcb(_handle, &DownloadAgent, ctx));
    }
  }

  /// <summary>
  /// Receives progress alerts during database and transaction operations. Setting a non-null delegate
  /// registers the native progress callback; setting <see langword="null"/> unregisters it.
  /// </summary>
  public unsafe Action<ProgressType, string, int, nuint, nuint>? ProgressHandler
  {
    get;
    set
    {
      ConfigureGuard();
      if (value == null)
      {
        ThrowIfError(NativeMethods.alpm_option_set_progresscb(_handle, null, null));
        field = null;
        return;
      }

      field = value;
      var ctx = (void*)WeakGCHandle<Callback>.ToIntPtr(_ctxHandle);
      ThrowIfError(NativeMethods.alpm_option_set_progresscb(_handle, &ProgressAgent, ctx));
      GC.KeepAlive(this);
    }
  }

  /// <summary>
  /// Receives libalpm's log messages, already expanded from the <c>(fmt, va_list)</c> pair the
  /// native callback is given. Setting a non-null delegate registers the native log callback;
  /// setting <see langword="null"/> unregisters it.
  /// </summary>
  /// <remarks>
  /// A message that could not be expanded (a null format string, or an allocation failure) arrives
  /// as an empty string rather than as <see langword="null"/>.
  /// </remarks>
  public unsafe Action<LogLevel, string>? LogHandler
  {
    get;
    set
    {
      ConfigureGuard();
      if (value == null)
      {
        ThrowIfError(NativeMethods.alpm_option_set_logcb(_handle, null, null));
        field = null;
        return;
      }

      field = value;
      var ctx = (void*)WeakGCHandle<Callback>.ToIntPtr(_ctxHandle);
      ThrowIfError(NativeMethods.alpm_option_set_logcb(_handle, &LogAgent, ctx));
    }
  }

  /// <summary>
  /// Optional observer for exceptions thrown by the user handlers (<see cref="EventHandler"/>,
  /// <see cref="FetchHandler"/>, <see cref="QuestionHandler"/>, <see cref="DownloadHandler"/>,
  /// <see cref="ProgressHandler"/>, <see cref="LogHandler"/>).
  /// </summary>
  /// <remarks>
  /// This is a managed observer hook and does not register a native callback with libalpm.
  /// Handler exceptions never cross the FFI boundary: they are caught inside the native thunks
  /// and, when this is set, forwarded here so they can be logged. Exceptions thrown by this
  /// observer are swallowed as well, so it cannot terminate the process either. Leave it null to
  /// ignore handler failures.
  /// </remarks>
  public Action<Exception>? HandlerException { get; set; }

  /// <summary>
  /// Invokes a user handler body so that no exception escapes to the native caller, which has no
  /// managed frame left to catch it.
  /// </summary>
  /// <param name="body">The handler invocation to protect.</param>
  /// <param name="observer">
  /// When non-null and <paramref name="body"/> throws, called with the thrown exception. Exceptions
  /// thrown by the observer are swallowed too.
  /// </param>
  internal static void SafeInvoke(Action body, Action<Exception>? observer)
  {
    try
    {
      body();
    }
    catch (Exception ex)
    {
      NotifyHandlerException(observer, ex);
    }
  }

  /// <summary>
  /// <c>SafeInvoke</c> for handlers that return a value:
  /// <paramref name="onException"/> is returned when the body throws. The fetch thunk uses -1 so a
  /// failing handler is reported to libalpm as an error instead of a bogus success.
  /// </summary>
  internal static int SafeInvoke(Func<int> body, int onException, Action<Exception>? observer)
  {
    try
    {
      return body();
    }
    catch (Exception ex)
    {
      NotifyHandlerException(observer, ex);
      return onException;
    }
  }

  private static void NotifyHandlerException(Action<Exception>? observer, Exception ex)
  {
    try
    {
      observer?.Invoke(ex);
    }
    catch
    {
      // An observer must not be able to escape the FFI boundary either.
    }
  }

  /// <summary>
  /// Releases the callback ctx handle. Deliberately <c>internal</c>: the handle is owned by
  /// <see cref="Alpm"/>, which releases it once <c>alpm_release</c> has returned successfully. A
  /// public entry point would let a consumer invalidate the ctx while native code may still call
  /// back.
  /// </summary>
  /// <remarks>
  /// Not called when <c>alpm_release</c> failed: the handle it belongs to is still alive then (it is
  /// what leaked), and its native thunks would dereference this ctx handle - see
  /// <see cref="Alpm.Dispose()"/>.
  /// </remarks>
  internal void Dispose()
  {
    if (_detached) return;
    _detached = true;
    if (_ctxHandle.IsAllocated) _ctxHandle.Dispose();
  }
}

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
/// Every handler invocation is routed through <c>SafeInvoke</c>, which swallows handler exceptions
/// at the FFI boundary and reports them through <see cref="HandlerException"/> when that observer is
/// set. The ctx handle is owned by <see cref="Alpm"/>, which releases it only once <c>alpm_release</c>
/// has returned successfully.
/// </para>
/// </remarks>
public sealed class Callback
{
  private readonly unsafe _alpm_handle_t* _handle;
  private int _invokeDepth;
  private bool _detached;

  // do not Dispose this before the callback class has been disposed
  // otherwise it will screw up the callbacks
  private GCHandle<Callback> _ctxHandle;

  private Action<EventType>? _eventHandler;
  private Func<string, string, bool, FetchResult>? _fetchHandler;
  private Action<QuestionType>? _questionHandler;
  private Action<string, DownloadEventType>? _downloadHandler;
  private Action<ProgressType, string, int, nuint, nuint>? _progressHandler;
  private Action<LogLevel, string>? _logHandler;

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void EventAgent(void* ctx, _alpm_event_t* eventT)
  {
    var callback = GCHandle<Callback>.FromIntPtr((nint)ctx).Target;
    callback._invokeDepth++;
    try
    {
      SafeInvoke(() => callback.EventHandler?.Invoke(EventType.FromUnion(eventT)), callback.HandlerException);
    }
    finally
    {
      callback._invokeDepth--;
    }
  }

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe int FetchAgent(void* ctx, byte* url, byte* localPath, int force)
  {
    var callback = GCHandle<Callback>.FromIntPtr((nint)ctx).Target;
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

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void QuestionAgent(void* ctx, _alpm_question_t* questionT)
  {
    var callback = GCHandle<Callback>.FromIntPtr((nint)ctx).Target;
    callback._invokeDepth++;
    try
    {
      SafeInvoke(() => callback.QuestionHandler?.Invoke(QuestionType.FromUnion(questionT)), callback.HandlerException);
    }
    finally
    {
      callback._invokeDepth--;
    }
  }

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void ProgressAgent(void* ctx, _alpm_progress_t progress, byte* pkg, int percent, nuint howmany,
    nuint current)
  {
    var callback = GCHandle<Callback>.FromIntPtr((nint)ctx).Target;
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

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void DownloadAgent(void* ctx, byte* filename, _alpm_download_event_type_t eventType, void* data)
  {
    var callback = GCHandle<Callback>.FromIntPtr((nint)ctx).Target;
    callback._invokeDepth++;
    try
    {
      SafeInvoke(
        () => callback.DownloadHandler?.Invoke(NativeString.FromNative((nint)filename) ?? "",
          DownloadEventType.FromUnion(eventType, data)), callback.HandlerException);
    }
    finally
    {
      callback._invokeDepth--;
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
    var callback = GCHandle<Callback>.FromIntPtr((nint)ctx).Target;
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
    }
  }

  internal unsafe Callback(_alpm_handle_t* alpmHandle)
  {
    _handle = alpmHandle;
    _ctxHandle = new GCHandle<Callback>(this);
  }

  private void ValidateCanConfigure()
  {
    ObjectDisposedException.ThrowIf(_detached, this);
    if (_invokeDepth != 0)
    {
      throw new InvalidOperationException(
        "Callback handlers are configuration, not control flow, and cannot be modified from within a handler.");
    }
  }

  private unsafe void ThrowIfError(int err)
  {
    if (err != 0)
    {
      throw ErrorHandler.ToException(NativeMethods.alpm_errno(_handle));
    }
  }

  /// <summary>
  /// <c>alpm_option_set_logcb</c>, declared by hand rather than taken from the generated bindings.
  /// </summary>
  /// <remarks>
  /// The generated declaration names the <c>va_list</c> parameter <c>__va_list_tag*</c>, which is
  /// the x86_64 SysV representation of it. That type does not exist on AArch64 (where bindgen emits
  /// a 32-byte <c>va_list</c> struct instead), so regenerating the bindings on another architecture
  /// would leave this call site referring to a type that is no longer generated. Declaring the
  /// parameter as <c>void*</c> keeps the managed side architecture-independent: a
  /// <c>va_list</c> argument is delivered as a pointer on every ABI .NET supports on Linux.
  /// </remarks>
  [DllImport("libalpm", EntryPoint = "alpm_option_set_logcb", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
  private static extern unsafe int SetLogCallback(_alpm_handle_t* handle,
    delegate* unmanaged[Cdecl]<void*, _alpm_loglevel_t, byte*, void*, void> callback, void* ctx);

  /// <summary>
  /// Receives libalpm event notifications. Setting a non-null delegate registers the native event
  /// callback; setting <see langword="null"/> unregisters it.
  /// </summary>
  public unsafe Action<EventType>? EventHandler
  {
    get => _eventHandler;
    set
    {
      ValidateCanConfigure();
      _eventHandler = value;
      delegate* unmanaged[Cdecl]<void*, _alpm_event_t*, void> shim = value != null ? &EventAgent : null;
      void* ctx = value != null ? (void*)GCHandle<Callback>.ToIntPtr(_ctxHandle) : null;
      ThrowIfError(NativeMethods.alpm_option_set_eventcb(_handle, shim, ctx));
    }
  }

  /// <summary>
  /// Custom file retriever callback. Setting a non-null delegate registers an external download
  /// agent with libalpm; setting <see langword="null"/> restores libalpm's built-in libcurl downloader.
  /// </summary>
  public unsafe Func<string, string, bool, FetchResult>? FetchHandler
  {
    get => _fetchHandler;
    set
    {
      ValidateCanConfigure();
      _fetchHandler = value;
      delegate* unmanaged[Cdecl]<void*, byte*, byte*, int, int> shim = value != null ? &FetchAgent : null;
      void* ctx = value != null ? (void*)GCHandle<Callback>.ToIntPtr(_ctxHandle) : null;
      ThrowIfError(NativeMethods.alpm_option_set_fetchcb(_handle, shim, ctx));
    }
  }

  /// <summary>
  /// Receives interactive prompts from libalpm. Setting a non-null delegate registers the native
  /// question callback; setting <see langword="null"/> leaves libalpm to use its pre-seeded default answers.
  /// </summary>
  public unsafe Action<QuestionType>? QuestionHandler
  {
    get => _questionHandler;
    set
    {
      ValidateCanConfigure();
      _questionHandler = value;
      delegate* unmanaged[Cdecl]<void*, _alpm_question_t*, void> shim = value != null ? &QuestionAgent : null;
      void* ctx = value != null ? (void*)GCHandle<Callback>.ToIntPtr(_ctxHandle) : null;
      ThrowIfError(NativeMethods.alpm_option_set_questioncb(_handle, shim, ctx));
    }
  }

  /// <summary>
  /// Receives progress events during downloads handled by libalpm's internal downloader.
  /// Setting a non-null delegate registers the native download callback; setting <see langword="null"/>
  /// unregisters it. Note: this callback only fires when libalpm downloads files itself, and is inactive
  /// while <see cref="FetchHandler"/> is configured.
  /// </summary>
  public unsafe Action<string, DownloadEventType>? DownloadHandler
  {
    get => _downloadHandler;
    set
    {
      ValidateCanConfigure();
      _downloadHandler = value;
      delegate* unmanaged[Cdecl]<void*, byte*, _alpm_download_event_type_t, void*, void> shim = value != null ? &DownloadAgent : null;
      void* ctx = value != null ? (void*)GCHandle<Callback>.ToIntPtr(_ctxHandle) : null;
      ThrowIfError(NativeMethods.alpm_option_set_dlcb(_handle, shim, ctx));
    }
  }

  /// <summary>
  /// Receives progress alerts during database and transaction operations. Setting a non-null delegate
  /// registers the native progress callback; setting <see langword="null"/> unregisters it.
  /// </summary>
  public unsafe Action<ProgressType, string, int, nuint, nuint>? ProgressHandler
  {
    get => _progressHandler;
    set
    {
      ValidateCanConfigure();
      _progressHandler = value;
      delegate* unmanaged[Cdecl]<void*, _alpm_progress_t, byte*, int, nuint, nuint, void> shim = value != null ? &ProgressAgent : null;
      void* ctx = value != null ? (void*)GCHandle<Callback>.ToIntPtr(_ctxHandle) : null;
      ThrowIfError(NativeMethods.alpm_option_set_progresscb(_handle, shim, ctx));
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
    get => _logHandler;
    set
    {
      ValidateCanConfigure();
      _logHandler = value;
      delegate* unmanaged[Cdecl]<void*, _alpm_loglevel_t, byte*, void*, void> shim = value != null ? &LogAgent : null;
      void* ctx = value != null ? (void*)GCHandle<Callback>.ToIntPtr(_ctxHandle) : null;
      ThrowIfError(SetLogCallback(_handle, shim, ctx));
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

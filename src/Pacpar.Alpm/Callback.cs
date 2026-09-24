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
/// The handlers are invoked from the native <c>[UnmanagedCallersOnly]</c> thunks below. Those
/// thunks are entered from native code, so there is no managed frame left to catch an exception:
/// letting one escape terminates the process. Every handler invocation is therefore routed
/// through <c>SafeInvoke</c>, which swallows handler
/// exceptions at the FFI boundary and reports them through <see cref="HandlerException"/> when
/// that observer is set. The ctx handle is owned by <see cref="Alpm"/>, which is the only type
/// allowed to release it - and which keeps it when the native handle could not be released, because
/// that handle still points at it.
/// </remarks>
public sealed class Callback
{

  // do not Dispose this before the callback class has been disposed
  // otherwise it will screw up the callbacks
  private GCHandle<Callback> _ctxHandle;

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void EventAgent(void* ctx, _alpm_event_t* eventT)
  {
    var callback = GCHandle<Callback>.FromIntPtr((nint)ctx).Target;
    SafeInvoke(() => callback.EventHandler?.Invoke(EventType.FromUnion(eventT)), callback.HandlerException);
  }

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe int FetchAgent(void* ctx, byte* url, byte* localPath, int force)
  {
    var callback = GCHandle<Callback>.FromIntPtr((nint)ctx).Target;

    return SafeInvoke(() =>
    {
      var urlString = NativeString.FromNative((IntPtr)url) ?? "";
      var localPathString = NativeString.FromNative((IntPtr)localPath) ?? "";

      return (int)(callback.FetchHandler?.Invoke(urlString, localPathString, force != 0) ?? FetchResult.Success);
    }, (int)FetchResult.Error, callback.HandlerException);
  }

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void QuestionAgent(void* ctx, _alpm_question_t* questionT)
  {
    var callback = GCHandle<Callback>.FromIntPtr((nint)ctx).Target;
    SafeInvoke(() => callback.QuestionHandler?.Invoke(QuestionType.FromUnion(questionT)), callback.HandlerException);
  }

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void ProgressAgent(void* ctx, _alpm_progress_t progress, byte* pkg, int percent, nuint howmany,
    nuint current)
  {
    var callback = GCHandle<Callback>.FromIntPtr((nint)ctx).Target;
    SafeInvoke(
      () => callback.ProgressHandler?.Invoke((ProgressType)(uint)progress, NativeString.FromNative((nint)pkg) ?? "", percent,
        howmany, current), callback.HandlerException);
  }

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void DownloadAgent(void* ctx, byte* filename, _alpm_download_event_type_t eventType, void* data)
  {
    var callback = GCHandle<Callback>.FromIntPtr((nint)ctx).Target;
    SafeInvoke(
      () => callback.DownloadHandler?.Invoke(NativeString.FromNative((nint)filename) ?? "",
        DownloadEventType.FromUnion(eventType, data)), callback.HandlerException);
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
    SafeInvoke(
      () => callback.LogHandler?.Invoke((LogLevel)(uint)level, LogMessageFormatter.Format(fmt, vaList) ?? ""),
      callback.HandlerException);
  }

  internal unsafe Callback(_alpm_handle_t* alpmHandle)
  {
    _ctxHandle = new GCHandle<Callback>(this);

    var err = NativeMethods.alpm_option_set_eventcb(alpmHandle, &EventAgent,
      (void*)GCHandle<Callback>.ToIntPtr(_ctxHandle));
    if (err != 0) throw ErrorHandler.ToException(NativeMethods.alpm_errno(alpmHandle));

    err = NativeMethods.alpm_option_set_fetchcb(alpmHandle, &FetchAgent,
      (void*)GCHandle<Callback>.ToIntPtr(_ctxHandle));
    if (err != 0) throw ErrorHandler.ToException(NativeMethods.alpm_errno(alpmHandle));

    err = NativeMethods.alpm_option_set_questioncb(alpmHandle, &QuestionAgent,
      (void*)GCHandle<Callback>.ToIntPtr(_ctxHandle));
    if (err != 0) throw ErrorHandler.ToException(NativeMethods.alpm_errno(alpmHandle));

    err = NativeMethods.alpm_option_set_progresscb(alpmHandle, &ProgressAgent,
      (void*)GCHandle<Callback>.ToIntPtr(_ctxHandle));
    if (err != 0) throw ErrorHandler.ToException(NativeMethods.alpm_errno(alpmHandle));

    err = NativeMethods.alpm_option_set_dlcb(alpmHandle, &DownloadAgent,
      (void*)GCHandle<Callback>.ToIntPtr(_ctxHandle));
    if (err != 0) throw ErrorHandler.ToException(NativeMethods.alpm_errno(alpmHandle));

    err = SetLogCallback(alpmHandle, &LogAgent, (void*)GCHandle<Callback>.ToIntPtr(_ctxHandle));
    if (err != 0) throw ErrorHandler.ToException(NativeMethods.alpm_errno(alpmHandle));
  }

  /// <summary>
  /// <c>alpm_option_set_logcb</c>, declared by hand rather than taken from the generated bindings.
  /// </summary>
  /// <remarks>
  /// The generated declaration names the <c>va_list</c> parameter <c>__va_list_tag*</c>, which is
  /// the x86_64 SysV representation of it. That type does not exist on AArch64 (where bindgen emits
  /// a 32-byte <c>va_list</c> struct instead), so regenerating the bindings on another architecture
  /// would leave this call site referring to a type that is no longer generated. Declaring the
  /// parameter as <see cref="void"/> keeps the managed side architecture-independent: a
  /// <c>va_list</c> argument is delivered as a pointer on every ABI .NET supports on Linux.
  /// </remarks>
  [DllImport("libalpm", EntryPoint = "alpm_option_set_logcb", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
  private static extern unsafe int SetLogCallback(_alpm_handle_t* handle,
    delegate* unmanaged[Cdecl]<void*, _alpm_loglevel_t, byte*, void*, void> callback, void* ctx);

  public Action<EventType>? EventHandler { get; set; }

  public Func<string, string, bool, FetchResult>? FetchHandler { get; set; }

  public Action<QuestionType>? QuestionHandler { get; set; }

  public Action<string, DownloadEventType>? DownloadHandler { get; set; }

  public Action<ProgressType, string, int, nuint, nuint>? ProgressHandler { get; set; }

  /// <summary>
  /// Receives libalpm's log messages, already expanded from the <c>(fmt, va_list)</c> pair the
  /// native callback is given.
  /// </summary>
  /// <remarks>
  /// A message that could not be expanded (a null format string, or an allocation failure) arrives
  /// as an empty string rather than as <see langword="null"/>.
  /// </remarks>
  public Action<LogLevel, string>? LogHandler { get; set; }

  /// <summary>
  /// Optional observer for exceptions thrown by the user handlers (<see cref="EventHandler"/>,
  /// <see cref="FetchHandler"/>, <see cref="QuestionHandler"/>, <see cref="DownloadHandler"/>,
  /// <see cref="ProgressHandler"/>, <see cref="LogHandler"/>).
  /// </summary>
  /// <remarks>
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
    if (_ctxHandle.IsAllocated) _ctxHandle.Dispose();
  }
}

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

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
/// allowed to release it.
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
      var urlString = Marshal.PtrToStringAnsi((IntPtr)url) ?? "";
      var localPathString = Marshal.PtrToStringAnsi((IntPtr)localPath) ?? "";

      return callback.FetchHandler?.Invoke(urlString, localPathString, force != 0) ?? 0;
    }, -1, callback.HandlerException);
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
      () => callback.ProgressHandler?.Invoke(progress, Marshal.PtrToStringAnsi((nint)pkg) ?? "", percent, howmany,
        current), callback.HandlerException);
  }

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void DownloadAgent(void* ctx, byte* filename, _alpm_download_event_type_t eventType, void* data)
  {
    var callback = GCHandle<Callback>.FromIntPtr((nint)ctx).Target;
    SafeInvoke(
      () => callback.DownloadHandler?.Invoke(Marshal.PtrToStringAnsi((nint)filename) ?? "",
        DownloadEventType.FromUnion(eventType, data)), callback.HandlerException);
  }

  internal unsafe Callback(byte* alpmHandle)
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
  }

  public Action<EventType>? EventHandler { get; set; }

  public Func<string, string, bool, int>? FetchHandler { get; set; }

  public Action<QuestionType>? QuestionHandler { get; set; }

  public Action<string, DownloadEventType>? DownloadHandler { get; set; }

  public Action<_alpm_progress_t, string, int, nuint, nuint>? ProgressHandler { get; set; }

  /// <summary>
  /// Optional observer for exceptions thrown by the user handlers (<see cref="EventHandler"/>,
  /// <see cref="FetchHandler"/>, <see cref="QuestionHandler"/>, <see cref="DownloadHandler"/>,
  /// <see cref="ProgressHandler"/>).
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
  /// <see cref="Alpm"/>, which releases it after <c>alpm_release</c> returned. A public entry
  /// point would let a consumer invalidate the ctx while native code may still call back.
  /// </summary>
  internal void Dispose()
  {
    if (_ctxHandle.IsAllocated) _ctxHandle.Dispose();
  }
}

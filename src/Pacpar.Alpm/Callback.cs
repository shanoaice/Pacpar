using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

public sealed class Callback : IDisposable
{
  private bool _disposed;

  // do not Dispose this before the callback class has been disposed
  // otherwise it will screw up the callbacks
  private GCHandle<Callback> _ctxHandle;

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void EventAgent(void* ctx, _alpm_event_t* eventT)
  {
    var cbCtx = GCHandle<Callback>.FromIntPtr((nint)ctx);
    cbCtx.Target.EventHandler?.Invoke(EventType.FromUnion(eventT));
  }

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe int FetchAgent(void* ctx, byte* url, byte* localPath, int force)
  {
    var cbCtx = GCHandle<Callback>.FromIntPtr((nint)ctx);
    var urlString = Marshal.PtrToStringAnsi((IntPtr)url) ?? "";
    var localPathString = Marshal.PtrToStringAnsi((IntPtr)localPath) ?? "";

    return cbCtx.Target.FetchHandler?.Invoke(urlString, localPathString, force != 0) ?? 0;
  }

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void QuestionAgent(void* ctx, _alpm_question_t* questionT)
  {
    var cbCtx = GCHandle<Callback>.FromIntPtr((nint)ctx);
    cbCtx.Target.QuestionHandler?.Invoke(QuestionType.FromUnion(questionT));
  }

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void ProgressAgent(void* ctx, _alpm_progress_t progress, byte* pkg, int percent, nuint howmany,
    nuint current)
  {
    var cbCtx = GCHandle<Callback>.FromIntPtr((nint)ctx);
    cbCtx.Target.ProgressHandler?.Invoke(progress, Marshal.PtrToStringAnsi((nint)pkg) ?? "", percent, howmany, current);
  }

  internal unsafe Callback(byte* alpmHandle)
  {
    _ctxHandle = new GCHandle<Callback>(this);

    var err = NativeMethods.alpm_option_set_eventcb(alpmHandle, &EventAgent,
      (void*)GCHandle<Callback>.ToIntPtr(_ctxHandle));
    if (err != 0) throw ErrorHandler.GetException(NativeMethods.alpm_errno(alpmHandle))!;
    err = NativeMethods.alpm_option_set_fetchcb(alpmHandle, &FetchAgent,
      (void*)GCHandle<Callback>.ToIntPtr(_ctxHandle));
    if (err != 0) throw ErrorHandler.GetException(NativeMethods.alpm_errno(alpmHandle))!;
    err = NativeMethods.alpm_option_set_questioncb(alpmHandle, &QuestionAgent,
      (void*)GCHandle<Callback>.ToIntPtr(_ctxHandle));
    if (err != 0) throw ErrorHandler.GetException(NativeMethods.alpm_errno(alpmHandle))!;
    err = NativeMethods.alpm_option_set_progresscb(alpmHandle, &ProgressAgent,
      (void*)GCHandle<Callback>.ToIntPtr(_ctxHandle));
  }

  public Action<EventType>? EventHandler { get; set; }

  public Func<string, string, bool, int>? FetchHandler { get; set; }

  public Action<QuestionType>? QuestionHandler { get; set; }

  public Action<_alpm_progress_t, string, int, nuint, nuint>? ProgressHandler { get; set; }

  public void Dispose()
  {
    GC.SuppressFinalize(this);
    Dispose(disposing: true);
  }

  private void Dispose(bool disposing)
  {
    if (_disposed) return;
    if (disposing)
    {
      if (_ctxHandle.IsAllocated) _ctxHandle.Dispose();
    }

    _disposed = true;
  }

  ~Callback()
  {
    Dispose(disposing: false);
  }
}

using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit.Callbacks;

public sealed class CallbackRegistrationTests
{
  [Fact]
  public unsafe void InitialHandle_HasNoCallbacksRegistered()
  {
    using var env = new IsolatedAlpmEnvironment();
    var handle = env.AlpmSession.Handle;

    Assert.True(NativeMethods.alpm_option_get_eventcb(handle) == null, "eventcb should not be registered initially");
    Assert.True(NativeMethods.alpm_option_get_fetchcb(handle) == null, "fetchcb should not be registered initially");
    Assert.True(NativeMethods.alpm_option_get_questioncb(handle) == null, "questioncb should not be registered initially");
    Assert.True(NativeMethods.alpm_option_get_progresscb(handle) == null, "progresscb should not be registered initially");
    Assert.True(NativeMethods.alpm_option_get_dlcb(handle) == null, "dlcb should not be registered initially");
    Assert.True(NativeMethods.alpm_option_get_logcb(handle) == null, "logcb should not be registered initially");
  }

  [Fact]
  public unsafe void Handlers_RegisterWhenSet_AndUnregisterWhenNull()
  {
    using var env = new IsolatedAlpmEnvironment();
    var handle = env.AlpmSession.Handle;
    var cb = env.AlpmSession.Callback;

    Assert.True(NativeMethods.alpm_option_get_eventcb(handle) == null);
    cb.EventHandler = _ => { };
    Assert.True(NativeMethods.alpm_option_get_eventcb(handle) != null);
    cb.EventHandler = null;
    Assert.True(NativeMethods.alpm_option_get_eventcb(handle) == null);

    Assert.True(NativeMethods.alpm_option_get_fetchcb(handle) == null);
    cb.FetchHandler = (_, _, _) => FetchResult.Success;
    Assert.True(NativeMethods.alpm_option_get_fetchcb(handle) != null);
    cb.FetchHandler = null;
    Assert.True(NativeMethods.alpm_option_get_fetchcb(handle) == null);

    Assert.True(NativeMethods.alpm_option_get_questioncb(handle) == null);
    cb.QuestionHandler = _ => { };
    Assert.True(NativeMethods.alpm_option_get_questioncb(handle) != null);
    cb.QuestionHandler = null;
    Assert.True(NativeMethods.alpm_option_get_questioncb(handle) == null);

    Assert.True(NativeMethods.alpm_option_get_progresscb(handle) == null);
    cb.ProgressHandler = _ => { };
    Assert.True(NativeMethods.alpm_option_get_progresscb(handle) != null);
    cb.ProgressHandler = null;
    Assert.True(NativeMethods.alpm_option_get_progresscb(handle) == null);

    Assert.True(NativeMethods.alpm_option_get_dlcb(handle) == null);
    cb.DownloadHandler = (_, _) => { };
    Assert.True(NativeMethods.alpm_option_get_dlcb(handle) != null);
    cb.DownloadHandler = null;
    Assert.True(NativeMethods.alpm_option_get_dlcb(handle) == null);

    Assert.True(NativeMethods.alpm_option_get_logcb(handle) == null);
    cb.LogHandler = (_, _) => { };
    Assert.True(NativeMethods.alpm_option_get_logcb(handle) != null);
    cb.LogHandler = null;
    Assert.True(NativeMethods.alpm_option_get_logcb(handle) == null);
  }

  [Fact]
  public void ReentrancyGuard_ThrowsInvalidOperationException_WhenMutatingFromInsideHandler()
  {
    var observed = new List<Exception>();

    using (var environment = new IsolatedAlpmEnvironment())
    {
      environment.AlpmSession.Callback.HandlerException = observed.Add;
      environment.AlpmSession.Callback.LogHandler = (_, _) =>
      {
        environment.AlpmSession.Callback.FetchHandler = (_, _, _) => FetchResult.Success;
      };
    }

    Assert.Contains(observed, ex => ex is InvalidOperationException);
  }

  [Fact]
  public void DisposedHandle_ThrowsObjectDisposedException_WhenMutatingHandler()
  {
    var environment = new IsolatedAlpmEnvironment();
    var callback = environment.AlpmSession.Callback;
    environment.Dispose();

    Assert.Throws<ObjectDisposedException>(() => callback.EventHandler = _ => { });
    Assert.Throws<ObjectDisposedException>(() => callback.FetchHandler = (_, _, _) => FetchResult.Success);
    Assert.Throws<ObjectDisposedException>(() => callback.QuestionHandler = _ => { });
    Assert.Throws<ObjectDisposedException>(() => callback.DownloadHandler = (_, _) => { });
    Assert.Throws<ObjectDisposedException>(() => callback.ProgressHandler = _ => { });
    Assert.Throws<ObjectDisposedException>(() => callback.LogHandler = (_, _) => { });
  }

  [Fact]
  public void LeakedHandle_ThrowsAlpmLifetimeException_WhenMutatingHandler()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.AlpmSession;
    var callback = alpm.Callback;
    _ = alpm.BeginTransaction();
    alpm.CurrentTransaction = null; // hide transaction so alpm_release fails
    alpm.Dispose(); // alpm_release fails; _detached remains false, but root lifetime is invalidated

    Assert.Throws<AlpmLifetimeException>(() => callback.EventHandler = _ => { });
    Assert.Throws<AlpmLifetimeException>(() => callback.FetchHandler = (_, _, _) => FetchResult.Success);
    Assert.Throws<AlpmLifetimeException>(() => callback.QuestionHandler = _ => { });
    Assert.Throws<AlpmLifetimeException>(() => callback.DownloadHandler = (_, _) => { });
    Assert.Throws<AlpmLifetimeException>(() => callback.ProgressHandler = _ => { });
    Assert.Throws<AlpmLifetimeException>(() => callback.LogHandler = (_, _) => { });
  }
}

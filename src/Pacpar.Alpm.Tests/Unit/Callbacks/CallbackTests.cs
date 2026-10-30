using System.Reflection;

namespace Pacpar.Alpm.Tests.Unit.Callbacks;

/// <summary>
/// Locks in the callback safety contract: a user handler that throws must not let the
/// exception escape an <c>[UnmanagedCallersOnly]</c> thunk (that would terminate the process); it
/// is reported through <see cref="AlpmCallbacks.HandlerException"/> instead. The callback ctx handle is
/// owned by <see cref="AlpmSession"/> and has no public release path - its lifetime is pinned by
/// <see cref="CallbackContextLifetimeTests"/>.
/// </summary>
/// <remarks>
/// The last three tests read accessibility rather than behaviour, which is what pins the decisions
/// the compiler cannot: a user never disposes a <see cref="AlpmCallbacks"/> (the session owns it), the
/// session still has to be able to, and <see cref="AlpmCallbacks.HandlerException"/> is the one hook a
/// caller has for seeing a handler that failed.
/// </remarks>
public sealed class CallbackTests
{
  [Fact]
  public void SafeInvoke_DoesNotPropagateHandlerExceptions()
  {
    var thrown = new InvalidOperationException("handler failed");

    var exception = Record.Exception(() => AlpmCallbacks.SafeInvoke(() => throw thrown, null));

    Assert.Null(exception);
  }

  [Fact]
  public void SafeInvoke_ReportsHandlerExceptionToObserver()
  {
    var thrown = new InvalidOperationException("handler failed");
    Exception? observed = null;

    AlpmCallbacks.SafeInvoke(() => throw thrown, ex => observed = ex);

    Assert.Same(thrown, observed);
  }

  [Fact]
  public void SafeInvoke_RunsBody_WithoutCallingObserver()
  {
    var invoked = false;

    AlpmCallbacks.SafeInvoke(() => invoked = true, _ => throw new InvalidOperationException("observer must not run"));

    Assert.True(invoked);
  }

  [Fact]
  public void SafeInvoke_SwallowsObserverExceptions()
  {
    var exception = Record.Exception(() => AlpmCallbacks.SafeInvoke(
      () => throw new InvalidOperationException("handler failed"),
      _ => throw new InvalidOperationException("observer failed")));

    Assert.Null(exception);
  }

  /// <summary>The fetch thunk must report -1 (error) to libalpm when the handler throws.</summary>
  [Fact]
  public void SafeInvoke_WithResult_ReturnsFallback_AndReportsHandlerException()
  {
    var thrown = new InvalidOperationException("fetch handler failed");
    Exception? observed = null;

    var result = AlpmCallbacks.SafeInvoke(() => throw thrown, -1, ex => observed = ex);

    Assert.Equal(-1, result);
    Assert.Same(thrown, observed);
  }

  [Fact]
  public void SafeInvoke_WithResult_ReturnsHandlerValue_WhenItDoesNotThrow()
    => Assert.Equal(7, AlpmCallbacks.SafeInvoke(() => 7, -1, null));

  [Fact]
  public void Dispose_IsNotPublicApi()
  {
    var publicDispose = typeof(AlpmCallbacks)
      .GetMethods(BindingFlags.Public | BindingFlags.Instance)
      .Where(method => method.Name == "Dispose");

    Assert.Empty(publicDispose);
    Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(AlpmCallbacks)));
  }

  [Fact]
  public void Dispose_StaysAvailableToAlpm()
  {
    var internalDispose = typeof(AlpmCallbacks)
      .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
      .Where(method => method.Name == "Dispose" && method.IsAssembly);

    Assert.Single(internalDispose);
  }

  [Fact]
  public void HandlerException_IsAPublicObserverHook()
  {
    var property = typeof(AlpmCallbacks).GetProperty("HandlerException");

    Assert.NotNull(property);
    Assert.Equal(typeof(Action<Exception>), property.PropertyType);
    Assert.True(property.CanRead);
    Assert.True(property.CanWrite);
  }
}

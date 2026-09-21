using System.Reflection;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Locks in the callback safety contract (report §B): a user handler that throws must not let the
/// exception escape an <c>[UnmanagedCallersOnly]</c> thunk (that would terminate the process); it
/// is reported through <see cref="Callback.HandlerException"/> instead. The callback ctx handle is
/// owned by <see cref="Alpm"/> and has no public release path.
/// </summary>
public sealed class CallbackTests
{
  [Fact]
  public void SafeInvoke_DoesNotPropagateHandlerExceptions()
  {
    var thrown = new InvalidOperationException("handler failed");

    var exception = Record.Exception(() => Callback.SafeInvoke(() => throw thrown, null));

    Assert.Null(exception);
  }

  [Fact]
  public void SafeInvoke_ReportsHandlerExceptionToObserver()
  {
    var thrown = new InvalidOperationException("handler failed");
    Exception? observed = null;

    Callback.SafeInvoke(() => throw thrown, ex => observed = ex);

    Assert.Same(thrown, observed);
  }

  [Fact]
  public void SafeInvoke_RunsBody_WithoutCallingObserver()
  {
    var invoked = false;

    Callback.SafeInvoke(() => invoked = true, _ => throw new InvalidOperationException("observer must not run"));

    Assert.True(invoked);
  }

  [Fact]
  public void SafeInvoke_SwallowsObserverExceptions()
  {
    var exception = Record.Exception(() => Callback.SafeInvoke(
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

    var result = Callback.SafeInvoke(() => throw thrown, -1, ex => observed = ex);

    Assert.Equal(-1, result);
    Assert.Same(thrown, observed);
  }

  [Fact]
  public void SafeInvoke_WithResult_ReturnsHandlerValue_WhenItDoesNotThrow()
    => Assert.Equal(7, Callback.SafeInvoke(() => 7, -1, null));

  [Fact]
  public void Dispose_IsNotPublicApi()
  {
    var publicDispose = typeof(Callback)
      .GetMethods(BindingFlags.Public | BindingFlags.Instance)
      .Where(method => method.Name == "Dispose");

    Assert.Empty(publicDispose);
    Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(Callback)));
  }

  [Fact]
  public void Dispose_StaysAvailableToAlpm()
  {
    var internalDispose = typeof(Callback)
      .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
      .Where(method => method.Name == "Dispose" && method.IsAssembly);

    Assert.Single(internalDispose);
  }

  [Fact]
  public void HandlerException_IsAPublicObserverHook()
  {
    var property = typeof(Callback).GetProperty("HandlerException");

    Assert.NotNull(property);
    Assert.Equal(typeof(Action<Exception>), property.PropertyType);
    Assert.True(property.CanRead);
    Assert.True(property.CanWrite);
  }
}

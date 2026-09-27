using System.Reflection;
using System.Runtime.CompilerServices;

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

  /// <summary>
  /// The finalizer path must release the callback ctx handle once the handle itself was released.
  /// <see cref="Callback"/> is strongly
  /// rooted by that very handle, so nothing but <see cref="Alpm"/> can ever free it: without this,
  /// dropping an <see cref="Alpm"/> without disposing it leaks the Callback and everything its
  /// handler delegates keep alive.
  /// </summary>
  [Fact]
  public void AlpmFinalizer_ReleasesTheCallbackContext()
  {
    var workspace = Path.Combine(Path.GetTempPath(), "pacpar-finalizer-tests", Guid.NewGuid().ToString("n"));
    var root = Path.Combine(workspace, "root");
    var dbpath = Path.Combine(workspace, "var", "lib", "pacman");
    Directory.CreateDirectory(root);
    Directory.CreateDirectory(Path.Combine(dbpath, "local"));
    Directory.CreateDirectory(Path.Combine(root, "tmp"));

    var callback = CreateAlpmWithoutDisposing(root, dbpath);

    for (var i = 0; i < 10 && callback.IsAlive; ++i)
    {
      GC.Collect();
      GC.WaitForPendingFinalizers();
    }

    Assert.False(callback.IsAlive,
      "Callback is still rooted after Alpm was finalized: its ctx GCHandle was not released.");
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static WeakReference CreateAlpmWithoutDisposing(string root, string dbpath)
  {
    var alpm = new Alpm(root, dbpath);
    var weak = new WeakReference(alpm.Callback);

    // Intentionally not disposed: this exercises ~Alpm().
    return weak;
  }

  /// <summary>
  /// The other half of the finalizer contract: when the native handle cannot be released, it stays
  /// alive - still holding this ctx handle, which its thunks dereference - so the context must be
  /// kept with it. Freeing it cleans nothing up and leaves the leaked handle pointing at a freed
  /// GCHandle, so the next callback entered from that handle (a log message during a later
  /// <c>alpm_trans_release</c>, say) throws inside <c>LogAgent</c> and takes the process down.
  /// </summary>
  [Fact]
  public void AlpmFinalizer_KeepsTheCallbackContext_WhenTheHandleCouldNotBeReleased()
  {
    var workspace = Path.Combine(Path.GetTempPath(), "pacpar-finalizer-tests", Guid.NewGuid().ToString("n"));
    var root = Path.Combine(workspace, "root");
    var dbpath = Path.Combine(workspace, "var", "lib", "pacman");
    Directory.CreateDirectory(root);
    Directory.CreateDirectory(Path.Combine(dbpath, "local"));
    Directory.CreateDirectory(Path.Combine(root, "tmp"));

    var callback = CreateAlpmWithAnUnreleasableHandle(root, dbpath);

    for (var i = 0; i < 10 && callback.IsAlive; ++i)
    {
      GC.Collect();
      GC.WaitForPendingFinalizers();
    }

    Assert.True(callback.IsAlive,
      "Callback was freed although the leaked handle still points at its ctx GCHandle.");

    if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
  }

  /// <summary>
  /// An <see cref="Alpm"/> whose release must fail: the transaction it owns is hidden from the
  /// wrapper, which is the state a consumer's forgotten transaction leaves libalpm in. The handle
  /// (and its lock) then leak for the life of the process, which is what this test needs.
  /// </summary>
  [MethodImpl(MethodImplOptions.NoInlining)]
  private static WeakReference CreateAlpmWithAnUnreleasableHandle(string root, string dbpath)
  {
    var alpm = new Alpm(root, dbpath);
    var weak = new WeakReference(alpm.Callback);
    _ = alpm.BeginTransaction((TransactionFlags)0);
    alpm.CurrentTransaction = null;

    // Intentionally not disposed: this exercises ~Alpm().
    return weak;
  }
}

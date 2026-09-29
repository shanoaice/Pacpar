using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

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
  /// Dropping an <see cref="Alpm"/> without disposing it must not root the <see cref="Callback"/>:
  /// the ctx handle is weak, so the handler delegates it keeps alive die with the instance. (The ctx
  /// handle slot itself is not freed on this path - nothing runs <c>Callback.Dispose()</c> during
  /// finalization - which is the accepted leak of the weak-ctx contract; the end-to-end guarantee
  /// that a stale ctx is a safe no-op is pinned by
  /// <see cref="LeakedHandle_LogCallbacksAfterCollection_AreSafeNoOps"/>.)
  /// </summary>
  [Fact]
  public void AlpmFinalizer_DoesNotRootTheCallback()
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
      "Callback is still rooted after Alpm was finalized although the ctx handle is weak.");
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static WeakReference CreateAlpmWithoutDisposing(string root, string dbpath)
  {
    var alpm = new Alpm(root, dbpath);
    var weak = new WeakReference(alpm.Callback);

    // Intentionally not disposed: this exercises SafeAlpmHandle's critical finalizer.
    return weak;
  }

  /// <summary>
  /// The finalizer counterpart of <see cref="AlpmDispose_KeepsTheCallbackContext_WhenTheHandleCouldNotBeReleased"/>:
  /// when the native handle cannot be released it stays alive with its callbacks registered,
  /// carrying the ctx handle value, so the ctx handle slot must stay allocated - freeing it would
  /// let the runtime reuse the slot for a foreign object that
  /// <c>WeakGCHandle&lt;Callback&gt;.TryGetTarget</c> would then read as a <see cref="Callback"/>
  /// (it casts without a type check). Only object liveness is observable here: holding the
  /// <see cref="Callback"/> alive to inspect the slot would also root its
  /// <see cref="SafeAlpmHandle"/> and suppress the finalizer this test is about. The slot
  /// mechanism itself is pinned deterministically by the <c>AlpmDispose_</c> tests above and
  /// below; the end-to-end stale-ctx behavior by
  /// <see cref="LeakedHandle_LogCallbacksAfterCollection_AreSafeNoOps"/>.
  /// </summary>
  [Fact]
  public void AlpmFinalizer_DoesNotRootTheCallback_WhenTheHandleCouldNotBeReleased()
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

    Assert.False(callback.IsAlive,
      "Callback is rooted although the ctx handle is weak and the Alpm instance is gone.");

    if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
  }

  /// <summary>
  /// The disposal counterpart of
  /// <see cref="AlpmFinalizer_DoesNotRootTheCallback_WhenTheHandleCouldNotBeReleased"/>: when
  /// <c>alpm_release</c> fails during <see cref="Alpm.Dispose()"/>, the native handle is what
  /// leaks - alive, with the callback registrations still in place and the ctx handle value still
  /// in <c>logcb_ctx</c> and friends. The ctx handle slot must therefore stay allocated: freeing it
  /// would let the runtime hand the slot to another GC handle allocation, and
  /// <c>WeakGCHandle&lt;Callback&gt;.TryGetTarget</c> casts its result without a type check
  /// (dotnet/runtime <c>WeakGCHandle.T.cs</c>: "Skip the type check"), so the next callback entered
  /// from the leaked handle would read a foreign object as a <see cref="Callback"/> - memory
  /// corruption, not a catchable exception. The <see cref="Callback"/> object itself may be
  /// collected; see <see cref="AlpmDispose_DoesNotRootTheCallback_WhenTheHandleCouldNotBeReleased"/>.
  /// </summary>
  [Fact]
  public void AlpmDispose_KeepsTheCallbackContext_WhenTheHandleCouldNotBeReleased()
  {
    var workspace = Path.Combine(Path.GetTempPath(), "pacpar-dispose-tests", Guid.NewGuid().ToString("n"));
    var root = Path.Combine(workspace, "root");
    var dbpath = Path.Combine(workspace, "var", "lib", "pacman");
    Directory.CreateDirectory(root);
    Directory.CreateDirectory(Path.Combine(dbpath, "local"));
    Directory.CreateDirectory(Path.Combine(root, "tmp"));

    var callback = DisposeAlpmWithAnUnreleasableHandle(root, dbpath);

    Assert.True(IsContextHandleAllocated(callback),
      "The ctx handle was freed although the leaked native handle still carries its value.");

    if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
  }

  /// <summary>
  /// The other half of the weak-ctx contract: the ctx handle being kept allocated must not keep the
  /// <see cref="Callback"/> (and with it the user handler delegates) alive. This is what the
  /// migration from a self-rooting <c>Normal</c> GC handle to
  /// <c>WeakGCHandle&lt;Callback&gt;</c> bought; a regression to a rooting handle fails here.
  /// </summary>
  [Fact]
  public void AlpmDispose_DoesNotRootTheCallback_WhenTheHandleCouldNotBeReleased()
  {
    var workspace = Path.Combine(Path.GetTempPath(), "pacpar-dispose-tests", Guid.NewGuid().ToString("n"));
    var root = Path.Combine(workspace, "root");
    var dbpath = Path.Combine(workspace, "var", "lib", "pacman");
    Directory.CreateDirectory(root);
    Directory.CreateDirectory(Path.Combine(dbpath, "local"));
    Directory.CreateDirectory(Path.Combine(root, "tmp"));

    var callback = DisposeAlpmWithAnUnreleasableHandleUnrooted(root, dbpath);

    for (var i = 0; i < 10 && callback.IsAlive; ++i)
    {
      GC.Collect();
      GC.WaitForPendingFinalizers();
    }

    Assert.False(callback.IsAlive,
      "Callback is rooted although the ctx handle is weak and the Alpm instance is gone.");

    if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
  }

  /// <summary>
  /// The deterministic success half of <see cref="Alpm.Dispose()"/>: once <c>alpm_release</c>
  /// destroyed the native handle, nothing can enter a thunk with the ctx value anymore, so the
  /// slot must be returned to the runtime. This is what bounds the
  /// <see cref="AlpmDispose_KeepsTheCallbackContext_WhenTheHandleCouldNotBeReleased"/> leak to
  /// buggy usage instead of routine usage.
  /// </summary>
  [Fact]
  public void AlpmDispose_ReleasesTheCallbackContext()
  {
    var workspace = Path.Combine(Path.GetTempPath(), "pacpar-dispose-tests", Guid.NewGuid().ToString("n"));
    var root = Path.Combine(workspace, "root");
    var dbpath = Path.Combine(workspace, "var", "lib", "pacman");
    Directory.CreateDirectory(root);
    Directory.CreateDirectory(Path.Combine(dbpath, "local"));
    Directory.CreateDirectory(Path.Combine(root, "tmp"));

    var alpm = new Alpm(root, dbpath);
    alpm.Callback.LogHandler = (_, _) => { };

    alpm.Dispose();

    Assert.False(IsContextHandleAllocated(alpm.Callback),
      "The ctx handle was not freed although alpm_release destroyed the native handle.");

    if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
  }

  /// <summary>
  /// The <see cref="Alpm.Dispose()"/> scenario where release must fail: the transaction it owns is
  /// hidden from the wrapper, which is the state a consumer's forgotten transaction leaves libalpm
  /// in. The native handle (and its lock) then leak for the life of the process. Returns the
  /// <see cref="Callback"/> strongly so the ctx handle slot can be inspected; use
  /// <see cref="DisposeAlpmWithAnUnreleasableHandleUnrooted"/> for GC-liveness checks.
  /// </summary>
  [MethodImpl(MethodImplOptions.NoInlining)]
  private static Callback DisposeAlpmWithAnUnreleasableHandle(string root, string dbpath)
  {
    var alpm = new Alpm(root, dbpath);
    // Registration is presence-driven: without a handler no thunk is registered and the leaked
    // handle never dereferences the ctx. The log thunk is the one teardown would enter.
    alpm.Callback.LogHandler = (_, _) => { };
    _ = alpm.BeginTransaction((TransactionFlags)0);
    alpm.CurrentTransaction = null;
    var handle = alpm.Handle;   // captured before Dispose: the getter refuses afterwards

    alpm.Dispose();
    Assert.False(handle.ReleaseSucceeded,
      "Test setup must leave alpm_release failing, or the scenario silently becomes the success path.");

    return alpm.Callback;
  }

  // Separate wrapper so the WeakReference is created inside a non-inlined frame: in Debug builds
  // the JIT extends local lifetimes to the end of the method, which would defeat the GC tests.
  [MethodImpl(MethodImplOptions.NoInlining)]
  private static WeakReference DisposeAlpmWithAnUnreleasableHandleUnrooted(string root, string dbpath)
    => new(DisposeAlpmWithAnUnreleasableHandle(root, dbpath));

  /// <summary>
  /// An <see cref="Alpm"/> whose release must fail: the transaction it owns is hidden from the
  /// wrapper, which is the state a consumer's forgotten transaction leaves libalpm in. The handle
  /// (and its lock) then leak for the life of the process, which is what this test needs.
  /// </summary>
  [MethodImpl(MethodImplOptions.NoInlining)]
  private static WeakReference CreateAlpmWithAnUnreleasableHandle(string root, string dbpath)
  {
    var alpm = new Alpm(root, dbpath);
    alpm.Callback.LogHandler = (_, _) => { };
    var weak = new WeakReference(alpm.Callback);
    _ = alpm.BeginTransaction((TransactionFlags)0);
    alpm.CurrentTransaction = null;

    // Intentionally not disposed: this exercises SafeAlpmHandle's critical finalizer.
    return weak;
  }

  /// <summary>
  /// End-to-end check of the hazard the ctx handle lifetime exists for. After a failed
  /// <c>alpm_release</c> the leaked native handle still has the log thunk registered with the stale
  /// ctx value; this drives a real libalpm log message through it while the <see cref="Callback"/>
  /// is alive (proving the thunk really is entered through the leaked handle's registration) and
  /// again after it was collected. Had the slot been freed and reused, the second call would read a
  /// foreign object as a <see cref="Callback"/> - <c>WeakGCHandle&lt;Callback&gt;.TryGetTarget</c>
  /// casts without a type check - and corrupt memory; the contract is a silent no-op instead.
  /// </summary>
  [Fact]
  public void LeakedHandle_LogCallbacksAfterCollection_AreSafeNoOps()
  {
    var workspace = Path.Combine(Path.GetTempPath(), "pacpar-dispose-tests", Guid.NewGuid().ToString("n"));
    var root = Path.Combine(workspace, "root");
    var dbpath = Path.Combine(workspace, "var", "lib", "pacman");
    Directory.CreateDirectory(root);
    Directory.CreateDirectory(Path.Combine(dbpath, "local"));
    Directory.CreateDirectory(Path.Combine(root, "tmp"));

    var (callback, handle, received) = SetUpLeakedLogHandle(root, dbpath);

    for (var i = 0; i < 10 && callback.IsAlive; ++i)
    {
      GC.Collect();
      GC.WaitForPendingFinalizers();
    }
    Assert.False(callback.IsAlive, "Callback is rooted although the ctx handle is weak.");

    var count = received.Count;
    DriveLogMessage(handle);
    Assert.Equal(count, received.Count);

    if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
  }

  /// <summary>
  /// The <see cref="DisposeAlpmWithAnUnreleasableHandle"/> setup plus the behavioral probe: returns
  /// the <see cref="Callback"/> weakly - the strong references die with this frame - along with the
  /// leaked native handle and the sink its log handler feeds. The probe inside proves the leaked
  /// handle still enters the thunk at this point, so the caller's post-collection probe is
  /// meaningful rather than a bare not-throw.
  /// </summary>
  [MethodImpl(MethodImplOptions.NoInlining)]
  private static (WeakReference Callback, SafeAlpmHandle Handle, List<string> Received) SetUpLeakedLogHandle(
    string root, string dbpath)
  {
    var alpm = new Alpm(root, dbpath);
    var received = new List<string>();
    alpm.Callback.LogHandler = (_, message) => received.Add(message);
    _ = alpm.BeginTransaction((TransactionFlags)0);
    alpm.CurrentTransaction = null;
    var handle = alpm.Handle;   // captured before Dispose: the getter refuses afterwards

    alpm.Dispose();             // alpm_release fails; the handle and its registrations leak
    Assert.False(handle.ReleaseSucceeded,
      "Test setup must leave alpm_release failing, or the scenario silently becomes the success path.");

    var count = received.Count;
    DriveLogMessage(handle);
    Assert.Contains(received.Skip(count), message => message.Contains("cachedir"));

    return (new WeakReference(alpm.Callback), handle, received);
  }

  /// <summary>
  /// Fires one log message through the given handle's registered log callback:
  /// <c>alpm_option_add_cachedir</c> logs "option 'cachedir' = ..." at DEBUG through
  /// <c>handle-&gt;logcb</c>. The handle is deliberately driven through the raw binding - the
  /// SafeHandle marshaller refuses a disposed wrapper, and a leaked handle is exactly the case
  /// under test.
  /// </summary>
  private static unsafe void DriveLogMessage(SafeAlpmHandle handle)
  {
    var raw = (_alpm_handle_t*)handle.DangerousGetHandle();
    var cachedir = (byte*)Marshal.StringToCoTaskMemUTF8("/tmp/pacpar-callback-ctx-test");
    try
    {
      Assert.Equal(0, NativeMethods.alpm_option_add_cachedir(raw, cachedir));
    }
    finally
    {
      Marshal.FreeCoTaskMem((nint)cachedir);
    }
  }

  /// <summary>
  /// Test seam without production surface: <c>Callback._ctxHandle</c> is private, and its slot
  /// state is exactly what the ctx lifetime contract is about - the <see cref="Callback"/> object
  /// being alive says nothing about it under a weak ctx handle.
  /// </summary>
  private static readonly FieldInfo CtxHandleField =
    typeof(Callback).GetField("_ctxHandle", BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Callback._ctxHandle was renamed; update this test seam.");

  private static bool IsContextHandleAllocated(Callback callback)
    => ((WeakGCHandle<Callback>)CtxHandleField.GetValue(callback)!).IsAllocated;
}

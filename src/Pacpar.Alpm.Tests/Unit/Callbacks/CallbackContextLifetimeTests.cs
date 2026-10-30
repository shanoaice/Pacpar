using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit.Callbacks;

/// <summary>
/// The ctx handle is how a native thunk finds its <see cref="AlpmCallbacks"/>: libalpm stores one value per
/// registered callback and hands it back on every invocation. These tests pin the lifetime of that
/// value - weak enough that a dropped session is collected, persistent enough that a handle which
/// could not be released never sees its slot reused.
/// </summary>
/// <remarks>
/// The slot is freed only when <c>alpm_release</c> destroyed the handle. A failed release leaves a
/// native handle that still carries the value with its callbacks registered, so freeing the slot
/// there would let the runtime hand it to a foreign object;
/// <c>WeakGCHandle&lt;AlpmCallbacks&gt;.TryGetTarget</c> casts its result without a type check
/// (dotnet/runtime <c>WeakGCHandle.T.cs</c>: "Skip the type check"), which is memory corruption rather
/// than a catchable exception. <see cref="LeakedHandle_LogCallbacksAfterCollection_AreSafeNoOps"/>
/// drives that hazard end to end.
/// <para>
/// The cases that drop a session without disposing it cannot clean up through
/// <see cref="IsolatedAlpmEnvironment.Dispose"/> - keeping the environment reachable would keep the
/// session reachable - so they delete the workspace themselves once the assertions are done.
/// </para>
/// </remarks>
public sealed class CallbackContextLifetimeTests
{
  /// <summary>
  /// Dropping an <see cref="AlpmSession"/> without disposing it must not root the <see cref="AlpmCallbacks"/>:
  /// the ctx handle is weak, so the handler delegates it keeps alive die with the instance. (The ctx
  /// handle slot itself is not freed on this path - nothing runs <c>AlpmCallbacks.Dispose()</c> during
  /// finalization - which is the accepted leak of the weak-ctx contract; the end-to-end guarantee
  /// that a stale ctx is a safe no-op is pinned by
  /// <see cref="LeakedHandle_LogCallbacksAfterCollection_AreSafeNoOps"/>.)
  /// </summary>
  [Fact]
  public void AlpmFinalizer_DoesNotRootTheCallback()
  {
    var (callback, workspace) = CreateAlpmWithoutDisposing();

    CollectUntilDead(callback);

    Assert.False(callback.IsAlive,
      "Callback is still rooted after Alpm was finalized although the ctx handle is weak.");

    DeleteWorkspace(workspace);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static (WeakReference Callback, string Workspace) CreateAlpmWithoutDisposing()
  {
    var environment = new IsolatedAlpmEnvironment();
    var weak = new WeakReference(environment.AlpmSession.Callback);

    // Intentionally not disposed: this exercises SafeAlpmHandle's critical finalizer.
    return (weak, environment.WorkspaceRoot);
  }

  /// <summary>
  /// The finalizer counterpart of <see cref="AlpmDispose_KeepsTheCallbackContext_WhenTheHandleCouldNotBeReleased"/>:
  /// when the native handle cannot be released it stays alive with its callbacks registered,
  /// carrying the ctx handle value, so the ctx handle slot must stay allocated - freeing it would
  /// let the runtime reuse the slot for a foreign object that
  /// <c>WeakGCHandle&lt;AlpmCallbacks&gt;.TryGetTarget</c> would then read as a <see cref="AlpmCallbacks"/>
  /// (it casts without a type check). Only object liveness is observable here: holding the
  /// <see cref="AlpmCallbacks"/> alive to inspect the slot would also root its
  /// <see cref="SafeAlpmHandle"/> and suppress the finalizer this test is about. The slot
  /// mechanism itself is pinned deterministically by the <c>AlpmDispose_</c> tests below; the
  /// end-to-end stale-ctx behavior by
  /// <see cref="LeakedHandle_LogCallbacksAfterCollection_AreSafeNoOps"/>.
  /// </summary>
  [Fact]
  public void AlpmFinalizer_DoesNotRootTheCallback_WhenTheHandleCouldNotBeReleased()
  {
    var (callback, workspace) = CreateAlpmWithAnUnreleasableHandle();

    CollectUntilDead(callback);

    Assert.False(callback.IsAlive,
      "Callback is rooted although the ctx handle is weak and the Alpm instance is gone.");

    DeleteWorkspace(workspace);
  }

  /// <summary>
  /// The disposal counterpart of
  /// <see cref="AlpmFinalizer_DoesNotRootTheCallback_WhenTheHandleCouldNotBeReleased"/>: when
  /// <c>alpm_release</c> fails during <see cref="AlpmSession.Dispose()"/>, the native handle is what
  /// leaks - alive, with the callback registrations still in place and the ctx handle value still
  /// in <c>logcb_ctx</c> and friends. The ctx handle slot must therefore stay allocated: freeing it
  /// would let the runtime hand the slot to another GC handle allocation, and
  /// <c>WeakGCHandle&lt;AlpmCallbacks&gt;.TryGetTarget</c> casts its result without a type check
  /// (dotnet/runtime <c>WeakGCHandle.T.cs</c>: "Skip the type check"), so the next callback entered
  /// from the leaked handle would read a foreign object as a <see cref="AlpmCallbacks"/> - memory
  /// corruption, not a catchable exception. The <see cref="AlpmCallbacks"/> object itself may be
  /// collected; see <see cref="AlpmDispose_DoesNotRootTheCallback_WhenTheHandleCouldNotBeReleased"/>.
  /// </summary>
  [Fact]
  public void AlpmDispose_KeepsTheCallbackContext_WhenTheHandleCouldNotBeReleased()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var callback = DisposeAlpmWithAnUnreleasableHandle(environment.AlpmSession);

    Assert.True(IsContextHandleAllocated(callback),
      "The ctx handle was freed although the leaked native handle still carries its value.");
  }

  /// <summary>
  /// The other half of the weak-ctx contract: the ctx handle being kept allocated must not keep the
  /// <see cref="AlpmCallbacks"/> (and with it the user handler delegates) alive. This is what the
  /// migration from a self-rooting <c>Normal</c> GC handle to
  /// <c>WeakGCHandle&lt;AlpmCallbacks&gt;</c> bought; a regression to a rooting handle fails here.
  /// </summary>
  [Fact]
  public void AlpmDispose_DoesNotRootTheCallback_WhenTheHandleCouldNotBeReleased()
  {
    var (callback, workspace) = DisposeAlpmWithAnUnreleasableHandleUnrooted();

    CollectUntilDead(callback);

    Assert.False(callback.IsAlive,
      "Callback is rooted although the ctx handle is weak and the Alpm instance is gone.");

    DeleteWorkspace(workspace);
  }

  /// <summary>
  /// The deterministic success half of <see cref="AlpmSession.Dispose()"/>: once <c>alpm_release</c>
  /// destroyed the native handle, nothing can enter a thunk with the ctx value anymore, so the
  /// slot must be returned to the runtime. This is what bounds the
  /// <see cref="AlpmDispose_KeepsTheCallbackContext_WhenTheHandleCouldNotBeReleased"/> leak to
  /// buggy usage instead of routine usage.
  /// </summary>
  [Fact]
  public void AlpmDispose_ReleasesTheCallbackContext()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.AlpmSession;
    var callback = alpm.Callback;
    callback.LogHandler = (_, _) => { };

    alpm.Dispose();

    Assert.False(IsContextHandleAllocated(callback),
      "The ctx handle was not freed although alpm_release destroyed the native handle.");
  }

  /// <summary>
  /// The <see cref="AlpmSession.Dispose()"/> scenario where release must fail: the transaction it owns
  /// is hidden from the wrapper, which is the state a consumer's forgotten transaction leaves libalpm
  /// in. The native handle (and its lock) then leak for the life of the process. Returns the
  /// <see cref="AlpmCallbacks"/> strongly so the ctx handle slot can be inspected; use
  /// <see cref="DisposeAlpmWithAnUnreleasableHandleUnrooted"/> for GC-liveness checks.
  /// </summary>
  private static AlpmCallbacks DisposeAlpmWithAnUnreleasableHandle(AlpmSession alpm)
  {
    var callback = alpm.Callback;
    // Registration is presence-driven: without a handler no thunk is registered and the leaked
    // handle never dereferences the ctx. The log thunk is the one teardown would enter.
    callback.LogHandler = (_, _) => { };
    _ = alpm.BeginTransaction();
    alpm.CurrentTransaction = null;
    var handle = alpm.Handle;   // captured before Dispose: the getter refuses afterwards

    alpm.Dispose();
    Assert.False(handle.ReleaseSucceeded,
      "Test setup must leave alpm_release failing, or the scenario silently becomes the success path.");

    return callback;
  }

  // Separate wrapper so the WeakReference is created inside a non-inlined frame: in Debug builds
  // the JIT extends local lifetimes to the end of the method, which would defeat the GC tests.
  [MethodImpl(MethodImplOptions.NoInlining)]
  private static (WeakReference Callback, string Workspace) DisposeAlpmWithAnUnreleasableHandleUnrooted()
  {
    var environment = new IsolatedAlpmEnvironment();
    var callback = DisposeAlpmWithAnUnreleasableHandle(environment.AlpmSession);

    return (new WeakReference(callback), environment.WorkspaceRoot);
  }

  /// <summary>
  /// An <see cref="AlpmSession"/> whose release must fail: the transaction it owns is hidden from the
  /// wrapper, which is the state a consumer's forgotten transaction leaves libalpm in. The handle
  /// (and its lock) then leak for the life of the process, which is what this test needs.
  /// </summary>
  [MethodImpl(MethodImplOptions.NoInlining)]
  private static (WeakReference Callback, string Workspace) CreateAlpmWithAnUnreleasableHandle()
  {
    var environment = new IsolatedAlpmEnvironment();
    environment.AlpmSession.Callback.LogHandler = (_, _) => { };
    var weak = new WeakReference(environment.AlpmSession.Callback);
    _ = environment.AlpmSession.BeginTransaction();
    environment.AlpmSession.CurrentTransaction = null;

    // Intentionally not disposed: this exercises SafeAlpmHandle's critical finalizer.
    return (weak, environment.WorkspaceRoot);
  }

  /// <summary>
  /// End-to-end check of the hazard the ctx handle lifetime exists for. After a failed
  /// <c>alpm_release</c> the leaked native handle still has the log thunk registered with the stale
  /// ctx value; this drives a real libalpm log message through it while the <see cref="AlpmCallbacks"/>
  /// is alive (proving the thunk really is entered through the leaked handle's registration) and
  /// again after it was collected. Had the slot been freed and reused, the second call would read a
  /// foreign object as a <see cref="AlpmCallbacks"/> - <c>WeakGCHandle&lt;AlpmCallbacks&gt;.TryGetTarget</c>
  /// casts without a type check - and corrupt memory; the contract is a silent no-op instead.
  /// </summary>
  [Fact]
  public void LeakedHandle_LogCallbacksAfterCollection_AreSafeNoOps()
  {
    var (callback, handle, received, workspace) = SetUpLeakedLogHandle();

    CollectUntilDead(callback);
    Assert.False(callback.IsAlive, "Callback is rooted although the ctx handle is weak.");

    var count = received.Count;
    DriveLogMessage(handle);
    Assert.Equal(count, received.Count);

    DeleteWorkspace(workspace);
  }

  /// <summary>
  /// The <see cref="DisposeAlpmWithAnUnreleasableHandle"/> setup plus the behavioral probe: returns
  /// the <see cref="AlpmCallbacks"/> weakly - the strong references die with this frame - along with the
  /// leaked native handle and the sink its log handler feeds. The probe inside proves the leaked
  /// handle still enters the thunk at this point, so the caller's post-collection probe is
  /// meaningful rather than a bare not-throw.
  /// </summary>
  [MethodImpl(MethodImplOptions.NoInlining)]
  private static (WeakReference Callback, SafeAlpmHandle Handle, List<string> Received, string Workspace)
    SetUpLeakedLogHandle()
  {
    var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.AlpmSession;
    var callback = alpm.Callback;
    var received = new List<string>();
    callback.LogHandler = (_, message) => received.Add(message);
    _ = alpm.BeginTransaction();
    alpm.CurrentTransaction = null;
    var handle = alpm.Handle;   // captured before Dispose: the getter refuses afterwards

    alpm.Dispose();             // alpm_release fails; the handle and its registrations leak
    Assert.False(handle.ReleaseSucceeded,
      "Test setup must leave alpm_release failing, or the scenario silently becomes the success path.");

    var count = received.Count;
    DriveLogMessage(handle);
    Assert.Contains(received.Skip(count), message => message.Contains("cachedir"));

    return (new WeakReference(callback), handle, received, environment.WorkspaceRoot);
  }

  /// <summary>
  /// Fires one log message through the given handle's registered log callback:
  /// <c>alpm_option_add_cachedir</c> logs "option 'cachedir' = ..." at DEBUG through
  /// <c>handle->logcb</c>. The handle is deliberately driven through the raw binding - the
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

  private static void CollectUntilDead(WeakReference reference)
  {
    for (var i = 0; i < 10 && reference.IsAlive; ++i)
    {
      GC.Collect();
      GC.WaitForPendingFinalizers();
    }
  }

  private static void DeleteWorkspace(string workspace)
  {
    if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
  }

  /// <summary>
  /// Test seam without production surface: <c>AlpmCallbacks._ctxHandle</c> is private, and its slot
  /// state is exactly what the ctx lifetime contract is about - the <see cref="AlpmCallbacks"/> object
  /// being alive says nothing about it under a weak ctx handle.
  /// </summary>
  private static readonly FieldInfo CtxHandleField =
    typeof(AlpmCallbacks).GetField("_ctxHandle", BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("AlpmCallbacks._ctxHandle was renamed; update this test seam.");

  private static bool IsContextHandleAllocated(AlpmCallbacks callback)
    => ((WeakGCHandle<AlpmCallbacks>)CtxHandleField.GetValue(callback)!).IsAllocated;
}

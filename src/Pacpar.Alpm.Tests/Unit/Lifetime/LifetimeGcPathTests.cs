using Pacpar.Alpm.List;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// The two things that can happen when a caller drops a session without disposing it.
/// </summary>
/// <remarks>
/// These tests read the reference graph rather than libalpm, and they need a session of their own: the
/// class fixture keeps its session reachable for the whole test, which would defeat the point.
/// <para>
/// What they pin, in order of importance:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>A live view keeps the session alive.</b> A view holds a stamp, the stamp holds its domain, and
/// the domain holds the owner. That one-hop chain is what makes it impossible for the handle's
/// finalizer to free the native graph underneath a view that is still reading it. It is the primary
/// guarantee, and it is stronger than any invalidation could be.
/// </description></item>
/// <item><description>
/// <b>With no views left, the session is collected and the handle is released.</b> The SafeHandle's
/// critical finalizer runs <c>alpm_release</c>; before that it retires the root domain's stamps, so
/// the release path never frees memory that some wrapper could still validate against.
/// </description></item>
/// </list>
/// </remarks>
public sealed class LifetimeGcPathTests
{
  private static (string Root, string DbPath, string Workspace) NewWorkspace()
  {
    var workspace = Path.Combine(Path.GetTempPath(), "pacpar-gc-path", Guid.NewGuid().ToString("n"));
    var root = Path.Combine(workspace, "root");
    var dbpath = Path.Combine(workspace, "var", "lib", "pacman");

    Directory.CreateDirectory(root);
    Directory.CreateDirectory(Path.Combine(dbpath, "local"));
    Directory.CreateDirectory(Path.Combine(root, "var", "cache", "pacman", "pkg"));

    return (root, dbpath, workspace);
  }

  private static void CollectEverything()
  {
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    GC.WaitForPendingFinalizers();
  }

  [Fact]
  public void ALiveView_KeepsTheSessionAlive_SoTheGcPathCannotFreeUnderIt()
  {
    var (root, dbpath, workspace) = NewWorkspace();

    var (view, session) = Create();

    CollectEverything();

    // The caller dropped the session, but the view still reaches it through
    // view -> stamp -> domain -> owner, so nothing was finalized and nothing was freed.
    Assert.True(session.IsAlive,
      "a live view must keep the session reachable, or its finalizer could free the graph underneath");
    Assert.Empty(view.ToArray()); // still usable, not merely non-crashing

    view = null;
    session = null;
    CollectEverything();

    Directory.Delete(workspace, recursive: true);

    (AlpmStringList, WeakReference) Create()
    {
      var alpm = new Alpm(root, dbpath);
      // Stamped with the local database's domain, whose root is the session.
      var servers = alpm.GetLocalDatabase().GetServers();
      return (servers, new WeakReference(alpm));
    }
  }

  [Fact]
  public void SessionWithoutViews_IsCollected_AndItsHandleIsReleased()
  {
    var (root, dbpath, workspace) = NewWorkspace();

    var session = Create();

    CollectEverything();

    // Nothing holds the session, so its SafeAlpmHandle was finalized: ReleaseHandle retired the root
    // domain and then ran alpm_release. Reaching this line without an exception is part of the
    // assertion - it means the release path ran to completion outside a test-thread call.
    Assert.False(session.IsAlive, "the session should have been collected");

    Directory.Delete(workspace, recursive: true);

    WeakReference Create()
    {
      var alpm = new Alpm(root, dbpath);
      return new WeakReference(alpm);
    }
  }
}

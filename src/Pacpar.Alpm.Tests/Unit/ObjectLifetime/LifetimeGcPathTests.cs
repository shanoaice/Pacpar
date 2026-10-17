using System.Runtime.CompilerServices;
using Pacpar.Alpm.List;
using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit.ObjectLifetime;

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
/// <para>
/// Both sessions are dropped without disposing them, so the workspace cannot be cleaned up through
/// <see cref="IsolatedAlpmEnvironment.Dispose"/> - the tests delete the directory themselves.
/// </para>
/// </remarks>
public sealed class LifetimeGcPathTests
{
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
    var (view, session, workspace) = CreateWithALiveView();

    CollectEverything();

    // The caller dropped the session, but the view still reaches it through
    // view -> stamp -> domain -> owner, so nothing was finalized and nothing was freed.
    Assert.True(session.IsAlive,
      "a live view must keep the session reachable, or its finalizer could free the graph underneath");
    Assert.Empty(view.ToArray()); // still usable, not merely non-crashing

    view = null;
    session = null;
    CollectEverything();

    DeleteWorkspace(workspace);
  }

  [Fact]
  public void SessionWithoutViews_IsCollected_AndItsHandleIsReleased()
  {
    var (session, workspace) = CreateWithoutViews();

    CollectEverything();

    // Nothing holds the session, so its SafeAlpmHandle was finalized: ReleaseHandle retired the root
    // domain and then ran alpm_release. Reaching this line without an exception is part of the
    // assertion - it means the release path ran to completion outside a pacpar-thread call.
    Assert.False(session.IsAlive, "the session should have been collected");

    DeleteWorkspace(workspace);
  }

  /// <summary>Answers with a view the caller keeps, a weak handle on the session behind it, and the
  /// workspace to delete afterwards.</summary>
  [MethodImpl(MethodImplOptions.NoInlining)]
  private static (AlpmStringList View, WeakReference Session, string Workspace) CreateWithALiveView()
  {
    var environment = new IsolatedAlpmEnvironment();

    // Stamped with the local database's domain, whose root is the session.
    return (environment.AlpmSession.GetLocalDatabase().Servers,
      new WeakReference(environment.AlpmSession),
      environment.WorkspaceRoot);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static (WeakReference Session, string Workspace) CreateWithoutViews()
  {
    var environment = new IsolatedAlpmEnvironment();

    return (new WeakReference(environment.AlpmSession), environment.WorkspaceRoot);
  }

  private static void DeleteWorkspace(string workspace)
  {
    if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
  }
}

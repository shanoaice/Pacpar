namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Pure managed coverage of the lifetime token tree (lifetime-token-preview.md, sections 6.1 and
/// 6.2): root anchoring, parent-chain cascading, first-reason-wins invalidation, the handle
/// registry's pointer deduplication, and the exception text callers see.
/// </summary>
/// <remarks>
/// No libalpm handle is needed: tokens are managed objects, and the "pointers" used with the
/// registry are only ever used as dictionary keys - nothing here dereferences them.
/// </remarks>
public sealed unsafe class LifetimeTests
{
  private static Lifetime Root(string target = "the ALPM handle") => Lifetime.CreateRoot(new object(), target);

  [Fact]
  public void CreateRoot_StartsAliveWithItsTarget()
  {
    var token = Root();

    Assert.True(token.IsAlive);
    Assert.Null(token.Parent);
    Assert.Equal("the ALPM handle", token.Target);
    Assert.Null(token.InvalidatedBy);
  }

  [Fact]
  public void CreateRoot_WithNullOwner_Throws()
    => Assert.Throws<ArgumentNullException>(() => Lifetime.CreateRoot(null!, "orphan"));

  [Fact]
  public void CreateChild_InheritsTheChain_AndDiesWithItsAncestors()
  {
    var root = Root();
    var child = root.CreateChild("the local database");
    var grandchild = child.CreateChild("a view");

    Assert.Same(root, child.Parent);
    Assert.Same(child, grandchild.Parent);
    Assert.True(root.IsAlive && child.IsAlive && grandchild.IsAlive);

    // Invalidating an ancestor retires the whole subtree without touching it.
    child.Invalidate("Database.Unregister()");
    Assert.False(child.IsAlive);
    Assert.False(grandchild.IsAlive);
    // ...but not the other way around: a dead child cannot poison its ancestors.
    Assert.True(root.IsAlive);
  }

  [Fact]
  public void ThrowIfStale_ReportsTheNearestDeadToken()
  {
    var root = Root();
    var child = root.CreateChild("the local database");
    var grandchild = child.CreateChild("a view");

    child.Invalidate("Database.Unregister()");

    var thrown = Assert.Throws<AlpmLifetimeException>(() => grandchild.ThrowIfStale());
    Assert.Equal("the local database", thrown.Target);
    Assert.Equal("Database.Unregister()", thrown.InvalidatedBy);
    Assert.IsType<AlpmLifetimeException>(thrown);
    Assert.IsAssignableFrom<InvalidOperationException>(thrown);
  }

  [Fact]
  public void ThrowIfStale_ReportsTheRootWhenOnlyTheRootDied()
  {
    var root = Root();
    var grandchild = root.CreateChild("a view");

    root.Invalidate("Alpm.Dispose()");

    var thrown = Assert.Throws<AlpmLifetimeException>(() => grandchild.ThrowIfStale());
    Assert.Equal("the ALPM handle", thrown.Target);
    Assert.Equal("Alpm.Dispose()", thrown.InvalidatedBy);
  }

  [Fact]
  public void ThrowIfStale_IsSilentWhileEveryLinkIsAlive()
  {
    var token = Root().CreateChild("the local database");

    token.ThrowIfStale(); // must not throw
    Assert.True(token.IsAlive);
  }

  [Fact]
  public void Invalidate_IsIdempotent_AndKeepsTheFirstReason()
  {
    var token = Root();

    token.Invalidate("Transactions.Commit()");
    token.Invalidate("a later, conflicting reason");

    Assert.False(token.IsAlive);
    Assert.Equal("Transactions.Commit()", token.InvalidatedBy);

    // And the first reason is what callers see through ThrowIfStale as well.
    var thrown = Assert.Throws<AlpmLifetimeException>(() => token.ThrowIfStale());
    Assert.Equal("Transactions.Commit()", thrown.InvalidatedBy);
  }

  [Fact]
  public void Registry_ReturnsTheSameTokenForTheSamePointer()
  {
    var owner = Root();
    void* handle = (void*)0x1234; // only ever used as a dictionary key

    var first = owner.GetLifetimeTokenForHandle(handle, "the sync database core");
    var second = owner.GetLifetimeTokenForHandle(handle, "a differently spelled target");

    // Deduplicated: the first registration wins, target text included.
    Assert.Same(first, second);
    Assert.Equal("the sync database core", first.Target);

    // A different pointer gets its own token, as a child of the registering token.
    var other = owner.GetLifetimeTokenForHandle((void*)0x5678, "the sync database extra");
    Assert.NotSame(first, other);
    Assert.Same(owner, first.Parent);
  }

  [Fact]
  public void ForgetHandle_LetsTheSameAddressGetAFreshToken()
  {
    var owner = Root();
    void* handle = (void*)0x1234;

    var first = owner.GetLifetimeTokenForHandle(handle, "the sync database core");
    owner.ForgetHandle(handle);
    first.Invalidate("Database.Unregister()");

    var second = owner.GetLifetimeTokenForHandle(handle, "the sync database core");

    // The address was re-issued, so a brand-new live token is handed out instead of the dead one -
    // this is what keeps a re-registration from inheriting a stale identity.
    Assert.NotSame(first, second);
    Assert.True(second.IsAlive);
    second.ThrowIfStale(); // must not throw
  }

  [Fact]
  public void InvalidateHandles_RetiresEveryRegisteredToken_AndClearsTheRegistry()
  {
    var owner = Root();
    void* first = (void*)0x1234;
    void* second = (void*)0x5678;
    var firstToken = owner.GetLifetimeTokenForHandle(first, "the sync database core");
    var secondToken = owner.GetLifetimeTokenForHandle(second, "the sync database extra");

    owner.InvalidateHandles("Alpm.UnregisterAllSyncDatabases()");

    Assert.False(firstToken.IsAlive);
    Assert.False(secondToken.IsAlive);

    var thrown = Assert.Throws<AlpmLifetimeException>(() => firstToken.ThrowIfStale());
    Assert.Equal("Alpm.UnregisterAllSyncDatabases()", thrown.InvalidatedBy);

    // The registry was cleared, so a recycled address starts over with a live token.
    var fresh = owner.GetLifetimeTokenForHandle(first, "the sync database core");
    Assert.NotSame(firstToken, fresh);
    Assert.True(fresh.IsAlive);
  }

  [Fact]
  public void InvalidateHandles_OnAnEmptyRegistry_IsANoOp()
  {
    var token = Root();

    token.InvalidateHandles("Alpm.UnregisterAllSyncDatabases()");

    Assert.True(token.IsAlive);
  }

  [Fact]
  public void Exception_WithoutAReason_SaysTheTargetIsNoLongerValid()
  {
    var thrown = new AlpmLifetimeException("the local database", null);

    Assert.Equal("the local database", thrown.Target);
    Assert.Null(thrown.InvalidatedBy);
    Assert.StartsWith("the local database is no longer valid, so this object points into unmanaged memory",
      thrown.Message);
    Assert.Contains("call ToSnapshot() before releasing the owner", thrown.Message);
  }

  [Fact]
  public void Exception_WithAReason_NamesTheReleasingOperation()
  {
    var thrown = new AlpmLifetimeException("the local database", "Transactions.Commit()");

    Assert.Equal("Transactions.Commit()", thrown.InvalidatedBy);
    Assert.StartsWith("the local database was released by Transactions.Commit(), so this object points into unmanaged memory",
      thrown.Message);
  }
}

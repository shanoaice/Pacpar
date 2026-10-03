namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Managed coverage of the lifetime domains and stamps: root anchoring, the two-level check, the
/// first-reason-wins invalidation, the handle registry's pointer deduplication, and the exception
/// text callers see.
/// </summary>
/// <remarks>
/// No libalpm handle is needed: a domain is a managed object, and the "pointers" used with the
/// registry are only dictionary keys - nothing here dereferences them.
/// <para>
/// The old token tree returned a live ancestor for every descendant and walked the chain on each
/// check. The domains replace both with a counter: a stamp records the generations of its own
/// resource and of the session, so a check is two comparisons and one increment retires every stamp
/// taken from the domain - including from a finalizer thread.
/// </para>
/// </remarks>
public sealed unsafe class LifetimeTests
{
  private static Lifetime Root(string target = "the ALPM handle") => Lifetime.CreateRoot(new object(), target);

  [Fact]
  public void CreateRoot_StartsAliveWithItsTarget()
  {
    var domain = Root();

    Assert.True(domain.IsAlive);
    Assert.Same(domain, domain.Root);
    Assert.Equal(0, domain.Generation);
    Assert.Equal("the ALPM handle", domain.Target);
    Assert.Null(domain.InvalidatedBy);
  }

  [Fact]
  public void CreateRoot_WithNullOwner_Throws()
    => Assert.Throws<ArgumentNullException>(() => Lifetime.CreateRoot(null!, "orphan"));

  [Fact]
  public void CreateChild_BelongsToTheRoot_AndAChildCannotCreateChildren()
  {
    var root = Root();
    var database = root.CreateChild("the local database");

    Assert.Same(root, database.Root);

    // The hierarchy is exactly two levels, because a stamp only records those two. Building a third
    // would produce a domain that no stamp covers, so it is refused loudly.
    Assert.Throws<InvalidOperationException>(() => database.CreateChild("a view"));
  }

  [Fact]
  public void Stamp_IsSilentWhileTheDomainAndRootAreUnbumped()
  {
    var stamp = Root().CreateChild("the local database").Capture();

    stamp.ThrowIfStale(); // must not throw
  }

  [Fact]
  public void Stamp_DiesWhenItsOwnResourceIsInvalidated()
  {
    var root = Root();
    var database = root.CreateChild("the local database");
    var stamp = database.Capture();

    database.Invalidate("Database.Unregister()");

    var thrown = Assert.Throws<AlpmLifetimeException>(stamp.ThrowIfStale);
    Assert.Equal("the local database", thrown.Target);
    Assert.Equal("Database.Unregister()", thrown.InvalidatedBy);
    Assert.False(database.IsAlive);
    Assert.True(root.IsAlive); // a dead child cannot poison its ancestors
  }

  [Fact]
  public void Stamp_DiesWhenTheSessionIsInvalidated_EvenIfItsOwnResourceIsUntouched()
  {
    var root = Root();
    var database = root.CreateChild("the local database");
    var stamp = database.Capture();

    root.Invalidate("Alpm.Dispose()");

    // Nothing was pushed into the database domain: the stamp compares against the root's generation
    // and finds it changed. That is what lets a finalizer retire everything with one increment.
    var thrown = Assert.Throws<AlpmLifetimeException>(stamp.ThrowIfStale);
    Assert.Equal("the ALPM handle", thrown.Target);
    Assert.Equal("Alpm.Dispose()", thrown.InvalidatedBy);
    Assert.False(database.IsAlive);
  }

  [Fact]
  public void Stamp_IsUnaffectedByASiblingResource()
  {
    var root = Root();
    var core = root.CreateChild("the sync database core");
    var extra = root.CreateChild("the sync database extra");
    var extraStamp = extra.Capture();

    core.Invalidate("Database.Unregister()");

    // Per-resource granularity is preserved: retiring one database leaves its siblings alone.
    extraStamp.ThrowIfStale();
    Assert.True(extra.IsAlive);
  }

  [Fact]
  public void Stamp_StaysInvalidAcrossFurtherBumps()
  {
    var domain = Root().CreateChild("the local database");
    var stamp = domain.Capture();

    domain.Invalidate("Transaction.Commit()");
    domain.Invalidate("a later, unrelated reason");

    // Generations are monotonic: once a stamp is behind, it never becomes valid again.
    Assert.Throws<AlpmLifetimeException>(stamp.ThrowIfStale);
  }

  [Fact]
  public void Invalidate_IsIdempotent_AndKeepsTheFirstReason()
  {
    var domain = Root();
    var stamp = domain.Capture();

    domain.Invalidate("Transaction.Commit()");
    domain.Invalidate("a later, conflicting reason");

    Assert.False(domain.IsAlive);
    Assert.Equal("Transaction.Commit()", domain.InvalidatedBy);

    var thrown = Assert.Throws<AlpmLifetimeException>(stamp.ThrowIfStale);
    Assert.Equal("Transaction.Commit()", thrown.InvalidatedBy);
  }

  [Fact]
  public void Registry_ReturnsTheSameDomainForTheSamePointer()
  {
    var owner = Root();
    void* handle = (void*)0x1234; // only ever used as a dictionary key

    var first = owner.GetLifetimeTokenForHandle(handle, "the sync database core");
    var second = owner.GetLifetimeTokenForHandle(handle, "a differently spelled target");

    // Deduplicated: the first registration wins, target text included. Two wrappers for one native
    // database must share a domain, or invalidating one would leave the other's views alive.
    Assert.Same(first, second);
    Assert.Equal("the sync database core", first.Target);
    Assert.Same(owner, first.Root);

    var other = owner.GetLifetimeTokenForHandle((void*)0x5678, "the sync database extra");
    Assert.NotSame(first, other);
    Assert.Same(owner, other.Root);
  }

  [Fact]
  public void ForgetHandle_LetsTheSameAddressGetAFreshDomain()
  {
    var owner = Root();
    void* handle = (void*)0x1234;

    var first = owner.GetLifetimeTokenForHandle(handle, "the sync database core");
    owner.ForgetHandle(handle);
    first.Invalidate("Database.Unregister()");

    var second = owner.GetLifetimeTokenForHandle(handle, "the sync database core");

    // The address was re-issued, so a brand-new domain is handed out instead of the retired one -
    // this is what keeps a re-registration from inheriting a stale identity.
    Assert.NotSame(first, second);
    Assert.True(second.IsAlive);
    second.Capture().ThrowIfStale(); // must not throw
  }

  [Fact]
  public void InvalidateHandles_RetiresEveryRegisteredDomain_AndClearsTheRegistry()
  {
    var owner = Root();
    void* core = (void*)0x1234;
    void* extra = (void*)0x5678;
    var coreDomain = owner.GetLifetimeTokenForHandle(core, "the sync database core");
    var extraDomain = owner.GetLifetimeTokenForHandle(extra, "the sync database extra");
    var coreStamp = coreDomain.Capture();

    owner.InvalidateHandles("Alpm.UnregisterAllSyncDatabases()");

    // The sync-database list lives on the handle, so the root is bumped too: the list view has to die
    // with its entries. Every child stamp dies through the root comparison.
    Assert.False(owner.IsAlive);
    Assert.False(coreDomain.IsAlive);
    Assert.False(extraDomain.IsAlive);

    var thrown = Assert.Throws<AlpmLifetimeException>(coreStamp.ThrowIfStale);
    Assert.Equal("Alpm.UnregisterAllSyncDatabases()", thrown.InvalidatedBy);

    // The registry was cleared, so a recycled address starts over with a live domain.
    var fresh = owner.GetLifetimeTokenForHandle(core, "the sync database core");
    Assert.NotSame(coreDomain, fresh);
    Assert.Equal(0, fresh.Generation);
  }

  [Fact]
  public void InvalidateHandles_OnAnEmptyRegistry_StillRetiresTheRoot()
  {
    var owner = Root();

    owner.InvalidateHandles("Alpm.UnregisterAllSyncDatabases()");

    // Deliberate change from the token tree: unregistering every sync database alters the handle's own
    // list, so the handle-level view dies too instead of surviving a change to the list it wraps.
    Assert.False(owner.IsAlive);
    Assert.Equal("Alpm.UnregisterAllSyncDatabases()", owner.InvalidatedBy);
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
    var thrown = new AlpmLifetimeException("the local database", "Transaction.Commit()");

    Assert.Equal("Transaction.Commit()", thrown.InvalidatedBy);
    Assert.StartsWith("the local database was released by Transaction.Commit(), so this object points into unmanaged memory",
      thrown.Message);
  }
}

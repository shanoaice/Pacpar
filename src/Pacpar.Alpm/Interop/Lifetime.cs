using System.Runtime.CompilerServices;

namespace Pacpar.Alpm;

/// <summary>
/// A native lifetime domain: the counter that decides whether the wrappers issued from it are still
/// safe to read.
/// </summary>
/// <remarks>
/// One domain exists per object that can release native memory on its own:
/// <list type="bullet">
/// <item><description>the session, as the root domain (<see cref="RootLifetime"/>, its <see cref="AlpmSession"/> handle owns everything
/// beneath it);</description></item>
/// <item><description>each sync database, the local database, each transaction, and callback execution frames, as child domains
/// (<see cref="ChildLifetime"/>) of that session;</description></item>
/// <item><description>each file-loaded package, as a child of the session that loaded it - the
/// session owns it until a hand-over or <see cref="AlpmSession.Dispose()"/> releases it.</description></item>
/// </list>
/// <para>
/// A domain owns a monotonically increasing <see cref="Generation"/>. Invalidating it means
/// incrementing that counter. A <see cref="LifetimeStamp"/> captured earlier compares unequal from
/// then on, so <b>one increment retires every stamp ever taken from the domain</b>. Nothing is
/// pushed into the views: each view compares against the domain's current value when it is read.
/// That is what makes invalidation safe on a finalizer thread, where enumerating views is not.
/// </para>
/// <para>
/// The domain also holds a direct reference to the managed object that owns the native resource
/// (<c>_owner</c>), and every view holds a stamp that holds the domain. That one-hop reference keeps
/// the owner - and therefore its finalizer - out of reach while any view is alive.
/// </para>
/// <para>
/// A stamp is two levels deep, which is what libalpm itself offers: a view's validity depends on its
/// own resource (a database, a transaction, a loaded package, or a callback frame) and on the session
/// at the root. Some resources also have parts that are released independently of the resource - a
/// database's server list is replaced or shortened without touching the database - so a child domain
/// can own facets (<see cref="LifetimeFacet"/>). A facet is not a third stamp level: its stamp
/// records the facet and the root, and its parent's invalidation cascades into it. The facet is drawn
/// below its parent because that is where its memory comes from, not because a check walks through
/// the parent.
/// </para>
/// </remarks>
internal abstract class Lifetime
{
  // Directly anchors the owner (Alpm or LoadedPackage): one hop from any stamp, so a live view keeps
  // the owner out of the finalizer queue.
  private protected readonly object _owner;

  private long _generation;
  private int _reported;
  private string? _invalidatedBy;

  private protected Lifetime(object owner, string target)
  {
    _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    Target = target;
  }

  /// <summary>Creates the root domain for <paramref name="owner"/>, held strongly to anchor it.</summary>
  internal static RootLifetime CreateRoot(object owner, string target) => RootLifetime.Create(owner, target);

  /// <summary>Human-readable description of the resource, used in exception messages.</summary>
  internal string Target { get; }

  /// <summary>Whether this domain is the root domain.</summary>
  internal abstract bool IsRoot { get; }

  /// <summary>The first reason passed to <see cref="Invalidate"/>, or <c>null</c> while untouched.</summary>
  internal string? InvalidatedBy => Volatile.Read(ref _invalidatedBy);

  /// <summary>This domain's current generation, compared against the value captured in a stamp.</summary>
  internal long Generation => Volatile.Read(ref _generation);

  /// <summary>
  /// Whether this domain and its root are still unbumped. Used for diagnostics and tests; wrappers
  /// compare stamps instead, because a stamp is what records <i>when</i> it was taken.
  /// </summary>
  internal abstract bool IsAlive { get; }

  /// <summary>Creates a child domain: a resource this session can release on its own.</summary>
  internal virtual ChildLifetime CreateChild(string target)
    => throw new InvalidOperationException(
      $"'{Target}' is not a root domain; the lifetime hierarchy is exactly two levels deep.");

  /// <summary>
  /// Retires every stamp taken from this domain. The first reason wins; later calls only bump.
  /// </summary>
  /// <remarks>
  /// Safe on a finalizer thread: it writes one reference and increments one counter, and never
  /// enumerates views. Callers must invalidate <i>before</i> freeing the native memory, so no view
  /// can pass its check while libalpm is freeing underneath it. Invalidating too early only makes a
  /// still-valid view report failure, which is the safe direction. A child domain overrides this to
  /// retire its facets as well, which is a bounded loop over a fixed-size array.
  /// </remarks>
  internal virtual void Invalidate(string invalidatedBy)
  {
    if (Interlocked.CompareExchange(ref _reported, 1, 0) == 0)
    {
      // Written before the increment, which is a full fence and therefore publishes it.
      _invalidatedBy = invalidatedBy;
    }

    Interlocked.Increment(ref _generation);
  }

  // ---- Stamps ----

  /// <summary>Captures a stamp that becomes invalid when this domain or its root is invalidated.</summary>
  internal LifetimeStamp Capture() => CreateStamp();

  private protected abstract LifetimeStamp CreateStamp();
}

/// <summary>
/// A root lifetime domain: belongs to the session (<see cref="AlpmSession"/>). Only a root domain can create
/// child domains or manage handle-registered domains.
/// </summary>
internal sealed class RootLifetime : Lifetime
{
  // Native pointer -> child domain registry, used by root domains only. It deduplicates domains per
  // native database pointer: two wrappers for the same _alpm_db_t* must share one domain, or
  // invalidating one would leave the other's views alive.
  private Dictionary<nint, ChildLifetime>? _handles;

  internal static RootLifetime Create(object owner, string target) => new(owner, target);

  private RootLifetime(object owner, string target) : base(owner, target)
  {
  }

  internal override bool IsRoot => true;

  internal override bool IsAlive => Generation == 0;

  private protected override LifetimeStamp CreateStamp() => new(this, this);

  /// <summary>Creates a child domain: a resource this session can release on its own.</summary>
  internal override ChildLifetime CreateChild(string target) => new(_owner, target, this);

  // ---- Handle Registry (native pointer deduplication) ----

  /// <summary>
  /// The domain for <paramref name="handle"/>, created as a child of this one on first use, so every
  /// wrapper for the same native pointer shares one domain and one invalidation retires them all.
  /// </summary>
  /// <remarks>Must run on the session's thread; see the note on the registry field.</remarks>
  internal unsafe ChildLifetime GetLifetimeTokenForHandle(void* handle, string target)
  {
    _handles ??= [];
    var key = (nint)handle;
    if (_handles.TryGetValue(key, out var token)) return token;

    token = CreateChild(target);
    _handles[key] = token;
    return token;
  }

  /// <summary>
  /// Drops the registry entry for <paramref name="handle"/> after its resource was released, so a
  /// later allocation at the same address does not inherit a bumped domain.
  /// </summary>
  /// <remarks>Must run on the session's thread; see the note on the registry field.</remarks>
  internal unsafe void ForgetHandle(void* handle) => _handles?.Remove((nint)handle);

  /// <summary>
  /// Retires every registered child domain and clears the registry, for the call that releases all of
  /// them at once (<c>alpm_unregister_all_syncdbs</c>).
  /// </summary>
  internal void InvalidateHandles(string invalidatedBy)
  {
    Invalidate(invalidatedBy);
    _handles?.Clear();
  }
}

/// <summary>
/// The parts of a child domain that are released independently of it, used as the index of that
/// domain's facet array.
/// </summary>
/// <remarks>
/// The numeric values are array indexes, not identities: keep them dense and zero-based, and keep
/// <see cref="Count"/> last. Reordering is safe because every call site names its member. A value at
/// or beyond <see cref="Count"/> fails with <see cref="IndexOutOfRangeException"/> instead of
/// silently addressing another facet, so a mistake here is loud rather than silent.
/// </remarks>
internal enum LifetimeFacet
{
  /// <summary>
  /// The database's server list (<c>alpm_db_get_servers</c>), released by adding, removing, or
  /// replacing a server.
  /// </summary>
  Servers = 0,

  /// <summary>
  /// The database's cache-server list (<c>alpm_db_get_cache_servers</c>), released by the
  /// cache-server operations.
  /// </summary>
  CacheServers = 1,

  /// <summary>The number of facets. Not a facet itself.</summary>
  Count = 2,
}

/// <summary>
/// A child lifetime domain: belongs to one database, transaction, loaded package, or callback frame.
/// Cannot create further child domains, and owns the facets its resource can release separately.
/// </summary>
/// <remarks>
/// A facet (<see cref="LifetimeFacet"/>) is a domain in its own right, issued by <see cref="Facet"/>
/// and retired by <see cref="InvalidateFacet"/>. It hangs below this domain for ownership only:
/// invalidating this domain cascades into every facet, so no facet outlives the memory it describes.
/// Its stamp still records the facet and the session root, never this domain, which keeps a check at
/// two comparisons.
/// </remarks>
internal sealed class ChildLifetime : Lifetime
{
  private readonly RootLifetime _root;

  // Allocated on first use and indexed by LifetimeFacet. Null while the domain issues no facet: most
  // domains never need one, and the slot costs eight bytes per domain rather than a dictionary.
  private ChildLifetime?[]? _facets;

  internal ChildLifetime(object owner, string target, RootLifetime root) : base(owner, target)
  {
    _root = root ?? throw new ArgumentNullException(nameof(root));
  }

  internal override bool IsRoot => false;

  internal override bool IsAlive => Generation == 0 && _root.Generation == 0;

  /// <summary>
  /// The facet domain of this resource, created on first use and reissued once it has been retired.
  /// </summary>
  /// <remarks>
  /// Must run on the session's thread. The operation that releases the facet retires it; the next
  /// read starts a fresh domain, so views taken before the release keep reporting it.
  /// </remarks>
  internal ChildLifetime Facet(LifetimeFacet facet, string target)
  {
    if (!IsAlive)
    {
      throw new InvalidOperationException(
        $"'{Target}' has been invalidated; it cannot issue a new '{target}' domain.");
    }

    var facets = _facets ??= new ChildLifetime[(int)LifetimeFacet.Count];
    var current = facets[(int)facet];
    if (current is { IsAlive: true }) return current;

    return facets[(int)facet] = new ChildLifetime(_owner, target, _root);
  }

  /// <summary>Retires the views of one facet, leaving this domain and the other facets alive.</summary>
  /// <remarks>
  /// A no-op while this domain has never issued that facet: nothing is stamped against it, and
  /// allocating it here would let a write path create a domain no read ever asked for.
  /// </remarks>
  internal void InvalidateFacet(LifetimeFacet facet, string invalidatedBy)
  {
    if (_facets is not { } facets) return;

    facets[(int)facet]?.Invalidate(invalidatedBy);
  }

  internal override void Invalidate(string invalidatedBy)
  {
    base.Invalidate(invalidatedBy);

    // This domain's own removal frees the memory behind every facet, and the parent is not part of a
    // facet's stamp, so nothing else would retire one. Cascade before the caller frees the memory.
    if (_facets is not { } facets) return;

    foreach (var facet in facets)
    {
      facet?.Invalidate(invalidatedBy);
    }
  }

  private protected override LifetimeStamp CreateStamp() => new(this, _root);
}

/// <summary>
/// What a borrowed wrapper holds: the domain it came from, and the generations observed when it was
/// created. Reading the wrapper checks the stamp first.
/// </summary>
/// <remarks>
/// The stamp stores both levels (the resource and the session root), so a check is two integer
/// comparisons and two field reads, with no pointer chasing. It is a struct on purpose: a child that
/// inherits a stamp observes exactly the same generations as its parent, rather than re-reading them.
/// </remarks>
internal readonly struct LifetimeStamp
{
  private readonly Lifetime _resource;
  private readonly long _resourceGeneration;
  private readonly RootLifetime _root;
  private readonly long _rootGeneration;

  internal LifetimeStamp(Lifetime resource, RootLifetime root)
  {
    _resource = resource ?? throw new ArgumentNullException(nameof(resource));
    _resourceGeneration = resource.Generation;
    _root = root ?? throw new ArgumentNullException(nameof(root));
    _rootGeneration = _root.Generation;
  }

  /// <summary>The domain this stamp came from; a child wrapper inherits it.</summary>
  internal Lifetime Domain => _resource;

  /// <summary>
  /// Throws <see cref="AlpmLifetimeException"/> naming the invalidated level, nearest first.
  /// </summary>
  /// <remarks>
  /// The resource is checked before the root so the message names the most specific cause: a commit
  /// bumps both, and "the local database was released by Transaction.Commit()" is the useful reading,
  /// where the root-first order would report the handle. A session release bumps only the root, so
  /// that case still reports the handle.
  /// </remarks>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal void ThrowIfStale()
  {
    if (!ReferenceEquals(_root, _resource) && _resourceGeneration != _resource.Generation)
    {
      throw new AlpmLifetimeException(_resource.Target, _resource.InvalidatedBy);
    }

    if (_rootGeneration != _root.Generation)
    {
      throw new AlpmLifetimeException(_root.Target, _root.InvalidatedBy);
    }
  }
}

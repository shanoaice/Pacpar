using System.Runtime.CompilerServices;

namespace Pacpar.Alpm;

/// <summary>
/// A native lifetime domain: the counter that decides whether the wrappers issued from it are still
/// safe to read.
/// </summary>
/// <remarks>
/// One domain exists per object that can release native memory on its own:
/// <list type="bullet">
/// <item><description>the session, as the root domain (its <see cref="Alpm"/> handle owns everything
/// beneath it);</description></item>
/// <item><description>each sync database, the local database, and each transaction, as child domains
/// of that session;</description></item>
/// <item><description>each file-loaded package, as a root domain of its own.</description></item>
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
/// The hierarchy is exactly two levels deep, which is what libalpm itself offers: a view's validity
/// depends on its own resource (a database, a transaction, or a loaded package) and on the session
/// at the root. Nothing below a package can be released independently of that package.
/// </para>
/// </remarks>
internal sealed class Lifetime
{
  // The root domain. Null on a root; set on a child. The link exists so a stamp can hold both levels
  // and check them with two comparisons instead of walking a chain.
  private readonly Lifetime? _root;

  // Directly anchors the owner (Alpm or LoadedPackage): one hop from any stamp, so a live view keeps
  // the owner out of the finalizer queue.
  private readonly object _owner;

  private long _generation;
  private int _reported;
  private string? _invalidatedBy;

  // Native pointer -> child domain registry, used by root domains only. It deduplicates domains per
  // native database pointer: two wrappers for the same _alpm_db_t* must share one domain, or
  // invalidating one would leave the other's views alive.
  private Dictionary<nint, Lifetime>? _handles;

  /// <summary>Creates the root domain for <paramref name="owner"/>, held strongly to anchor it.</summary>
  internal static Lifetime CreateRoot(object owner, string target) => new(owner, target, null);

  private Lifetime(object owner, string target, Lifetime? root)
  {
    _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    _root = root;
    Target = target;
  }

  /// <summary>Creates a child domain: a resource this session can release on its own.</summary>
  /// <remarks>
  /// Only a root may create children. A stamp records two levels - the resource and the session - so a
  /// grandchild domain would not be covered by any stamp taken from its parent. libalpm's ownership
  /// never needs one: every independently releasable resource hangs directly off the handle.
  /// </remarks>
  internal Lifetime CreateChild(string target)
    => _root is null
      ? new Lifetime(_owner, target, this)
      : throw new InvalidOperationException(
        $"'{Target}' is not a root domain; the lifetime hierarchy is exactly two levels deep.");

  /// <summary>Human-readable description of the resource, used in exception messages.</summary>
  internal string Target { get; }

  /// <summary>The root domain: this one, or the session this child belongs to.</summary>
  internal Lifetime Root => _root ?? this;

  /// <summary>The first reason passed to <see cref="Invalidate"/>, or <c>null</c> while untouched.</summary>
  internal string? InvalidatedBy => Volatile.Read(ref _invalidatedBy);

  /// <summary>This domain's current generation, compared against the value captured in a stamp.</summary>
  internal long Generation => Volatile.Read(ref _generation);

  /// <summary>
  /// Whether this domain and its root are still unbumped. Used for diagnostics and tests; wrappers
  /// compare stamps instead, because a stamp is what records <i>when</i> it was taken.
  /// </summary>
  internal bool IsAlive => _generation == 0 && Root._generation == 0;

  /// <summary>
  /// Retires every stamp taken from this domain. The first reason wins; later calls only bump.
  /// </summary>
  /// <remarks>
  /// Safe on a finalizer thread: it writes one reference and increments one counter, and never
  /// enumerates views. Callers must invalidate <i>before</i> freeing the native memory, so no view
  /// can pass its check while libalpm is freeing underneath it. Invalidating too early only makes a
  /// still-valid view report failure, which is the safe direction.
  /// </remarks>
  internal void Invalidate(string invalidatedBy)
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
  internal LifetimeStamp Capture() => new(this);

  // ---- Handle Registry (native pointer deduplication) ----

  /// <summary>
  /// The domain for <paramref name="handle"/>, created as a child of this one on first use, so every
  /// wrapper for the same native pointer shares one domain and one invalidation retires them all.
  /// </summary>
  internal unsafe Lifetime GetLifetimeTokenForHandle(void* handle, string target)
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
  internal unsafe void ForgetHandle(void* handle) => _handles?.Remove((nint)handle);

  /// <summary>
  /// Retires every registered child domain and clears the registry, for the call that releases all of
  /// them at once (<c>alpm_unregister_all_syncdbs</c>).
  /// </summary>
  /// <remarks>
  /// This bumps <i>this</i> domain as well: the sync-database list lives on the handle, so the list
  /// view has to die with its entries. A finalizer never calls this - it enumerates the registry -
  /// and does not need to: <see cref="Invalidate"/> on the root already retires every child stamp
  /// through the root comparison.
  /// </remarks>
  internal void InvalidateHandles(string invalidatedBy)
  {
    Invalidate(invalidatedBy);
    _handles?.Clear();
  }
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
  private readonly Lifetime _root;
  private readonly long _rootGeneration;

  internal LifetimeStamp(Lifetime resource)
  {
    _resource = resource ?? throw new ArgumentNullException(nameof(resource));
    _resourceGeneration = resource.Generation;
    _root = resource.Root;
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

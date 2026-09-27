using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Pacpar.Alpm;

/// <summary>
/// Tracks the validity of unmanaged memory resources and anchors root owners in the GC graph.
/// </summary>
/// <remarks>
/// Every token belongs to a tree rooted at the owner of the native resource (<see cref="Alpm"/> or
/// <see cref="LoadedPackage"/>). Two axes make the tree safe:
/// <list type="bullet">
/// <item><description>
/// <b>GC anchoring</b>: every token holds <c>_root</c>, a direct reference to the root owner, so a
/// reachable view keeps the owner reachable and its finalizer cannot run underneath a native read.
/// The reference is one hop away from any token in the tree; no chain walking is needed to keep the
/// owner alive.
/// </description></item>
/// <item><description>
/// <b>Hierarchical invalidation</b>: <c>_parent</c> links let a release invalidate a whole subtree
/// in O(1) - <see cref="IsAlive"/> and <see cref="ThrowIfStale"/> consult ancestors, so releasing
/// the handle also kills every database, transaction and view token below it without enumerating
/// them.
/// </description></item>
/// </list>
/// Root tokens additionally own a pointer registry (<c>_handles</c>) that deduplicates tokens per
/// native handle, so two managed wrappers for the same <c>_alpm_db_t*</c> share one token and one
/// invalidation retires them all.
/// <para>
/// Invariants: invalidate strictly <i>after</i> the native release returned successfully; finalizers
/// only call <see cref="Invalidate"/> with <c>fromFinalizer: true</c> and never touch the registry;
/// tokens follow libalpm's single-threaded handle model - views are not thread-safe, and cross-thread
/// retention goes through <c>ToSnapshot()</c>.
/// </para>
/// </remarks>
internal sealed class Lifetime
{
  private readonly Lifetime? _parent;

  // Directly anchors the root owner (Alpm or LoadedPackage): one hop from any token in the tree,
  // so a live view keeps the owner out of the finalizer queue.
  private readonly object _root;

  private volatile bool _alive = true;
  private int _reported;

  // Native pointer -> child token registry. Used exclusively by root handle tokens, and only from
  // non-finalizer threads (see the fromFinalizer contract on Invalidate).
  private Dictionary<nint, Lifetime>? _handles;

#if DEBUG
  private int _issuedViews;
#endif

  /// <summary>
  /// Creates a root lifetime token anchoring <paramref name="root"/>, the unmanaged resource owner.
  /// </summary>
  /// <param name="root">The owner instance; held strongly so views keep it alive.</param>
  /// <param name="target">Description of the resource, used in exception messages.</param>
  internal static Lifetime CreateRoot(object root, string target) => new(root, target);

  private Lifetime(object root, string target)
  {
    _root = root ?? throw new ArgumentNullException(nameof(root));
    Target = target;
  }

  internal Lifetime(string target, Lifetime parent)
  {
    _parent = parent ?? throw new ArgumentNullException(nameof(parent));
    _root = parent._root; // Inherited: keeps 1-hop reachability from any token to the root owner.
    Target = target;
  }

  /// <summary>
  /// Human-readable description of the resource this token guards (e.g. "the ALPM handle",
  /// "the local database"), used in <see cref="AlpmLifetimeException"/> messages.
  /// </summary>
  internal string Target { get; }

  /// <summary>The first reason passed to <see cref="Invalidate"/>, or <c>null</c> while alive.</summary>
  internal string? InvalidatedBy { get; private set; }

  /// <summary>The parent token, or <c>null</c> for a root token.</summary>
  internal Lifetime? Parent => _parent;

  /// <summary>Returns <c>true</c> if this token and all ancestor tokens are alive.</summary>
  internal bool IsAlive => _alive && (_parent is null || _parent.IsAlive);

  /// <summary>Creates a child token that dies together with this one.</summary>
  internal Lifetime CreateChild(string target) => new(target, this);

  /// <summary>
  /// Invalidates this token and all descendant views.
  /// Must be called strictly AFTER the unmanaged free function returns successfully: a failed
  /// release leaves the native memory alive, and invalidating anyway would produce false-positive
  /// exceptions on still-valid resources.
  /// </summary>
  /// <param name="invalidatedBy">Description of the releasing operation; the first one wins.</param>
  /// <param name="fromFinalizer">
  /// <c>true</c> when called on the finalizer thread. Suppresses the debug assertion below and is
  /// the contract that keeps finalization O(1) and free of managed-collection access.
  /// </param>
  internal void Invalidate(string invalidatedBy, bool fromFinalizer = false)
  {
    // Record the first invalidation reason exactly once; later callers cannot overwrite it.
    if (Interlocked.CompareExchange(ref _reported, 1, 0) == 0)
    {
      InvalidatedBy = invalidatedBy;
    }

    _alive = false;

#if DEBUG
    if (!fromFinalizer && _issuedViews > 0)
    {
      Debug.Fail($"{Target} was invalidated by {invalidatedBy} while {_issuedViews} views had been issued from it.");
    }
#endif
  }

  /// <summary>
  /// Verifies token validity across the parent chain. Throws <see cref="AlpmLifetimeException"/>
  /// naming the first dead token when this token or any ancestor was invalidated.
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal void ThrowIfStale()
  {
    for (var token = this; token is not null; token = token._parent)
    {
      if (!token._alive)
      {
        throw new AlpmLifetimeException(token.Target, token.InvalidatedBy);
      }
    }
  }

#if DEBUG
  /// <summary>Counts a view issued from this token, for the debug assertion in <see cref="Invalidate"/>.</summary>
  internal void CountIssuedView() => Interlocked.Increment(ref _issuedViews);
#endif

  // ---- Handle Registry (Native Pointer Deduplication) ----

  /// <summary>
  /// The token for <paramref name="handle"/>: created as a child of this token on first use and
  /// remembered, so every managed wrapper for the same native pointer shares one token and a single
  /// invalidation retires them all.
  /// </summary>
  /// <param name="handle">The native pointer to key the registry on.</param>
  /// <param name="target">Description of the resource, used when a new token is created.</param>
  internal unsafe Lifetime GetLifetimeTokenForHandle(void* handle, string target)
  {
    _handles ??= [];
    var key = (nint)handle;
    if (_handles.TryGetValue(key, out var token)) return token;

    token = new Lifetime(target, this);
    _handles[key] = token;
    return token;
  }

  /// <summary>
  /// Drops the registry entry for <paramref name="handle"/> after its resource was released, so a
  /// future allocation at the same address receives a fresh token instead of the dead one.
  /// </summary>
  internal unsafe void ForgetHandle(void* handle) => _handles?.Remove((nint)handle);

  /// <summary>
  /// Invalidates every registered handle token and clears the registry. Used when a single native
  /// call releases all of them at once (<c>alpm_unregister_all_syncdbs</c>). Never call from a
  /// finalizer: it enumerates a managed collection.
  /// </summary>
  internal void InvalidateHandles(string invalidatedBy)
  {
    if (_handles is null) return;
    foreach (var token in _handles.Values)
    {
      token.Invalidate(invalidatedBy);
    }

    _handles.Clear();
  }
}

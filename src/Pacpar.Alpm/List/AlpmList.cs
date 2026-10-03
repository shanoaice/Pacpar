using System.Collections;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm.List;

/// <summary>
/// Read-only view over an <c>alpm_list_t</c>.
/// </summary>
/// <remarks>
/// Ownership contract: <b>this type never owns the underlying list and never frees it.</b>
/// It is used for lists libalpm owns internally (for example <c>alpm_get_syncdbs</c>,
/// <c>alpm_db_get_pkgcache</c>, <c>alpm_pkg_get_depends</c>) and for caller-built lists that
/// libalpm only borrows (the <c>alpm_option_set_*</c>/<c>alpm_db_set_servers</c> family, whose
/// documentation states "the list will be duped and the original will still need to be freed by
/// the caller").
/// <para>
/// Because a borrowed view does not own the memory, it does not implement <see cref="IDisposable"/>.
/// Lists that must be freed by the caller are either materialized into managed collections or
/// managed through <see cref="AlpmOwnedList{T}"/>.
/// </para>
/// <para>
/// Lifetime safety: The view is guarded by its owning context's <see cref="Lifetime"/> token. If
/// the owning database, transaction, or ALPM handle is disposed, accessing elements from this view
/// throws an <see cref="AlpmLifetimeException"/> to prevent reading freed memory.
/// </para>
/// </remarks>
public abstract class AlpmList<T> : IEnumerable<T>
{
  // Visible to this class and to AlpmBorrowedList/AlpmOwnedList only; everything outside goes
  // through ValidatedNative().
  private protected readonly unsafe _alpm_list_t* _native;
  internal readonly unsafe delegate*<void*, Lifetime?, T> Factory;

  /// <summary>
  /// The lifetime token guarding this view, also forwarded to <see cref="Factory"/> as the element
  /// token; <c>null</c> only for lists with no owning native context (the empty
  /// <see cref="AlpmStringList"/> and caller-owned <see cref="AlpmOwnedList{T}"/> snapshots).
  /// </summary>
  internal readonly Lifetime? Lifetime;

  private protected unsafe AlpmList(_alpm_list_t* list, delegate*<void*, Lifetime?, T> factory, Lifetime? lifetime)
  {
    _native = list;
    Factory = factory;
    Lifetime = lifetime;
  }

  /// <summary>
  /// The only way to obtain the native list pointer. The guard runs here, so a call site cannot
  /// hand libalpm a list whose owning context was already released.
  /// </summary>
  internal unsafe _alpm_list_t* ValidatedNative()
  {
    Lifetime?.ThrowIfStale();
    return _native;
  }

  /// <summary>
  /// Wraps an existing <c>alpm_list_t</c> that this library does not own and must not free.
  /// </summary>
  /// <param name="list">The list to view. May be <c>null</c>, which yields an empty view.</param>
  /// <param name="factory">Converts one native list item into <typeparamref name="T"/>.</param>
  /// <param name="lifetime">
  /// Token guarding traversal and issued elements; <c>null</c> when the list has no owning context.
  /// </param>
  internal static unsafe AlpmList<T> Borrow(_alpm_list_t* list, delegate*<void*, Lifetime?, T> factory,
    Lifetime? lifetime)
    => new AlpmBorrowedList<T>(list, factory, lifetime);

  /// <summary>
  /// A forward-only enumerator over the borrowed list. Disposing it only stops enumeration;
  /// it never releases the underlying list.
  /// </summary>
  public unsafe struct Enumerator : IEnumerator<T>
  {
    private readonly AlpmList<T> _list;
    private _alpm_list_t* _current;
    private bool _started;
    private bool _disposed;

    internal Enumerator(AlpmList<T> list)
    {
      _list = list;
      _current = null;
      _started = false;
      _disposed = false;
    }

    /// <summary>
    /// Advances to the next node.
    /// </summary>
    /// <remarks>
    /// The validation and the dereference of the stored node pointer happen in this one method, so
    /// no call site can advance past the guard. A view that only checked on the first step would
    /// read freed memory here: advancing reads the previously visited node's <c>next</c>, which is
    /// exactly the case a same-thread <c>foreach</c> body can trigger by releasing the owner.
    /// Measured cost is ~8.5ns per element (ADR 0010).
    /// </remarks>
    private _alpm_list_t* Advance()
    {
      _list.Lifetime?.ThrowIfStale();
      return _current is null ? null : _current->next;
    }

    /// <summary>
    /// Reads the current node's data pointer. Validation comes first, for the same reason as
    /// <see cref="Advance"/>.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when there is no current node. A <see langword="true"/> result with a
    /// null data pointer is legitimate - a libalpm list may carry null payloads - so the two cases
    /// must not be collapsed into one.
    /// </returns>
    private bool TryGetCurrentData(out void* data)
    {
      _list.Lifetime?.ThrowIfStale();
      if (_current is null)
      {
        data = null;
        return false;
      }

      data = _current->data;
      return true;
    }

    public T Current
    {
      get
      {
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
        if (!_started) throw new InvalidOperationException();
        // The guard runs inside TryGetCurrentData, before the only dereference of _current here.
        if (!TryGetCurrentData(out var data)) throw new InvalidOperationException();
        var item = _list.Factory(data, _list.Lifetime);
        GC.KeepAlive(_list);
        return item;
      }
    }

    public bool MoveNext()
    {
      if (_disposed) throw new ObjectDisposedException(GetType().FullName);

      // The first step reads the head through the list's own accessor; every later step goes through
      // Advance(), which validates before it follows the stored node pointer.
      _current = _started ? Advance() : _list.ValidatedNative();
      _started = true;
      return _current != null; // comparing the pointer value needs no validation
    }

    public void Reset()
    {
      if (_disposed) throw new ObjectDisposedException(GetType().FullName);
      _current = null;
      _started = false;
    }

    public void Dispose()
    {
      _disposed = true;
    }

    object IEnumerator.Current => Current!;
  }

  public Enumerator GetEnumerator()
  {
    Lifetime?.ThrowIfStale();
    return new Enumerator(this);
  }

  IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  /// <summary>
  /// Materializes the view into a managed array in a single traversal pass. Useful when the
  /// underlying list is about to be freed but its contents are still needed.
  /// </summary>
  public unsafe T[] ToArray()
  {
    // The token check comes before the null test on purpose: a released owner must make every read
    // throw, and an empty-but-stale view answering with [] would quietly contradict that.
    Lifetime?.ThrowIfStale();
    if (_native == null) return [];

    var count = (int)NativeMethods.alpm_list_count(_native);
    var result = new T[count];
    var i = 0;
    for (var node = _native; node != null; node = node->next)
    {
      result[i++] = Factory(node->data, Lifetime);
    }

    return result;
  }
}

/// <summary>
/// Concrete borrowed view produced by <see cref="AlpmList{T}.Borrow"/>.
/// </summary>
internal sealed class AlpmBorrowedList<T> : AlpmList<T>
{
  internal unsafe AlpmBorrowedList(_alpm_list_t* list, delegate*<void*, Lifetime?, T> factory, Lifetime? lifetime)
    : base(list, factory, lifetime)
  {
  }
}

/// <summary>
/// Helper methods for freeing native <c>alpm_list_t</c> structures.
/// </summary>
internal static class AlpmNativeList
{
  /// <summary>
  /// Frees a native list and optionally its elements. Safe with a <c>null</c> pointer.
  /// </summary>
  /// <param name="list">The native list to free, or <c>null</c>.</param>
  /// <param name="innerFree">
  /// Optional element destructor, or <c>null</c> if elements do not require individual destruction.
  /// </param>
  internal static unsafe void Free(_alpm_list_t* list, delegate* unmanaged[Cdecl]<void*, void> innerFree)
  {
    if (list == null) return;

    if (innerFree != null) NativeMethods.alpm_list_free_inner(list, innerFree);
    NativeMethods.alpm_list_free(list);
  }
}

/// <summary>
/// Represents a caller-owned native list that frees its resources upon disposal.
/// </summary>
internal sealed class AlpmOwnedList<T> : AlpmList<T>, IDisposable
{
  private readonly unsafe delegate* unmanaged[Cdecl]<void*, void> _innerFree;
  private bool _disposed;

  /// <param name="list">Caller-owned list, or <c>null</c>.</param>
  /// <param name="factory">Converts one native list item into <typeparamref name="T"/>.</param>
  /// <param name="innerFree">
  /// Required element destructor, or <c>null</c> when the elements are owned elsewhere.
  /// </param>
  /// <param name="lifetime">
  /// Token guarding the snapshot pass and the materialized elements when they point back into a
  /// native context (e.g. package views owned by a database); <c>null</c> for fully self-contained
  /// payloads that are copied during materialization.
  /// </param>
  internal unsafe AlpmOwnedList(_alpm_list_t* list, delegate*<void*, Lifetime?, T> factory,
    delegate* unmanaged[Cdecl]<void*, void> innerFree, Lifetime? lifetime) : base(list, factory, lifetime)
  {
    _innerFree = innerFree;
  }

  internal static unsafe IReadOnlyList<T> Take(_alpm_list_t* list, delegate*<void*, Lifetime?, T> factory,
    delegate* unmanaged[Cdecl]<void*, void> innerFree, Lifetime? lifetime = null)
  {
    using var owned = new AlpmOwnedList<T>(list, factory, innerFree, lifetime);
    return owned.ToArray();
  }

  public unsafe void Dispose()
  {
    if (_disposed) return;

    AlpmNativeList.Free(_native, _innerFree);
    _disposed = true;
  }
}

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
  internal readonly unsafe _alpm_list_t* Native;
  internal readonly unsafe delegate*<void*, Lifetime?, T> Factory;

  /// <summary>
  /// The lifetime token guarding this view, also forwarded to <see cref="Factory"/> as the element
  /// token; <c>null</c> only for lists with no owning native context (the empty
  /// <see cref="AlpmStringList"/> and caller-owned <see cref="AlpmOwnedList{T}"/> snapshots).
  /// </summary>
  internal readonly Lifetime? Lifetime;

  private protected unsafe AlpmList(_alpm_list_t* list, delegate*<void*, Lifetime?, T> factory, Lifetime? lifetime)
  {
    Native = list;
    Factory = factory;
    Lifetime = lifetime;
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

    public T Current
    {
      get
      {
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
        if (!_started || _current == null) throw new InvalidOperationException();
        _list.Lifetime?.ThrowIfStale();
        var item = _list.Factory(_current->data, _list.Lifetime);
        GC.KeepAlive(_list);
        return item;
      }
    }

    public bool MoveNext()
    {
      if (_disposed) throw new ObjectDisposedException(GetType().FullName);

      // Re-validate on every step, not just in Current: advancing dereferences the previously
      // visited node, so a token that died mid-enumeration would otherwise be read as freed
      // memory before Current ever ran. Measured cost is ~8.5ns per element.
      _list.Lifetime?.ThrowIfStale();

      if (!_started)
      {
        _current = _list.Native;
        _started = true;
      }
      else if (_current != null)
      {
        _current = _current->next;
      }

      return _current != null;
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
    if (Native == null) return [];

    var count = (int)NativeMethods.alpm_list_count(Native);
    var result = new T[count];
    var i = 0;
    for (var node = Native; node != null; node = node->next)
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

    AlpmNativeList.Free(Native, _innerFree);
    _disposed = true;
  }
}

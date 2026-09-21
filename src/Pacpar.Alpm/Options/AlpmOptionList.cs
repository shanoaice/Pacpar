using System.Collections;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm.Options;

/// <summary>
/// Template for the <see cref="AlpmOptions"/> properties that libalpm exposes as an
/// <c>alpm_list_t</c> and that this library surfaces as an <see cref="ICollection{T}"/>.
/// </summary>
/// <remarks>
/// A subclass supplies only the native calls that differ: the list getter, the adder, the remover
/// and (for non-string elements) how an element is marshalled and compared. The collection
/// contract, the <c>errno</c> check and the allocate/free lifecycle live here once.
/// <para>
/// The variation points are abstract methods rather than function pointers on purpose. Address-of
/// on a <c>[DllImport]</c> method yields a <i>managed</i> pointer to the P/Invoke stub, so it cannot
/// be stored as <c>delegate* unmanaged[Cdecl]</c> (CS8786); obtaining a real native pointer would
/// need one <c>[UnmanagedCallersOnly]</c> thunk per libalpm entry point.
/// </para>
/// </remarks>
internal abstract unsafe class AlpmOptionList<T> : ICollection<T>
{
  private readonly byte* _handle;

  private protected AlpmOptionList(byte* handle)
  {
    _handle = handle;
  }

  /// <summary>The native list getter, for example <c>alpm_option_get_ignorepkgs</c>.</summary>
  private protected abstract _alpm_list_t* GetList(byte* handle);

  /// <summary>The native adder. Returns 0 on success, anything else on failure.</summary>
  private protected abstract int AddNative(byte* handle, byte* item);

  /// <summary>
  /// The native remover, returning libalpm's raw result: <c>1</c> when it removed the entry,
  /// <c>0</c> when it found nothing and <c>-1</c> on error.
  /// </summary>
  private protected abstract int RemoveNative(byte* handle, byte* item);

  /// <summary>Borrows or marshals an item for a native lookup or removal.</summary>
  /// <param name="owned">Whether <see cref="Release"/> must free the returned pointer.</param>
  private protected abstract byte* Acquire(T item, out bool owned);

  /// <summary>Releases a pointer produced by <see cref="Acquire"/>.</summary>
  private protected abstract void Release(byte* item, bool owned);

  /// <summary>Wraps a native list in the borrowed view for <typeparamref name="T"/>.</summary>
  private protected abstract AlpmList<T> View(_alpm_list_t* list);

  /// <summary>
  /// Marshals an item for <see cref="Add"/>. Defaults to <see cref="Acquire"/>; a type that cannot
  /// be handed back to libalpm overrides this to throw instead of passing a null pointer.
  /// </summary>
  private protected virtual byte* AcquireForAdd(T item, out bool owned) => Acquire(item, out owned);

  /// <summary>
  /// Finds an item in the native list. libalpm's string finder returns the stored element, so the
  /// caller compares the result with the needle pointer it just passed in.
  /// </summary>
  private protected virtual byte* FindIn(_alpm_list_t* list, byte* item)
    => NativeMethods.alpm_list_find_str(list, item);

  public bool IsReadOnly => false;

  public int Count => (int)NativeMethods.alpm_list_count(GetList(_handle));

  public AlpmList<T>.Enumerator GetEnumerator() => View(GetList(_handle)).GetEnumerator();

  IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  public void Add(T item)
  {
    var itemPtr = AcquireForAdd(item, out var owned);
    try
    {
      var err = AddNative(_handle, itemPtr);
      if (err != 0) throw ErrorHandler.ToException(NativeMethods.alpm_errno(_handle));
    }
    finally
    {
      Release(itemPtr, owned);
    }
  }

  public bool Contains(T item)
  {
    var itemPtr = Acquire(item, out var owned);
    try
    {
      // A detached item that libalpm knows nothing about is never contained.
      if (itemPtr == null) return false;
      return FindIn(GetList(_handle), itemPtr) == itemPtr;
    }
    finally
    {
      Release(itemPtr, owned);
    }
  }

  public bool Remove(T item)
  {
    var itemPtr = Acquire(item, out var owned);
    try
    {
      // Nothing libalpm knows about, so nothing to remove; this also keeps a null pointer out of
      // alpm_option_remove_*, which does not accept one.
      if (itemPtr == null) return false;

      // alpm_option_remove_* answer 1 when they removed the entry, 0 when they found nothing and
      // -1 on error, while their header documents "0 on success, -1 on error". Testing the result
      // against 0, as this wrapper used to, therefore made Remove answer with the inverse of the
      // ICollection<T> contract. Only a positive result means the item was removed.
      var err = RemoveNative(_handle, itemPtr);
      if (err < 0) throw ErrorHandler.ToException(NativeMethods.alpm_errno(_handle));
      return err > 0;
    }
    finally
    {
      Release(itemPtr, owned);
    }
  }

  public void Clear()
  {
    // Snapshot first: the enumerator caches the current native node, so removing while
    // enumerating leaves it pointing at a freed node on the next MoveNext().
    foreach (var item in View(GetList(_handle)).ToArray())
    {
      Remove(item);
    }
  }

  public void CopyTo(T[] array, int arrayIndex)
  {
    ArgumentNullException.ThrowIfNull(array);
    ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);

    var source = View(GetList(_handle)).ToArray();
    if (array.Length - arrayIndex < source.Length)
    {
      throw new ArgumentException("The destination array is not long enough to hold all items.", nameof(array));
    }

    for (var i = 0; i < source.Length; ++i)
    {
      array[arrayIndex + i] = source[i];
    }
  }
}

/// <summary>
/// <see cref="AlpmOptionList{T}"/> for the option lists whose elements are C strings.
/// </summary>
internal abstract unsafe class AlpmStringOptionList(byte* handle) : AlpmOptionList<string>(handle)
{
  private protected override byte* Acquire(string item, out bool owned)
  {
    owned = true;
    return NativeString.ToNative(item);
  }

  private protected override void Release(byte* item, bool owned)
  {
    if (owned) Marshal.FreeHGlobal((nint)item);
  }

  private protected override AlpmList<string> View(_alpm_list_t* list) => new AlpmStringList(list);
}

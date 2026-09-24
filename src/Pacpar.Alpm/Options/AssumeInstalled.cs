using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm.Options;

/// <summary>
/// <c>alpm_option_get_assumeinstalled</c> and its add/remove pair, as an
/// <see cref="ICollection{T}"/> of <see cref="Depend"/> snapshots.
/// </summary>
/// <remarks>
/// Unlike the string option lists, libalpm stores its <i>own</i> copy of a dependency: a probe that
/// added a hand-built struct and then searched the list with <c>alpm_list_find_ptr</c> found a copy
/// rather than the pointer it passed in. Both directions of the hand-back therefore need care, and
/// they are not the same operation:
/// <list type="bullet">
/// <item><description>
/// <b>Add</b> materialises the snapshot (<see cref="Depend.ToNative"/>) and lets libalpm copy it.
/// The struct carries no <c>name_hash</c>; libalpm recomputes that field for the copy it stores.
/// </description></item>
/// <item><description>
/// <b>Contains</b>/<b>Remove</b> compare by <i>value</i> through <see cref="Depend.Matches"/> and
/// then hand libalpm the element it stored, because its removal predicate compares the
/// <c>name_hash</c> too and a struct this layer built can never satisfy that.
/// </description></item>
/// </list>
/// </remarks>
internal sealed unsafe class AssumeInstalled(byte* handle) : AlpmOptionList<Depend>(handle)
{
  private protected override _alpm_list_t* GetList(byte* h) => NativeMethods.alpm_option_get_assumeinstalled(h);

  private protected override int AddNative(byte* h, byte* item)
    => NativeMethods.alpm_option_add_assumeinstalled(h, (_alpm_depend_t*)item);

  private protected override int RemoveNative(byte* h, byte* item)
    => NativeMethods.alpm_option_remove_assumeinstalled(h, (_alpm_depend_t*)item);

  /// <summary>
  /// Answers with the element libalpm stored that has the same value as <paramref name="item"/>, or
  /// <c>null</c> when the list holds no such dependency.
  /// </summary>
  /// <remarks>
  /// The lookup is a managed comparison on purpose. libalpm's removal predicate
  /// (<c>alpm_option_remove_assumeinstalled</c>) compares name, version and modifier <i>and</i> the
  /// internal <c>name_hash</c>; a struct built here has no hash libalpm recognises, and the stored
  /// copy's hash cannot be recomputed from the snapshot - libalpm's hash function is private and not
  /// exported. Handing the stored element back keeps both operations agreeing with each other and
  /// with libalpm: the element passed to the native remover is the one it would have matched.
  /// </remarks>
  private protected override byte* Acquire(Depend item, out bool owned)
  {
    owned = false;

    for (var node = NativeList; node != null; node = NativeMethods.alpm_list_next(node))
    {
      var stored = (_alpm_depend_t*)node->data;
      if (new Depend(stored).Matches(item)) return (byte*)stored;
    }

    return null;
  }

  /// <summary>
  /// Materialises the snapshot for <c>Add</c>. libalpm copies the dependency into the list, so the
  /// temporary struct is released again as soon as the call returns.
  /// </summary>
  private protected override byte* AcquireForAdd(Depend item, out bool owned)
  {
    owned = true;
    return (byte*)item.ToNative();
  }

  private protected override void Release(byte* item, bool owned)
  {
    if (owned) Depend.FreeNative((_alpm_depend_t*)item);
  }

  private protected override AlpmList<Depend> View(_alpm_list_t* list) => Depend.ListFactory(list);

  /// <summary>
  /// Identity search: <see cref="Acquire"/> already resolved the snapshot to the stored element, and
  /// returning that same pointer is what makes <see cref="AlpmOptionList{T}.Contains"/> answer
  /// <c>true</c>.
  /// </summary>
  private protected override byte* FindIn(_alpm_list_t* list, byte* item)
    => (byte*)NativeMethods.alpm_list_find_ptr(list, item);
}

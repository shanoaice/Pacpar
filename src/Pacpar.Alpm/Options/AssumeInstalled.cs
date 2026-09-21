using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm.Options;

internal sealed unsafe class AssumeInstalled(byte* handle) : AlpmOptionList<Depend>(handle)
{
  private protected override _alpm_list_t* GetList(byte* h) => NativeMethods.alpm_option_get_assumeinstalled(h);

  private protected override int AddNative(byte* h, byte* item)
    => NativeMethods.alpm_option_add_assumeinstalled(h, (_alpm_depend_t*)item);

  private protected override int RemoveNative(byte* h, byte* item)
    => NativeMethods.alpm_option_remove_assumeinstalled(h, (_alpm_depend_t*)item);

  // The dependency struct is borrowed: libalpm dups whatever it stores, so nothing is released.
  private protected override byte* Acquire(Depend item, out bool owned)
  {
    owned = false;
    return (byte*)item.BackingStruct;
  }

  private protected override void Release(byte* item, bool owned)
  {
  }

  // A detached snapshot (depmissing) has no native struct, so Add must keep throwing for it.
  private protected override byte* AcquireForAdd(Depend item, out bool owned)
  {
    owned = false;
    return (byte*)item.NativePtrOrThrow(nameof(item));
  }

  private protected override AlpmList<Depend> View(_alpm_list_t* list) => Depend.ListFactory(list);

  // Dependencies are compared by identity, not by string contents.
  private protected override byte* FindIn(_alpm_list_t* list, byte* item)
    => (byte*)NativeMethods.alpm_list_find_ptr(list, item);
}

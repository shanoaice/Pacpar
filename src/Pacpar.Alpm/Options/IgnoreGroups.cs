using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm.Options;

internal sealed unsafe class IgnoreGroups(byte* handle) : AlpmStringOptionList(handle)
{
  private protected override _alpm_list_t* GetList(byte* h) => NativeMethods.alpm_option_get_ignoregroups(h);

  private protected override int AddNative(byte* h, byte* item) => NativeMethods.alpm_option_add_ignoregroup(h, item);

  private protected override int RemoveNative(byte* h, byte* item)
    => NativeMethods.alpm_option_remove_ignoregroup(h, item);
}

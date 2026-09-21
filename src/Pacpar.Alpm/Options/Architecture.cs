using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm.Options;

internal sealed unsafe class Architecture(byte* handle) : AlpmStringOptionList(handle)
{
  private protected override _alpm_list_t* GetList(byte* h) => NativeMethods.alpm_option_get_architectures(h);

  private protected override int AddNative(byte* h, byte* item) => NativeMethods.alpm_option_add_architecture(h, item);

  private protected override bool RemoveNative(byte* h, byte* item)
    => NativeMethods.alpm_option_remove_architecture(h, item) == 0;
}

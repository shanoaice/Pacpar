using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm.Options;

internal sealed unsafe class OverwritableFiles(byte* handle) : AlpmStringOptionList(handle)
{
  private protected override _alpm_list_t* GetList(byte* h) => NativeMethods.alpm_option_get_overwrite_files(h);

  private protected override int AddNative(byte* h, byte* item) => NativeMethods.alpm_option_add_overwrite_file(h, item);

  private protected override bool RemoveNative(byte* h, byte* item)
    => NativeMethods.alpm_option_remove_overwrite_file(h, item) == 0;
}

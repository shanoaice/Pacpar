using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm.Options;

internal sealed unsafe class OverwritableFiles(SafeAlpmHandle handle, Lifetime lifetime)
  : AlpmStringOptionList(handle, lifetime)
{
  private protected override _alpm_list_t* GetList(SafeAlpmHandle h) => NativeMethods.alpm_option_get_overwrite_files(h);

  private protected override int AddNative(SafeAlpmHandle h, byte* item) => NativeMethods.alpm_option_add_overwrite_file(h, item);

  private protected override int RemoveNative(SafeAlpmHandle h, byte* item)
    => NativeMethods.alpm_option_remove_overwrite_file(h, item);
}

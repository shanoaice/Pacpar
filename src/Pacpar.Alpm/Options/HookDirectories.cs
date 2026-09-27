using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm.Options;

internal sealed unsafe class HookDirectories(SafeAlpmHandle handle, Lifetime lifetime)
  : AlpmStringOptionList(handle, lifetime)
{
  private protected override _alpm_list_t* GetList(SafeAlpmHandle h) => NativeMethods.alpm_option_get_hookdirs(h);

  private protected override int AddNative(SafeAlpmHandle h, byte* item) => NativeMethods.alpm_option_add_hookdir(h, item);

  private protected override int RemoveNative(SafeAlpmHandle h, byte* item)
    => NativeMethods.alpm_option_remove_hookdir(h, item);
}

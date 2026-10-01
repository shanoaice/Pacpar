using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm.Options;

internal sealed unsafe class NoExtract(SafeAlpmHandle handle, Lifetime lifetime)
  : AlpmStringOptionList(handle, lifetime)
{
  private protected override _alpm_list_t* GetList(SafeAlpmHandle h) => NativeMethods.alpm_option_get_noextracts(h);

  private protected override int AddNative(SafeAlpmHandle h, byte* item) => NativeMethods.alpm_option_add_noextract(h, item);

  private protected override int RemoveNative(SafeAlpmHandle h, byte* item)
    => NativeMethods.alpm_option_remove_noextract(h, item);
}

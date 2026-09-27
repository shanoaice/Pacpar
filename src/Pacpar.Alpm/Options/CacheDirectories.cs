using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm.Options;

internal sealed unsafe class CacheDirectories(SafeAlpmHandle handle, Lifetime lifetime)
  : AlpmStringOptionList(handle, lifetime)
{
  private protected override _alpm_list_t* GetList(SafeAlpmHandle h) => NativeMethods.alpm_option_get_cachedirs(h);

  private protected override int AddNative(SafeAlpmHandle h, byte* item) => NativeMethods.alpm_option_add_cachedir(h, item);

  private protected override int RemoveNative(SafeAlpmHandle h, byte* item)
    => NativeMethods.alpm_option_remove_cachedir(h, item);
}

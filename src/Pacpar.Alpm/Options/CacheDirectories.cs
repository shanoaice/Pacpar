using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm.Options;

internal sealed unsafe class CacheDirectories(_alpm_handle_t* handle) : AlpmStringOptionList(handle)
{
  private protected override _alpm_list_t* GetList(_alpm_handle_t* h) => NativeMethods.alpm_option_get_cachedirs(h);

  private protected override int AddNative(_alpm_handle_t* h, byte* item) => NativeMethods.alpm_option_add_cachedir(h, item);

  private protected override int RemoveNative(_alpm_handle_t* h, byte* item)
    => NativeMethods.alpm_option_remove_cachedir(h, item);
}

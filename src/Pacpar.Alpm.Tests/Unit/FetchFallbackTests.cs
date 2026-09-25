using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit;

public sealed class FetchFallbackTests
{
  [Fact]
  public unsafe void FetchPackageUrl_WithoutFetchHandler_UsesInternalDownloader_FailsWithRetrieveFailedEvent()
  {
    using var env = new IsolatedAlpmEnvironment();
    var handle = (_alpm_handle_t*)env.Alpm.AsHandle();

    var events = new List<EventType>();
    env.Alpm.Callback.EventHandler = events.Add;

    var urlPtr = NativeString.ToNative("http://127.0.0.1:1/x-1.0-1-x86_64.pkg.tar.zst");
    _alpm_list_t* urls = null;
    urls = NativeMethods.alpm_list_add(urls, (void*)urlPtr);
    _alpm_list_t* fetched = null;

    try
    {
      var ret = NativeMethods.alpm_fetch_pkgurl(handle, urls, &fetched);
      Assert.Equal(-1, ret);

      // With libalpm's internal downloader, a network failure emits PackageRetrieveFailed.
      // With phantom registration (shim claiming Success), libalpm emits PackageRetrieveDone!
      Assert.Contains(events, e => e is EventType.PackageRetrieveFailed);
      Assert.DoesNotContain(events, e => e is EventType.PackageRetrieveDone);
    }
    finally
    {
      NativeMethods.alpm_list_free(urls);
      Marshal.FreeHGlobal((nint)urlPtr);
      if (fetched != null)
      {
        NativeMethods.alpm_list_free_inner(fetched, &FreeData);
        NativeMethods.alpm_list_free(fetched);
      }
    }
  }

  [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
  private static unsafe void FreeData(void* data)
  {
    Marshal.FreeHGlobal((nint)data);
  }
}

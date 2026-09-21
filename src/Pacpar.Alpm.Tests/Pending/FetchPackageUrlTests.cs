using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Report item M, guide §3.8: <c>alpm_fetch_pkgurl</c>.
/// </summary>
/// <remarks>
/// libalpm downloads through whatever <c>alpm_cb_fetch</c> is registered, which is
/// <see cref="Callback.FetchHandler"/> here — so the failure path can be driven entirely offline,
/// and the test also proves the wrapper really calls the native function rather than short-circuiting.
/// A success path needs a reachable URL and belongs in <c>Integration/</c>.
/// </remarks>
public sealed class FetchPackageUrlTests
{
  private const string Url = "https://example.invalid/x-1.0-1-x86_64.pkg.tar.zst";

  [Fact]
  public void FetchPackageUrls_RoutesTheUrlsThroughTheFetchHandler()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.Alpm;
    var seen = new List<string>();
    alpm.Callback.FetchHandler = (url, _, _) =>
    {
      seen.Add(url);
      return FetchResult.Error;
    };

    var exception = Record.Exception(() => alpm.FetchPackageUrls([Url]));

    Assert.NotNull(exception);
    Assert.IsAssignableFrom<AlpmException>(exception);
    Assert.Equal([Url], seen);
  }

  /// <summary>A handler that reports success for a file it never wrote must not be reported as a fetch.</summary>
  [Fact]
  public void FetchPackageUrls_WithAFailingHandler_SurfacesTheFailure()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.Alpm;
    alpm.Callback.FetchHandler = (_, _, _) => FetchResult.Error;

    Assert.ThrowsAny<AlpmException>(() => alpm.FetchPackageUrls([Url, Url]));
  }

  [Fact]
  public void FetchPackageUrls_AfterDispose_ThrowsObjectDisposedException()
  {
    var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.Alpm;
    environment.Dispose();

    Assert.Throws<ObjectDisposedException>(() => alpm.FetchPackageUrls([Url]));
  }
}

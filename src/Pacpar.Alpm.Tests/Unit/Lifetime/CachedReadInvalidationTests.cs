using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// A cached read must still report that its owner is gone.
/// </summary>
/// <remarks>
/// Every cached member of <see cref="PackageBase"/> answers out of its cache from the second read
/// on, and a cache hit never evaluates the guarded pointer accessor - so each of those members has
/// to check the stamp itself. This class warms every cache first, because the missing guard only
/// shows on the read that comes <i>out of</i> the cache: the first read still crosses the interop
/// boundary, and the guarded accessor throws for it whether or not the member checks anything.
/// </remarks>
public sealed class CachedReadInvalidationTests : IDisposable
{
  private readonly IsolatedAlpmEnvironment _environment = new();

  private readonly string _packageDirectory =
    Path.Combine(Path.GetTempPath(), "pacpar-cached-read", Guid.NewGuid().ToString("n"));

  public CachedReadInvalidationTests() => Directory.CreateDirectory(_packageDirectory);

  public void Dispose()
  {
    _environment.Dispose();

    if (Directory.Exists(_packageDirectory))
    {
      Directory.Delete(_packageDirectory, recursive: true);
    }
  }

  private void Install(string name)
  {
    using var transaction = _environment.Alpm.BeginTransaction();
    transaction.AddPackage(_environment.Alpm.LoadPackage(
      PackageArchive.Create(_packageDirectory, name), full: true, SigLevel.AlpmSigUseDefault));
    transaction.Prepare();
    transaction.Commit();
  }

  [Fact]
  public void EveryCachedMember_ThrowsOnceTheSessionIsGone()
  {
    Install("cached-read-pkg");
    var pkg = _environment.Alpm.GetLocalDatabase().GetPackage("cached-read-pkg")!;

    Assert.Equal("cached-read-pkg", pkg.Name);
    _ = pkg.Filename;
    _ = pkg.Base;
    _ = pkg.Description;
    _ = pkg.Url;
    _ = pkg.Packager;
    _ = pkg.Md5Sum;
    _ = pkg.Sha256Sum;
    _ = pkg.Arch;
    _ = pkg.Base64Signature;

    _environment.Alpm.Dispose();

    Assert.Throws<AlpmLifetimeException>(() => pkg.Name);
    Assert.Throws<AlpmLifetimeException>(() => pkg.Filename);
    Assert.Throws<AlpmLifetimeException>(() => pkg.Base);
    Assert.Throws<AlpmLifetimeException>(() => pkg.Description);
    Assert.Throws<AlpmLifetimeException>(() => pkg.Url);
    Assert.Throws<AlpmLifetimeException>(() => pkg.Packager);
    Assert.Throws<AlpmLifetimeException>(() => pkg.Md5Sum);
    Assert.Throws<AlpmLifetimeException>(() => pkg.Sha256Sum);
    Assert.Throws<AlpmLifetimeException>(() => pkg.Arch);
    Assert.Throws<AlpmLifetimeException>(() => pkg.Base64Signature);
  }
}

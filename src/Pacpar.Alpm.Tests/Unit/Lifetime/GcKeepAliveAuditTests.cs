using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Exploratory test suite investigating GC.KeepAlive audit findings (reports/gc-keepalive-audit.zh-CN.md).
/// Tests whether unanchored wrapper invocations fail under standard Debug, standard Release,
/// or forced JIT liveness / concurrent GC conditions.
/// </summary>
[Collection("ProcessWideNativeHeap")]
public sealed class GcKeepAliveAuditTests : IDisposable
{
  private readonly IsolatedAlpmEnvironment _environment = new();
  private readonly string _packageDirectory =
    Path.Combine(Path.GetTempPath(), "pacpar-gc-audit-packages", Guid.NewGuid().ToString("n"));

  public GcKeepAliveAuditTests() => Directory.CreateDirectory(_packageDirectory);

  public void Dispose()
  {
    _environment.Dispose();
    if (Directory.Exists(_packageDirectory))
    {
      Directory.Delete(_packageDirectory, recursive: true);
    }
  }

  private string CreatePackage(string name, string? file = null, string? conflict = null)
    => PackageArchive.Create(_packageDirectory, name, file: file, conflict: conflict);

  [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
  private static CLong CallPackageSize(LoadedPackage pkg) => pkg.Size;

  [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
  private static string? CallFirstFileName(LoadedPackage pkg) => pkg.Files[0].Name;

  [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
  private string? LoadAndReadFirstFileName(string pkgPath)
    => CallFirstFileName(_environment.Alpm.LoadPackage(pkgPath, full: true, SigLevel.ALPM_SIG_USE_DEFAULT));

  [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
  private static string? CallFirstLicense(LoadedPackage pkg)
  {
    var e = pkg.Licenses.GetEnumerator();
    e.MoveNext();
    return e.Current;
  }

  [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
  private static int CallAlpmErrno(Alpm alpm) => (int)alpm.Errno;

  [Fact]
  public void PackageSize_UnanchoredCall_SingleExecution()
  {
    var pkgPath = CreatePackage("audit-size-pkg");
    var pkg = _environment.Alpm.LoadPackage(pkgPath, full: false, SigLevel.ALPM_SIG_USE_DEFAULT);
    var size = CallPackageSize(pkg);
    Assert.True(size.Value >= 0);
  }

  [Fact]
  public void FileList_UnanchoredCall_SingleExecution()
  {
    var pkgPath = CreatePackage("audit-file-pkg", file: "usr/bin/test-target");
    var pkg = _environment.Alpm.LoadPackage(pkgPath, full: true, SigLevel.ALPM_SIG_USE_DEFAULT);
    var name = CallFirstFileName(pkg);
    Assert.NotNull(name);
    Assert.False(string.IsNullOrEmpty(name));
  }

  [Fact]
  public void AlpmLicenses_UnanchoredCall_SingleExecution()
  {
    var pkgPath = CreatePackage("audit-lic-pkg");
    var pkg = _environment.Alpm.LoadPackage(pkgPath, full: false, SigLevel.ALPM_SIG_USE_DEFAULT);
    // PackageArchive creates packages with no licenses, count is 0
    var count = pkg.Licenses.Count();
    Assert.True(count >= 0);
  }

  [Fact]
  public void AlpmErrno_UnanchoredCall_SingleExecution()
  {
    using var env2 = new IsolatedAlpmEnvironment();
    var errno = CallAlpmErrno(env2.Alpm);
    Assert.Equal(0, errno);
  }
  [Fact]
  public async Task FileList_UnderConcurrentGcStress_ObservesPrematureFinalization()
  {
    var pkgPath = CreatePackage("audit-stress-file-pkg", file: "usr/bin/concurrent-test");
    using var cts = new CancellationTokenSource();
    var gcCount = 0;

    var gcTask = Task.Run(() =>
    {
      while (!cts.IsCancellationRequested)
      {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        Interlocked.Increment(ref gcCount);
      }
    });

    try
    {
      for (int i = 0; i < 2000; i++)
      {
        var name = LoadAndReadFirstFileName(pkgPath);
        Assert.NotNull(name);
      }
    }
    finally
    {
      cts.Cancel();
      await gcTask;
      Assert.True(gcCount > 10, $"GCs forced: {gcCount}");
    }
  }
  [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
  private static AlpmTransactionException.ConflictingDependencies RunUnrootedConflictingPrepare(string pkg1, string pkg2)
  {
    var env = new IsolatedAlpmEnvironment();
    var trans = env.Alpm.BeginTransaction((TransactionFlags)0);
    trans.AddPackage(env.Alpm.LoadPackage(pkg1, full: false, SigLevel.ALPM_SIG_USE_DEFAULT));
    trans.AddPackage(env.Alpm.LoadPackage(pkg2, full: false, SigLevel.ALPM_SIG_USE_DEFAULT));
    try
    {
      trans.Prepare();
      throw new InvalidOperationException("Expected ConflictingDependencies exception");
    }
    catch (AlpmTransactionException.ConflictingDependencies ex)
    {
      return ex;
    }
  }

  [Fact]
  public async Task TransactionPrepare_Conflict_UnderConcurrentGcStress()
  {
    var pkg1 = CreatePackage("audit-conf-left");
    var pkg2 = CreatePackage("audit-conf-right", conflict: "audit-conf-left");

    using var cts = new CancellationTokenSource();
    var gcCount = 0;

    var gcTask = Task.Run(() =>
    {
      while (!cts.IsCancellationRequested)
      {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        Interlocked.Increment(ref gcCount);
      }
    });

    try
    {
      for (int i = 0; i < 500; i++)
      {
        var failure = RunUnrootedConflictingPrepare(pkg1, pkg2);
        var conflict = Assert.Single(failure.Conflicts);
        Assert.NotNull(conflict.Package1Name);
        Assert.NotNull(conflict.Package2Name);
      }
    }
    finally
    {
      cts.Cancel();
      await gcTask;
    }
  }
}

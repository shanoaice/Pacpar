using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Covers the ownership hand-over of <see cref="Transactions.AddPackage(LoadedPackage)"/> (report item
/// F2): libalpm frees a file-loaded package when the transaction is released, so the wrapper must stop
/// owning it at that moment - otherwise the natural <c>using</c> pattern aborts the process with a
/// double free. The two <c>AddPackage</c> overloads cannot be confused, because
/// <see cref="Package"/> and <see cref="LoadedPackage"/> do not convert to one another.
/// </summary>
/// <remarks>
/// Reaching the end of <see cref="AddPackage_TransfersOwnership_SoBothDisposalsAreSafe"/> is half of the
/// test: the old wrapper freed the package a second time there and died with SIGABRT (probe
/// <c>.dsh-scratch/audit-probe/own.c</c>, variants c and d).
/// </remarks>
public sealed class PackageTransferTests : IDisposable
{
  /// <summary>No flags: the tests only care about ownership, but the transaction must still be sound.</summary>
  private const TransactionFlags NoFlags = (TransactionFlags)0;

  private readonly IsolatedAlpmEnvironment _environment = new();

  private readonly string _packageDirectory =
    Path.Combine(Path.GetTempPath(), "pacpar-transfer-packages", Guid.NewGuid().ToString("n"));

  public PackageTransferTests() => Directory.CreateDirectory(_packageDirectory);

  public void Dispose()
  {
    _environment.Dispose();

    if (Directory.Exists(_packageDirectory))
    {
      Directory.Delete(_packageDirectory, recursive: true);
    }
  }

  private LoadedPackage Load(string name, string? file = null)
    => _environment.Alpm.LoadPackage(PackageArchive.Create(_packageDirectory, name, file: file),
      full: true, SigLevel.ALPM_SIG_USE_DEFAULT);

  /// <summary>
  /// The shape that used to abort. Both <c>using</c> declarations run their <c>Dispose</c> at the end
  /// of the method - the transaction first (which frees the package), then the wrapper (which must not
  /// touch it again).
  /// </summary>
  [Fact]
  public void AddPackage_TransfersOwnership_SoBothDisposalsAreSafe()
  {
    using var package = Load("audit-transfer", file: "usr/bin/probe-file");
    using var transaction = _environment.Alpm.BeginTransaction(NoFlags);

    var view = transaction.AddPackage(package);

    Assert.Equal("audit-transfer", view.Name);

    transaction.Prepare();
    transaction.Commit();
  }

  /// <summary>
  /// Handing the package over gives the caller a borrowed view back: it replaces the wrapper whose
  /// ownership was given up, and it does not own anything itself.
  /// </summary>
  [Fact]
  public void AddPackage_ReturnsABorrowedView()
  {
    var package = Load("audit-view");
    using var transaction = _environment.Alpm.BeginTransaction(NoFlags);

    var view = transaction.AddPackage(package);

    Assert.IsType<Package>(view);
    Assert.Equal("audit-view", view.Name);
    Assert.False(view is LoadedPackage, "the view must not be an owning type");
  }

  /// <summary>
  /// After the hand-over the wrapper is inert: reads throw (the pointer stays valid only until the
  /// transaction is released, which the wrapper cannot observe) and <c>Dispose</c> does nothing.
  /// </summary>
  [Fact]
  public void AddPackage_RetiresTheWrapperThatWasHandedOver()
  {
    var package = Load("audit-retired");
    using var transaction = _environment.Alpm.BeginTransaction(NoFlags);

    transaction.AddPackage(package);

    Assert.False(package.OwnsPackage);
    Assert.Throws<ObjectDisposedException>(() => package.Name);

    // Must not throw, and must not release the package the transaction owns.
    package.Dispose();

    Assert.Throws<ObjectDisposedException>(() => package.Origin);
  }

  /// <summary>
  /// A second hand-over is a caller error, and it is rejected before libalpm is touched. libalpm would
  /// not act on it either - it deduplicates the same package pointer in the transaction's list
  /// (probed: <c>alpm_add_pkg</c> twice leaves exactly one entry).
  /// </summary>
  [Fact]
  public void AddPackage_OfAnAlreadyHandedOverPackage_Throws()
  {
    var package = Load("audit-twice");
    using var transaction = _environment.Alpm.BeginTransaction(NoFlags);
    transaction.AddPackage(package);

    Assert.Throws<ObjectDisposedException>(() => transaction.AddPackage(package));

    Assert.Single(transaction.GetAddedPackages());
  }

  /// <summary>
  /// The borrowing overload is the one for a package libalpm owns. Note what libalpm does with a
  /// package that is *already installed*: it refuses it with <c>ALPM_ERR_WRONG_ARGS</c> - adding one is
  /// only meaningful for a sync-database package - and the exception carries the very package the call
  /// was operating on.
  /// </summary>
  /// <remarks>
  /// This is also the deterministic way to reach <see cref="AlpmPackageException"/>'s package payload:
  /// libalpm offers no easier failing <c>alpm_add_pkg</c> (the same package twice is deduplicated, and
  /// an ignored package is still accepted - probed).
  /// </remarks>
  [Fact]
  public void AddPackage_OfAnInstalledPackage_IsRefusedWithThatPackageInThePayload()
  {
    var path = PackageArchive.Create(_packageDirectory, "audit-installed", file: "usr/bin/probe-file");

    using (var installer = _environment.Alpm.BeginTransaction(NoFlags))
    {
      installer.AddPackage(_environment.Alpm.LoadPackage(path, full: true, SigLevel.ALPM_SIG_USE_DEFAULT));
      installer.Prepare();
      installer.Commit();
    }

    var installed = _environment.Alpm.GetLocalDatabase().GetPackage("audit-installed");
    Assert.NotNull(installed);

    using var transaction = _environment.Alpm.BeginTransaction(NoFlags);

    var failure = Assert.Throws<AlpmPackageException>(() => transaction.AddPackage(installed));

    Assert.Equal(_alpm_errno_t.ALPM_ERR_WRONG_ARGS, failure.Errno);
    Assert.Same(installed, failure.Package);
    Assert.Empty(transaction.GetAddedPackages());

    // The transaction borrowed nothing and freed nothing: the package still belongs to the local db.
    Assert.Equal("audit-installed", installed.Name);
  }
}

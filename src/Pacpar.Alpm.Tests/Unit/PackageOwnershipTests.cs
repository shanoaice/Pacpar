namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Report item J: ownership is expressed by the type, not by a <c>bool</c> argument.
/// </summary>
/// <remarks>
/// <see cref="Package"/> used to take <c>bool fromDatabase</c> and expose it as a public field, with
/// the disposal decision read from it at run time. The split now lives in the type system, mirroring
/// what <c>AlpmList&lt;T&gt;</c> (borrowed) versus its owned counterpart already do: a
/// <see cref="Package"/> is libalpm's and is not disposable, while a <see cref="LoadedPackage"/> owns
/// what it holds.
/// <para>
/// The two are <b>siblings</b> under <see cref="PackageBase"/> rather than one deriving from the
/// other, so that <c>AddPackage(Package)</c> (borrow) and <c>AddPackage(LoadedPackage)</c> (hand
/// over) cannot be reached with the wrong kind: neither type converts to the other, and the compiler
/// picks the overload. This test locks that relationship in.
/// </para>
/// </remarks>
public sealed class PackageOwnershipTests
{
  [Fact]
  public void BorrowedPackagesAreNotDisposable_LoadedOnesAre()
  {
    Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(Package)),
      "a database package is owned by libalpm; the type must not invite Dispose()");
    Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(LoadedPackage)));
  }

  /// <summary>
  /// No conversion between the two kinds, in either direction: that is what makes the
  /// <see cref="Transactions.AddPackage(Package)"/> / <see cref="Transactions.AddPackage(LoadedPackage)"/>
  /// pair safe without a run-time check.
  /// </summary>
  [Fact]
  public void TheTwoPackageKindsAreSiblings()
  {
    Assert.False(typeof(LoadedPackage).IsSubclassOf(typeof(Package)),
      "a LoadedPackage must not be acceptable where a libalpm-owned Package is expected");
    Assert.False(typeof(Package).IsSubclassOf(typeof(LoadedPackage)));

    Assert.Equal(typeof(PackageBase), typeof(Package).BaseType);
    Assert.Equal(typeof(PackageBase), typeof(LoadedPackage).BaseType);
    Assert.True(typeof(PackageBase).IsAbstract,
      "the base is a read-only abstraction; packages are one of the two concrete kinds");
  }

  [Fact]
  public void LoadPackageReturnsTheOwningType()
    => Assert.Equal(typeof(LoadedPackage),
      typeof(Alpm).GetMethod(nameof(Alpm.LoadPackage))!.ReturnType);

  /// <summary>
  /// Handing a package over returns the borrowed view the caller can keep reading, and the two
  /// <c>AddPackage</c> overloads are distinguishable by parameter type alone.
  /// </summary>
  [Fact]
  public void AddPackageOverloadsDifferInOwnership()
  {
    var borrowed = typeof(Transactions).GetMethod(nameof(Transactions.AddPackage), [typeof(Package)]);
    var owning = typeof(Transactions).GetMethod(nameof(Transactions.AddPackage), [typeof(LoadedPackage)]);

    Assert.NotNull(borrowed);
    Assert.NotNull(owning);

    Assert.Equal(typeof(void), borrowed.ReturnType);
    Assert.Equal(typeof(Package), owning.ReturnType);
  }
}

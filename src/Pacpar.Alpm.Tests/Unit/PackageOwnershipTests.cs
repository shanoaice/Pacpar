namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Report item J: ownership is expressed by the type, not by a <c>bool</c> argument.
/// </summary>
/// <remarks>
/// <see cref="Package"/> used to take <c>bool fromDatabase</c> and expose it as a public field,
/// with the disposal decision read from it at run time. The same split now exists in the type
/// system, mirroring what <c>AlpmList&lt;T&gt;</c> (borrowed) versus its owned counterpart already
/// do: a <see cref="Package"/> is libalpm's and is not disposable, while a
/// <see cref="LoadedPackage"/> owns what it holds.
/// </remarks>
public sealed class PackageOwnershipTests
{
  [Fact]
  public void BorrowedPackagesAreNotDisposable_LoadedOnesAre()
  {
    Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(Package)),
      "a database package is owned by libalpm; the type must not invite Dispose()");
    Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(LoadedPackage)));
    Assert.True(typeof(LoadedPackage).IsSubclassOf(typeof(Package)));
  }

  [Fact]
  public void LoadPackageReturnsTheOwningType()
    => Assert.Equal(typeof(LoadedPackage),
      typeof(Alpm).GetMethod(nameof(Alpm.LoadPackage))!.ReturnType);
}

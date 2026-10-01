using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm.Tests.Unit.Snapshots;

/// <summary>
/// Report §E-8: the public surface must not expose <c>Pacpar.Alpm.Bindings</c> enums (or raw
/// pointers) for data this library already models. Every managed enum mirrors its native
/// counterpart value-for-value, so a libalpm update that adds a member turns these tests red
/// instead of silently dropping the new value.
/// </summary>
public sealed class ManagedEnumsTests
{
  [Theory]
  [InlineData(typeof(_alpm_pkgfrom_t), typeof(PackageOrigin))]
  [InlineData(typeof(_alpm_pkgreason_t), typeof(PackageReason))]
  [InlineData(typeof(_alpm_depmod_t), typeof(DepMod))]
  [InlineData(typeof(_alpm_hook_when_t), typeof(HookWhen))]
  [InlineData(typeof(_alpm_package_operation_t), typeof(PackageOperation))]
  [InlineData(typeof(_alpm_progress_t), typeof(ProgressType))]
  [InlineData(typeof(_alpm_loglevel_t), typeof(LogLevel))]
  public void ManagedEnum_MatchesTheNativeEnum(Type nativeType, Type managedType)
    => AssertMirrors(nativeType, managedType);
  [Fact]
  public void PublicMembersUseTheManagedTypes()
  {
    Assert.Equal(typeof(PackageOrigin), typeof(PackageView).GetProperty(nameof(PackageView.Origin))!.PropertyType);
    Assert.Equal(typeof(PackageReason), typeof(PackageView).GetProperty(nameof(PackageView.Reason))!.PropertyType);
    Assert.Equal(typeof(DepMod), typeof(Depend).GetProperty(nameof(Depend.Depmod))!.PropertyType);
    Assert.Equal(typeof(HookWhen),
      typeof(AlpmEvent.HookStart).GetProperty(nameof(AlpmEvent.HookStart.When))!.PropertyType);
    Assert.Equal(typeof(PackageOperation),
      typeof(AlpmEvent.PackageOperationStart).GetProperty(nameof(AlpmEvent.PackageOperationStart.Operation))!.PropertyType);
    Assert.Equal(typeof(ProgressType),
      typeof(Callback).GetProperty(nameof(Callback.ProgressHandler))!.PropertyType.GenericTypeArguments[0]);
    Assert.Equal(typeof(LogLevel),
      typeof(Callback).GetProperty(nameof(Callback.LogHandler))!.PropertyType.GenericTypeArguments[0]);
    Assert.Equal(typeof(FetchResult),
      typeof(Callback).GetProperty(nameof(Callback.FetchHandler))!.PropertyType.GenericTypeArguments[^1]);

    Assert.Equal(typeof(bool),
      typeof(AlpmQuestion.InstallIgnoredPackage).GetProperty(nameof(AlpmQuestion.InstallIgnoredPackage.Install))!.PropertyType);
    Assert.Equal(typeof(PackageSnapshot),
      typeof(AlpmQuestion.InstallIgnoredPackage).GetProperty(nameof(AlpmQuestion.InstallIgnoredPackage.Package))!.PropertyType);
    Assert.Equal(typeof(PackageSnapshot),
      typeof(AlpmQuestion.ReplacePackage).GetProperty(nameof(AlpmQuestion.ReplacePackage.OldPackage))!.PropertyType);
    Assert.Equal(typeof(PackageSnapshot),
      typeof(AlpmQuestion.ReplacePackage).GetProperty(nameof(AlpmQuestion.ReplacePackage.NewPackage))!.PropertyType);
    Assert.Equal(typeof(PackageSnapshot),
      typeof(AlpmQuestion.ConflictPkg).GetProperty(nameof(AlpmQuestion.ConflictPkg.Package1))!.PropertyType);
    Assert.Equal(typeof(PackageSnapshot),
      typeof(AlpmQuestion.ConflictPkg).GetProperty(nameof(AlpmQuestion.ConflictPkg.Package2))!.PropertyType);
    // A callback payload is a snapshot, so the packages inside it - and the member lists carrying them -
    // are copied rather than viewed: libalpm's question union and the list hanging off it are built on
    // the calling thread's stack, and the packages they point at die with their database or transaction.
    Assert.Equal(typeof(IReadOnlyList<PackageSnapshot>),
      typeof(AlpmQuestion.RemovePkgs).GetProperty(nameof(AlpmQuestion.RemovePkgs.Packages))!.PropertyType);
    Assert.Equal(typeof(IReadOnlyList<PackageSnapshot>),
      typeof(AlpmQuestion.SelectProvider).GetProperty(nameof(AlpmQuestion.SelectProvider.Providers))!.PropertyType);
    Assert.Equal(typeof(int),
      typeof(AlpmQuestion.SelectProvider).GetProperty(nameof(AlpmQuestion.SelectProvider.UseIndex))!.PropertyType);
  }

  /// <summary>Both enums must cover exactly the same numeric values.</summary>
  private static void AssertMirrors(Type nativeType, Type managedType)
  {
    foreach (var native in Enum.GetValues(nativeType))
    {
      var value = Convert.ToUInt32(native);
      var managed = Enum.ToObject(managedType, value);

      Assert.True(Enum.IsDefined(managedType, managed), $"{managedType.Name} has no member for {native} ({value})");
    }

    foreach (var managed in Enum.GetValues(managedType))
    {
      var value = Convert.ToUInt32(managed);
      var native = Enum.ToObject(nativeType, value);

      Assert.True(Enum.IsDefined(nativeType, native), $"{nativeType.Name} has no member for {managed} ({value})");
    }
  }
}

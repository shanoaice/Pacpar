using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Report §E-8: the public surface must not expose <c>Pacpar.Alpm.Bindings</c> enums (or raw
/// pointers) for data this library already models. Every managed enum mirrors its native
/// counterpart value-for-value, so a libalpm update that adds a member turns these tests red
/// instead of silently dropping the new value.
/// </summary>
public sealed class ManagedEnumsTests
{
  [Fact]
  public void PackageOrigin_MatchesTheNativeEnum() => AssertMirrors<_alpm_pkgfrom_t, PackageOrigin>();

  [Fact]
  public void PackageReason_MatchesTheNativeEnum() => AssertMirrors<_alpm_pkgreason_t, PackageReason>();

  [Fact]
  public void DepMod_MatchesTheNativeEnum() => AssertMirrors<_alpm_depmod_t, DepMod>();

  [Fact]
  public void HookWhen_MatchesTheNativeEnum() => AssertMirrors<_alpm_hook_when_t, HookWhen>();

  [Fact]
  public void PackageOperation_MatchesTheNativeEnum() => AssertMirrors<_alpm_package_operation_t, PackageOperation>();

  [Fact]
  public void ProgressType_MatchesTheNativeEnum() => AssertMirrors<_alpm_progress_t, ProgressType>();

  [Fact]
  public void PublicMembersUseTheManagedTypes()
  {
    Assert.Equal(typeof(PackageOrigin), typeof(Package).GetProperty(nameof(Package.Origin))!.PropertyType);
    Assert.Equal(typeof(PackageReason), typeof(Package).GetProperty(nameof(Package.Reason))!.PropertyType);
    Assert.Equal(typeof(DepMod), typeof(Depend).GetProperty(nameof(Depend.Depmod))!.PropertyType);
    Assert.Equal(typeof(HookWhen),
      typeof(EventType.HookStart).GetProperty(nameof(EventType.HookStart.When))!.PropertyType);
    Assert.Equal(typeof(PackageOperation),
      typeof(EventType.PackageOperationStart).GetProperty(nameof(EventType.PackageOperationStart.Operation))!.PropertyType);
    Assert.Equal(typeof(ProgressType),
      typeof(Callback).GetProperty(nameof(Callback.ProgressHandler))!.PropertyType.GenericTypeArguments[0]);

    Assert.Equal(typeof(bool),
      typeof(QuestionType.InstallIgnoredPackage).GetProperty(nameof(QuestionType.InstallIgnoredPackage.Install))!.PropertyType);
    Assert.Equal(typeof(AlpmList<Package>),
      typeof(QuestionType.RemovePkgs).GetProperty(nameof(QuestionType.RemovePkgs.Packages))!.PropertyType);
  }

  /// <summary>Both enums must cover exactly the same numeric values.</summary>
  private static void AssertMirrors<TNative, TManaged>() where TNative : struct, Enum where TManaged : struct, Enum
  {
    foreach (var native in Enum.GetValues<TNative>())
    {
      var value = Convert.ToUInt32(native);
      var managed = (TManaged)Enum.ToObject(typeof(TManaged), value);

      Assert.True(Enum.IsDefined(managed), $"{typeof(TManaged).Name} has no member for {native} ({value})");
    }

    foreach (var managed in Enum.GetValues<TManaged>())
    {
      var value = Convert.ToUInt32(managed);
      var native = (TNative)Enum.ToObject(typeof(TNative), value);

      Assert.True(Enum.IsDefined(native), $"{typeof(TNative).Name} has no member for {managed} ({value})");
    }
  }
}

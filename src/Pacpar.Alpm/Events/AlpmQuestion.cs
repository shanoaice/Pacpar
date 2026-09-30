using System.Diagnostics.CodeAnalysis;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

/// <summary>
/// A question libalpm asks a callback handler (<c>alpm_question_t</c>).
/// </summary>
/// <remarks>
/// Every question subclass is a managed snapshot whose values are copied from libalpm during callback
/// execution. Instances are safe to persist or inspect after the callback completes.
/// <para>
/// Packages in <see cref="RemovePkgs.Packages"/> and <see cref="SelectProvider.Providers"/>
/// are exposed as <see cref="PackageView"/> instances bound to the lifetime of the parent <see cref="Alpm"/>
/// handle, remaining readable as long as the parent handle is active.
/// </para>
/// </remarks>
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public abstract class AlpmQuestion
{
  internal static unsafe AlpmQuestion FromUnion(_alpm_question_t* backingStruct, Lifetime? lifetime)
  {
    return backingStruct->type_ switch
    {
      _alpm_question_type_t.ALPM_QUESTION_INSTALL_IGNOREPKG => new InstallIgnoredPackage(backingStruct),
      _alpm_question_type_t.ALPM_QUESTION_REPLACE_PKG => new ReplacePackage(backingStruct),
      _alpm_question_type_t.ALPM_QUESTION_CONFLICT_PKG => new ConflictPkg(backingStruct),
      _alpm_question_type_t.ALPM_QUESTION_CORRUPTED_PKG => new CorruptedPkg(backingStruct),
      _alpm_question_type_t.ALPM_QUESTION_REMOVE_PKGS => new RemovePkgs(backingStruct, lifetime),
      _alpm_question_type_t.ALPM_QUESTION_SELECT_PROVIDER => new SelectProvider(backingStruct, lifetime),
      _alpm_question_type_t.ALPM_QUESTION_IMPORT_KEY => new ImportKey(backingStruct),
      _ => throw new ArgumentException($"Unknown question type: {backingStruct->type_}"),
    };
  }

  /// <summary>Question asked when attempting to install a package marked in IgnorePkg.</summary>
  public class InstallIgnoredPackage : AlpmQuestion
  {
    internal unsafe InstallIgnoredPackage(_alpm_question_t* question)
    {
      Install = question->install_ignorepkg.install != 0;
      Package = NativeString.FromNative((nint)question->install_ignorepkg.pkg) ?? "";
    }

    /// <summary>Gets whether to install the ignored package.</summary>
    public bool Install { get; }
    /// <summary>Gets the name of the ignored package.</summary>
    public string Package { get; }
  }

  /// <summary>Question asked when an existing package is to be replaced by another package.</summary>
  public class ReplacePackage : AlpmQuestion
  {
    internal unsafe ReplacePackage(_alpm_question_t* question)
    {
      Replace = question->replace.replace != 0;
      OldPackage = NativeString.FromNative((nint)question->replace.oldpkg) ?? "";
      NewPackage = NativeString.FromNative((nint)question->replace.newpkg) ?? "";
      NewDatabase = NativeString.FromNative((nint)question->replace.newdb) ?? "";
    }

    /// <summary>Gets whether the package should be replaced.</summary>
    public bool Replace { get; }
    /// <summary>Gets the name of the existing package to be replaced.</summary>
    public string OldPackage { get; }
    /// <summary>Gets the name of the replacement package.</summary>
    public string NewPackage { get; }
    /// <summary>Gets the name of the database providing the replacement package.</summary>
    public string NewDatabase { get; }
  }

  /// <summary>Question asked when two packages conflict during transaction preparation.</summary>
  public class ConflictPkg : AlpmQuestion
  {
    internal unsafe ConflictPkg(_alpm_question_t* question)
    {
      var conflict = question->conflict.conflict;

      Remove = question->conflict.remove != 0;
      Package1 = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_name(conflict->package1)) ?? "";
      Package2 = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_name(conflict->package2)) ?? "";
      Name = NativeString.FromNative((nint)conflict->reason->name) ?? "";
      Version = NativeString.FromNative((nint)conflict->reason->version) ?? "";
      Description = NativeString.FromNative((nint)conflict->reason->desc) ?? "";
    }

    /// <summary>Gets whether the conflicting package should be removed.</summary>
    public bool Remove { get; }
    /// <summary>Gets the name of the first conflicting package.</summary>
    public string Package1 { get; }
    /// <summary>Gets the name of the second conflicting package.</summary>
    public string Package2 { get; }
    /// <summary>Gets the dependency name causing the conflict.</summary>
    public string Name { get; }
    /// <summary>Gets the dependency version requirement causing the conflict.</summary>
    public string Version { get; }
    /// <summary>Gets the description of the conflict reason.</summary>
    public string Description { get; }
  }

  /// <summary>Question asked when a downloaded package file is found to be corrupted.</summary>
  public class CorruptedPkg : AlpmQuestion
  {
    internal unsafe CorruptedPkg(_alpm_question_t* question)
    {
      Remove = question->corrupted.remove != 0;
      FilePath = NativeString.FromNative((nint)question->corrupted.filepath) ?? "";
    }

    /// <summary>Gets whether the corrupted package file should be removed.</summary>
    public bool Remove { get; }
    /// <summary>Gets the filesystem path to the corrupted package file.</summary>
    public string FilePath { get; }
  }

  /// <summary>Question asked when unresolvable dependencies require removing packages.</summary>
  public class RemovePkgs : AlpmQuestion
  {
    internal unsafe RemovePkgs(_alpm_question_t* question, Lifetime? lifetime)
    {
      Skip = question->remove_pkgs.skip != 0;
      Packages = [.. AlpmList<PackageView>.Borrow(question->remove_pkgs.packages, &PackageView.Factory, lifetime)];
    }

    /// <summary>Gets whether to skip removing the packages.</summary>
    public bool Skip { get; }
    /// <summary>Gets the packages proposed for removal.</summary>
    public IReadOnlyList<PackageView> Packages { get; }
  }

  /// <summary>Question asked when multiple providers satisfy a dependency and a selection is required.</summary>
  public class SelectProvider : AlpmQuestion
  {
    internal unsafe SelectProvider(_alpm_question_t* question, Lifetime? lifetime)
    {
      UseIndex = question->select_provider.use_index != 0;
      Providers = [.. AlpmList<PackageView>.Borrow(question->select_provider.providers, &PackageView.Factory, lifetime)];
      Name = NativeString.FromNative((nint)question->select_provider.depend->name) ?? "";
      Version = NativeString.FromNative((nint)question->select_provider.depend->version) ?? "";
      Description = NativeString.FromNative((nint)question->select_provider.depend->desc) ?? "";
    }

    /// <summary>Gets whether the provider selection is based on index rather than name.</summary>
    public bool UseIndex { get; }
    /// <summary>Gets the candidate packages providing the dependency.</summary>
    public IReadOnlyList<PackageView> Providers { get; }
    /// <summary>Gets the name of the dependency needing a provider.</summary>
    public string Name { get; }
    /// <summary>Gets the required version of the dependency.</summary>
    public string Version { get; }
    /// <summary>Gets the description of the dependency.</summary>
    public string Description { get; }
  }

  /// <summary>Question asked when an unknown PGP key needs to be imported into the keyring.</summary>
  public class ImportKey : AlpmQuestion
  {
    internal unsafe ImportKey(_alpm_question_t* question)
    {
      Import = question->import_key.import != 0;
      Uid = NativeString.FromNative((nint)question->import_key.uid) ?? "";
      Fingerprint = NativeString.FromNative((nint)question->import_key.fingerprint) ?? "";
    }

    /// <summary>Gets whether to import the key.</summary>
    public bool Import { get; }
    /// <summary>Gets the user ID or identity associated with the key.</summary>
    public string Uid { get; }
    /// <summary>Gets the fingerprint of the key.</summary>
    public string Fingerprint { get; }
  }
}

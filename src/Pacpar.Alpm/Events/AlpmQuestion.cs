using System.Diagnostics.CodeAnalysis;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;
using ZLinq;

namespace Pacpar.Alpm;

/// <summary>
/// A question libalpm asks a callback handler (<c>alpm_question_t</c>).
/// </summary>
/// <remarks>
/// Every question subclass is a managed snapshot whose values are copied from libalpm during callback
/// execution. Nothing native is retained, so instances are safe to persist or inspect after the
/// callback completes - including after the database that owned a package is unregistered, or after
/// the transaction that raised the question is released.
/// <para>
/// Packages in <see cref="InstallIgnoredPackage.Package"/>, <see cref="ReplacePackage.OldPackage"/>,
/// <see cref="ReplacePackage.NewPackage"/>, <see cref="ConflictPkg.Package1"/>, <see cref="ConflictPkg.Package2"/>,
/// <see cref="RemovePkgs.Packages"/>, and <see cref="SelectProvider.Providers"/>
/// are exposed as <see cref="PackageSnapshot"/> instances copied at the same moment, so the payload
/// borrows no package memory. <see cref="AlpmBindingConfig.QuestionPayloadIncludeFiles"/> selects whether
/// the package's file list is copied in addition to metadata.
/// </para>
/// <para>
/// A snapshot is not a <see cref="PackageView"/>: members that only make sense against a live handle,
/// such as <see cref="PackageBase.ShouldIgnore"/> and <see cref="PackageBase.CheckMd5Sum"/>, are not
/// available on the payload.
/// </para>
/// </remarks>
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public abstract class AlpmQuestion
{
  internal static unsafe AlpmQuestion FromUnion(_alpm_question_t* backingStruct, AlpmBindingConfig binding)
  {
    return backingStruct->type_ switch
    {
      _alpm_question_type_t.ALPM_QUESTION_INSTALL_IGNOREPKG => new InstallIgnoredPackage(backingStruct,
        binding),
      _alpm_question_type_t.ALPM_QUESTION_REPLACE_PKG => new ReplacePackage(backingStruct, binding),
      _alpm_question_type_t.ALPM_QUESTION_CONFLICT_PKG => new ConflictPkg(backingStruct, binding),
      _alpm_question_type_t.ALPM_QUESTION_CORRUPTED_PKG => new CorruptedPkg(backingStruct),
      _alpm_question_type_t.ALPM_QUESTION_REMOVE_PKGS => new RemovePkgs(backingStruct, binding),
      _alpm_question_type_t.ALPM_QUESTION_SELECT_PROVIDER => new SelectProvider(backingStruct, binding),
      _alpm_question_type_t.ALPM_QUESTION_IMPORT_KEY => new ImportKey(backingStruct),
      _ => throw new ArgumentException($"Unknown question type: {backingStruct->type_}"),
    };
  }

  /// <summary>
  /// Copies a package list out of libalpm. libalpm owns the list nodes only for the duration of the
  /// call, so it is traversed and copied here rather than viewed. Similarly, this is why a lifetime
  /// parameter is not needed.
  /// </summary>
  private static unsafe PackageSnapshot[] SnapshotPackageList(_alpm_list_t* packages,
    bool snapshotFiles = false)
  {
    var snapshots = AlpmList<PackageView>.Borrow(packages, &PackageView.Factory, null)
      .AsValueEnumerable()
      .Select(package => package.ToSnapshot(snapshotFiles)).ToArray();

    return snapshots;
  }

  private static unsafe PackageSnapshot SnapshotPackage(_alpm_pkg_t* package, bool snapshotFiles = false)
  {
    return new PackageView(package, null).ToSnapshot(snapshotFiles);
  }

  /// <summary>Question asked when attempting to install a package marked in IgnorePkg.</summary>
  public class InstallIgnoredPackage : AlpmQuestion
  {
    internal unsafe InstallIgnoredPackage(_alpm_question_t* question, AlpmBindingConfig binding)
    {
      Install = question->install_ignorepkg.install != 0;
      Package = SnapshotPackage(question->install_ignorepkg.pkg, binding.QuestionPayloadIncludeFiles);
    }

    /// <summary>Gets whether to install the ignored package.</summary>
    public bool Install { get; }

    /// <summary>Gets the ignored package proposed for installation, copied out of libalpm.</summary>
    public PackageSnapshot Package { get; }
  }

  /// <summary>Question asked when an existing package is to be replaced by another package.</summary>
  public class ReplacePackage : AlpmQuestion
  {
    internal unsafe ReplacePackage(_alpm_question_t* question, AlpmBindingConfig binding)
    {
      Replace = question->replace.replace != 0;
      OldPackage = SnapshotPackage(question->replace.oldpkg, binding.QuestionPayloadIncludeFiles);
      NewPackage = SnapshotPackage(question->replace.newpkg, binding.QuestionPayloadIncludeFiles);
      NewDatabase = NativeString.FromNative((nint)NativeMethods.alpm_db_get_name(question->replace.newdb)) ?? "";
    }

    /// <summary>Gets whether the package should be replaced.</summary>
    public bool Replace { get; }

    /// <summary>Gets the existing package to be replaced, copied out of libalpm.</summary>
    public PackageSnapshot OldPackage { get; }

    /// <summary>Gets the replacement package, copied out of libalpm.</summary>
    public PackageSnapshot NewPackage { get; }

    /// <summary>Gets the name of the database providing the replacement package.</summary>
    public string NewDatabase { get; }
  }

  /// <summary>Question asked when two packages conflict during transaction preparation.</summary>
  public class ConflictPkg : AlpmQuestion
  {
    internal unsafe ConflictPkg(_alpm_question_t* question, AlpmBindingConfig binding)
    {
      var conflict = question->conflict.conflict;

      Remove = question->conflict.remove != 0;
      Package1 = SnapshotPackage(conflict->package1, binding.QuestionPayloadIncludeFiles);
      Package2 = SnapshotPackage(conflict->package2, binding.QuestionPayloadIncludeFiles);
      Name = NativeString.FromNative((nint)conflict->reason->name) ?? "";
      Version = NativeString.FromNative((nint)conflict->reason->version) ?? "";
      Description = NativeString.FromNative((nint)conflict->reason->desc) ?? "";
    }

    /// <summary>Gets whether the conflicting package should be removed.</summary>
    public bool Remove { get; }

    /// <summary>Gets the first conflicting package, copied out of libalpm.</summary>
    public PackageSnapshot Package1 { get; }

    /// <summary>Gets the second conflicting package, copied out of libalpm.</summary>
    public PackageSnapshot Package2 { get; }

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
    internal unsafe RemovePkgs(_alpm_question_t* question, AlpmBindingConfig binding)
    {
      Skip = question->remove_pkgs.skip != 0;
      Packages = SnapshotPackageList(question->remove_pkgs.packages, binding.QuestionPayloadIncludeFiles);
    }

    /// <summary>Gets whether to skip removing the packages.</summary>
    public bool Skip { get; }

    /// <summary>Gets the packages proposed for removal, copied out of libalpm.</summary>
    public IReadOnlyList<PackageSnapshot> Packages { get; }
  }

  /// <summary>Question asked when multiple providers satisfy a dependency and a selection is required.</summary>
  public class SelectProvider : AlpmQuestion
  {
    internal unsafe SelectProvider(_alpm_question_t* question, AlpmBindingConfig binding)
    {
      UseIndex = question->select_provider.use_index != 0;
      Providers = SnapshotPackageList(question->select_provider.providers, binding.QuestionPayloadIncludeFiles);
      Name = NativeString.FromNative((nint)question->select_provider.depend->name) ?? "";
      Version = NativeString.FromNative((nint)question->select_provider.depend->version) ?? "";
      Description = NativeString.FromNative((nint)question->select_provider.depend->desc) ?? "";
    }

    /// <summary>Gets whether the provider selection is based on index rather than name.</summary>
    public bool UseIndex { get; }

    /// <summary>Gets the candidate packages providing the dependency, copied out of libalpm.</summary>
    public IReadOnlyList<PackageSnapshot> Providers { get; }

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

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

/// <summary>
/// A question libalpm asks a callback handler (<c>alpm_question_t</c>).
/// </summary>
/// <remarks>
/// Question payloads are borrowed references valid only during the synchronous callback frame execution.
/// Answers must be set before the handler returns, as libalpm reads the answer immediately.
/// Packages in payload properties are exposed as borrowed <see cref="PackageView"/> instances anchored to the
/// callback frame's lifetime; callers wishing to retain package data after the callback returns must
/// call <see cref="PackageBase.ToSnapshot"/> to create an independent managed snapshot.
/// </remarks>
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public abstract unsafe class AlpmQuestion
{
  private protected _alpm_question_t* BackingStruct;
  private protected readonly LifetimeStamp Stamp;

  private protected AlpmQuestion(_alpm_question_t* question, ChildLifetime? lifetime)
  {
    BackingStruct = question;
    Stamp = lifetime?.Capture() ?? default;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private protected void ThrowIfStale()
  {
    Stamp.ThrowIfStale();
  }

  internal static AlpmQuestion FromUnion(_alpm_question_t* backingStruct, ChildLifetime? lifetime)
  {
    return backingStruct->type_ switch
    {
      _alpm_question_type_t.ALPM_QUESTION_INSTALL_IGNOREPKG => new InstallIgnoredPackage(backingStruct, lifetime),
      _alpm_question_type_t.ALPM_QUESTION_REPLACE_PKG => new ReplacePackage(backingStruct, lifetime),
      _alpm_question_type_t.ALPM_QUESTION_CONFLICT_PKG => new ConflictPkg(backingStruct, lifetime),
      _alpm_question_type_t.ALPM_QUESTION_CORRUPTED_PKG => new CorruptedPkg(backingStruct, lifetime),
      _alpm_question_type_t.ALPM_QUESTION_REMOVE_PKGS => new RemovePkgs(backingStruct, lifetime),
      _alpm_question_type_t.ALPM_QUESTION_SELECT_PROVIDER => new SelectProvider(backingStruct, lifetime),
      _alpm_question_type_t.ALPM_QUESTION_IMPORT_KEY => new ImportKey(backingStruct, lifetime),
      _ => throw new ArgumentException($"Unknown question type: {backingStruct->type_}"),
    };
  }

  /// <summary>Question asked when attempting to install a package marked in IgnorePkg.</summary>
  public class InstallIgnoredPackage : AlpmQuestion
  {
    private bool _install;

    internal InstallIgnoredPackage(_alpm_question_t* question, ChildLifetime? lifetime) : base(question, lifetime)
    {
      _install = question->install_ignorepkg.install != 0;
      Package = new PackageView(question->install_ignorepkg.pkg, lifetime);
    }

    /// <summary>Gets or sets whether to install the ignored package.</summary>
    public bool Install
    {
      get => _install;
      set
      {
        ThrowIfStale();
        BackingStruct->install_ignorepkg.install = value ? 1 : 0;
        _install = value;
      }
    }

    /// <summary>Gets the ignored package proposed for installation as a borrowed view.</summary>
    public PackageView Package { get; }
  }

  /// <summary>Question asked when an existing package is to be replaced by another package.</summary>
  public class ReplacePackage : AlpmQuestion
  {
    private bool _replace;

    internal ReplacePackage(_alpm_question_t* question, ChildLifetime? lifetime) : base(question, lifetime)
    {
      _replace = question->replace.replace != 0;
      OldPackage = new PackageView(question->replace.oldpkg, lifetime);
      NewPackage = new PackageView(question->replace.newpkg, lifetime);
      NewDatabase = NativeString.FromNative((nint)NativeMethods.alpm_db_get_name(question->replace.newdb)) ?? "";
    }

    /// <summary>Gets or sets whether the package should be replaced.</summary>
    public bool Replace
    {
      get => _replace;
      set
      {
        ThrowIfStale();
        BackingStruct->replace.replace = value ? 1 : 0;
        _replace = value;
      }
    }

    /// <summary>Gets the existing package to be replaced as a borrowed view.</summary>
    public PackageView OldPackage { get; }

    /// <summary>Gets the replacement package as a borrowed view.</summary>
    public PackageView NewPackage { get; }

    /// <summary>Gets the name of the database providing the replacement package.</summary>
    public string NewDatabase { get; }
  }

  /// <summary>Question asked when two packages conflict during transaction preparation.</summary>
  public class ConflictPkg : AlpmQuestion
  {
    private bool _remove;

    internal ConflictPkg(_alpm_question_t* question, ChildLifetime? lifetime) : base(question, lifetime)
    {
      var conflict = question->conflict.conflict;

      _remove = question->conflict.remove != 0;
      Package1 = new PackageView(conflict->package1, lifetime);
      Package2 = new PackageView(conflict->package2, lifetime);
      Name = NativeString.FromNative((nint)conflict->reason->name) ?? "";
      Version = NativeString.FromNative((nint)conflict->reason->version) ?? "";
      Description = NativeString.FromNative((nint)conflict->reason->desc) ?? "";
    }

    /// <summary>Gets or sets whether the conflicting package should be removed.</summary>
    public bool Remove
    {
      get => _remove;
      set
      {
        ThrowIfStale();
        BackingStruct->conflict.remove = value ? 1 : 0;
        _remove = value;
      }
    }

    /// <summary>Gets the first conflicting package as a borrowed view.</summary>
    public PackageView Package1 { get; }

    /// <summary>Gets the second conflicting package as a borrowed view.</summary>
    public PackageView Package2 { get; }

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
    private bool _remove;

    internal CorruptedPkg(_alpm_question_t* question, ChildLifetime? lifetime) : base(question, lifetime)
    {
      _remove = question->corrupted.remove != 0;
      FilePath = NativeString.FromNative((nint)question->corrupted.filepath) ?? "";
      Reason = ErrorHandler.StrError(question->corrupted.reason) ?? "";
    }

    /// <summary>Gets or sets whether the corrupted package file should be removed.</summary>
    public bool Remove
    {
      get => _remove;
      set
      {
        ThrowIfStale();
        BackingStruct->corrupted.remove = value ? 1 : 0;
        _remove = value;
      }
    }

    /// <summary>Gets the filesystem path to the corrupted package file.</summary>
    public string FilePath { get; }

    /// <summary>Gets the description of why the package file is considered corrupted.</summary>
    public string Reason { get; }
  }

  /// <summary>Question asked when unresolvable dependencies require removing packages.</summary>
  public class RemovePkgs : AlpmQuestion
  {
    private bool _skip;

    internal RemovePkgs(_alpm_question_t* question, ChildLifetime? lifetime) : base(question, lifetime)
    {
      _skip = question->remove_pkgs.skip != 0;
      Packages = AlpmList<PackageView>.Borrow(question->remove_pkgs.packages, &PackageView.Factory, lifetime).ToArray();
    }

    /// <summary>Gets or sets whether to skip removing the packages.</summary>
    public bool Skip
    {
      get => _skip;
      set
      {
        ThrowIfStale();
        BackingStruct->remove_pkgs.skip = value ? 1 : 0;
        _skip = value;
      }
    }

    /// <summary>Gets the packages proposed for removal as borrowed views.</summary>
    public IReadOnlyList<PackageView> Packages { get; }
  }

  /// <summary>Question asked when multiple providers satisfy a dependency and a selection is required.</summary>
  public class SelectProvider : AlpmQuestion
  {
    private int _useIndex;

    internal SelectProvider(_alpm_question_t* question, ChildLifetime? lifetime) : base(question, lifetime)
    {
      _useIndex = question->select_provider.use_index;
      Providers = AlpmList<PackageView>.Borrow(question->select_provider.providers, &PackageView.Factory, lifetime).ToArray();
      Name = NativeString.FromNative((nint)question->select_provider.depend->name) ?? "";
      Version = NativeString.FromNative((nint)question->select_provider.depend->version) ?? "";
      Description = NativeString.FromNative((nint)question->select_provider.depend->desc) ?? "";
    }

    /// <summary>Gets or sets the 0-based index of the chosen provider from <see cref="Providers"/>.</summary>
    public int UseIndex
    {
      get => _useIndex;
      set
      {
        ThrowIfStale();
        BackingStruct->select_provider.use_index = value;
        _useIndex = value;
      }
    }

    /// <summary>Gets the candidate packages providing the dependency as borrowed views.</summary>
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
    private bool _import;

    internal ImportKey(_alpm_question_t* question, ChildLifetime? lifetime) : base(question, lifetime)
    {
      _import = question->import_key.import != 0;
      Uid = NativeString.FromNative((nint)question->import_key.uid) ?? "";
      Fingerprint = NativeString.FromNative((nint)question->import_key.fingerprint) ?? "";
    }

    /// <summary>Gets or sets whether to import the key.</summary>
    public bool Import
    {
      get => _import;
      set
      {
        ThrowIfStale();
        BackingStruct->import_key.import = value ? 1 : 0;
        _import = value;
      }
    }

    /// <summary>Gets the user ID or identity associated with the key.</summary>
    public string Uid { get; }

    /// <summary>Gets the fingerprint of the key.</summary>
    public string Fingerprint { get; }
  }
}

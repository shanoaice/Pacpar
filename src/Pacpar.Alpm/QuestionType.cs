using System.Diagnostics.CodeAnalysis;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public abstract class QuestionType
{
  internal static unsafe QuestionType FromUnion(_alpm_question_t* backingStruct)
  {
    return backingStruct->type_ switch
    {
      _alpm_question_type_t.ALPM_QUESTION_INSTALL_IGNOREPKG => new InstallIgnoredPackage(backingStruct),
      _alpm_question_type_t.ALPM_QUESTION_REPLACE_PKG => new ReplacePackage(backingStruct),
      _alpm_question_type_t.ALPM_QUESTION_CONFLICT_PKG => new ConflictPkg(backingStruct),
      _alpm_question_type_t.ALPM_QUESTION_CORRUPTED_PKG => new CorruptedPkg(backingStruct),
      _alpm_question_type_t.ALPM_QUESTION_REMOVE_PKGS => new RemovePkgs(backingStruct),
      _alpm_question_type_t.ALPM_QUESTION_SELECT_PROVIDER => new SelectProvider(backingStruct),
      _alpm_question_type_t.ALPM_QUESTION_IMPORT_KEY => new ImportKey(backingStruct),
      _ => throw new ArgumentException($"Unknown question type: {backingStruct->type_}"),
    };
  }

  public unsafe class InstallIgnoredPackage : QuestionType
  {
    private readonly _alpm_question_t* backingStruct;

    internal InstallIgnoredPackage(_alpm_question_t* backingStruct)
    {
      this.backingStruct = backingStruct;
    }

    public bool Install => backingStruct->install_ignorepkg.install != 0;
    public string Package => field ??= NativeString.FromNative((nint)backingStruct->install_ignorepkg.pkg) ?? "";
  }

  public unsafe class ReplacePackage : QuestionType
  {
    private readonly _alpm_question_t* backingStruct;

    internal ReplacePackage(_alpm_question_t* backingStruct)
    {
      this.backingStruct = backingStruct;
    }


    public bool Replace => backingStruct->replace.replace != 0;
    public string OldPackage => field ??= NativeString.FromNative((nint)backingStruct->replace.oldpkg) ?? "";
    public string NewPackage => field ??= NativeString.FromNative((nint)backingStruct->replace.newpkg) ?? "";
    public string NewDatabase => field ??= NativeString.FromNative((nint)backingStruct->replace.newdb) ?? "";
  }

  public unsafe class ConflictPkg : QuestionType
  {
    private readonly _alpm_question_t* backingStruct;

    internal ConflictPkg(_alpm_question_t* backingStruct)
    {
      this.backingStruct = backingStruct;
    }

    public bool Remove => backingStruct->conflict.remove != 0;
    public string Package1 => field ??= NativeString.FromNative((nint)backingStruct->conflict.conflict->package1) ?? "";
    public string Package2 => field ??= NativeString.FromNative((nint)backingStruct->conflict.conflict->package2) ?? "";
    public string Name => field ??= NativeString.FromNative((nint)backingStruct->conflict.conflict->reason->name) ?? "";
    public string Version => field ??= NativeString.FromNative((nint)backingStruct->conflict.conflict->reason->version) ?? "";
    public string Description => field ??= NativeString.FromNative((nint)backingStruct->conflict.conflict->reason->desc) ?? "";
  }

  public unsafe class CorruptedPkg : QuestionType
  {
    private readonly _alpm_question_t* backingStruct;

    internal CorruptedPkg(_alpm_question_t* backingStruct)
    {
      this.backingStruct = backingStruct;
    }

    public bool Remove => backingStruct->corrupted.remove != 0;
    public string FilePath => field ??= NativeString.FromNative((nint)backingStruct->corrupted.filepath) ?? "";
  }

  public unsafe class RemovePkgs : QuestionType
  {
    private readonly _alpm_question_t* backingStruct;

    internal RemovePkgs(_alpm_question_t* backingStruct)
    {
      this.backingStruct = backingStruct;
    }

    public bool Skip => backingStruct->remove_pkgs.skip != 0;
    public AlpmList<Package> Packages =>
      AlpmList<Package>.Borrow(backingStruct->remove_pkgs.packages, &Package.FactoryFromDatabase);
  }

  public unsafe class SelectProvider : QuestionType
  {
    private readonly _alpm_question_t* backingStruct;

    internal SelectProvider(_alpm_question_t* backingStruct)
    {
      this.backingStruct = backingStruct;
    }

    public bool UseIndex => backingStruct->select_provider.use_index != 0;
    public AlpmList<Package> Providers =>
      AlpmList<Package>.Borrow(backingStruct->select_provider.providers, &Package.FactoryFromDatabase);
    public string Name => field ??= NativeString.FromNative((nint)backingStruct->select_provider.depend->name) ?? "";
    public string Version => field ??= NativeString.FromNative((nint)backingStruct->select_provider.depend->version) ?? "";
    public string Description => field ??= NativeString.FromNative((nint)backingStruct->select_provider.depend->desc) ?? "";
  }

  public unsafe class ImportKey : QuestionType
  {
    private readonly _alpm_question_t* backingStruct;

    internal ImportKey(_alpm_question_t* backingStruct)
    {
      this.backingStruct = backingStruct;
    }

    public bool Import => backingStruct->import_key.import != 0;
    public string Uid => field ??= NativeString.FromNative((nint)backingStruct->import_key.uid) ?? "";
    public string Fingerprint => field ??= NativeString.FromNative((nint)backingStruct->import_key.fingerprint) ?? "";
  }
}

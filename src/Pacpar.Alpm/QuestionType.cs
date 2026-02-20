using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public abstract class QuestionType
{
  public static unsafe QuestionType FromUnion(_alpm_question_t* backingStruct)
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

  public unsafe class InstallIgnoredPackage(_alpm_question_t* backingStruct) : QuestionType
  {
    public int Install => backingStruct->install_ignorepkg.install;
    public string Package => field ??= Marshal.PtrToStringAnsi((nint)backingStruct->install_ignorepkg.pkg) ?? "";
  }

  public unsafe class ReplacePackage(_alpm_question_t* backingStruct) : QuestionType
  {

    public int Replace => backingStruct->replace.replace;
    public string OldPackage => field ??= Marshal.PtrToStringAnsi((nint)backingStruct->replace.oldpkg) ?? "";
    public string NewPackage => field ??= Marshal.PtrToStringAnsi((nint)backingStruct->replace.newpkg) ?? "";
    public string NewDatabase => field ??= Marshal.PtrToStringAnsi((nint)backingStruct->replace.newdb) ?? "";
  }

  public unsafe class ConflictPkg(_alpm_question_t* backingStruct) : QuestionType
  {
    public int Remove => backingStruct->conflict.remove;
    public string Package1 => field ??= Marshal.PtrToStringAnsi((nint)backingStruct->conflict.conflict->package1) ?? "";
    public string Package2 => field ??= Marshal.PtrToStringAnsi((nint)backingStruct->conflict.conflict->package2) ?? "";
    public string Name => field ??= Marshal.PtrToStringAnsi((nint)backingStruct->conflict.conflict->reason->name) ?? "";
    public string Version => field ??= Marshal.PtrToStringAnsi((nint)backingStruct->conflict.conflict->reason->version) ?? "";
    public string Description => field ??= Marshal.PtrToStringAnsi((nint)backingStruct->conflict.conflict->reason->desc) ?? "";
  }

  public unsafe class CorruptedPkg(_alpm_question_t* backingStruct) : QuestionType
  {
    public int Remove => backingStruct->corrupted.remove;
    public string FilePath => field ??= Marshal.PtrToStringAnsi((nint)backingStruct->corrupted.filepath) ?? "";
  }

  public unsafe class RemovePkgs(_alpm_question_t* backingStruct) : QuestionType
  {
    public int Skip => backingStruct->remove_pkgs.skip;
    public _alpm_list_t* Packages => backingStruct->remove_pkgs.packages;
  }

  public unsafe class SelectProvider(_alpm_question_t* backingStruct) : QuestionType
  {
    public int UseIndex => backingStruct->select_provider.use_index;
    public _alpm_list_t* Providers => backingStruct->select_provider.providers;
    public string Name => field ??= Marshal.PtrToStringAnsi((nint)backingStruct->select_provider.depend->name) ?? "";
    public string Version => field ??= Marshal.PtrToStringAnsi((nint)backingStruct->select_provider.depend->version) ?? "";
    public string Description => field ??= Marshal.PtrToStringAnsi((nint)backingStruct->select_provider.depend->desc) ?? "";
  }

  public unsafe class ImportKey(_alpm_question_t* backingStruct) : QuestionType
  {
    public int Import => backingStruct->import_key.import;
    public string Uid => field ??= Marshal.PtrToStringAnsi((nint)backingStruct->import_key.uid) ?? "";
    public string Fingerprint => field ??= Marshal.PtrToStringAnsi((nint)backingStruct->import_key.fingerprint) ?? "";
  }
}

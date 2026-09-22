using System.Diagnostics.CodeAnalysis;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

/// <summary>
/// A question libalpm asks a callback handler (<c>alpm_question_t</c>).
/// </summary>
/// <remarks>
/// Every case is a managed snapshot: its fields - including the package names and the member lists
/// - are copied out of libalpm's union while the callback runs, which is the only time that union
/// exists (probed: libalpm builds it on the calling thread's stack and overwrites it as soon as the
/// callback returns - report item F9). Keeping a case and reading it after the callback is
/// therefore safe, which the borrowed views this replaces were not.
/// <para>
/// The packages in <see cref="RemovePkgs.Packages"/> and <see cref="SelectProvider.Providers"/>
/// remain <see cref="Package"/> views: the <i>list</i> is callback-scoped and is copied, the
/// packages themselves belong to libalpm.
/// </para>
/// </remarks>
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

  public class InstallIgnoredPackage : QuestionType
  {
    internal unsafe InstallIgnoredPackage(_alpm_question_t* question)
    {
      Install = question->install_ignorepkg.install != 0;
      Package = NativeString.FromNative((nint)question->install_ignorepkg.pkg) ?? "";
    }

    public bool Install { get; }
    public string Package { get; }
  }

  public class ReplacePackage : QuestionType
  {
    internal unsafe ReplacePackage(_alpm_question_t* question)
    {
      Replace = question->replace.replace != 0;
      OldPackage = NativeString.FromNative((nint)question->replace.oldpkg) ?? "";
      NewPackage = NativeString.FromNative((nint)question->replace.newpkg) ?? "";
      NewDatabase = NativeString.FromNative((nint)question->replace.newdb) ?? "";
    }

    public bool Replace { get; }
    public string OldPackage { get; }
    public string NewPackage { get; }
    public string NewDatabase { get; }
  }

  public class ConflictPkg : QuestionType
  {
    /// <remarks>
    /// <c>alpm_conflict_t</c> holds <c>alpm_pkg_t</c> pointers, not names (alpm.h: "The first
    /// package"), so <see cref="Package1"/>/<see cref="Package2"/> read the packages' names through
    /// <c>alpm_pkg_get_name</c>. The previous version interpreted those pointers as C strings.
    /// </remarks>
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

    public bool Remove { get; }
    public string Package1 { get; }
    public string Package2 { get; }
    public string Name { get; }
    public string Version { get; }
    public string Description { get; }
  }

  public class CorruptedPkg : QuestionType
  {
    internal unsafe CorruptedPkg(_alpm_question_t* question)
    {
      Remove = question->corrupted.remove != 0;
      FilePath = NativeString.FromNative((nint)question->corrupted.filepath) ?? "";
    }

    public bool Remove { get; }
    public string FilePath { get; }
  }

  public class RemovePkgs : QuestionType
  {
    internal unsafe RemovePkgs(_alpm_question_t* question)
    {
      Skip = question->remove_pkgs.skip != 0;
      Packages = [.. AlpmList<Package>.Borrow(question->remove_pkgs.packages, &Package.Factory)];
    }

    public bool Skip { get; }
    public IReadOnlyList<Package> Packages { get; }
  }

  public class SelectProvider : QuestionType
  {
    internal unsafe SelectProvider(_alpm_question_t* question)
    {
      UseIndex = question->select_provider.use_index != 0;
      Providers = [.. AlpmList<Package>.Borrow(question->select_provider.providers, &Package.Factory)];
      Name = NativeString.FromNative((nint)question->select_provider.depend->name) ?? "";
      Version = NativeString.FromNative((nint)question->select_provider.depend->version) ?? "";
      Description = NativeString.FromNative((nint)question->select_provider.depend->desc) ?? "";
    }

    public bool UseIndex { get; }
    public IReadOnlyList<Package> Providers { get; }
    public string Name { get; }
    public string Version { get; }
    public string Description { get; }
  }

  public class ImportKey : QuestionType
  {
    internal unsafe ImportKey(_alpm_question_t* question)
    {
      Import = question->import_key.import != 0;
      Uid = NativeString.FromNative((nint)question->import_key.uid) ?? "";
      Fingerprint = NativeString.FromNative((nint)question->import_key.fingerprint) ?? "";
    }

    public bool Import { get; }
    public string Uid { get; }
    public string Fingerprint { get; }
  }
}

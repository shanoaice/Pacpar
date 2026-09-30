using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>
/// Represents a file conflict between packages or with the filesystem.
/// </summary>
public class FileConflict
{
  /// <summary>
  /// The target or package that causes the conflict.
  /// </summary>
  public string? Ctarget { get; }

  /// <summary>
  /// The path of the conflicting file.
  /// </summary>
  public string? File { get; }

  /// <summary>
  /// The existing package or filesystem target involved in the conflict.
  /// </summary>
  public string? Target { get; }

  internal unsafe FileConflict(_alpm_fileconflict_t* backingStruct)
  {
    Ctarget = NativeString.FromNative((nint)backingStruct->ctarget);
    File = NativeString.FromNative((nint)backingStruct->file);
    Target = NativeString.FromNative((nint)backingStruct->target);
  }

  internal static unsafe FileConflict Factory(void* ptr, Lifetime? lifetime) => new((_alpm_fileconflict_t*)ptr);
}

/// <summary>
/// Represents a package dependency conflict between two packages.
/// </summary>
public class Conflict
{
  /// <summary>
  /// The name of the first conflicting package.
  /// </summary>
  public string Package1Name { get; }

  /// <summary>
  /// The name of the second conflicting package.
  /// </summary>
  public string Package2Name { get; }

  internal unsafe Conflict(_alpm_conflict_t* backingStruct)
  {
    Reason = new Depend(backingStruct->reason);
    Package1Name = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_name(backingStruct->package1))!;
    Package2Name = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_name(backingStruct->package2))!;
  }

  internal static unsafe Conflict Factory(void* ptr, Lifetime? lifetime) => new((_alpm_conflict_t*)ptr);

  /// <summary>
  /// The dependency relationship that caused this conflict.
  /// </summary>
  public Depend Reason { get; }
}

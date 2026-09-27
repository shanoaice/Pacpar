using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

public class FileConflict
{
  public string? Ctarget { get; }
  public string? File { get; }
  public string? Target { get; }

  internal unsafe FileConflict(_alpm_fileconflict_t* backingStruct)
  {
    Ctarget = NativeString.FromNative((nint)backingStruct->ctarget);
    File = NativeString.FromNative((nint)backingStruct->file);
    Target = NativeString.FromNative((nint)backingStruct->target);
  }

  // Token parameter unused: a FileConflict is an eager snapshot of caller-owned failure data.
  internal static unsafe FileConflict Factory(void* ptr, Lifetime? lifetime) => new((_alpm_fileconflict_t*)ptr);
}

public class Conflict
{
  public string Package1Name { get; }
  public string Package2Name { get; }

  internal unsafe Conflict(_alpm_conflict_t* backingStruct)
  {
    Reason = new Depend(backingStruct->reason);
    // Names are read eagerly instead of building PackageView wrappers: the conflict payload is a
    // self-contained snapshot, and a view would need a lifetime token this context does not have.
    Package1Name = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_name(backingStruct->package1))!;
    Package2Name = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_name(backingStruct->package2))!;
  }

  // Token parameter unused: a Conflict is an eager snapshot of caller-owned failure data.
  internal static unsafe Conflict Factory(void* ptr, Lifetime? lifetime) => new((_alpm_conflict_t*)ptr);

  /// <summary>
  /// The conflicting dependency. Borrowed from the conflict struct: it is released by
  /// <c>alpm_conflict_free</c>, not by this type.
  /// </summary>
  public Depend Reason { get; }
}

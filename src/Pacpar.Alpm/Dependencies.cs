using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

/// <summary>The version comparison a dependency uses (libalpm's <c>_alpm_depmod_t</c>).</summary>
public enum DepMod : uint
{
  /// <summary>Any version satisfies the dependency.</summary>
  ANY = 1,

  /// <summary>=</summary>
  EQUAL = 2,

  /// <summary>&gt;=</summary>
  GREATER_OR_EQUAL = 3,

  /// <summary>&lt;=</summary>
  LESS_OR_EQUAL = 4,

  /// <summary>&gt;</summary>
  GREATER = 5,

  /// <summary>&lt;</summary>
  LESS = 6
}


/// <summary>
/// A dependency (<c>alpm_depend_t</c>).
/// </summary>
/// <remarks>
/// This is a borrowed view: it never frees the underlying struct, because every dependency reachable
/// today is owned by a package, a database or an option list. Strings are copied out on
/// construction, so the properties stay valid for as long as the containing list does.
/// <para>
/// Instances produced by <see cref="Snapshot"/> carry no native pointer at all; they are detached
/// copies used by <see cref="DepMissing"/>. Such an instance cannot be handed back to libalpm list
/// operations.
/// </para>
/// </remarks>
public unsafe class Depend
{
  private readonly _alpm_depend_t* _backingStruct;

  internal Depend(_alpm_depend_t* backingStruct)
  {
    _backingStruct = backingStruct;
    Name = NativeString.FromNative((nint)backingStruct->name);
    Version = NativeString.FromNative((nint)backingStruct->version);
    Description = NativeString.FromNative((nint)backingStruct->desc);
    Depmod = (DepMod)(uint)backingStruct->mod_;
  }

  private Depend(string? name, string? version, string? description, DepMod depmod)
  {
    _backingStruct = null;
    Name = name;
    Version = version;
    Description = description;
    Depmod = depmod;
  }

  internal static Depend Factory(void* ptr) => new((_alpm_depend_t*)ptr);

  /// <summary>
  /// Borrowed view over a dependency list owned by libalpm (for example
  /// <c>alpm_pkg_get_depends</c>, <c>alpm_option_get_assumeinstalled</c>).
  /// </summary>
  internal static AlpmList<Depend> ListFactory(_alpm_list_t* alpmList)
    => AlpmList<Depend>.Borrow(alpmList, &Factory);

  /// <summary>Creates a detached, fully managed copy of a native dependency.</summary>
  internal static Depend Snapshot(_alpm_depend_t* native)
    => new(NativeString.FromNative((nint)native->name),
      NativeString.FromNative((nint)native->version),
      NativeString.FromNative((nint)native->desc),
      (DepMod)(uint)native->mod_);

  /// <summary>
  /// The native struct used by list operations, or <c>null</c> for a detached snapshot.
  /// </summary>
  internal _alpm_depend_t* BackingStruct => _backingStruct;

  /// <summary>
  /// The native struct, or an exception when this instance is a detached snapshot that libalpm must
  /// not be handed.
  /// </summary>
  internal _alpm_depend_t* NativePtrOrThrow(string paramName)
    => _backingStruct != null
      ? _backingStruct
      : throw new ArgumentException(
        "This Depend is a detached snapshot and cannot be passed back to libalpm.", paramName);

  public string? Description { get; }

  public string? Name { get; }

  public string? Version { get; }

  public DepMod Depmod { get; }
}

/// <summary>
/// A dependency that is missing from the transaction outcome.
/// </summary>
/// <remarks>
/// Managed snapshot: the list libalpm dumps into <c>alpm_trans_prepare</c>'s output parameter is
/// caller-owned, so <see cref="Transactions.Prepare"/> copies the values out and frees the native
/// memory (list and elements). No native pointer is retained.
/// </remarks>
public sealed class DepMissing
{
  private DepMissing(string? target, string? causingPkg, Depend? depend)
  {
    Target = target;
    CausingPkg = causingPkg;
    Depend = depend;
  }

  internal static unsafe DepMissing FromNative(_alpm_depmissing_t* native)
    => new(NativeString.FromNative((nint)native->target),
      NativeString.FromNative((nint)native->causingpkg),
      native->depend != null ? Depend.Snapshot(native->depend) : null);

  public Depend? Depend { get; }

  public string? CausingPkg { get; }

  public string? Target { get; }
}

public unsafe class FileConflict : IDisposable
{
  internal readonly _alpm_fileconflict_t* BackingStruct;

  internal FileConflict(_alpm_fileconflict_t* backingStruct)
  {
    BackingStruct = backingStruct;
  }

  internal static FileConflict Factory(void* ptr) => new((_alpm_fileconflict_t*)ptr);

  private bool _disposed;

  protected void ThrowIfDisposed()
  {
    if (_disposed) throw new ObjectDisposedException(GetType().FullName);
  }

  public string? Ctarget
  {
    get
    {
      ThrowIfDisposed();
      field ??= NativeString.FromNative((IntPtr)BackingStruct->ctarget);
      return field;
    }
  }
  public string? File
  {
    get
    {
      ThrowIfDisposed();
      field ??= NativeString.FromNative((IntPtr)BackingStruct->file);
      return field;
    }
  }
  public string? Target
  {
    get
    {
      ThrowIfDisposed();
      field ??= NativeString.FromNative((IntPtr)BackingStruct->target);
      return field;
    }
  }

  public void Dispose()
  {
    GC.SuppressFinalize(this);
    Dispose(disposing: true);
  }

  protected virtual void Dispose(bool disposing)
  {
    if (!_disposed)
    {
      if (disposing)
      {
        // dispose managed state (managed objects)
      }
      NativeMethods.alpm_fileconflict_free(BackingStruct);
      _disposed = true;
    }
  }

  ~FileConflict() => Dispose(disposing: false);
}

public unsafe class Conflict : IDisposable
{
  internal readonly _alpm_conflict_t* BackingStruct;

  public Package Package1;
  public Package Package2;

  internal Conflict(_alpm_conflict_t* backingStruct)
  {
    BackingStruct = backingStruct;
    Package1 = new Package(backingStruct->package1);
    Package2 = new Package(backingStruct->package2);
    Reason = new Depend(backingStruct->reason);
  }

  internal static Conflict Factory(void* ptr) => new((_alpm_conflict_t*)ptr);

  /// <summary>
  /// The conflicting dependency. Borrowed from the conflict struct: it is released by
  /// <c>alpm_conflict_free</c>, not by this type.
  /// </summary>
  public Depend Reason;

  private bool _disposed;

  protected void ThrowIfDisposed()
  {
    if (_disposed) throw new ObjectDisposedException(GetType().FullName);
  }


  public void Dispose()
  {
    GC.SuppressFinalize(this);
    Dispose(disposing: true);
  }

  protected virtual void Dispose(bool disposing)
  {
    if (!_disposed)
    {
      // Package1/Package2 are database packages owned by their database, so there is nothing to
      // release here; alpm_conflict_free only frees the conflict itself.
      NativeMethods.alpm_conflict_free(BackingStruct);
      _disposed = true;
    }
  }

  ~Conflict() => Dispose(disposing: false);
}

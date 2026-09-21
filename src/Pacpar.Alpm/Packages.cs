using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

/// <summary>Where a package handle comes from (libalpm's <c>_alpm_pkgfrom_t</c>).</summary>
public enum PackageOrigin : uint
{
  /// <summary>Loaded from a package file.</summary>
  File = 1,

  /// <summary>From the local database.</summary>
  LocalDatabase = 2,

  /// <summary>From a sync database.</summary>
  SyncDatabase = 3
}

/// <summary>Why a package is installed (libalpm's <c>_alpm_pkgreason_t</c>).</summary>
public enum PackageReason : uint
{
  /// <summary>Explicitly installed by the user.</summary>
  Explicit = 0,

  /// <summary>Installed as a dependency.</summary>
  Dependency = 1,

  /// <summary>libalpm could not determine the reason.</summary>
  Unknown = 2
}

public unsafe class Version(byte* version) : IComparable<Version>
{
  internal byte* VersionPtr => version;

  public int CompareTo(Version? other) => other == null ? 1 : NativeMethods.alpm_pkg_vercmp(version, other.VersionPtr);

  public override string ToString() => Marshal.PtrToStringAnsi((nint)version)!;
}

public unsafe class Signature(byte* sig, int len) : IDisposable
{
  // ReSharper disable once RedundantDefaultMemberInitializer
  private bool _disposed = false;

  private void ThrowIfDisposed()
  {
    if (_disposed) throw new ObjectDisposedException(GetType().FullName);
  }

  public Span<byte> AsSpan
  {
    get
    {
      ThrowIfDisposed();
      return new Span<byte>(sig, len);
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
      MemoryManagement.CFree(sig); // hope this binds to the correct malloc-free
      _disposed = true;
    }
  }

  ~Signature() => Dispose(disposing: false);
}

// ReSharper disable InconsistentNaming
/// <summary>
///  Method used to validate a package.
/// </summary>
[Flags]
public enum PackageValidation : uint
{
  /// <summary>
  ///  The package's validation type is unknown
  /// </summary>
  ALPM_PKG_VALIDATION_UNKNOWN = 0,
  /// <summary>
  ///  The package does not have any validation
  /// </summary>
  ALPM_PKG_VALIDATION_NONE = 1,
  /// <summary>
  ///  The package is validated with md5
  /// </summary>
  ALPM_PKG_VALIDATION_MD5SUM = 2,
  /// <summary>
  ///  The package is validated with sha256
  /// </summary>
  ALPM_PKG_VALIDATION_SHA256SUM = 4,
  /// <summary>
  ///  The package is validated with a PGP signature
  /// </summary>
  ALPM_PKG_VALIDATION_SIGNATURE = 8,
}
// ReSharper restore InconsistentNaming

public unsafe class Package(byte* backingStruct, bool fromDatabase = true) : IDisposable
{
  internal readonly byte* BackingStruct = backingStruct;
  // ReSharper disable once RedundantDefaultMemberInitializer
  private bool _disposed = false;
  public readonly bool FromDatabase = fromDatabase;

  private void ThrowIfDisposed()
  {
    if (_disposed) throw new ObjectDisposedException(GetType().FullName);
  }

  internal byte* LibraryHandle
  {
    get
    {
      ThrowIfDisposed();
      return NativeMethods.alpm_pkg_get_handle(BackingStruct);
    }
  }

  public static Package FactoryFromDatabase(void* ptr) => new((byte*)ptr);
  public static Package FactoryNotFromDatabase(void* ptr) => new((byte*)ptr, false);

  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  protected virtual void Dispose(bool disposing)
  {
    if (_disposed) return;
    if (disposing)
    {
      // dispose managed state (managed objects)
    }
    if (!FromDatabase)
    {
      // free package if not loaded from database
      // packages loaded from database are automatically freed
      // if it fails with -1, there's not much we can do
      // program shouldn't abort if this fails
      // we are not going to throw any errors, since users
      // will not catch such error during automatic disposal
      // or when finalizer is called, so just ignore it
      _ = NativeMethods.alpm_pkg_free(BackingStruct);
    }
    _disposed = true;
  }

  ~Package()
  {
    Dispose(false);
  }

  public string Name
  {
    get
    {
      ThrowIfDisposed();
      return field ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_name(BackingStruct))!;
    }
  }

  public bool CheckMd5Sum()
  {
    ThrowIfDisposed();
    return NativeMethods.alpm_pkg_checkmd5sum(BackingStruct) == 0;
  }

  public bool ShouldIgnore()
  {
    ThrowIfDisposed();
    return NativeMethods.alpm_pkg_should_ignore(LibraryHandle, BackingStruct) != 0;
  }

  public string? Filename
  {
    get
    {
      ThrowIfDisposed();
      return field ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_filename(BackingStruct));
    }
  }

  public string? Base
  {
    get
    {
      ThrowIfDisposed();
      return field ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_base(BackingStruct));
    }
  }

  public Version Version
  {
    get
    {
      ThrowIfDisposed();
      return new Version(NativeMethods.alpm_pkg_get_version(BackingStruct));
    }
  }

  public PackageOrigin Origin
  {
    get
    {
      ThrowIfDisposed();
      return (PackageOrigin)(uint)NativeMethods.alpm_pkg_get_origin(BackingStruct);
    }
  }

  public string? Description
  {
    get
    {
      ThrowIfDisposed();
      return field ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_desc(BackingStruct));
    }
  }

  public string? Url
  {
    get
    {
      ThrowIfDisposed();
      return field ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_url(BackingStruct));
    }
  }

  public DateTimeOffset BuildDate
  {
    get
    {
      ThrowIfDisposed();
      return DateTimeOffset.FromUnixTimeSeconds(NativeMethods.alpm_pkg_get_builddate(BackingStruct));
    }
  }

  public DateTimeOffset? InstallDate
  {
    get
    {
      ThrowIfDisposed();
      var date = NativeMethods.alpm_pkg_get_installdate(BackingStruct);
      return date == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(date);
    }
  }

  public string? Packager
  {
    get
    {
      ThrowIfDisposed();
      return field ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_packager(BackingStruct));
    }
  }

  public string? Md5Sum
  {
    get
    {
      ThrowIfDisposed();
      return field ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_md5sum(BackingStruct));
    }
  }

  public string? Sha256Sum
  {
    get
    {
      ThrowIfDisposed();
      return field ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_sha256sum(BackingStruct));
    }
  }

  public string? Arch
  {
    get
    {
      ThrowIfDisposed();
      return field ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_arch(BackingStruct));
    }
  }

  public CLong Size
  {
    get
    {
      ThrowIfDisposed();
      return NativeMethods.alpm_pkg_get_size(BackingStruct);
    }
  }

  public CLong InstalledSize
  {
    get
    {
      ThrowIfDisposed();
      return NativeMethods.alpm_pkg_get_isize(BackingStruct);
    }
  }

  public PackageReason Reason
  {
    get
    {
      ThrowIfDisposed();
      return (PackageReason)(uint)NativeMethods.alpm_pkg_get_reason(BackingStruct);
    }
  }

  public PackageValidation Validation
  {
    get
    {
      ThrowIfDisposed();
      return (PackageValidation)NativeMethods.alpm_pkg_get_validation(BackingStruct);
    }
  }

  public AlpmStringList Licenses
  {
    get
    {
      ThrowIfDisposed();
      return new AlpmStringList(NativeMethods.alpm_pkg_get_licenses(BackingStruct));
    }
  }

  public AlpmStringList Groups
  {
    get
    {
      ThrowIfDisposed();
      return new AlpmStringList(NativeMethods.alpm_pkg_get_groups(BackingStruct));
    }
  }

  public AlpmList<Depend> Depends
  {
    get
    {
      ThrowIfDisposed();
      return Depend.ListFactory(NativeMethods.alpm_pkg_get_depends(BackingStruct));
    }
  }

  public AlpmList<Depend> OptionalDepends
  {
    get
    {
      ThrowIfDisposed();
      return Depend.ListFactory(NativeMethods.alpm_pkg_get_optdepends(BackingStruct));
    }
  }

  public AlpmList<Depend> CheckDepends
  {
    get
    {
      ThrowIfDisposed();
      return Depend.ListFactory(NativeMethods.alpm_pkg_get_checkdepends(BackingStruct));
    }
  }

  public AlpmList<Depend> MakeDepends
  {
    get
    {
      ThrowIfDisposed();
      return Depend.ListFactory(NativeMethods.alpm_pkg_get_makedepends(BackingStruct));
    }
  }

  public AlpmList<Depend> Conflicts
  {
    get
    {
      ThrowIfDisposed();
      return Depend.ListFactory(NativeMethods.alpm_pkg_get_conflicts(BackingStruct));
    }
  }

  public AlpmList<Depend> Provides
  {
    get
    {
      ThrowIfDisposed();
      return Depend.ListFactory(NativeMethods.alpm_pkg_get_provides(BackingStruct));
    }
  }

  public AlpmList<Depend> Replaces
  {
    get
    {
      ThrowIfDisposed();
      return Depend.ListFactory(NativeMethods.alpm_pkg_get_replaces(BackingStruct));
    }
  }

  public FileList Files
  {
    get
    {
      ThrowIfDisposed();
      return new FileList(NativeMethods.alpm_pkg_get_files(BackingStruct));
    }
  }

  public AlpmList<Backup> Backup
  {
    get
    {
      ThrowIfDisposed();
      return Pacpar.Alpm.Backup.ListFactory(NativeMethods.alpm_pkg_get_backup(BackingStruct));
    }
  }

  // TODO: DB

  // TODO: CHANGELOG

  /// <summary>
  /// Packages that require this package.
  /// </summary>
  /// <remarks>
  /// libalpm computes this on demand and the caller owns the result ("a newly allocated list of
  /// package names (char*), it should be freed by the caller"), so the names are copied into a
  /// managed collection and the list plus its strings are freed here.
  /// </remarks>
  public IReadOnlyList<string> GetRequiredBy()
  {
    ThrowIfDisposed();
    return AlpmStringList.TakeOwned(NativeMethods.alpm_pkg_compute_requiredby(BackingStruct),
      &MemoryManagement.CFreeExtern);
  }

  /// <summary>
  /// Packages that optionally require this package. See <see cref="GetRequiredBy"/> for ownership.
  /// </summary>
  public IReadOnlyList<string> GetOptionalFor()
  {
    ThrowIfDisposed();
    return AlpmStringList.TakeOwned(NativeMethods.alpm_pkg_compute_optionalfor(BackingStruct),
      &MemoryManagement.CFreeExtern);
  }

  public string? Base64Signature
  {
    get
    {
      ThrowIfDisposed();
      return field ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_base64_sig(BackingStruct));
    }
  }

  public bool HasScriptlet
  {
    get
    {
      ThrowIfDisposed();
      return NativeMethods.alpm_pkg_has_scriptlet(BackingStruct) != 0;
    }
  }

  private Signature? _signature;
  public Signature GetSignature()
  {
    ThrowIfDisposed();
    if (_signature == null)
    {
      var bufferPtr = (byte**)Marshal.AllocHGlobal(sizeof(byte*));
      var lenPtr = (nuint*)Marshal.AllocHGlobal(sizeof(nuint));
      var result = NativeMethods.alpm_pkg_get_sig(BackingStruct, bufferPtr, lenPtr);
      if (result != 0) throw ErrorHandler.ToException(NativeMethods.alpm_errno(LibraryHandle));
      _signature = new Signature(*bufferPtr, (int)*lenPtr);
    }
    return _signature;
  }
}

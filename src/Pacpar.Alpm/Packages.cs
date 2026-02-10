using System.Runtime.InteropServices;

namespace Pacpar.Alpm;

public unsafe class Version(byte* version) : IComparable<Version>
{
  internal byte* VersionPtr => version;

  public int CompareTo(Version? other) => other == null ? 1 : NativeMethods.alpm_pkg_vercmp(version, other.VersionPtr);

  public override string ToString() => Marshal.PtrToStringAnsi((nint)version)!;
}

public unsafe class Signature(byte* sig, int len) : IDisposable
{
  private bool _disposed = false;

  internal readonly byte* sig = sig;

  internal readonly int len = len;

  public Span<byte> AsSpan => new(sig, len);

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

public unsafe class Package(byte* _backing_struct, bool fromDatabase = true) : IDisposable
{
  internal readonly byte* _backing_struct = _backing_struct;
  private bool _disposed = false;
  public readonly bool FromDatabase = fromDatabase;

  internal byte* LibraryHandle => NativeMethods.alpm_pkg_get_handle(_backing_struct);

  public static Package FactoryFromDatabase(void* ptr) => new((byte*)ptr);
  public static Package FactoryNotFromDatabase(void* ptr) => new((byte*)ptr, false);

  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  protected virtual unsafe void Dispose(bool disposing)
  {
    if (!_disposed)
    {
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
        _ = NativeMethods.alpm_pkg_free(_backing_struct);
      }
      _disposed = true;
    }
  }

  ~Package()
  {
    Dispose(false);
  }

  private string? _name;
  public string Name => _name ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_name(_backing_struct))!;

  public bool CheckMD5Sum() => NativeMethods.alpm_pkg_checkmd5sum(_backing_struct) == 0;

  public bool ShouldIgnore() => NativeMethods.alpm_pkg_should_ignore(LibraryHandle, _backing_struct) != 0;

  private string? _filename;
  public string? Filename => _filename ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_filename(_backing_struct));

  private string? _base;
  public string? Base => _base ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_base(_backing_struct));

  public readonly Version Version = new(NativeMethods.alpm_pkg_get_version(_backing_struct));

  public _alpm_pkgfrom_t Origin => NativeMethods.alpm_pkg_get_origin(_backing_struct);

  private string? desc;
  public string? Description => desc ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_desc(_backing_struct));

  private string? url;
  public string? Url => url ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_url(_backing_struct));

  public DateTimeOffset BuildDate => DateTimeOffset.FromUnixTimeSeconds(NativeMethods.alpm_pkg_get_builddate(_backing_struct));

  public DateTimeOffset? InstallDate
  {
    get
    {
      var date = NativeMethods.alpm_pkg_get_installdate(_backing_struct);
      return date == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(date);
    }
  }

  private string? _packager;
  public string? Packager => _packager ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_packager(_backing_struct));

  private string? _md5sum;
  public string? MD5Sum => _md5sum ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_md5sum(_backing_struct));

  private string? _sha256sum;
  public string? SHA256Sum => _sha256sum ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_sha256sum(_backing_struct));

  private string? _arch;
  public string? Arch => _arch ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_arch(_backing_struct));

  public CLong Size => NativeMethods.alpm_pkg_get_size(_backing_struct);

  public CLong InstalledSize => NativeMethods.alpm_pkg_get_isize(_backing_struct);

  public _alpm_pkgreason_t Reason => NativeMethods.alpm_pkg_get_reason(_backing_struct);

  public PackageValidation Validation => (PackageValidation)NativeMethods.alpm_pkg_get_validation(_backing_struct);

  public AlpmStringList Licenses => new(NativeMethods.alpm_pkg_get_licenses(_backing_struct));

  public AlpmStringList Groups => new(NativeMethods.alpm_pkg_get_groups(_backing_struct));

  public AlpmDisposableList<Depend> Depends => Depend.ListFactory(NativeMethods.alpm_pkg_get_depends(_backing_struct));

  public AlpmDisposableList<Depend> OptionalDepends => Depend.ListFactory(NativeMethods.alpm_pkg_get_optdepends(_backing_struct));

  public AlpmDisposableList<Depend> CheckDepends => Depend.ListFactory(NativeMethods.alpm_pkg_get_checkdepends(_backing_struct));

  public AlpmDisposableList<Depend> MakeDepends => Depend.ListFactory(NativeMethods.alpm_pkg_get_makedepends(_backing_struct));

  public AlpmDisposableList<Depend> Conflicts => Depend.ListFactory(NativeMethods.alpm_pkg_get_conflicts(_backing_struct));

  public AlpmDisposableList<Depend> Provides => Depend.ListFactory(NativeMethods.alpm_pkg_get_provides(_backing_struct));

  public AlpmDisposableList<Depend> Replaces => Depend.ListFactory(NativeMethods.alpm_pkg_get_replaces(_backing_struct));

  public FileList Files => new(NativeMethods.alpm_pkg_get_files(_backing_struct));

  public AlpmList<Backup> Backup => Pacpar.Alpm.Backup.ListFactory(NativeMethods.alpm_pkg_get_backup(_backing_struct));

  // TODO: DB

  // TODO: CHANGELOG

  public AlpmStringList GetRequiredBy() => new(NativeMethods.alpm_pkg_compute_requiredby(_backing_struct));

  public AlpmStringList GetOptionalFor() => new(NativeMethods.alpm_pkg_compute_optionalfor(_backing_struct));

  private string? _base64sig;
  public string? Base64Signature => _base64sig ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_base64_sig(_backing_struct));

  public bool HasScriptlet => NativeMethods.alpm_pkg_has_scriptlet(_backing_struct) != 0;

  private Signature? _signature;
  public Signature GetSignature()
  {
    if (_signature == null)
    {
      var bufferPtr = (byte**)Marshal.AllocHGlobal(sizeof(byte*));
      var lenPtr = (nuint*)Marshal.AllocHGlobal(sizeof(nuint));
      var result = NativeMethods.alpm_pkg_get_sig(_backing_struct, bufferPtr, lenPtr);
      if (result != 0) throw ErrorHandler.GetException(NativeMethods.alpm_errno(LibraryHandle))!;
      _signature = new(*bufferPtr, (int)*lenPtr);
    }
    return _signature;
  }
}

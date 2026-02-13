using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.list;

namespace Pacpar.Alpm;

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

  internal byte* LibraryHandle => NativeMethods.alpm_pkg_get_handle(BackingStruct);

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

  private string? _name;
  public string Name => _name ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_name(BackingStruct))!;

  public bool CheckMd5Sum() => NativeMethods.alpm_pkg_checkmd5sum(BackingStruct) == 0;

  public bool ShouldIgnore() => NativeMethods.alpm_pkg_should_ignore(LibraryHandle, BackingStruct) != 0;

  private string? _filename;
  public string? Filename => _filename ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_filename(BackingStruct));

  private string? _base;
  public string? Base => _base ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_base(BackingStruct));

  public readonly Version Version = new(NativeMethods.alpm_pkg_get_version(backingStruct));

  public _alpm_pkgfrom_t Origin => NativeMethods.alpm_pkg_get_origin(BackingStruct);

  private string? _desc;
  public string? Description => _desc ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_desc(BackingStruct));

  private string? _url;
  public string? Url => _url ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_url(BackingStruct));

  public DateTimeOffset BuildDate => DateTimeOffset.FromUnixTimeSeconds(NativeMethods.alpm_pkg_get_builddate(BackingStruct));

  public DateTimeOffset? InstallDate
  {
    get
    {
      var date = NativeMethods.alpm_pkg_get_installdate(BackingStruct);
      return date == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(date);
    }
  }

  private string? _packager;
  public string? Packager => _packager ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_packager(BackingStruct));

  private string? _md5Sum;
  public string? Md5Sum => _md5Sum ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_md5sum(BackingStruct));

  private string? _sha256Sum;
  public string? Sha256Sum => _sha256Sum ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_sha256sum(BackingStruct));

  private string? _arch;
  public string? Arch => _arch ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_arch(BackingStruct));

  public CLong Size => NativeMethods.alpm_pkg_get_size(BackingStruct);

  public CLong InstalledSize => NativeMethods.alpm_pkg_get_isize(BackingStruct);

  public _alpm_pkgreason_t Reason => NativeMethods.alpm_pkg_get_reason(BackingStruct);

  public PackageValidation Validation => (PackageValidation)NativeMethods.alpm_pkg_get_validation(BackingStruct);

  public AlpmStringList Licenses => new(NativeMethods.alpm_pkg_get_licenses(BackingStruct));

  public AlpmStringList Groups => new(NativeMethods.alpm_pkg_get_groups(BackingStruct));

  public AlpmDisposableList<Depend> Depends => Depend.ListFactory(NativeMethods.alpm_pkg_get_depends(BackingStruct));

  public AlpmDisposableList<Depend> OptionalDepends => Depend.ListFactory(NativeMethods.alpm_pkg_get_optdepends(BackingStruct));

  public AlpmDisposableList<Depend> CheckDepends => Depend.ListFactory(NativeMethods.alpm_pkg_get_checkdepends(BackingStruct));

  public AlpmDisposableList<Depend> MakeDepends => Depend.ListFactory(NativeMethods.alpm_pkg_get_makedepends(BackingStruct));

  public AlpmDisposableList<Depend> Conflicts => Depend.ListFactory(NativeMethods.alpm_pkg_get_conflicts(BackingStruct));

  public AlpmDisposableList<Depend> Provides => Depend.ListFactory(NativeMethods.alpm_pkg_get_provides(BackingStruct));

  public AlpmDisposableList<Depend> Replaces => Depend.ListFactory(NativeMethods.alpm_pkg_get_replaces(BackingStruct));

  public FileList Files => new(NativeMethods.alpm_pkg_get_files(BackingStruct));

  public AlpmList<Backup> Backup => Bindings.Backup.ListFactory(NativeMethods.alpm_pkg_get_backup(BackingStruct));

  // TODO: DB

  // TODO: CHANGELOG

  public AlpmStringList GetRequiredBy() => new(NativeMethods.alpm_pkg_compute_requiredby(BackingStruct));

  public AlpmStringList GetOptionalFor() => new(NativeMethods.alpm_pkg_compute_optionalfor(BackingStruct));

  private string? _base64Sig;
  public string? Base64Signature => _base64Sig ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_pkg_get_base64_sig(BackingStruct));

  public bool HasScriptlet => NativeMethods.alpm_pkg_has_scriptlet(BackingStruct) != 0;

  private Signature? _signature;
  public Signature GetSignature()
  {
    if (_signature == null)
    {
      var bufferPtr = (byte**)Marshal.AllocHGlobal(sizeof(byte*));
      var lenPtr = (nuint*)Marshal.AllocHGlobal(sizeof(nuint));
      var result = NativeMethods.alpm_pkg_get_sig(BackingStruct, bufferPtr, lenPtr);
      if (result != 0) throw ErrorHandler.GetException(NativeMethods.alpm_errno(LibraryHandle))!;
      _signature = new(*bufferPtr, (int)*lenPtr);
    }
    return _signature;
  }
}

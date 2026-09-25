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

/// <summary>
/// A package version (<c>alpm_pkg_get_version</c>), ordered by libalpm's own
/// <c>alpm_pkg_vercmp</c>.
/// </summary>
/// <remarks>
/// A managed snapshot: the version text is copied on construction, so a value taken from
/// <see cref="Package.Version"/> stays readable after the package that produced it is gone.
/// libalpm can only compare native strings, so <see cref="CompareTo"/> marshals both operands for
/// the duration of the call instead of holding a pointer to package-owned memory.
/// </remarks>
public class Version : IComparable<Version>
{
  private readonly string _value;

  internal unsafe Version(byte* version)
  {
    _value = NativeString.FromNative((nint)version) ?? string.Empty;
  }

  public unsafe int CompareTo(Version? other)
  {
    if (other == null) return 1;

    var left = NativeString.ToNative(_value);
    try
    {
      var right = NativeString.ToNative(other._value);
      try
      {
        return NativeMethods.alpm_pkg_vercmp(left, right);
      }
      finally
      {
        Marshal.FreeHGlobal((nint)right);
      }
    }
    finally
    {
      Marshal.FreeHGlobal((nint)left);
    }
  }

  public override string ToString() => _value;
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

/// <summary>
/// The read-only surface of a package: everything libalpm exposes about one, whoever owns it.
/// </summary>
/// <remarks>
/// There are exactly two concrete kinds, and they are deliberately <b>siblings</b> rather than one
/// deriving from the other:
/// <list type="bullet">
/// <item><description><see cref="Package"/> - libalpm owns it (a database package, a transaction
/// member, one reached through a group). It is not <see cref="IDisposable"/>.</description></item>
/// <item><description><see cref="LoadedPackage"/> - this library loaded it from a file and owns it,
/// so it is <see cref="IDisposable"/> and can hand the ownership to a transaction.</description></item>
/// </list>
/// Because neither type converts to the other, an overload pair such as
/// <see cref="Transactions.AddPackage(Package)"/> / <see cref="Transactions.AddPackage(LoadedPackage)"/>
/// cannot be reached with the wrong kind: the compiler picks the borrow overload for a database
/// package and the ownership-transferring one for a file package, and no run-time check is needed.
/// This base type is the common parameter type for code that only reads.
/// <para>
/// Scalar metadata libalpm may report as absent is cached behind an explicit boolean flag rather than
/// <c>field ??=</c>: coalescing only re-runs the native call while the field is null, so an absent value - a
/// local package has no <see cref="Filename"/>, a sync package no <see cref="Md5Sum"/> - would cross the
/// interop boundary again on every read. The flag makes the miss happen exactly once.
/// </para>
/// </remarks>
public abstract unsafe class PackageBase
{
  internal readonly _alpm_pkg_t* BackingStruct;

  private protected PackageBase(_alpm_pkg_t* backingStruct)
  {
    BackingStruct = backingStruct;
  }

  /// <summary>
  /// Set by <see cref="LoadedPackage"/> once it has released the package or handed it to a
  /// transaction. It retires the wrapper as a whole, reads included: after a hand-over the pointer
  /// stays valid only until the transaction is released, which this wrapper cannot observe.
  /// </summary>
  private protected bool Disposed;

  private protected void ThrowIfDisposed()
  {
    if (Disposed) throw new ObjectDisposedException(GetType().FullName);
  }

  internal _alpm_handle_t* LibraryHandle
  {
    get
    {
      ThrowIfDisposed();
      return NativeMethods.alpm_pkg_get_handle(BackingStruct);
    }
  }

  /// <remarks>
  /// libalpm always sets a package name, so the coalescing form has no miss to repeat; every property
  /// that can be absent uses a flag instead (see <see cref="PackageBase"/>).
  /// </remarks>
  public string Name
  {
    get
    {
      ThrowIfDisposed();
      return field ??= NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_name(BackingStruct))!;
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

  private string? _filename;
  private bool _filenameLoaded;

  public string? Filename
  {
    get
    {
      ThrowIfDisposed();
      if (!_filenameLoaded)
      {
        _filename = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_filename(BackingStruct));
        _filenameLoaded = true;
      }
      return _filename;
    }
  }

  private string? _base;
  private bool _baseLoaded;

  public string? Base
  {
    get
    {
      ThrowIfDisposed();
      if (!_baseLoaded)
      {
        _base = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_base(BackingStruct));
        _baseLoaded = true;
      }
      return _base;
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

  private string? _description;
  private bool _descriptionLoaded;

  public string? Description
  {
    get
    {
      ThrowIfDisposed();
      if (!_descriptionLoaded)
      {
        _description = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_desc(BackingStruct));
        _descriptionLoaded = true;
      }
      return _description;
    }
  }

  private string? _url;
  private bool _urlLoaded;

  public string? Url
  {
    get
    {
      ThrowIfDisposed();
      if (!_urlLoaded)
      {
        _url = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_url(BackingStruct));
        _urlLoaded = true;
      }
      return _url;
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

  private string? _packager;
  private bool _packagerLoaded;

  public string? Packager
  {
    get
    {
      ThrowIfDisposed();
      if (!_packagerLoaded)
      {
        _packager = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_packager(BackingStruct));
        _packagerLoaded = true;
      }
      return _packager;
    }
  }

  private string? _md5Sum;
  private bool _md5SumLoaded;

  public string? Md5Sum
  {
    get
    {
      ThrowIfDisposed();
      if (!_md5SumLoaded)
      {
        _md5Sum = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_md5sum(BackingStruct));
        _md5SumLoaded = true;
      }
      return _md5Sum;
    }
  }

  private string? _sha256Sum;
  private bool _sha256SumLoaded;

  public string? Sha256Sum
  {
    get
    {
      ThrowIfDisposed();
      if (!_sha256SumLoaded)
      {
        _sha256Sum = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_sha256sum(BackingStruct));
        _sha256SumLoaded = true;
      }
      return _sha256Sum;
    }
  }

  private string? _arch;
  private bool _archLoaded;

  public string? Arch
  {
    get
    {
      ThrowIfDisposed();
      if (!_archLoaded)
      {
        _arch = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_arch(BackingStruct));
        _archLoaded = true;
      }
      return _arch;
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

  /// <summary>
  /// The package's file list, read on demand.
  /// </summary>
  /// <remarks>
  /// Deliberately neither cached nor read at construction: eagerly loading it for every package costs
  /// about two orders of magnitude more than reading the rest of the metadata, and it is the first
  /// accessor the audit report singles out. <see cref="ToSnapshot(bool)"/> copies it only on request.
  /// </remarks>
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

  private string? _base64Signature;
  private bool _base64SignatureLoaded;

  public string? Base64Signature
  {
    get
    {
      ThrowIfDisposed();
      if (!_base64SignatureLoaded)
      {
        _base64Signature = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_base64_sig(BackingStruct));
        _base64SignatureLoaded = true;
      }
      return _base64Signature;
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

  private byte[]? _signature;
  private bool _signatureLoaded;

  /// <summary>
  /// The package's embedded PGP signature, or <c>null</c> when it carries none.
  /// </summary>
  /// <remarks>
  /// libalpm hands the signature out in a buffer the caller must release, so this returns a managed
  /// copy instead: a caller cannot leak the buffer, cannot read it after a double dispose, and no
  /// finalizer is involved. The copy happens once and every later call answers from the cached array.
  /// <see cref="Base64Signature"/> is the same data in the other encoding libalpm stores.
  /// </remarks>
  public ReadOnlyMemory<byte>? GetSignature()
  {
    ThrowIfDisposed();

    if (!_signatureLoaded)
    {
      byte* buffer = null;
      nuint len;
      var result = NativeMethods.alpm_pkg_get_sig(BackingStruct, &buffer, &len);
      if (result != 0) throw ErrorHandler.ToException(NativeMethods.alpm_errno(LibraryHandle));

      if (buffer != null)
      {
        _signature = new Span<byte>(buffer, (int)len).ToArray();
        MemoryManagement.CFree(buffer);
      }

      _signatureLoaded = true;
    }

    return _signature is null ? null : new ReadOnlyMemory<byte>(_signature);
  }

  /// <summary>
  /// Copies everything this package reads into a <see cref="PackageSnapshot"/> with no ties to
  /// libalpm, so the result stays valid after the database, transaction or handle it came from is gone.
  /// </summary>
  /// <param name="includeFiles">
  /// Also copies the file list. It is off by default because it is by far the most expensive part of
  /// the copy and most callers do not need it.
  /// </param>
  public PackageSnapshot ToSnapshot(bool includeFiles = false) => new(this, includeFiles);
}

/// <summary>
/// A package libalpm owns: a database package, a transaction member, or one reached through a group.
/// </summary>
/// <remarks>
/// Nothing here frees it, which is why this type is deliberately not <see cref="IDisposable"/>; a
/// package this library loaded from a file is a <see cref="LoadedPackage"/> instead, and the two do
/// not convert to one another (see <see cref="PackageBase"/>).
/// <para>
/// Being a view over libalpm's memory is exactly what makes bulk scans cheap, so keep it that way: read
/// the view while scanning, and call <see cref="ToSnapshot"/> only for the packages that must outlive
/// the scan.
/// </para>
/// </remarks>
public sealed unsafe class Package : PackageBase
{
  internal Package(_alpm_pkg_t* backingStruct) : base(backingStruct)
  {
  }

  internal static Package Factory(void* ptr) => new((_alpm_pkg_t*)ptr);
}

/// <summary>
/// A package this library loaded from a file with <c>alpm_pkg_load</c>. It owns the package and
/// releases it on <see cref="Dispose"/>, or from the finalizer when the caller forgets.
/// </summary>
/// <remarks>
/// Ownership can also be handed to a transaction, which takes over the release
/// (<see cref="Transactions.AddPackage(LoadedPackage)"/> and <c>alpm.h</c>: a package loaded by
/// <c>alpm_pkg_load()</c> is freed upon <c>alpm_trans_release</c>). After that hand-over this
/// instance is inert: <see cref="Dispose"/> and the finalizer do nothing, and reading the package
/// throws, so the pointer cannot be released twice.
/// </remarks>
public sealed unsafe class LoadedPackage : PackageBase, IDisposable
{
  internal LoadedPackage(_alpm_pkg_t* backingStruct) : base(backingStruct)
  {
  }
  /// <summary>Whether this instance still owns the package (it stops owning it on dispose or hand-over).</summary>
  internal bool OwnsPackage => !Disposed;

  /// <summary>Throws when the package was already released or handed to a transaction.</summary>
  /// <remarks>
  /// Checked by the caller <i>before</i> it touches libalpm: a hand-over that already happened is a
  /// caller error, and repeating the native call would be pointless (libalpm dedupes the same
  /// package pointer in a transaction's list anyway, probed).
  /// </remarks>
  internal void ThrowIfNotOwned()
  {
    if (!OwnsPackage) throw new ObjectDisposedException(GetType().FullName);
  }

  /// <summary>
  /// Gives up ownership <b>without</b> releasing the package, because somebody else owns the pointer
  /// now and releases it. <see cref="Dispose"/> and the finalizer become no-ops.
  /// </summary>
  /// <remarks>
  /// Named "disown" rather than "release" on purpose: nothing is freed here. Handing the package to
  /// a transaction is the only caller, and it must happen after the native call succeeded - the
  /// caller keeps ownership when it fails.
  /// </remarks>
  internal void Disown()
  {
    ThrowIfNotOwned();
    Disposed = true;
    GC.SuppressFinalize(this);
  }

  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  private void Dispose(bool disposing)
  {
    if (Disposed) return;

    // A failing alpm_pkg_free leaves nothing useful to do: the caller either disposed explicitly
    // or the finalizer is running, and neither can handle a thrown error.
    _ = NativeMethods.alpm_pkg_free(BackingStruct);
    Disposed = true;
  }

  ~LoadedPackage() => Dispose(false);
}

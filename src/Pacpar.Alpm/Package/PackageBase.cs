using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

/// <summary>
/// The read-only surface of a package: everything libalpm exposes about one, whoever owns it.
/// </summary>
/// <remarks>
/// There are exactly two concrete kinds, and they are deliberately <b>siblings</b> rather than one
/// deriving from the other:
/// <list type="bullet">
/// <item><description><see cref="PackageView"/> - libalpm owns it (a database package, a transaction
/// member, one reached through a group). It is not <see cref="IDisposable"/>.</description></item>
/// <item><description><see cref="LoadedPackage"/> - this library loaded it from a file and owns it,
/// so it is <see cref="IDisposable"/> and can hand the ownership to a transaction.</description></item>
/// </list>
/// Because neither type converts to the other, an overload pair such as
/// <see cref="Transaction.AddPackage(PackageView)"/> / <see cref="Transaction.AddPackage(LoadedPackage)"/>
/// cannot be reached with the wrong kind: the compiler picks the borrow overload for a database
/// package and the ownership-transferring one for a file package, and no run-time check is needed.
/// This base type is the common parameter type for code that only reads.
/// <para>
/// Scalar metadata libalpm may report as absent is cached behind an explicit boolean flag rather than
/// <c>field ??=</c>: coalescing only re-runs the native call while the field is null, so an absent value - a
/// local package has no <see cref="Filename"/>, a sync package no <see cref="Md5Sum"/> - would cross the
/// interop boundary again on every read. The flag makes the miss happen exactly once.
/// </para>
/// <para>
/// Every read goes through <see cref="ThrowIfDisposed"/>, which also verifies the <see cref="Lifetime"/>
/// token of the native context that owns <see cref="BackingStruct"/>: reading a package whose database,
/// transaction or handle was released throws <see cref="AlpmLifetimeException"/> instead of touching
/// freed memory.
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
  /// The lifetime token of the native context that owns <see cref="BackingStruct"/> - the database,
  /// transaction or handle this package was borrowed from, or this instance itself for a
  /// <see cref="LoadedPackage"/>. <c>null</c> means the wrapper carries no owning context to check.
  /// </summary>
  /// <remarks>
  /// Not <c>readonly</c> on purpose: a <see cref="LoadedPackage"/> creates its root token in its
  /// constructor body, because <c>this</c> is not available to a base constructor initializer.
  /// </remarks>
  private protected Lifetime? Lifetime;

  /// <summary>
  /// Set by <see cref="LoadedPackage"/> once it has released the package or handed it to a
  /// transaction. It retires the wrapper as a whole, reads included: after a hand-over the pointer
  /// stays valid only until the transaction is released, which this wrapper cannot observe.
  /// </summary>
  private protected bool Disposed;

  private protected void ThrowIfDisposed()
  {
    // The retirement flag wins: a disposed or handed-over wrapper reports ObjectDisposedException
    // even when its owning context is gone too; the token check covers contexts released while
    // this wrapper was still nominally usable.
    if (Disposed) throw new ObjectDisposedException(GetType().FullName);
    Lifetime?.ThrowIfStale();
  }

  internal _alpm_handle_t* LibraryHandle
  {
    get
    {
      ThrowIfDisposed();
      var handle = NativeMethods.alpm_pkg_get_handle(BackingStruct);
      GC.KeepAlive(this);
      return handle;
    }
  }

  /// <summary>
  /// The package name.
  /// </summary>
  public string Name
  {
    get
    {
      ThrowIfDisposed();
      return field ??= NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_name(BackingStruct))!;
    }
  }

  /// <summary>
  /// Checks whether the package's MD5 checksum matches the expected value.
  /// </summary>
  public bool CheckMd5Sum()
  {
    ThrowIfDisposed();
    var ok = NativeMethods.alpm_pkg_checkmd5sum(BackingStruct) == 0;
    GC.KeepAlive(this);
    return ok;
  }

  /// <summary>
  /// Checks whether this package should be ignored according to the library configuration.
  /// </summary>
  public bool ShouldIgnore()
  {
    ThrowIfDisposed();
    var ignore = NativeMethods.alpm_pkg_should_ignore(LibraryHandle, BackingStruct) != 0;
    GC.KeepAlive(this);
    return ignore;
  }

  private string? _filename;
  private bool _filenameLoaded;

  /// <summary>
  /// The archive filename of the package, or <c>null</c> if not applicable (such as for local database packages).
  /// </summary>
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

  /// <summary>
  /// The base package name (pkgbase) if part of a split package, or <c>null</c> if none.
  /// </summary>
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

  /// <summary>
  /// The version of the package.
  /// </summary>
  public PackageVersion Version
  {
    get
    {
      ThrowIfDisposed();
      var version = new PackageVersion(NativeMethods.alpm_pkg_get_version(BackingStruct));
      GC.KeepAlive(this);
      return version;
    }
  }

  /// <summary>
  /// The origin indicating where this package was loaded from.
  /// </summary>
  public PackageOrigin Origin
  {
    get
    {
      ThrowIfDisposed();
      var origin = (PackageOrigin)(uint)NativeMethods.alpm_pkg_get_origin(BackingStruct);
      GC.KeepAlive(this);
      return origin;
    }
  }
  private string? _description;
  private bool _descriptionLoaded;

  /// <summary>
  /// The description of the package.
  /// </summary>
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

  /// <summary>
  /// The upstream project website or homepage URL.
  /// </summary>
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

  /// <summary>
  /// The date and time when the package was built.
  /// </summary>
  public DateTimeOffset BuildDate
  {
    get
    {
      ThrowIfDisposed();
      var date = DateTimeOffset.FromUnixTimeSeconds(NativeMethods.alpm_pkg_get_builddate(BackingStruct));
      GC.KeepAlive(this);
      return date;
    }
  }

  /// <summary>
  /// The date and time when the package was installed locally, or <c>null</c> if not installed.
  /// </summary>
  public DateTimeOffset? InstallDate
  {
    get
    {
      ThrowIfDisposed();
      var date = NativeMethods.alpm_pkg_get_installdate(BackingStruct);
      GC.KeepAlive(this);
      return date == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(date);
    }
  }

  private string? _packager;
  private bool _packagerLoaded;

  /// <summary>
  /// The name and contact information of the packager.
  /// </summary>
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

  /// <summary>
  /// The MD5 checksum of the package archive, or <c>null</c> if not available.
  /// </summary>
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

  /// <summary>
  /// The SHA-256 checksum of the package archive, or <c>null</c> if not available.
  /// </summary>
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

  /// <summary>
  /// The target CPU architecture for this package (e.g. "x86_64", "any").
  /// </summary>
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

  /// <summary>
  /// The download size of the package archive in bytes.
  /// </summary>
  public long Size
  {
    get
    {
      ThrowIfDisposed();
      var size = NativeMethods.alpm_pkg_get_size(BackingStruct);
      GC.KeepAlive(this);
      return size.Value;
    }
  }

  /// <summary>
  /// The unpacked size of the package when installed on disk, in bytes.
  /// </summary>
  public long InstalledSize
  {
    get
    {
      ThrowIfDisposed();
      var installedSize = NativeMethods.alpm_pkg_get_isize(BackingStruct);
      GC.KeepAlive(this);
      return installedSize.Value;
    }
  }

  /// <summary>
  /// The reason why this package is installed (explicitly requested or installed as a dependency).
  /// </summary>
  public PackageReason Reason
  {
    get
    {
      ThrowIfDisposed();
      var reason = (PackageReason)(uint)NativeMethods.alpm_pkg_get_reason(BackingStruct);
      GC.KeepAlive(this);
      return reason;
    }
  }

  /// <summary>
  /// The validation methods used to verify this package.
  /// </summary>
  public PackageValidation Validation
  {
    get
    {
      ThrowIfDisposed();
      var val = (PackageValidation)NativeMethods.alpm_pkg_get_validation(BackingStruct);
      GC.KeepAlive(this);
      return val;
    }
  }
  /// <summary>
  /// The licenses governing the distribution and use of this package.
  /// </summary>
  public AlpmStringList Licenses
  {
    get
    {
      ThrowIfDisposed();
      return new AlpmStringList(NativeMethods.alpm_pkg_get_licenses(BackingStruct), Lifetime);
    }
  }

  /// <summary>
  /// The package groups that this package belongs to.
  /// </summary>
  public AlpmStringList Groups
  {
    get
    {
      ThrowIfDisposed();
      return new AlpmStringList(NativeMethods.alpm_pkg_get_groups(BackingStruct), Lifetime);
    }
  }

  /// <summary>
  /// The list of packages required to run this package.
  /// </summary>
  public AlpmList<Depend> Depends
  {
    get
    {
      ThrowIfDisposed();
      return Depend.ListFactory(NativeMethods.alpm_pkg_get_depends(BackingStruct), Lifetime);
    }
  }

  /// <summary>
  /// The list of optional packages that provide additional functionality.
  /// </summary>
  public AlpmList<Depend> OptionalDepends
  {
    get
    {
      ThrowIfDisposed();
      return Depend.ListFactory(NativeMethods.alpm_pkg_get_optdepends(BackingStruct), Lifetime);
    }
  }

  /// <summary>
  /// The list of dependencies required only to run the test suite when building.
  /// </summary>
  public AlpmList<Depend> CheckDepends
  {
    get
    {
      ThrowIfDisposed();
      return Depend.ListFactory(NativeMethods.alpm_pkg_get_checkdepends(BackingStruct), Lifetime);
    }
  }

  /// <summary>
  /// The list of dependencies required only to build the package from source.
  /// </summary>
  public AlpmList<Depend> MakeDepends
  {
    get
    {
      ThrowIfDisposed();
      return Depend.ListFactory(NativeMethods.alpm_pkg_get_makedepends(BackingStruct), Lifetime);
    }
  }

  /// <summary>
  /// The list of packages that conflict with this package.
  /// </summary>
  public AlpmList<Depend> Conflicts
  {
    get
    {
      ThrowIfDisposed();
      return Depend.ListFactory(NativeMethods.alpm_pkg_get_conflicts(BackingStruct), Lifetime);
    }
  }

  /// <summary>
  /// The virtual provisions or features provided by this package.
  /// </summary>
  public AlpmList<Depend> Provides
  {
    get
    {
      ThrowIfDisposed();
      return Depend.ListFactory(NativeMethods.alpm_pkg_get_provides(BackingStruct), Lifetime);
    }
  }

  /// <summary>
  /// The list of packages that this package replaces.
  /// </summary>
  public AlpmList<Depend> Replaces
  {
    get
    {
      ThrowIfDisposed();
      return Depend.ListFactory(NativeMethods.alpm_pkg_get_replaces(BackingStruct), Lifetime);
    }
  }

  /// <summary>
  /// The package's file list, read on demand.
  /// </summary>
  public FileList Files
  {
    get
    {
      ThrowIfDisposed();
      return new FileList(NativeMethods.alpm_pkg_get_files(BackingStruct), Lifetime);
    }
  }

  /// <summary>
  /// The list of configuration files marked for backup.
  /// </summary>
  public AlpmList<Backup> Backup
  {
    get
    {
      ThrowIfDisposed();
      return Pacpar.Alpm.Backup.ListFactory(NativeMethods.alpm_pkg_get_backup(BackingStruct), Lifetime);
    }
  }

  /// <summary>
  /// Gets the names of packages that depend on this package.
  /// </summary>
  public IReadOnlyList<string> GetRequiredBy()
  {
    ThrowIfDisposed();
    var list = AlpmStringList.TakeOwned(NativeMethods.alpm_pkg_compute_requiredby(BackingStruct),
      &MemoryManagement.CFreeExtern);
    GC.KeepAlive(this);
    return list;
  }

  /// <summary>
  /// Gets the names of packages that optionally depend on this package.
  /// </summary>
  public IReadOnlyList<string> GetOptionalFor()
  {
    ThrowIfDisposed();
    var list = AlpmStringList.TakeOwned(NativeMethods.alpm_pkg_compute_optionalfor(BackingStruct),
      &MemoryManagement.CFreeExtern);
    GC.KeepAlive(this);
    return list;
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
      var has = NativeMethods.alpm_pkg_has_scriptlet(BackingStruct) != 0;
      GC.KeepAlive(this);
      return has;
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
      if (result != 0)
      {
        var ex = ErrorHandler.ToException(NativeMethods.alpm_errno(LibraryHandle));
        GC.KeepAlive(this);
        throw ex;
      }
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

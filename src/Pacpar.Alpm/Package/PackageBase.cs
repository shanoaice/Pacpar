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
  /// interop boundary again on every read. The flag makes the miss happen exactly once. Caching has
  /// one consequence worth stating: a member that answers from its cache never evaluates
  /// <see cref="BackingStruct"/>, so the guarded accessor - and with it the stamp check - would not
  /// run at all. Every cached member therefore calls <see cref="ThrowIfDisposed"/> itself.
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
  // Only this class and its derived types can see the pointer. A sibling in the same assembly
  // (Transaction, for one) cannot read it, so "reach for the pointer without the guard" is not a
  // shape that can be written any more.
  private protected PackageBase(_alpm_pkg_t* backingStruct)
  {
    BackingStruct = backingStruct;
  }

  /// <summary>
  /// The native pointer this wrapper reads.
  /// </summary>
  /// <remarks>
  /// The backing field is compiler-generated (C# 14 <c>field</c>), so it has no name any member could
  /// read: the only way to the pointer is this accessor, which runs the guard first. That closes the
  /// gap the previous shape left - a member that read the field directly skipped the guard, and no
  /// compiler check could catch it.
  /// </remarks>
  internal _alpm_pkg_t* BackingStruct
  {
    get
    {
      ThrowIfDisposed();
      return field;
    }

    // Assignable from this class's constructor only: a wrapper's pointer is fixed for its lifetime.
    private init;
  }

  /// <summary>
  /// The lifetime token of the native context that owns <see cref="BackingStruct"/> - the database,
  /// transaction or handle this package was borrowed from, or - for a <see cref="LoadedPackage"/> -
  /// the domain of the package this instance owns, which the session that loaded it anchors.
  /// <c>null</c> means the wrapper carries no owning context to check.
  /// </summary>
  /// <remarks>
  /// Not <c>readonly</c> on purpose: a <see cref="LoadedPackage"/> creates its root token in its
  /// constructor body, because <c>this</c> is not available to a base constructor initializer.
  /// </remarks>
  private protected LifetimeStamp? Lifetime;

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
      // This one keeps an explicit guard: `field ??=` returns the cached name without evaluating its
      // right-hand side, so on every read after the first the guarded accessor never runs. A member
      // that can answer from a cache has to check the stamp itself.
      ThrowIfDisposed();
      return field ??= NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_name(BackingStruct))!;
    }
  }

  /// <summary>
  /// Checks whether the package's MD5 checksum matches the expected value.
  /// </summary>
  public bool CheckMd5Sum()
  {
    var ok = NativeMethods.alpm_pkg_checkmd5sum(BackingStruct) == 0;
    GC.KeepAlive(this);
    return ok;
  }

  /// <summary>
  /// Checks whether this package should be ignored according to the library configuration.
  /// </summary>
  public bool ShouldIgnore()
  {
    var ignore = NativeMethods.alpm_pkg_should_ignore(LibraryHandle, BackingStruct) != 0;
    GC.KeepAlive(this);
    return ignore;
  }

  private bool _filenameLoaded;

  /// <summary>
  /// The archive filename of the package, or <c>null</c> if not applicable (such as for local database packages).
  /// </summary>
  public string? Filename
  {
    get
    {
      // See Name: a cache hit returns before the guarded accessor runs, so check the stamp here.
      ThrowIfDisposed();
      if (_filenameLoaded) return field;
      field = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_filename(BackingStruct));
      _filenameLoaded = true;

      return field;
    }
  }

  private bool _baseLoaded;

  /// <summary>
  /// The base package name (pkgbase) if part of a split package, or <c>null</c> if none.
  /// </summary>
  public string? Base
  {
    get
    {
      // See Name: a cache hit returns before the guarded accessor runs, so check the stamp here.
      ThrowIfDisposed();
      if (!_baseLoaded)
      {
        field = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_base(BackingStruct));
        _baseLoaded = true;
      }

      return field;
    }
  }

  /// <summary>
  /// The version of the package.
  /// </summary>
  public PackageVersion Version
  {
    get
    {
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
      var origin = (PackageOrigin)(uint)NativeMethods.alpm_pkg_get_origin(BackingStruct);
      GC.KeepAlive(this);
      return origin;
    }
  }

  private bool _descriptionLoaded;

  /// <summary>
  /// The description of the package.
  /// </summary>
  public string? Description
  {
    get
    {
      // See Name: a cache hit returns before the guarded accessor runs, so check the stamp here.
      ThrowIfDisposed();
      if (!_descriptionLoaded)
      {
        field = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_desc(BackingStruct));
        _descriptionLoaded = true;
      }

      return field;
    }
  }

  private bool _urlLoaded;

  /// <summary>
  /// The upstream project website or homepage URL.
  /// </summary>
  public string? Url
  {
    get
    {
      // See Name: a cache hit returns before the guarded accessor runs, so check the stamp here.
      ThrowIfDisposed();
      if (!_urlLoaded)
      {
        field = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_url(BackingStruct));
        _urlLoaded = true;
      }

      return field;
    }
  }

  /// <summary>
  /// The date and time when the package was built.
  /// </summary>
  public DateTimeOffset BuildDate
  {
    get
    {
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
      var date = NativeMethods.alpm_pkg_get_installdate(BackingStruct);
      GC.KeepAlive(this);
      return date == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(date);
    }
  }

  private bool _packagerLoaded;

  /// <summary>
  /// The name and contact information of the packager.
  /// </summary>
  public string? Packager
  {
    get
    {
      // See Name: a cache hit returns before the guarded accessor runs, so check the stamp here.
      ThrowIfDisposed();
      if (!_packagerLoaded)
      {
        field = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_packager(BackingStruct));
        _packagerLoaded = true;
      }

      return field;
    }
  }

  private bool _md5SumLoaded;

  /// <summary>
  /// The MD5 checksum of the package archive, or <c>null</c> if not available.
  /// </summary>
  public string? Md5Sum
  {
    get
    {
      // See Name: a cache hit returns before the guarded accessor runs, so check the stamp here.
      ThrowIfDisposed();
      if (!_md5SumLoaded)
      {
        field = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_md5sum(BackingStruct));
        _md5SumLoaded = true;
      }

      return field;
    }
  }

  private bool _sha256SumLoaded;

  /// <summary>
  /// The SHA-256 checksum of the package archive, or <c>null</c> if not available.
  /// </summary>
  public string? Sha256Sum
  {
    get
    {
      // See Name: a cache hit returns before the guarded accessor runs, so check the stamp here.
      ThrowIfDisposed();
      if (!_sha256SumLoaded)
      {
        field = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_sha256sum(BackingStruct));
        _sha256SumLoaded = true;
      }

      return field;
    }
  }

  private bool _archLoaded;

  /// <summary>
  /// The target CPU architecture for this package (e.g. "x86_64", "any").
  /// </summary>
  public string? Arch
  {
    get
    {
      // See Name: a cache hit returns before the guarded accessor runs, so check the stamp here.
      ThrowIfDisposed();
      if (!_archLoaded)
      {
        field = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_arch(BackingStruct));
        _archLoaded = true;
      }

      return field;
    }
  }

  /// <summary>
  /// The download size of the package archive in bytes.
  /// </summary>
  public long Size
  {
    get
    {
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
      var val = (PackageValidation)NativeMethods.alpm_pkg_get_validation(BackingStruct);
      GC.KeepAlive(this);
      return val;
    }
  }
  /// <summary>
  /// The licenses governing the distribution and use of this package.
  /// </summary>
  public AlpmStringList Licenses => new(NativeMethods.alpm_pkg_get_licenses(BackingStruct), Lifetime?.Domain);

  /// <summary>
  /// The package groups that this package belongs to.
  /// </summary>
  public AlpmStringList Groups => new(NativeMethods.alpm_pkg_get_groups(BackingStruct), Lifetime?.Domain);

  /// <summary>
  /// The list of packages required to run this package.
  /// </summary>
  public AlpmList<Depend> Depends => Depend.ListFactory(NativeMethods.alpm_pkg_get_depends(BackingStruct), Lifetime?.Domain);

  /// <summary>
  /// The list of optional packages that provide additional functionality.
  /// </summary>
  public AlpmList<Depend> OptionalDepends => Depend.ListFactory(NativeMethods.alpm_pkg_get_optdepends(BackingStruct), Lifetime?.Domain);

  /// <summary>
  /// The list of dependencies required only to run the test suite when building.
  /// </summary>
  public AlpmList<Depend> CheckDepends => Depend.ListFactory(NativeMethods.alpm_pkg_get_checkdepends(BackingStruct), Lifetime?.Domain);

  /// <summary>
  /// The list of dependencies required only to build the package from source.
  /// </summary>
  public AlpmList<Depend> MakeDepends => Depend.ListFactory(NativeMethods.alpm_pkg_get_makedepends(BackingStruct), Lifetime?.Domain);

  /// <summary>
  /// The list of packages that conflict with this package.
  /// </summary>
  public AlpmList<Depend> Conflicts => Depend.ListFactory(NativeMethods.alpm_pkg_get_conflicts(BackingStruct), Lifetime?.Domain);

  /// <summary>
  /// The virtual provisions or features provided by this package.
  /// </summary>
  public AlpmList<Depend> Provides => Depend.ListFactory(NativeMethods.alpm_pkg_get_provides(BackingStruct), Lifetime?.Domain);

  /// <summary>
  /// The list of packages that this package replaces.
  /// </summary>
  public AlpmList<Depend> Replaces => Depend.ListFactory(NativeMethods.alpm_pkg_get_replaces(BackingStruct), Lifetime?.Domain);

  /// <summary>
  /// The package's file list, read on demand.
  /// </summary>
  public FileList Files => new(NativeMethods.alpm_pkg_get_files(BackingStruct), Lifetime?.Domain);

  /// <summary>
  /// The list of configuration files marked for backup.
  /// </summary>
  public AlpmList<Backup> Backup => Pacpar.Alpm.Backup.ListFactory(NativeMethods.alpm_pkg_get_backup(BackingStruct), Lifetime?.Domain);

  /// <summary>
  /// Gets the names of packages that depend on this package.
  /// </summary>
  public IReadOnlyList<string> GetRequiredBy()
  {
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
    var list = AlpmStringList.TakeOwned(NativeMethods.alpm_pkg_compute_optionalfor(BackingStruct),
      &MemoryManagement.CFreeExtern);
    GC.KeepAlive(this);
    return list;
  }

  private bool _base64SignatureLoaded;

  public string? Base64Signature
  {
    get
    {
      // See Name: a cache hit returns before the guarded accessor runs, so check the stamp here.
      ThrowIfDisposed();
      if (_base64SignatureLoaded) return field;
      field = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_base64_sig(BackingStruct));
      _base64SignatureLoaded = true;

      return field;
    }
  }

  public bool HasScriptlet
  {
    get
    {
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
    // See Name: a cache hit returns before the guarded accessor runs, so check the stamp here.
    ThrowIfDisposed();

    if (!_signatureLoaded)
    {
      // Captured before the call: on the failure path the errno read must be the next native
      // interaction, and this property is itself a native call (alpm_pkg_get_handle).
      var handlePtr = LibraryHandle;
      byte* buffer = null;
      nuint len;
      var result = NativeMethods.alpm_pkg_get_sig(BackingStruct, &buffer, &len);
      if (result != 0)
      {
        // Errno first, anchor second: KeepAlive keeps this alive only up to its own instruction, so
        // it has to come after the last native read on this path.
        var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
        GC.KeepAlive(this);
        throw NativeCall.Failure(rawErrno, "read package signature");
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

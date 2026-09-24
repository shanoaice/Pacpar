using System.ComponentModel;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

public unsafe class Database
{
  private readonly _alpm_db_t* backingStruct;

  internal Database(_alpm_db_t* backingStruct)
  {
    this.backingStruct = backingStruct;
  }

  internal static Database Factory(void* ptr) => new((_alpm_db_t*)ptr);

  public string Name => field ??= NativeString.FromNative((nint)NativeMethods.alpm_db_get_name(backingStruct))!;

  public Package? GetPackage(string name)
  {
    var nameCstr = NativeString.ToNative(name);
    var pkg = NativeMethods.alpm_db_get_pkg(backingStruct, nameCstr);
    Marshal.FreeHGlobal((nint)nameCstr);
    if ((nint)pkg == IntPtr.Zero) return null;
    return new Package(pkg);
  }

  /// <summary>
  /// The package cache of this database.
  /// </summary>
  /// <remarks>
  /// A <c>null</c> native cache with an ok errno is a legitimately empty cache and yields an empty
  /// view; any other errno is thrown. The view is borrowed and never frees the cache (see
  /// <see cref="AlpmList{T}"/>).
  /// </remarks>
  public AlpmList<Package> GetPackageCache()
  {
    var pkgCache = NativeMethods.alpm_db_get_pkgcache(backingStruct);
    ThrowIfErrnoSet();
    return AlpmList<Package>.Borrow(pkgCache, &Package.Factory);
  }

  /// <summary>
  /// The servers configured for this database; empty when none are set.
  /// </summary>
  /// <remarks>The list is borrowed from the database and is never freed by the view.</remarks>
  public AlpmStringList GetServers()
  {
    var servers = NativeMethods.alpm_db_get_servers(backingStruct);
    ThrowIfErrnoSet();
    return new AlpmStringList(servers);
  }

  /// <summary>
  /// The cache servers configured for this database; empty when none are set.
  /// </summary>
  /// <remarks>The list is borrowed from the database and is never freed by the view.</remarks>
  public AlpmStringList GetCacheServers()
  {
    var servers = NativeMethods.alpm_db_get_cache_servers(backingStruct);
    ThrowIfErrnoSet();
    return new AlpmStringList(servers);
  }

  public Group? GetGroup(string name)
  {
    var nameCstr = NativeString.ToNative(name);
    var group = NativeMethods.alpm_db_get_group(backingStruct, nameCstr);
    Marshal.FreeHGlobal((nint)nameCstr);
    if ((nint)group == IntPtr.Zero) return null;
    return new Group(group);
  }

  /// <summary>
  /// The group cache of this database. See <see cref="GetPackageCache"/> for the null/errno rule.
  /// </summary>
  public AlpmList<Group> GetGroupCache()
  {
    var groupCache = NativeMethods.alpm_db_get_groupcache(backingStruct);
    ThrowIfErrnoSet();
    return AlpmList<Group>.Borrow(groupCache, &Group.Factory);
  }

  /// <summary>The raw libalpm handle this database belongs to.</summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public nint AsHandle() => (nint)NativeMethods.alpm_db_get_handle(backingStruct);

  public SigLevel SigLevel => (SigLevel)NativeMethods.alpm_db_get_siglevel(backingStruct);

  // TODO: USAGE

  public void Unregister()
  {
    var err = NativeMethods.alpm_db_unregister(backingStruct);
    if (err != 0) throw ErrorHandler.ToException(NativeMethods.alpm_errno((_alpm_handle_t*)AsHandle()));
  }

  /// <summary>
  /// Whether this database is valid. Never throws: an invalid database is a normal answer.
  /// </summary>
  /// <remarks>libalpm reports validity as <c>0</c> == valid, <c>-1</c> == invalid.</remarks>
  public bool IsValid => NativeMethods.alpm_db_get_valid(backingStruct) == 0;

  /// <summary>
  /// Validates this database, throwing when it is invalid.
  /// </summary>
  /// <remarks>
  /// libalpm sets the handle errno when it reports an invalid database, and that errno becomes the
  /// thrown exception's. Use <see cref="IsValid"/> when an invalid database is an expected answer
  /// rather than an error.
  /// </remarks>
  public void Validate()
  {
    if (IsValid) return;

    var errno = NativeMethods.alpm_errno((_alpm_handle_t*)AsHandle());
    throw errno == _alpm_errno_t.ALPM_ERR_OK
      ? new InvalidOperationException("The database is invalid but libalpm did not set an error code.")
      : ErrorHandler.ToException(errno);
  }

  /// <summary>
  /// Throws when libalpm set the handle errno.
  /// </summary>
  /// <remarks>
  /// Used after a native call whose <c>null</c> return is legitimate when there is no error (an
  /// empty list, for instance): <c>null</c> plus an ok errno means "empty", <c>null</c> plus an
  /// errno means "failed".
  /// </remarks>
  private void ThrowIfErrnoSet()
  {
    var errno = NativeMethods.alpm_errno((_alpm_handle_t*)AsHandle());
    if (errno != _alpm_errno_t.ALPM_ERR_OK) throw ErrorHandler.ToException(errno);
  }
}

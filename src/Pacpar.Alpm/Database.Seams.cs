using System.Diagnostics.CodeAnalysis;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

public sealed unsafe partial class Database
{
  /// <summary>
  /// The failure-returning seam for <see cref="Search"/>.
  /// </summary>
  internal unsafe bool TrySearch(
    IEnumerable<string> needles,
    [NotNullWhen(true)] out IReadOnlyList<PackageView>? results,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ArgumentNullException.ThrowIfNull(needles);
    var dbPtr = ValidatedPtr;
    var handlePtr = RawHandle;

    var needleList = AlpmNativeList.BuildStringList(needles, out var buffers);
    _alpm_list_t* retList = null;
    try
    {
      var ret = NativeMethods.alpm_db_search(dbPtr, needleList, &retList);
      if (ret < 0)
      {
        if (retList != null) NativeMethods.alpm_list_free(retList);
        var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
        GC.KeepAlive(this);
        results = null;
        failure = NativeCall.Failure(rawErrno, "search database");
        return false;
      }

      GC.KeepAlive(this);
      results = retList != null
        ? AlpmOwnedList<PackageView>.Take(retList, &PackageView.Factory, null, Lifetime)
        : Array.Empty<PackageView>();
      failure = null;
      return true;
    }
    finally
    {
      AlpmNativeList.FreeStringListBuffers(buffers, needleList);
    }
  }

  /// <summary>
  /// The failure-returning seam for <see cref="LoadPackageCache"/>.
  /// </summary>
  /// <param name="cache">The borrowed view, or <c>null</c> when the call failed.</param>
  /// <param name="failure">The failure, or <c>null</c> when the call succeeded.</param>
  /// <returns><c>true</c> when the cache was read.</returns>
  /// <remarks>
  /// The first of the seam's two shapes: an entry point libalpm resets <c>pm_errno</c> in, so the
  /// handle's errno after the call is this call's own and no return value says failure (ADR 0012).
  /// An ok errno means "empty cache", not "no failure to report".
  /// </remarks>
  internal bool TryLoadPackageCache([NotNullWhen(true)] out AlpmList<PackageView>? cache,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    var handlePtr = RawHandle;
    var pkgCache = NativeMethods.alpm_db_get_pkgcache(ValidatedPtr);

    if (!LastCallFailed(handlePtr, "load package cache", out failure))
    {
      cache = null;
      return false;
    }

    cache = AlpmList<PackageView>.Borrow(pkgCache, &PackageView.Factory, Lifetime);
    return true;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="LoadGroupCache"/>. See
  /// <see cref="TryLoadPackageCache"/> for the shape and for why this entry point may read the errno.
  /// </summary>
  internal bool TryLoadGroupCache([NotNullWhen(true)] out AlpmList<Group>? cache,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    var handlePtr = RawHandle;
    var groupCache = NativeMethods.alpm_db_get_groupcache(ValidatedPtr);

    if (!LastCallFailed(handlePtr, "load group cache", out failure))
    {
      cache = null;
      return false;
    }

    cache = AlpmList<Group>.Borrow(groupCache, &Group.Factory, Lifetime);
    return true;
  }

  /// <summary>
  /// Reports the handle's errno as a failure when the call that just returned left one there.
  /// </summary>
  /// <param name="handlePtr">The handle, captured before the call being checked.</param>
  /// <param name="operation">What failed, e.g. <c>"load package cache"</c>.</param>
  /// <param name="failure">The failure, or <c>null</c> when the call left no errno.</param>
  /// <returns><c>true</c> when the handle reports no error.</returns>
  /// <remarks>
  /// This is only valid after an entry point that resets <c>pm_errno</c> at entry, because only then
  /// is a non-ok value guaranteed to come from that call. <c>alpm_db_get_pkgcache</c> and
  /// <c>alpm_db_get_groupcache</c> both do; the two server getters deliberately do not, which is why
  /// <see cref="Servers"/> and <see cref="CacheServers"/> cannot use this and must treat a null
  /// list as "none configured" whatever the handle's errno happens to say. Anything that reports
  /// failure through its return value goes through <see cref="NativeCall"/> instead.
  /// <para>
  /// An ok errno means the call succeeded, and here that includes "the cache is legitimately empty".
  /// It is not the same reading as <see cref="NativeCall.Failure(int, string)"/> gives the same
  /// value: there, the return value already said the call failed, so a missing errno is a failure
  /// libalpm cannot describe and is thrown.
  /// </para>
  /// </remarks>
  private bool LastCallFailed(_alpm_handle_t* handlePtr, string operation,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    var errno = (int)NativeMethods.alpm_errno(handlePtr);
    GC.KeepAlive(this);

    if (errno == (int)_alpm_errno_t.ALPM_ERR_OK)
    {
      failure = null;
      return true;
    }

    failure = AlpmFailure.Of(errno, operation);
    return false;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="Usage"/>.
  /// </summary>
  internal bool TryGetUsage(out DatabaseUsage usage, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    // capture the handle to prevent RawHandle accessor sabotaging errno
    var handlePtr = RawHandle;
    int rawUsage;
    var err = NativeMethods.alpm_db_get_usage(ValidatedPtr, &rawUsage);
    if (err != 0)
    {
      var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
      GC.KeepAlive(this);
      usage = default;
      failure = NativeCall.Failure(rawErrno, "get database usage");
      return false;
    }

    GC.KeepAlive(this);
    usage = (DatabaseUsage)(uint)rawUsage;
    failure = null;
    return true;
  }

  /// <summary>
  /// The failure-returning seam for setting <see cref="Usage"/>.
  /// </summary>
  internal bool TrySetUsage(DatabaseUsage usage, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    // capture the handle to prevent RawHandle accessor sabotaging errno
    var handlePtr = RawHandle;
    var err = NativeMethods.alpm_db_set_usage(ValidatedPtr, (int)usage);
    if (err != 0)
    {
      var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
      GC.KeepAlive(this);
      failure = NativeCall.Failure(rawErrno, "set database usage");
      return false;
    }

    GC.KeepAlive(this);
    failure = null;
    return true;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="AddServer"/>.
  /// </summary>
  internal bool TryAddServer(string url, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ArgumentNullException.ThrowIfNull(url);
    var dbPtr = ValidatedPtr;
    var handlePtr = RawHandle;
    Span<byte> scratch = stackalloc byte[256];
    using var urlBuf = new Utf8Buffer(url, scratch);

    var err = NativeMethods.alpm_db_add_server(dbPtr, urlBuf.Ptr);
    if (err != 0)
    {
      var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
      GC.KeepAlive(this);
      failure = NativeCall.Failure(rawErrno, "add server");
      return false;
    }

    // libalpm assigns a new list head (alpm_list_add), so a retained view may walk the old one.
    Lifetime.InvalidateFacet(LifetimeFacet.Servers, "Database.AddServer");
    GC.KeepAlive(this);
    failure = null;
    return true;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="RemoveServer"/>.
  /// </summary>
  internal bool TryRemoveServer(string url, out bool removed, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ArgumentNullException.ThrowIfNull(url);
    var dbPtr = ValidatedPtr;
    var handlePtr = RawHandle;
    Span<byte> scratch = stackalloc byte[256];
    using var urlBuf = new Utf8Buffer(url, scratch);

    var ret = NativeMethods.alpm_db_remove_server(dbPtr, urlBuf.Ptr);
    if (ret < 0)
    {
      var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
      GC.KeepAlive(this);
      removed = false;
      failure = NativeCall.Failure(rawErrno, "remove server");
      return false;
    }

    GC.KeepAlive(this);
    removed = ret == 0;

    // alpm_list_remove_str frees the matching node and may replace the head, so a retained view
    // would walk freed memory. Finding nothing leaves the list, and its views, untouched.
    if (removed)
    {
      Lifetime.InvalidateFacet(LifetimeFacet.Servers, "Database.RemoveServer");
    }

    failure = null;
    return true;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="SetServers"/>.
  /// </summary>
  internal bool TrySetServers(IEnumerable<string> servers, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ArgumentNullException.ThrowIfNull(servers);
    var dbPtr = ValidatedPtr;
    var handlePtr = RawHandle;
    var nativeList = AlpmNativeList.BuildStringList(servers, out var buffers);
    try
    {
      var err = NativeMethods.alpm_db_set_servers(dbPtr, nativeList);
      if (err != 0)
      {
        var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
        GC.KeepAlive(this);
        failure = NativeCall.Failure(rawErrno, "set servers");
        return false;
      }

      Lifetime.InvalidateFacet(LifetimeFacet.Servers, "Database.SetServers");
      GC.KeepAlive(this);
      failure = null;
      return true;
    }
    finally
    {
      AlpmNativeList.FreeStringListBuffers(buffers, nativeList);
    }
  }

  /// <summary>
  /// The failure-returning seam for <see cref="AddCacheServer"/>.
  /// </summary>
  internal bool TryAddCacheServer(string url, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ArgumentNullException.ThrowIfNull(url);
    var dbPtr = ValidatedPtr;
    var handlePtr = RawHandle;
    Span<byte> scratch = stackalloc byte[256];
    using var urlBuf = new Utf8Buffer(url, scratch);

    var err = NativeMethods.alpm_db_add_cache_server(dbPtr, urlBuf.Ptr);
    if (err != 0)
    {
      var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
      GC.KeepAlive(this);
      failure = NativeCall.Failure(rawErrno, "add cache server");
      return false;
    }

    // Same list re-link as the server list: the previous head may be gone.
    Lifetime.InvalidateFacet(LifetimeFacet.CacheServers, "Database.AddCacheServer");
    GC.KeepAlive(this);
    failure = null;
    return true;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="RemoveCacheServer"/>.
  /// </summary>
  internal bool TryRemoveCacheServer(string url, out bool removed, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ArgumentNullException.ThrowIfNull(url);
    var dbPtr = ValidatedPtr;
    var handlePtr = RawHandle;
    Span<byte> scratch = stackalloc byte[256];
    using var urlBuf = new Utf8Buffer(url, scratch);

    var ret = NativeMethods.alpm_db_remove_cache_server(dbPtr, urlBuf.Ptr);
    if (ret < 0)
    {
      var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
      GC.KeepAlive(this);
      removed = false;
      failure = NativeCall.Failure(rawErrno, "remove cache server");
      return false;
    }

    GC.KeepAlive(this);
    removed = ret == 0;

    if (removed)
    {
      Lifetime.InvalidateFacet(LifetimeFacet.CacheServers, "Database.RemoveCacheServer");
    }

    failure = null;
    return true;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="SetCacheServers"/>.
  /// </summary>
  internal bool TrySetCacheServers(IEnumerable<string> servers, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ArgumentNullException.ThrowIfNull(servers);
    var dbPtr = ValidatedPtr;
    var handlePtr = RawHandle;
    var nativeList = AlpmNativeList.BuildStringList(servers, out var buffers);
    try
    {
      var err = NativeMethods.alpm_db_set_cache_servers(dbPtr, nativeList);
      if (err != 0)
      {
        var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
        GC.KeepAlive(this);
        failure = NativeCall.Failure(rawErrno, "set cache servers");
        return false;
      }

      Lifetime.InvalidateFacet(LifetimeFacet.CacheServers, "Database.SetCacheServers");
      GC.KeepAlive(this);
      failure = null;
      return true;
    }
    finally
    {
      AlpmNativeList.FreeStringListBuffers(buffers, nativeList);
    }
  }

  /// <summary>
  /// The failure-returning seam for <see cref="Update"/>.
  /// </summary>
  internal bool TryUpdate(bool force, out bool updated, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    var dbPtr = ValidatedPtr;
    var handlePtr = RawHandle;

    InvalidateViews("Database.Update");

    _alpm_list_t* list = null;
    try
    {
      list = AlpmNativeList.BuildPointerList([(nint)dbPtr]);
      var ret = NativeMethods.alpm_db_update(handlePtr, list, force ? 1 : 0);
      if (ret < 0)
      {
        var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
        GC.KeepAlive(this);
        updated = false;
        failure = NativeCall.Failure(rawErrno, "update database");
        return false;
      }

      GC.KeepAlive(this);
      updated = ret == 0;
      failure = null;
      return true;
    }
    finally
    {
      AlpmNativeList.Free(list);
    }
  }

  /// <summary>
  /// The failure-returning seam for <see cref="Unregister"/>.
  /// </summary>
  internal bool TryUnregister([NotNullWhen(false)] out AlpmFailure? failure)
  {
    if (_isLocal)
    {
      throw new InvalidOperationException(
        "The local database cannot be unregistered. It represents installed packages and is released when the session is disposed.");
    }

    // Both captured before anything is invalidated. ValidatedPtr runs the stamp guard, so it cannot
    // be read again after the Invalidate below - it would throw on its own database. The handle has
    // to come from the same read, because on the failure path the errno read must be the next native
    // interaction after the call that failed.
    var dbPtr = ValidatedPtr;
    var handlePtr = NativeMethods.alpm_db_get_handle(dbPtr);
    var err = NativeMethods.alpm_db_unregister(dbPtr);
    if (err != 0)
    {
      // Read the errno where the failure is known, and only then anchor: KeepAlive keeps this alive
      // up to its own instruction, so it has to come after the last native read on this path.
      var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
      GC.KeepAlive(this);
      failure = NativeCall.Failure(rawErrno, "unregister database");
      return false;
    }

    Lifetime.Invalidate("Database.Unregister()");
    _onUnregistered?.Invoke();
    failure = null;
    return true;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="Validate"/>.
  /// </summary>
  internal bool TryValidate([NotNullWhen(false)] out AlpmFailure? failure)
  {
    var handlePtr = RawHandle;
    if (IsValid)
    {
      failure = null;
      return true;
    }

    var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
    GC.KeepAlive(this);
    failure = NativeCall.Failure(rawErrno, "validate database");
    return false;
  }
}

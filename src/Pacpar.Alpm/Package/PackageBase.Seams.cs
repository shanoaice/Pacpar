using System.Diagnostics.CodeAnalysis;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

public abstract unsafe partial class PackageBase
{
  /// <summary>
  /// The failure-returning seam for <see cref="CheckMd5Sum"/>.
  /// </summary>
  /// <param name="matches">
  /// When this method returns <c>true</c>, indicates whether the package archive's MD5 checksum
  /// matches the expected database entry (<c>true</c>) or is corrupted (<c>false</c>).
  /// </param>
  /// <param name="failure">
  /// The operational error when this method returns <c>false</c>; otherwise <c>null</c>.
  /// </param>
  /// <returns>
  /// <c>true</c> if the integrity check ran to completion;
  /// <c>false</c> if an operational failure prevented the check from running.
  /// </returns>
  internal bool TryCheckMd5Sum(out bool matches, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    var pkgPtr = BackingStruct;
    var handlePtr = LibraryHandle;

    var ret = NativeMethods.alpm_pkg_checkmd5sum(pkgPtr);
    if (ret == 0)
    {
      GC.KeepAlive(this);
      matches = true;
      failure = null;
      return true;
    }

    var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
    GC.KeepAlive(this);

    if (rawErrno == (int)_alpm_errno_t.ALPM_ERR_PKG_INVALID ||
        rawErrno == (int)_alpm_errno_t.ALPM_ERR_PKG_INVALID_CHECKSUM)
    {
      matches = false;
      failure = null;
      return true;
    }

    matches = false;
    failure = NativeCall.Failure(rawErrno, "check md5 sum");
    return false;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="GetMtree"/>.
  /// </summary>
  /// <param name="mtree">
  /// When this method returns <c>true</c>, carries a reader streaming the package's mtree entries,
  /// or <c>null</c> if the package carries no mtree manifest.
  /// </param>
  /// <param name="failure">
  /// The operational error when this method returns <c>false</c>; otherwise <c>null</c>.
  /// </param>
  /// <returns>
  /// <c>true</c> if the mtree file was opened successfully or was absent;
  /// <c>false</c> if an operational error occurred while opening the archive.
  /// </returns>
  internal bool TryGetMtree(out PackageMtreeReader? mtree, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    var handlePtr = LibraryHandle;
    var archive = NativeMethods.alpm_pkg_mtree_open(BackingStruct);
    if (archive == null)
    {
      var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
      GC.KeepAlive(this);
      if (rawErrno == 0)
      {
        mtree = null;
        failure = null;
        return true;
      }
      mtree = null;
      failure = NativeCall.Failure(rawErrno, "open package mtree");
      return false;
    }

    GC.KeepAlive(this);
    mtree = new PackageMtreeReader(this, archive);
    failure = null;
    return true;
  }
}

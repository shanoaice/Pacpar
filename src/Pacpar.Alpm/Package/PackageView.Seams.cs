using System.Diagnostics.CodeAnalysis;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

public sealed unsafe partial class PackageView
{
  /// <summary>
  /// The failure-returning seam for <see cref="SetReason"/>.
  /// </summary>
  internal bool TrySetReason(PackageReason reason, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    var err = NativeMethods.alpm_pkg_set_reason(BackingStruct, (_alpm_pkgreason_t)reason);
    if (err != 0)
    {
      var dbPtr = NativeMethods.alpm_pkg_get_db(BackingStruct);
      var handlePtr = dbPtr != null ? NativeMethods.alpm_db_get_handle(dbPtr) : null;
      var rawErrno = handlePtr != null ? (int)NativeMethods.alpm_errno(handlePtr) : 0;
      GC.KeepAlive(this);
      failure = NativeCall.Failure(rawErrno, "set package install reason");
      return false;
    }

    GC.KeepAlive(this);
    failure = null;
    return true;
  }
}

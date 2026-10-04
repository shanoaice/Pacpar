using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>
/// The only place in this library that reads libalpm's errno to decide what to throw. (<see
/// cref="Alpm.Errno"/> and <see cref="Alpm.GetCurrentError"/> read it too, but as diagnostics the
/// caller asks for explicitly - no control flow in this library depends on them.)
/// </summary>
/// <remarks>
/// libalpm keeps one errno per handle and does not clear it on success: whether a given entry point
/// resets it on entry is an implementation detail that varies function by function and is not
/// documented in <c>alpm.h</c>. Measured against libalpm 16.0.1: <c>alpm_db_get_servers</c> never
/// touches errno at all, while <c>alpm_db_get_pkgcache</c> clears it in its first statement, and both
/// succeed. Reading errno after a successful call therefore reports whatever the previous failure
/// left behind, and reading it after another native call reports that call's state instead.
/// <para>
/// Both are avoided by the rule this type exists to enforce: <b>the return value is the only
/// authority on whether a call failed, and errno is read here, as the first thing that happens after
/// that failure.</b> Hand in a handle captured <i>before</i> the failing call so that no native call
/// can run in between.
/// </para>
/// </remarks>
internal static class NativeCall
{
  /// <summary>
  /// The failure for a call that signalled failure through its return value, when the owner crosses
  /// the boundary as a <see cref="SafeAlpmHandle"/>.
  /// </summary>
  /// <remarks>
  /// The marshaller refcounts the handle for the duration of the errno read, so the owner cannot be
  /// finalized underneath it. No anchor is needed at the call site.
  /// </remarks>
  /// <param name="handle">The owning handle.</param>
  /// <param name="operation">What failed, e.g. <c>"unregister database"</c>.</param>
  /// <returns>
  /// A managed failure value. Throw <c>AlpmFailure.ToException()</c> to report it the way the public
  /// C# API does; hand the value on to a caller that would rather match on it.
  /// </returns>
  internal static AlpmFailure Failure(SafeAlpmHandle handle, string operation)
    => Failure((int)NativeMethods.alpm_errno(handle), operation);

  /// <summary>
  /// The failure for a call that failed while the owner crossed the boundary as a raw pointer.
  /// </summary>
  /// <param name="rawErrno">
  /// The errno the caller read immediately after the failing call, as the first statement of that
  /// failure branch.
  /// </param>
  /// <param name="operation">What failed, e.g. <c>"unregister database"</c>.</param>
  /// <remarks>
  /// This overload takes the value, not the handle, and that is the point. A raw-handle overload
  /// would read <c>alpm_errno</c> here - a second native read, after the caller's anchor - and a
  /// <c>GC.KeepAlive(owner)</c> written before that expression does not cover it: KeepAlive keeps
  /// the owner alive only up to its own instruction. Reading at the failure site and passing the
  /// value makes the anchor provable, because the order becomes <c>read errno</c> to
  /// <c>GC.KeepAlive(owner)</c> to report.
  /// </remarks>
  internal static AlpmFailure Failure(int rawErrno, string operation)
    // A failure with no errno, and ALPM_ERR_MEMORY, are thrown rather than returned: neither is an
    // ALPM operation failure this library can describe.
    => AlpmFailure.Of(rawErrno, operation);
}

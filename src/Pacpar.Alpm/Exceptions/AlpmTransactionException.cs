using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

/// <summary>
/// An error from libalpm's transaction handling: the <c>ALPM_ERR_TRANS_*</c> values, and the
/// payload-carrying errnos <c>alpm_trans_prepare</c>/<c>alpm_trans_commit</c> report.
/// </summary>
/// <remarks>
/// The type describes the failure: an instance that is none of the nested cases is the <b>base
/// state</b>, which is all libalpm offers for an errno whose output parameter it left empty. A
/// consumer can therefore match exhaustively - and, unlike branching on <see cref="AlpmException.Errno"/>,
/// each case fixes its own errno:
/// <code>
/// catch (AlpmTransactionException ex)
/// {
///   switch (ex)
///   {
///     case AlpmTransactionException.MissingDependencies missing: ...
///     case AlpmTransactionException.ConflictingDependencies conflicts: ...
///     default: ...   // base state: only Errno, StrError and the message are known
///   }
/// }
/// </code>
/// </remarks>
public class AlpmTransactionException(_alpm_errno_t errno, string? strError = null, string? context = null)
  : AlpmException(errno, strError, context)
{
  /// <summary>
  /// Dependencies the transaction could not satisfy (<c>ALPM_ERR_UNSATISFIED_DEPS</c>).
  /// </summary>
  public sealed class MissingDependencies(IReadOnlyList<DepMissing> dependencies, string? context = null)
    : AlpmTransactionException(_alpm_errno_t.ALPM_ERR_UNSATISFIED_DEPS, context: context)
  {
    /// <summary>The unsatisfied dependencies, snapshotted out of libalpm's list.</summary>
    public IReadOnlyList<DepMissing> Dependencies { get; } = dependencies;
  }

  /// <summary>
  /// Dependencies that conflict with each other (<c>ALPM_ERR_CONFLICTING_DEPS</c>).
  /// </summary>
  public sealed class ConflictingDependencies(IReadOnlyList<Conflict> conflicts, string? context = null)
    : AlpmTransactionException(_alpm_errno_t.ALPM_ERR_CONFLICTING_DEPS, context: context)
  {
    /// <summary>The conflicting pairs, snapshotted out of libalpm's list.</summary>
    public IReadOnlyList<Conflict> Conflicts { get; } = conflicts;
  }

  /// <summary>
  /// Files already on disk that a package in the transaction would overwrite
  /// (<c>ALPM_ERR_FILE_CONFLICTS</c>).
  /// </summary>
  public sealed class ConflictingFiles(IReadOnlyList<FileConflict> conflicts, string? context = null)
    : AlpmTransactionException(_alpm_errno_t.ALPM_ERR_FILE_CONFLICTS, context: context)
  {
    /// <summary>The file conflicts, snapshotted out of libalpm's list.</summary>
    public IReadOnlyList<FileConflict> Conflicts { get; } = conflicts;
  }

  /// <summary>
  /// Packages whose architecture the handle's architecture list does not accept
  /// (<c>ALPM_ERR_PKG_INVALID_ARCH</c>).
  /// </summary>
  /// <remarks>
  /// The strings are libalpm's own rendering of the offending packages -
  /// <c>pkgname-pkgver-pkgarch</c>, measured with an architecture-restricted handle and two
  /// foreign-architecture packages - and not the file names they were loaded from, so a caller cannot
  /// map them back to the path it passed to <c>alpm_pkg_load</c>.
  /// </remarks>
  public sealed class InvalidPackageArchitecture(IReadOnlyList<string> packages, string? context = null)
    : AlpmTransactionException(_alpm_errno_t.ALPM_ERR_PKG_INVALID_ARCH, context: context)
  {
    /// <summary>The rejected packages, as libalpm named them.</summary>
    public IReadOnlyList<string> Packages { get; } = packages;
  }

  /// <summary>Packages libalpm rejected as invalid (<c>ALPM_ERR_PKG_INVALID</c>).</summary>
  public sealed class InvalidPackage(IReadOnlyList<string> packages, string? context = null)
    : AlpmTransactionException(_alpm_errno_t.ALPM_ERR_PKG_INVALID, context: context)
  {
    /// <summary>The rejected packages, as libalpm named them.</summary>
    public IReadOnlyList<string> Packages { get; } = packages;
  }

  /// <summary>
  /// Packages whose checksum did not match (<c>ALPM_ERR_PKG_INVALID_CHECKSUM</c>).
  /// </summary>
  public sealed class InvalidPackageChecksum(IReadOnlyList<string> packages, string? context = null)
    : AlpmTransactionException(_alpm_errno_t.ALPM_ERR_PKG_INVALID_CHECKSUM, context: context)
  {
    /// <summary>The rejected packages, as libalpm named them.</summary>
    public IReadOnlyList<string> Packages { get; } = packages;
  }

  /// <summary>
  /// Packages whose signature did not verify (<c>ALPM_ERR_PKG_INVALID_SIG</c>).
  /// </summary>
  public sealed class InvalidPackageSignature(IReadOnlyList<string> packages, string? context = null)
    : AlpmTransactionException(_alpm_errno_t.ALPM_ERR_PKG_INVALID_SIG, context: context)
  {
    /// <summary>The rejected packages, as libalpm named them.</summary>
    public IReadOnlyList<string> Packages { get; } = packages;
  }

  /// <summary>
  /// Builds the exception describing a failed <c>alpm_trans_prepare</c>/<c>alpm_trans_commit</c> from
  /// <paramref name="errno"/> and the list libalpm dumped into that call's output parameter.
  /// </summary>
  /// <remarks>
  /// Every branch <b>consumes</b> <paramref name="data"/>: the entries are snapshotted into managed
  /// objects first, then the whole list - nodes and elements - is freed with the destructor that
  /// belongs to <paramref name="errno"/>: 45 <c>alpm_depmissing_free</c>, 46 <c>alpm_conflict_free</c>,
  /// 47 <c>alpm_fileconflict_free</c>, and for 42/35/36/37 libc <c>free</c> on the strings. Any other
  /// errno frees the nodes only and answers with the base state, because guessing an element
  /// destructor aborts the process (releasing an <c>alpm_conflict_t</c> with
  /// <c>alpm_depmissing_free</c> is a measured SIGABRT).
  /// </remarks>
  /// <param name="errno">The errno of the failed call, read before anything else runs.</param>
  /// <param name="data">The list libalpm dumped, or <c>null</c>. Always consumed.</param>
  /// <param name="context">Optional operation description, e.g. "Failed to prepare the transaction".</param>
  internal static unsafe AlpmTransactionException TakeFailure(_alpm_errno_t errno, _alpm_list_t* data,
    string? context = null)
  {
    switch (errno)
    {
      case _alpm_errno_t.ALPM_ERR_UNSATISFIED_DEPS:
        return new MissingDependencies(
          AlpmOwnedList<DepMissing>.Take(data, &DepMissing.Factory, &MemoryManagement.DepMissingFreeExtern),
          context);
      case _alpm_errno_t.ALPM_ERR_CONFLICTING_DEPS:
        return new ConflictingDependencies(
          AlpmOwnedList<Conflict>.Take(data, &Conflict.Factory, &MemoryManagement.ConflictFreeExtern),
          context);
      case _alpm_errno_t.ALPM_ERR_FILE_CONFLICTS:
        return new ConflictingFiles(
          AlpmOwnedList<FileConflict>.Take(data, &FileConflict.Factory, &MemoryManagement.FileConflictFreeExtern),
          context);
      case _alpm_errno_t.ALPM_ERR_PKG_INVALID_ARCH:
        return new InvalidPackageArchitecture(
          AlpmStringList.TakeOwned(data, &MemoryManagement.CFreeExtern), context);
      case _alpm_errno_t.ALPM_ERR_PKG_INVALID:
        return new InvalidPackage(AlpmStringList.TakeOwned(data, &MemoryManagement.CFreeExtern), context);
      case _alpm_errno_t.ALPM_ERR_PKG_INVALID_CHECKSUM:
        return new InvalidPackageChecksum(AlpmStringList.TakeOwned(data, &MemoryManagement.CFreeExtern), context);
      case _alpm_errno_t.ALPM_ERR_PKG_INVALID_SIG:
        return new InvalidPackageSignature(AlpmStringList.TakeOwned(data, &MemoryManagement.CFreeExtern), context);
      default:
        // No documented payload for this errno, so the elements are left alone: leaking a list that
        // libalpm is about to discard anyway beats aborting the process on a wrong free. The caller
        // still gets a usable exception, carrying the errno and libalpm's own message.
        AlpmNativeList.Free(data, null);
        return new AlpmTransactionException(errno, context: context);
    }
  }
}

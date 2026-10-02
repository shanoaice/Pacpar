using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

/// <summary>
/// The base type for every error libalpm reports.
/// </summary>
/// <remarks>
/// C# has no <c>Result&lt;T, E&gt;</c> and a type cannot be both a BCL exception and this library's
/// exception (single inheritance), so libalpm errors get one catchable base carrying the raw errno
/// and libalpm's own message. <see cref="Errno"/> is always set: branch on it, not on the type,
/// when the distinction matters.
/// <para>
/// The one deliberate exception to "everything derives from this": <c>ALPM_ERR_MEMORY</c> surfaces
/// as <see cref="OutOfMemoryException"/>, because allocation failure is a runtime condition that
/// should not be swallowed by a catch-all.
/// </para>
/// </remarks>
public class AlpmException : Exception
{
  /// <summary>
  /// Creates the exception for <paramref name="errno"/>.
  /// </summary>
  /// <param name="errno">The raw libalpm error code; consult <c>alpm.h</c> for its meaning.</param>
  /// <param name="strError">libalpm's message; defaults to <c>alpm_strerror(errno)</c> when omitted.</param>
  /// <param name="context">Optional operation description, e.g. "Failed to add package: foo".</param>
  /// <param name="inner">Optional inner exception.</param>
  public AlpmException(int errno, string? strError = null, string? context = null, Exception? inner = null)
    : base(BuildMessage(errno, strError ?? ErrorHandler.StrError(errno), context), inner)
  {
    Errno = errno;
    StrError = strError ?? ErrorHandler.StrError(errno);
  }

  /// <summary>
  /// The raw libalpm errno. It is libalpm's own numbering and carries no stability promise of its
  /// own; match on the exception type, and use this only for diagnostics or to distinguish a case the
  /// hierarchy deliberately collapses.
  /// </summary>
  public int Errno { get; }

  /// <summary>libalpm's own description of <see cref="Errno"/> (<c>alpm_strerror</c>), if any.</summary>
  public string? StrError { get; }

  private static string BuildMessage(int errno, string? strError, string? context)
  {
    var name = ErrorHandler.NameOf(errno);
    var detail = string.IsNullOrEmpty(strError) ? name : $"{name}: {strError}";

    return string.IsNullOrEmpty(context) ? detail : $"{context} ({detail})";
  }
}

/// <summary>An error from libalpm's database handling (<c>ALPM_ERR_DB_*</c>).</summary>
public class AlpmDatabaseException(int errno, string? strError = null, string? context = null)
  : AlpmException(errno, strError, context);

/// <summary>
/// An error concerning a package (<c>ALPM_ERR_PKG_*</c>).
/// </summary>
/// <remarks>
/// <see cref="AlpmPackageException.Package"/> is set when the failing call already knew the package it was operating on;
/// the errno-driven factory cannot fill it in. It is typed as the shared read-only surface, because a
/// failing call may have been operating on either kind of package.
/// </remarks>
public class AlpmPackageException(
  int errno,
  string? strError = null,
  PackageBase? package = null,
  string? context = null)
  : AlpmException(errno, strError, context)
{
  /// <summary>The package the failed call was operating on, when known.</summary>
  public PackageBase? Package { get; } = package;
}

/// <summary>A signature or keyring error (<c>ALPM_ERR_SIG_*</c>, missing signature support).</summary>
public class AlpmSignatureException(int errno, string? strError = null, string? context = null)
  : AlpmException(errno, strError, context);

/// <summary>A download or retrieval error (<c>ALPM_ERR_RETRIEVE*</c>, libcurl, external downloader).</summary>
public class AlpmRetrieveException(int errno, string? strError = null, string? context = null)
  : AlpmException(errno, strError, context);

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
public class AlpmTransactionException(int errno, string? strError = null, string? context = null)
  : AlpmException(errno, strError, context)
{
  /// <summary>
  /// Dependencies the transaction could not satisfy (<c>ALPM_ERR_UNSATISFIED_DEPS</c>).
  /// </summary>
  public sealed class MissingDependencies(IReadOnlyList<DepMissing> dependencies, string? context = null)
    : AlpmTransactionException((int)_alpm_errno_t.ALPM_ERR_UNSATISFIED_DEPS, context: context)
  {
    /// <summary>The unsatisfied dependencies, snapshotted out of libalpm's list.</summary>
    public IReadOnlyList<DepMissing> Dependencies { get; } = dependencies;
  }

  /// <summary>
  /// Dependencies that conflict with each other (<c>ALPM_ERR_CONFLICTING_DEPS</c>).
  /// </summary>
  public sealed class ConflictingDependencies(IReadOnlyList<Conflict> conflicts, string? context = null)
    : AlpmTransactionException((int)_alpm_errno_t.ALPM_ERR_CONFLICTING_DEPS, context: context)
  {
    /// <summary>The conflicting pairs, snapshotted out of libalpm's list.</summary>
    public IReadOnlyList<Conflict> Conflicts { get; } = conflicts;
  }

  /// <summary>
  /// Files already on disk that a package in the transaction would overwrite
  /// (<c>ALPM_ERR_FILE_CONFLICTS</c>).
  /// </summary>
  public sealed class ConflictingFiles(IReadOnlyList<FileConflict> conflicts, string? context = null)
    : AlpmTransactionException((int)_alpm_errno_t.ALPM_ERR_FILE_CONFLICTS, context: context)
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
    : AlpmTransactionException((int)_alpm_errno_t.ALPM_ERR_PKG_INVALID_ARCH, context: context)
  {
    /// <summary>The rejected packages, as libalpm named them.</summary>
    public IReadOnlyList<string> Packages { get; } = packages;
  }

  /// <summary>Packages libalpm rejected as invalid (<c>ALPM_ERR_PKG_INVALID</c>).</summary>
  public sealed class InvalidPackage(IReadOnlyList<string> packages, string? context = null)
    : AlpmTransactionException((int)_alpm_errno_t.ALPM_ERR_PKG_INVALID, context: context)
  {
    /// <summary>The rejected packages, as libalpm named them.</summary>
    public IReadOnlyList<string> Packages { get; } = packages;
  }

  /// <summary>
  /// Packages whose checksum did not match (<c>ALPM_ERR_PKG_INVALID_CHECKSUM</c>).
  /// </summary>
  public sealed class InvalidPackageChecksum(IReadOnlyList<string> packages, string? context = null)
    : AlpmTransactionException((int)_alpm_errno_t.ALPM_ERR_PKG_INVALID_CHECKSUM, context: context)
  {
    /// <summary>The rejected packages, as libalpm named them.</summary>
    public IReadOnlyList<string> Packages { get; } = packages;
  }

  /// <summary>
  /// Packages whose signature did not verify (<c>ALPM_ERR_PKG_INVALID_SIG</c>).
  /// </summary>
  public sealed class InvalidPackageSignature(IReadOnlyList<string> packages, string? context = null)
    : AlpmTransactionException((int)_alpm_errno_t.ALPM_ERR_PKG_INVALID_SIG, context: context)
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
        return new AlpmTransactionException((int)errno, context: context);
    }
  }
}

/// <summary>
/// Thrown when an operation attempts to access a borrowed view, database, or resource
/// whose underlying unmanaged memory has been invalidated or deallocated.
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/>: the object itself is intact, but the
/// operation is not valid in its current (released) state.
/// </remarks>
public sealed class AlpmLifetimeException : InvalidOperationException
{
  /// <summary>Creates the exception for the invalidated resource <paramref name="target"/>.</summary>
  /// <param name="target">The resource that was invalidated, e.g. <c>"the local database"</c>.</param>
  /// <param name="invalidatedBy">
  /// The operation that released it, e.g. <c>"Database.Unregister()"</c>; <c>null</c> when unknown.
  /// </param>
  public AlpmLifetimeException(string target, string? invalidatedBy)
    : base(BuildMessage(target, invalidatedBy))
  {
    Target = target;
    InvalidatedBy = invalidatedBy;
  }

  /// <summary>The target resource that was invalidated (e.g. 'the local database', 'the sync database core').</summary>
  public string Target { get; }

  /// <summary>The operation that caused the invalidation (e.g. 'Database.Unregister()', 'Transaction.Commit()').</summary>
  public string? InvalidatedBy { get; }

  private static string BuildMessage(string target, string? invalidatedBy)
  {
    var reason = invalidatedBy is null
      ? $"{target} is no longer valid"
      : $"{target} was released by {invalidatedBy}";

    return $"{reason}, so this object points into unmanaged memory that has been deallocated. "
           + "A borrowed resource or view remains valid only while its owning context is active: "
           + "use it within the active scope, or call ToSnapshot() before releasing the owner.";
  }
}

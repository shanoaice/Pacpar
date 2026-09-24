using System.Linq.Expressions;
using Pacpar.Alpm;
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
  /// <param name="errno">The raw libalpm error code.</param>
  /// <param name="strError">libalpm's message; defaults to <c>alpm_strerror(errno)</c> when omitted.</param>
  /// <param name="context">Optional operation description, e.g. "Failed to add package: foo".</param>
  /// <param name="inner">Optional inner exception.</param>
  public AlpmException(_alpm_errno_t errno, string? strError = null, string? context = null, Exception? inner = null)
    : base(BuildMessage(errno, strError ?? ErrorHandler.StrError(errno), context), inner)
  {
    Errno = errno;
    StrError = strError ?? ErrorHandler.StrError(errno);
  }

  /// <summary>The raw libalpm errno.</summary>
  public _alpm_errno_t Errno { get; }

  /// <summary>libalpm's own description of <see cref="Errno"/> (<c>alpm_strerror</c>), if any.</summary>
  public string? StrError { get; }

  private static string BuildMessage(_alpm_errno_t errno, string? strError, string? context)
  {
    var detail = string.IsNullOrEmpty(strError) ? errno.ToString() : $"{errno}: {strError}";

    return string.IsNullOrEmpty(context) ? detail : $"{context} ({detail})";
  }
}

/// <summary>An error from libalpm's database handling (<c>ALPM_ERR_DB_*</c>).</summary>
public class AlpmDatabaseException(_alpm_errno_t errno, string? strError = null, string? context = null)
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

/// <summary>
/// An error concerning a package (<c>ALPM_ERR_PKG_*</c>).
/// </summary>
/// <remarks>
/// <see cref="Package"/> is set when the failing call already knew the package it was operating on;
/// the errno-driven factory cannot fill it in. It is typed as the shared read-only surface, because a
/// failing call may have been operating on either kind of package.
/// </remarks>
public class AlpmPackageException(
  _alpm_errno_t errno,
  string? strError = null,
  PackageBase? package = null,
  string? context = null)
  : AlpmException(errno, strError, context)
{
  /// <summary>The package the failed call was operating on, when known.</summary>
  public PackageBase? Package { get; } = package;
}

/// <summary>A signature or keyring error (<c>ALPM_ERR_SIG_*</c>, missing signature support).</summary>
public class AlpmSignatureException(_alpm_errno_t errno, string? strError = null, string? context = null)
  : AlpmException(errno, strError, context);

/// <summary>A download or retrieval error (<c>ALPM_ERR_RETRIEVE*</c>, libcurl, external downloader).</summary>
public class AlpmRetrieveException(_alpm_errno_t errno, string? strError = null, string? context = null)
  : AlpmException(errno, strError, context);

/// <summary>
/// Turns libalpm errnos into exceptions.
/// </summary>
public static class ErrorHandler
{
  /// <summary>
  /// The exception describing <paramref name="errno"/>, or <c>null</c> if and only if it is
  /// <see cref="_alpm_errno_t.ALPM_ERR_OK"/> (there is no error to report).
  /// </summary>
  /// <remarks>
  /// Every non-OK value yields a non-null exception. An errno this library does not classify - for
  /// example one added by a libalpm newer than <see cref="Categorize"/> - still yields a concrete
  /// <see cref="AlpmException"/> carrying the errno and libalpm's message, instead of a
  /// <see cref="NullReferenceException"/> from <c>throw GetException(...)!</c>.
  /// </remarks>
  public static Exception? GetException(_alpm_errno_t errno)
    => errno == _alpm_errno_t.ALPM_ERR_OK ? null : Create(errno);

  /// <summary>
  /// The exception describing a non-OK <paramref name="errno"/>; never <c>null</c>.
  /// </summary>
  /// <remarks>Prefer this over <c>GetException(...)!</c> at throw sites.</remarks>
  /// <exception cref="ArgumentOutOfRangeException">
  /// <paramref name="errno"/> is <see cref="_alpm_errno_t.ALPM_ERR_OK"/> — there is no error to report.
  /// </exception>
  public static Exception ToException(_alpm_errno_t errno)
    => errno == _alpm_errno_t.ALPM_ERR_OK
      ? throw new ArgumentOutOfRangeException(nameof(errno), errno,
        "ALPM_ERR_OK is not an error; there is no exception to report.")
      : Create(errno);

  /// <summary>
  /// libalpm's description of <paramref name="errno"/>; <c>null</c> when there is none.
  /// </summary>
  internal static string? StrError(_alpm_errno_t errno) => StrErrorCore(errno);

  private static unsafe string? StrErrorCore(_alpm_errno_t errno)
  {
    // alpm_strerror is bounds-checked: out-of-range values come back as "unexpected error".
    return NativeString.FromNative((nint)NativeMethods.alpm_strerror(errno));
  }

  private static Exception Create(_alpm_errno_t errno)
  {
    var strError = StrError(errno);

    return Categorize(errno) switch
    {
      // The one BCL type kept on purpose: an allocation failure is not a library-domain error and
      // must not be caught by a broad `catch (AlpmException)`.
      AlpmErrorCategory.Memory => new OutOfMemoryException(strError),
      AlpmErrorCategory.Database => new AlpmDatabaseException(errno, strError),
      AlpmErrorCategory.Package => new AlpmPackageException(errno, strError),
      AlpmErrorCategory.Transaction => new AlpmTransactionException(errno, strError),
      AlpmErrorCategory.Signature => new AlpmSignatureException(errno, strError),
      AlpmErrorCategory.Retrieve => new AlpmRetrieveException(errno, strError),
      _ => new AlpmException(errno, strError)
    };
  }

  /// <summary>
  /// Classifies an errno into the exception type libalpm errors of that kind are reported as.
  /// </summary>
  /// <remarks>
  /// Every errno known to the bindings must be listed here; only values this library does not know
  /// return <see cref="AlpmErrorCategory.Unknown"/>, and
  /// <c>ErrorHandlerTests.Categorize_ClassifiesEveryKnownErrno</c> fails when a libalpm update adds
  /// one. That is what keeps the mapping in sync instead of letting new errnos degrade silently.
  /// </remarks>
  internal static AlpmErrorCategory Categorize(_alpm_errno_t errno)
  {
    return errno switch
    {
      _alpm_errno_t.ALPM_ERR_MEMORY => AlpmErrorCategory.Memory,

      _alpm_errno_t.ALPM_ERR_DB_OPEN
        or _alpm_errno_t.ALPM_ERR_DB_CREATE
        or _alpm_errno_t.ALPM_ERR_DB_NULL
        or _alpm_errno_t.ALPM_ERR_DB_NOT_NULL
        or _alpm_errno_t.ALPM_ERR_DB_NOT_FOUND
        or _alpm_errno_t.ALPM_ERR_DB_INVALID
        or _alpm_errno_t.ALPM_ERR_DB_INVALID_SIG
        or _alpm_errno_t.ALPM_ERR_DB_VERSION
        or _alpm_errno_t.ALPM_ERR_DB_WRITE
        or _alpm_errno_t.ALPM_ERR_DB_REMOVE => AlpmErrorCategory.Database,

      _alpm_errno_t.ALPM_ERR_PKG_NOT_FOUND
        or _alpm_errno_t.ALPM_ERR_PKG_IGNORED
        or _alpm_errno_t.ALPM_ERR_PKG_INVALID
        or _alpm_errno_t.ALPM_ERR_PKG_INVALID_CHECKSUM
        or _alpm_errno_t.ALPM_ERR_PKG_INVALID_SIG
        or _alpm_errno_t.ALPM_ERR_PKG_MISSING_SIG
        or _alpm_errno_t.ALPM_ERR_PKG_OPEN
        or _alpm_errno_t.ALPM_ERR_PKG_CANT_REMOVE
        or _alpm_errno_t.ALPM_ERR_PKG_INVALID_NAME
        or _alpm_errno_t.ALPM_ERR_PKG_INVALID_ARCH => AlpmErrorCategory.Package,

      _alpm_errno_t.ALPM_ERR_TRANS_NOT_NULL
        or _alpm_errno_t.ALPM_ERR_TRANS_NULL
        or _alpm_errno_t.ALPM_ERR_TRANS_DUP_TARGET
        or _alpm_errno_t.ALPM_ERR_TRANS_DUP_FILENAME
        or _alpm_errno_t.ALPM_ERR_TRANS_NOT_INITIALIZED
        or _alpm_errno_t.ALPM_ERR_TRANS_NOT_PREPARED
        or _alpm_errno_t.ALPM_ERR_TRANS_ABORT
        or _alpm_errno_t.ALPM_ERR_TRANS_TYPE
        or _alpm_errno_t.ALPM_ERR_TRANS_NOT_LOCKED
        or _alpm_errno_t.ALPM_ERR_TRANS_HOOK_FAILED => AlpmErrorCategory.Transaction,

      _alpm_errno_t.ALPM_ERR_SIG_MISSING
        or _alpm_errno_t.ALPM_ERR_SIG_INVALID
        or _alpm_errno_t.ALPM_ERR_MISSING_CAPABILITY_SIGNATURES => AlpmErrorCategory.Signature,

      _alpm_errno_t.ALPM_ERR_RETRIEVE_PREPARE
        or _alpm_errno_t.ALPM_ERR_RETRIEVE
        or _alpm_errno_t.ALPM_ERR_LIBCURL
        or _alpm_errno_t.ALPM_ERR_EXTERNAL_DOWNLOAD => AlpmErrorCategory.Retrieve,

      // No dedicated category: still an AlpmException, with the errno and libalpm's message.
      _alpm_errno_t.ALPM_ERR_BADPERMS
        or _alpm_errno_t.ALPM_ERR_SYSTEM
        or _alpm_errno_t.ALPM_ERR_NOT_A_FILE
        or _alpm_errno_t.ALPM_ERR_NOT_A_DIR
        or _alpm_errno_t.ALPM_ERR_WRONG_ARGS
        or _alpm_errno_t.ALPM_ERR_DISK_SPACE
        or _alpm_errno_t.ALPM_ERR_HANDLE_NULL
        or _alpm_errno_t.ALPM_ERR_HANDLE_NOT_NULL
        or _alpm_errno_t.ALPM_ERR_HANDLE_LOCK
        or _alpm_errno_t.ALPM_ERR_SERVER_BAD_URL
        or _alpm_errno_t.ALPM_ERR_SERVER_NONE
        or _alpm_errno_t.ALPM_ERR_UNSATISFIED_DEPS
        or _alpm_errno_t.ALPM_ERR_CONFLICTING_DEPS
        or _alpm_errno_t.ALPM_ERR_FILE_CONFLICTS
        or _alpm_errno_t.ALPM_ERR_INVALID_REGEX
        or _alpm_errno_t.ALPM_ERR_LIBARCHIVE
        or _alpm_errno_t.ALPM_ERR_GPGME => AlpmErrorCategory.Generic,

      _ => AlpmErrorCategory.Unknown
    };
  }
}

/// <summary>The exception type an errno's errors are reported as.</summary>
internal enum AlpmErrorCategory
{
  /// <summary>Not known to this library (a libalpm newer than the bindings); reported as <see cref="AlpmException"/>.</summary>
  Unknown,

  /// <summary>No dedicated category; reported as <see cref="AlpmException"/>.</summary>
  Generic,

  /// <summary>Reported as <see cref="OutOfMemoryException"/>.</summary>
  Memory,

  /// <summary>Reported as <see cref="AlpmDatabaseException"/>.</summary>
  Database,

  /// <summary>Reported as <see cref="AlpmPackageException"/>.</summary>
  Package,

  /// <summary>Reported as <see cref="AlpmTransactionException"/>.</summary>
  Transaction,

  /// <summary>Reported as <see cref="AlpmSignatureException"/>.</summary>
  Signature,

  /// <summary>Reported as <see cref="AlpmRetrieveException"/>.</summary>
  Retrieve
}

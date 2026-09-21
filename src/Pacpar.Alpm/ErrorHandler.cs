using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

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

/// <summary>An error from libalpm's transaction handling (<c>ALPM_ERR_TRANS_*</c>).</summary>
public class AlpmTransactionException(_alpm_errno_t errno, string? strError = null, string? context = null)
  : AlpmException(errno, strError, context);

/// <summary>
/// An error concerning a package (<c>ALPM_ERR_PKG_*</c>).
/// </summary>
/// <remarks>
/// <see cref="Package"/> is set when the failing call already knew the package it was operating on;
/// the errno-driven factory cannot fill it in.
/// </remarks>
public class AlpmPackageException(
  _alpm_errno_t errno,
  string? strError = null,
  Package? package = null,
  string? context = null)
  : AlpmException(errno, strError, context)
{
  /// <summary>The package the failed call was operating on, when known.</summary>
  public Package? Package { get; } = package;
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
    return Marshal.PtrToStringUTF8((nint)NativeMethods.alpm_strerror(errno));
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

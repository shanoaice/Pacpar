using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

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
  /// Returns libalpm's textual description of <paramref name="errno"/>, or a fallback error string if unknown.
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
  Unknown,
  Memory,
  Database,
  Package,
  Transaction,
  Signature,
  Retrieve,
  Generic
}

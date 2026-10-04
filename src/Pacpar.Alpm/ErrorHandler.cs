using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>
/// Turns libalpm errnos into exceptions.
/// </summary>
internal static class ErrorHandler
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
  /// The <see cref="int"/> overload: the public error vocabulary is a plain number since ADR 0006,
  /// and call sites that already carry one should not have to name the generated enum.
  /// </summary>
  public static Exception? GetException(int errno) => GetException((_alpm_errno_t)errno);

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

  /// <summary>The <see cref="int"/> overload of <see cref="ToException(_alpm_errno_t)"/>.</summary>
  public static Exception ToException(int errno) => ToException((_alpm_errno_t)errno);

  /// <summary>
  /// The exception for a failure observed at a call site, with the operation recorded in its message.
  /// </summary>
  internal static Exception ToException(int errno, string? context)
    => Create((_alpm_errno_t)errno, context);

  /// <summary>
  /// Returns libalpm's textual description of <paramref name="errno"/>, or a fallback error string if unknown.
  /// </summary>
  internal static string? StrError(_alpm_errno_t errno) => StrErrorCore(errno);

  /// <summary>The <see cref="int"/> overload of <see cref="StrError(_alpm_errno_t)"/>.</summary>
  internal static string? StrError(int errno) => StrErrorCore((_alpm_errno_t)errno);

  /// <summary>
  /// The <c>ALPM_ERR_*</c> name for a raw code, for use in messages. Unknown codes fall back to the
  /// number, which is all that can be said about them.
  /// </summary>
  internal static string NameOf(int errno) => ((_alpm_errno_t)errno).ToString();

  private static unsafe string? StrErrorCore(_alpm_errno_t errno)
  {
    // alpm_strerror is bounds-checked: out-of-range values come back as "unexpected error".
    return NativeString.FromNative((nint)NativeMethods.alpm_strerror(errno));
  }

  private static Exception Create(_alpm_errno_t errno, string? context = null)
  {
    var strError = StrError(errno);

    return Categorize(errno) switch
    {
      // The one BCL type kept on purpose: an allocation failure is not a library-domain error and
      // must not be caught by a broad `catch (AlpmException)`.
      AlpmErrorCategory.Memory => new OutOfMemoryException(strError),
      AlpmErrorCategory.Database => new AlpmDatabaseException((int)errno, strError, context),
      AlpmErrorCategory.Package => new AlpmPackageException((int)errno, strError, context: context),
      AlpmErrorCategory.Transaction => new AlpmTransactionException((int)errno, strError, context),
      AlpmErrorCategory.Signature => new AlpmSignatureException((int)errno, strError, context),
      AlpmErrorCategory.Retrieve => new AlpmRetrieveException((int)errno, strError, context),
      _ => new AlpmException((int)errno, strError, context)
    };
  }

  /// <summary>
  /// The exception for a failure this library already described, reusing its message instead of
  /// asking libalpm for it a second time.
  /// </summary>
  /// <param name="errno">The raw libalpm code.</param>
  /// <param name="message">libalpm's description of it, or <c>null</c> to read one.</param>
  /// <param name="context">Optional operation description.</param>
  /// <remarks>
  /// The entry point for <see cref="AlpmFailure.ToException"/>: the failure already read
  /// <c>alpm_strerror</c>, and a second read would be a native call on the throw path.
  /// </remarks>
  internal static Exception Create(int errno, string? message, string? context)
  {
    message ??= StrError(errno);

    return Categorize((_alpm_errno_t)errno) switch
    {
      AlpmErrorCategory.Memory => new OutOfMemoryException(message),
      AlpmErrorCategory.Database => new AlpmDatabaseException(errno, message, context),
      AlpmErrorCategory.Package => new AlpmPackageException(errno, message, context: context),
      AlpmErrorCategory.Transaction => new AlpmTransactionException(errno, message, context),
      AlpmErrorCategory.Signature => new AlpmSignatureException(errno, message, context),
      AlpmErrorCategory.Retrieve => new AlpmRetrieveException(errno, message, context),
      _ => new AlpmException(errno, message, context)
    };
  }

  /// <summary>
  /// The stable semantic code for an errno, or <see cref="AlpmFailureCode.UnknownNative"/> for a
  /// valid code this mapping does not know.
  /// </summary>
  /// <remarks>
  /// Every errno known to the bindings must be listed here;
  /// <c>ErrorHandlerTests.CodeOf_KnowsEveryKnownErrno</c> fails when a libalpm update adds one.
  /// <see cref="CodeOf"/> is the only place a raw errno becomes part of this library's vocabulary:
  /// <see cref="CategoryOf"/> maps the code to a family, so the family is derived rather than
  /// written a second time.
  /// </remarks>
  internal static AlpmFailureCode CodeOf(int errno) => ((_alpm_errno_t)errno) switch
  {
    _alpm_errno_t.ALPM_ERR_DB_OPEN => AlpmFailureCode.DatabaseOpen,
    _alpm_errno_t.ALPM_ERR_DB_CREATE => AlpmFailureCode.DatabaseCreate,
    _alpm_errno_t.ALPM_ERR_DB_NULL => AlpmFailureCode.DatabaseNull,
    _alpm_errno_t.ALPM_ERR_DB_NOT_NULL => AlpmFailureCode.DatabaseNotNull,
    _alpm_errno_t.ALPM_ERR_DB_NOT_FOUND => AlpmFailureCode.DatabaseNotFound,
    _alpm_errno_t.ALPM_ERR_DB_INVALID => AlpmFailureCode.DatabaseInvalid,
    _alpm_errno_t.ALPM_ERR_DB_INVALID_SIG => AlpmFailureCode.DatabaseInvalidSignature,
    _alpm_errno_t.ALPM_ERR_DB_VERSION => AlpmFailureCode.DatabaseVersion,
    _alpm_errno_t.ALPM_ERR_DB_WRITE => AlpmFailureCode.DatabaseWrite,
    _alpm_errno_t.ALPM_ERR_DB_REMOVE => AlpmFailureCode.DatabaseRemove,

    _alpm_errno_t.ALPM_ERR_PKG_NOT_FOUND => AlpmFailureCode.PackageNotFound,
    _alpm_errno_t.ALPM_ERR_PKG_IGNORED => AlpmFailureCode.PackageIgnored,
    _alpm_errno_t.ALPM_ERR_PKG_MISSING_SIG => AlpmFailureCode.PackageMissingSignature,
    _alpm_errno_t.ALPM_ERR_PKG_OPEN => AlpmFailureCode.PackageOpen,
    _alpm_errno_t.ALPM_ERR_PKG_CANT_REMOVE => AlpmFailureCode.PackageCannotRemove,
    _alpm_errno_t.ALPM_ERR_PKG_INVALID_NAME => AlpmFailureCode.PackageInvalidName,

    _alpm_errno_t.ALPM_ERR_TRANS_NOT_NULL => AlpmFailureCode.TransactionNotNull,
    _alpm_errno_t.ALPM_ERR_TRANS_NULL => AlpmFailureCode.TransactionNull,
    _alpm_errno_t.ALPM_ERR_TRANS_DUP_TARGET => AlpmFailureCode.DuplicateTarget,
    _alpm_errno_t.ALPM_ERR_TRANS_DUP_FILENAME => AlpmFailureCode.DuplicateFileName,
    _alpm_errno_t.ALPM_ERR_TRANS_NOT_INITIALIZED => AlpmFailureCode.TransactionNotInitialized,
    _alpm_errno_t.ALPM_ERR_TRANS_NOT_PREPARED => AlpmFailureCode.TransactionNotPrepared,
    _alpm_errno_t.ALPM_ERR_TRANS_ABORT => AlpmFailureCode.TransactionAbort,
    _alpm_errno_t.ALPM_ERR_TRANS_TYPE => AlpmFailureCode.TransactionType,
    _alpm_errno_t.ALPM_ERR_TRANS_NOT_LOCKED => AlpmFailureCode.TransactionNotLocked,
    _alpm_errno_t.ALPM_ERR_TRANS_HOOK_FAILED => AlpmFailureCode.HookFailed,
    _alpm_errno_t.ALPM_ERR_UNSATISFIED_DEPS => AlpmFailureCode.UnsatisfiedDependencies,
    _alpm_errno_t.ALPM_ERR_CONFLICTING_DEPS => AlpmFailureCode.ConflictingDependencies,
    _alpm_errno_t.ALPM_ERR_FILE_CONFLICTS => AlpmFailureCode.FileConflicts,

    // ADR 0007: these four carry a payload when a transaction call fails, so they are transaction
    // failures from every entry point. Only the payload distinguishes them.
    _alpm_errno_t.ALPM_ERR_PKG_INVALID => AlpmFailureCode.PackageInvalid,
    _alpm_errno_t.ALPM_ERR_PKG_INVALID_CHECKSUM => AlpmFailureCode.PackageInvalidChecksum,
    _alpm_errno_t.ALPM_ERR_PKG_INVALID_SIG => AlpmFailureCode.PackageInvalidSignature,
    _alpm_errno_t.ALPM_ERR_PKG_INVALID_ARCH => AlpmFailureCode.PackageInvalidArchitecture,

    _alpm_errno_t.ALPM_ERR_SIG_MISSING => AlpmFailureCode.SignatureMissing,
    _alpm_errno_t.ALPM_ERR_SIG_INVALID => AlpmFailureCode.SignatureInvalid,
    _alpm_errno_t.ALPM_ERR_MISSING_CAPABILITY_SIGNATURES => AlpmFailureCode.MissingCapabilitySignatures,

    _alpm_errno_t.ALPM_ERR_RETRIEVE_PREPARE => AlpmFailureCode.RetrievePrepare,
    _alpm_errno_t.ALPM_ERR_RETRIEVE => AlpmFailureCode.Retrieve,
    _alpm_errno_t.ALPM_ERR_LIBCURL => AlpmFailureCode.LibCurl,
    _alpm_errno_t.ALPM_ERR_EXTERNAL_DOWNLOAD => AlpmFailureCode.ExternalDownload,

    _alpm_errno_t.ALPM_ERR_BADPERMS => AlpmFailureCode.BadPermissions,
    _alpm_errno_t.ALPM_ERR_SYSTEM => AlpmFailureCode.System,
    _alpm_errno_t.ALPM_ERR_NOT_A_FILE => AlpmFailureCode.NotAFile,
    _alpm_errno_t.ALPM_ERR_NOT_A_DIR => AlpmFailureCode.NotADirectory,
    _alpm_errno_t.ALPM_ERR_WRONG_ARGS => AlpmFailureCode.WrongArguments,
    _alpm_errno_t.ALPM_ERR_DISK_SPACE => AlpmFailureCode.DiskSpace,
    _alpm_errno_t.ALPM_ERR_HANDLE_NULL => AlpmFailureCode.HandleNull,
    _alpm_errno_t.ALPM_ERR_HANDLE_NOT_NULL => AlpmFailureCode.HandleNotNull,
    _alpm_errno_t.ALPM_ERR_HANDLE_LOCK => AlpmFailureCode.HandleLock,
    _alpm_errno_t.ALPM_ERR_SERVER_BAD_URL => AlpmFailureCode.ServerBadUrl,
    _alpm_errno_t.ALPM_ERR_SERVER_NONE => AlpmFailureCode.ServerNone,
    _alpm_errno_t.ALPM_ERR_INVALID_REGEX => AlpmFailureCode.InvalidRegex,
    _alpm_errno_t.ALPM_ERR_LIBARCHIVE => AlpmFailureCode.LibArchive,
    _alpm_errno_t.ALPM_ERR_GPGME => AlpmFailureCode.Gpgme,

    _ => AlpmFailureCode.UnknownNative
  };

  /// <summary>
  /// The family a failure code belongs to, which is what picks its exception type.
  /// </summary>
  internal static AlpmErrorCategory CategoryOf(AlpmFailureCode code) => code switch
  {
    AlpmFailureCode.DatabaseOpen or AlpmFailureCode.DatabaseCreate or AlpmFailureCode.DatabaseNull
      or AlpmFailureCode.DatabaseNotNull or AlpmFailureCode.DatabaseNotFound
      or AlpmFailureCode.DatabaseInvalid or AlpmFailureCode.DatabaseInvalidSignature
      or AlpmFailureCode.DatabaseVersion or AlpmFailureCode.DatabaseWrite
      or AlpmFailureCode.DatabaseRemove => AlpmErrorCategory.Database,

    AlpmFailureCode.PackageNotFound or AlpmFailureCode.PackageIgnored
      or AlpmFailureCode.PackageMissingSignature or AlpmFailureCode.PackageOpen
      or AlpmFailureCode.PackageCannotRemove or AlpmFailureCode.PackageInvalidName
      => AlpmErrorCategory.Package,

    AlpmFailureCode.TransactionNotNull or AlpmFailureCode.TransactionNull
      or AlpmFailureCode.DuplicateTarget or AlpmFailureCode.DuplicateFileName
      or AlpmFailureCode.TransactionNotInitialized or AlpmFailureCode.TransactionNotPrepared
      or AlpmFailureCode.TransactionAbort or AlpmFailureCode.TransactionType
      or AlpmFailureCode.TransactionNotLocked or AlpmFailureCode.HookFailed
      or AlpmFailureCode.UnsatisfiedDependencies or AlpmFailureCode.ConflictingDependencies
      or AlpmFailureCode.FileConflicts or AlpmFailureCode.PackageInvalid
      or AlpmFailureCode.PackageInvalidChecksum or AlpmFailureCode.PackageInvalidSignature
      or AlpmFailureCode.PackageInvalidArchitecture => AlpmErrorCategory.Transaction,

    AlpmFailureCode.SignatureMissing or AlpmFailureCode.SignatureInvalid
      or AlpmFailureCode.MissingCapabilitySignatures => AlpmErrorCategory.Signature,

    AlpmFailureCode.RetrievePrepare or AlpmFailureCode.Retrieve or AlpmFailureCode.LibCurl
      or AlpmFailureCode.ExternalDownload => AlpmErrorCategory.Retrieve,

    AlpmFailureCode.BadPermissions or AlpmFailureCode.System or AlpmFailureCode.NotAFile
      or AlpmFailureCode.NotADirectory or AlpmFailureCode.WrongArguments
      or AlpmFailureCode.DiskSpace or AlpmFailureCode.HandleNull or AlpmFailureCode.HandleNotNull
      or AlpmFailureCode.HandleLock or AlpmFailureCode.ServerBadUrl or AlpmFailureCode.ServerNone
      or AlpmFailureCode.InvalidRegex or AlpmFailureCode.LibArchive or AlpmFailureCode.Gpgme
      => AlpmErrorCategory.Generic,

    _ => AlpmErrorCategory.Unknown
  };

  /// <summary>
  /// Classifies an errno into the exception type libalpm errors of that kind are reported as.
  /// </summary>
  /// <remarks>
  /// Every errno known to the bindings must reach a real code through <see cref="CodeOf"/>; only
  /// values this library does not know return <see cref="AlpmErrorCategory.Unknown"/>, and
  /// <c>ErrorHandlerTests.Categorize_ClassifiesEveryKnownErrno</c> fails when a libalpm update adds
  /// one. That is what keeps the mapping in sync instead of letting new errnos degrade silently.
  /// </remarks>
  internal static AlpmErrorCategory Categorize(_alpm_errno_t errno)
    => errno == _alpm_errno_t.ALPM_ERR_MEMORY
      ? AlpmErrorCategory.Memory
      : CategoryOf(CodeOf((int)errno));
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

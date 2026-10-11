using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

/// <summary>
/// A managed ALPM operation failure: what libalpm said went wrong, in this library's own vocabulary
/// rather than in libalpm's errno numbering.
/// </summary>
/// <remarks>
/// A failure is a value, not an exception. The public exception hierarchy is a projection of it —
/// <see cref="ToException"/> is the only way one becomes the other — so the throwing C# facade and
/// the failure-returning seam cannot disagree about what an errno means.
/// <para>
/// Two things are deliberately <b>not</b> failures and are never returned: <c>ALPM_ERR_MEMORY</c>,
/// which stays an <see cref="OutOfMemoryException"/>, and a failure libalpm reported without setting
/// an errno, which stays an <see cref="AlpmNativeFailureException"/>. Both are thrown where they are
/// observed, so no instance of this type carries them.
/// </para>
/// </remarks>
internal sealed class AlpmFailure
{
  private AlpmFailure(AlpmFailureCode code, int nativeCode, string? message, string operation,
    AlpmFailureDetail? detail)
  {
    Code = code;
    NativeCode = nativeCode;
    Message = message;
    Operation = operation;
    Detail = detail;
  }

  /// <summary>The stable semantic code. This is the value the F# layer matches on.</summary>
  internal AlpmFailureCode Code { get; }

  /// <summary>
  /// libalpm's own <c>pm_errno</c>, kept for diagnostics and for the codes
  /// <see cref="AlpmFailureCode"/> collapses (ADR 0009). It carries no stability promise.
  /// </summary>
  internal int NativeCode { get; }

  /// <summary>libalpm's own description of <see cref="NativeCode"/> (<c>alpm_strerror</c>).</summary>
  internal string? Message { get; }

  /// <summary>
  /// What failed, e.g. <c>"load package"</c>. The default wording of <see cref="ToException"/>; a
  /// caller may override it, and the F# layer ignores it.
  /// </summary>
  internal string Operation { get; }

  /// <summary>Structured evidence libalpm dumped, such as missing dependencies. <c>null</c> when it dumped none.</summary>
  internal AlpmFailureDetail? Detail { get; }

  /// <summary>
  /// The failure for a call that signalled failure through its <b>return value</b>, read at the
  /// failure site. Carries no payload: nothing was dumped.
  /// </summary>
  /// <param name="errno">The errno read immediately after the failing call, as the first statement of the failure branch.</param>
  /// <param name="operation">What failed, e.g. <c>"load package"</c>.</param>
  /// <exception cref="OutOfMemoryException"><paramref name="errno"/> is <c>ALPM_ERR_MEMORY</c>.</exception>
  /// <exception cref="AlpmNativeFailureException"><paramref name="errno"/> is <c>ALPM_ERR_OK</c>.</exception>
  internal static AlpmFailure Of(int errno, string operation)
  {
    RejectNotAFailureValue(errno, operation);
    return new AlpmFailure(ErrorHandler.CodeOf(errno), errno, ErrorHandler.StrError(errno), operation, null);
  }

  /// <summary>
  /// The failure for a failed <c>alpm_trans_prepare</c>/<c>alpm_trans_commit</c>, from the errno and
  /// the list libalpm dumped into that call's output parameter.
  /// </summary>
  /// <remarks>
  /// Every branch <b>consumes</b> <paramref name="data"/>: the entries are snapshotted into managed
  /// objects first, then the whole list is freed with the element destructor the errno requires. An
  /// errno with no documented payload frees the nodes only — guessing a destructor aborts the
  /// process.
  /// </remarks>
  internal static unsafe AlpmFailure Take(int errno, _alpm_list_t* data, string operation)
  {
    RejectNotAFailureValue(errno, operation);

    var code = ErrorHandler.CodeOf(errno);

    AlpmFailureDetail? detail = code switch
    {
      AlpmFailureCode.UnsatisfiedDependencies =>
        new AlpmFailureDetail.MissingDependencies(
          AlpmOwnedList<DepMissing>.Take(data, &DepMissing.Factory, &MemoryManagement.DepMissingFreeExtern)),
      AlpmFailureCode.ConflictingDependencies =>
        new AlpmFailureDetail.ConflictingDependencies(
          AlpmOwnedList<Conflict>.Take(data, &Conflict.Factory, &MemoryManagement.ConflictFreeExtern)),
      AlpmFailureCode.FileConflicts =>
        new AlpmFailureDetail.ConflictingFiles(
          AlpmOwnedList<FileConflict>.Take(data, &FileConflict.Factory, &MemoryManagement.FileConflictFreeExtern)),
      AlpmFailureCode.PackageInvalid
        or AlpmFailureCode.PackageInvalidChecksum
        or AlpmFailureCode.PackageInvalidSignature
        or AlpmFailureCode.PackageInvalidArchitecture =>
        new AlpmFailureDetail.RejectedPackages(AlpmStringList.TakeOwned(data, &MemoryManagement.CFreeExtern)),
      _ => null
    };

    if (detail is null)
    {
      AlpmNativeList.Free(data, null);
    }

    return new AlpmFailure(code, errno, ErrorHandler.StrError(errno), operation, detail);
  }

  /// <summary>
  /// The exception this failure is reported as. One errno has one public exception type (ADR 0007),
  /// so the type follows from <see cref="Code"/>.
  /// </summary>
  /// <param name="context">Overrides <see cref="Operation"/> in the message.</param>
  /// <remarks>
  /// Reads no native memory through a handle: the only native call it can make is
  /// <c>alpm_strerror</c>, a table lookup that never touches one. That is what lets a caller throw
  /// the result after its <c>GC.KeepAlive</c>.
  /// </remarks>
  internal Exception ToException(string? context = null)
  {
    context ??= Operation;

    switch (Code)
    {
      case AlpmFailureCode.UnsatisfiedDependencies when Detail is AlpmFailureDetail.MissingDependencies missing:
        return new AlpmTransactionException.MissingDependencies(missing.Dependencies, context);
      case AlpmFailureCode.ConflictingDependencies when Detail is AlpmFailureDetail.ConflictingDependencies conflicts:
        return new AlpmTransactionException.ConflictingDependencies(conflicts.Conflicts, context);
      case AlpmFailureCode.FileConflicts when Detail is AlpmFailureDetail.ConflictingFiles files:
        return new AlpmTransactionException.ConflictingFiles(files.Conflicts, context);
      case AlpmFailureCode.PackageInvalid when Detail is AlpmFailureDetail.RejectedPackages rejected:
        return new AlpmTransactionException.InvalidPackage(rejected.Packages, context);
      case AlpmFailureCode.PackageInvalidChecksum when Detail is AlpmFailureDetail.RejectedPackages rejected:
        return new AlpmTransactionException.InvalidPackageChecksum(rejected.Packages, context);
      case AlpmFailureCode.PackageInvalidSignature when Detail is AlpmFailureDetail.RejectedPackages rejected:
        return new AlpmTransactionException.InvalidPackageSignature(rejected.Packages, context);
      case AlpmFailureCode.PackageInvalidArchitecture when Detail is AlpmFailureDetail.RejectedPackages rejected:
        return new AlpmTransactionException.InvalidPackageArchitecture(rejected.Packages, context);
    }

    return ErrorHandler.Create(NativeCode, Message, context);
  }

  /// <inheritdoc />
  public override string ToString()
    => $"{Code} (native {NativeCode}): {Message}";

  private static void RejectNotAFailureValue(int errno, string operation)
  {
    if (errno == (int)_alpm_errno_t.ALPM_ERR_MEMORY)
    {
      throw new OutOfMemoryException(ErrorHandler.StrError(errno));
    }

    if (errno == (int)_alpm_errno_t.ALPM_ERR_OK)
    {
      throw new AlpmNativeFailureException(operation);
    }
  }
}

/// <summary>
/// Turns libalpm errnos into semantic codes and exceptions.
/// </summary>
internal static class ErrorHandler
{
  /// <summary>
  /// The exception describing <paramref name="errno"/>, or <c>null</c> if and only if it is
  /// <see cref="_alpm_errno_t.ALPM_ERR_OK"/> (there is no error to report).
  /// </summary>
  public static Exception? GetException(_alpm_errno_t errno)
    => errno == _alpm_errno_t.ALPM_ERR_OK ? null : Create((int)errno, StrError(errno), null);

  /// <summary>
  /// The <see cref="int"/> overload of <see cref="GetException(_alpm_errno_t)"/>.
  /// </summary>
  public static Exception? GetException(int errno) => GetException((_alpm_errno_t)errno);

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
    return NativeString.FromNative((nint)NativeMethods.alpm_strerror(errno));
  }

  /// <summary>
  /// The exception for a failure this library already described, reusing its message instead of
  /// asking libalpm for it a second time.
  /// </summary>
  /// <param name="errno">The raw libalpm code.</param>
  /// <param name="message">libalpm's description of it, or <c>null</c> to read one.</param>
  /// <param name="context">Optional operation description.</param>
  internal static Exception Create(int errno, string? message, string? context = null)
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
  internal static AlpmErrorCategory Categorize(_alpm_errno_t errno)
    => errno == _alpm_errno_t.ALPM_ERR_MEMORY
      ? AlpmErrorCategory.Memory
      : CategoryOf(CodeOf((int)errno));
}

/// <summary>
/// What went wrong, in this library's own vocabulary.
/// </summary>
/// <remarks>
/// The generated <c>_alpm_errno_t</c> is a binding contract that moves with libalpm; this is a
/// language-facing contract that does not. A libalpm that adds errnos lands them on
/// <see cref="UnknownNative"/> without breaking anything, and a meaning this project already named
/// keeps its member. Adding a member is therefore always safe, and removing or renaming one is not.
/// </remarks>
internal enum AlpmFailureCode
{
  /// <summary>A valid errno this mapping does not know. <see cref="AlpmFailure.NativeCode"/> carries it.</summary>
  UnknownNative,

  DatabaseOpen,
  DatabaseCreate,
  DatabaseNull,
  DatabaseNotNull,
  DatabaseNotFound,
  DatabaseInvalid,
  DatabaseInvalidSignature,
  DatabaseVersion,
  DatabaseWrite,
  DatabaseRemove,

  PackageNotFound,
  PackageIgnored,
  PackageMissingSignature,
  PackageOpen,
  PackageCannotRemove,
  PackageInvalidName,

  TransactionNotNull,
  TransactionNull,
  DuplicateTarget,
  DuplicateFileName,
  TransactionNotInitialized,
  TransactionNotPrepared,
  TransactionAbort,
  TransactionType,
  TransactionNotLocked,
  HookFailed,
  UnsatisfiedDependencies,
  ConflictingDependencies,
  FileConflicts,

  /// <summary>A package libalpm rejected as invalid.</summary>
  PackageInvalid,

  /// <summary>A package whose checksum did not match.</summary>
  PackageInvalidChecksum,

  /// <summary>A package whose signature did not verify.</summary>
  PackageInvalidSignature,

  /// <summary>A package whose architecture the handle does not accept.</summary>
  PackageInvalidArchitecture,

  SignatureMissing,
  SignatureInvalid,
  MissingCapabilitySignatures,

  RetrievePrepare,
  Retrieve,
  LibCurl,
  ExternalDownload,

  BadPermissions,
  System,
  NotAFile,
  NotADirectory,
  WrongArguments,
  DiskSpace,
  HandleNull,
  HandleNotNull,
  HandleLock,
  ServerBadUrl,
  ServerNone,
  InvalidRegex,
  LibArchive,
  Gpgme
}

/// <summary>
/// Structured evidence libalpm attached to an ALPM operation failure.
/// </summary>
/// <remarks>
/// The cases mirror the payload-carrying exception cases, so the detail a failure holds and the
/// exception it becomes read the same at the call site. Every case is a managed snapshot: the native
/// list is consumed and freed while it is built.
/// </remarks>
internal abstract class AlpmFailureDetail
{
  /// <summary>Dependencies the transaction could not satisfy.</summary>
  internal sealed class MissingDependencies(IReadOnlyList<DepMissing> dependencies) : AlpmFailureDetail
  {
    internal IReadOnlyList<DepMissing> Dependencies { get; } = dependencies;
  }

  /// <summary>Dependencies that conflict with each other.</summary>
  internal sealed class ConflictingDependencies(IReadOnlyList<Conflict> conflicts) : AlpmFailureDetail
  {
    internal IReadOnlyList<Conflict> Conflicts { get; } = conflicts;
  }

  /// <summary>Files already on disk that a package in the transaction would overwrite.</summary>
  internal sealed class ConflictingFiles(IReadOnlyList<FileConflict> conflicts) : AlpmFailureDetail
  {
    internal IReadOnlyList<FileConflict> Conflicts { get; } = conflicts;
  }

  /// <summary>
  /// The packages libalpm rejected, as libalpm named them.
  /// <see cref="AlpmFailure.Code"/> says why: invalid, checksum, signature or architecture.
  /// </summary>
  internal sealed class RejectedPackages(IReadOnlyList<string> packages) : AlpmFailureDetail
  {
    internal IReadOnlyList<string> Packages { get; } = packages;
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

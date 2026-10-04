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

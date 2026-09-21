using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

#pragma warning disable CA2208
#pragma warning disable NotResolvedInText
namespace Pacpar.Alpm;

/// <summary>
/// Represents exceptions during package handling (transactions).
/// The corresponding package is available via the <see cref="Package"/> property.
/// The corresponding errno is available via <see cref="Exception.InnerException"/>, in formatted exception form,
/// or via <see cref="Errno"/>, in raw errno form.
/// </summary>
/// <param name="message"></param>
/// <param name="package"></param>
/// <param name="errno"></param>
public class PackageException(string message, Package package, _alpm_errno_t errno) : Exception(message, ErrorHandler.GetException(errno))
{
  public Package Package => package;
  public _alpm_errno_t Errno => errno;
}

/// <summary>
/// A libalpm error whose errno has no more specific .NET exception mapping.
/// </summary>
/// <remarks>
/// The errno is known — only the .NET exception type is generic. Carrying the raw errno plus
/// libalpm's own message keeps an error introduced by a newer libalpm diagnosable instead of
/// turning into a null dereference at the throw site.
/// </remarks>
public class AlpmException(string message, _alpm_errno_t errno, string? strError) : Exception(message)
{
  /// <summary>The raw libalpm errno.</summary>
  public _alpm_errno_t Errno { get; } = errno;

  /// <summary>libalpm's own description of <see cref="Errno"/>, or <c>null</c>.</summary>
  public string? StrError { get; } = strError;
}

public static class ErrorHandler
{
  /// <summary>
  /// The exception describing <paramref name="errno"/>, or <c>null</c> if and only if it is
  /// <see cref="_alpm_errno_t.ALPM_ERR_OK"/> (there is no error to report).
  /// </summary>
  /// <remarks>
  /// Every non-OK value yields a non-null exception. An errno without a specific mapping — for
  /// example one added by a libalpm newer than this switch — falls back to
  /// <see cref="AlpmException"/>, so a forgotten sync after a libalpm update surfaces as a
  /// diagnosable error instead of a <see cref="NullReferenceException"/> thrown by
  /// <c>throw GetException(...)!</c>.
  /// <para>
  /// A libalpm update that adds an errno is caught by
  /// <c>ErrorHandlerTests.GetException_MapsEveryKnownErrno_ToASpecificException</c>: an unmapped
  /// enum member fails that test rather than silently taking the fallback.
  /// </para>
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

  private static Exception Create(_alpm_errno_t errno)
  {
    return errno switch
    {
      _alpm_errno_t.ALPM_ERR_MEMORY => new OutOfMemoryException(),
      _alpm_errno_t.ALPM_ERR_BADPERMS => new UnauthorizedAccessException(),
      _alpm_errno_t.ALPM_ERR_SYSTEM => new SystemException(),
      _alpm_errno_t.ALPM_ERR_NOT_A_FILE => new ArgumentException("Path is not a file", "file"),
      _alpm_errno_t.ALPM_ERR_NOT_A_DIR => new ArgumentException("Path is not a directory", "directory"),
      _alpm_errno_t.ALPM_ERR_WRONG_ARGS => new ArgumentException("Wrong arguments", "arguments"),
      _alpm_errno_t.ALPM_ERR_DISK_SPACE => new IOException("Not enough disk space"),
      _alpm_errno_t.ALPM_ERR_HANDLE_NULL => new ArgumentNullException("handle"),
      _alpm_errno_t.ALPM_ERR_HANDLE_NOT_NULL => new ArgumentException("handle"),
      _alpm_errno_t.ALPM_ERR_HANDLE_LOCK => new IOException("Failed to acquire lock"),
      _alpm_errno_t.ALPM_ERR_DB_OPEN => new IOException("Failed to open database"),
      _alpm_errno_t.ALPM_ERR_DB_CREATE => new IOException("Failed to create database"),
      _alpm_errno_t.ALPM_ERR_DB_NULL => new ArgumentNullException("database"),
      _alpm_errno_t.ALPM_ERR_DB_NOT_NULL => new ArgumentException("database should be null", "database"),
      _alpm_errno_t.ALPM_ERR_DB_NOT_FOUND => new FileNotFoundException("database not found"),
      _alpm_errno_t.ALPM_ERR_DB_INVALID => new InvalidOperationException("database is invalid"),
      _alpm_errno_t.ALPM_ERR_DB_INVALID_SIG => new InvalidOperationException("database signature is invalid"),
      _alpm_errno_t.ALPM_ERR_DB_VERSION => new InvalidOperationException("The localdb is in a newer/older format than libalpm expects"),
      _alpm_errno_t.ALPM_ERR_DB_WRITE => new IOException("Failed to write to database"),
      _alpm_errno_t.ALPM_ERR_DB_REMOVE => new Exception("Failed to remove entry from database"),
      _alpm_errno_t.ALPM_ERR_SERVER_BAD_URL => new UriFormatException("Server URL is in an invalid format"),
      _alpm_errno_t.ALPM_ERR_SERVER_NONE => new InvalidOperationException("The database has no configured servers"),
      _alpm_errno_t.ALPM_ERR_TRANS_NOT_NULL => new InvalidOperationException("A transaction is already initialized"),
      _alpm_errno_t.ALPM_ERR_TRANS_NULL => new InvalidOperationException("A transaction has not been initialized"),
      _alpm_errno_t.ALPM_ERR_TRANS_DUP_TARGET => new InvalidOperationException("Duplicate target in transaction"),
      _alpm_errno_t.ALPM_ERR_TRANS_DUP_FILENAME => new InvalidOperationException("Duplicate filename in transaction"),
      _alpm_errno_t.ALPM_ERR_TRANS_NOT_INITIALIZED => new InvalidOperationException("A transaction has not been initialized"),
      _alpm_errno_t.ALPM_ERR_TRANS_NOT_PREPARED => new InvalidOperationException("Transaction has not been prepared"),
      _alpm_errno_t.ALPM_ERR_TRANS_ABORT => new Exception("Transaction was aborted"),
      _alpm_errno_t.ALPM_ERR_TRANS_TYPE => new Exception("Failed to interrupt transaction"),
      _alpm_errno_t.ALPM_ERR_TRANS_NOT_LOCKED => new InvalidOperationException("Tried to commit transaction without locking the database"),
      _alpm_errno_t.ALPM_ERR_TRANS_HOOK_FAILED => new Exception("A hook failed to run"),
      _alpm_errno_t.ALPM_ERR_PKG_NOT_FOUND => new FileNotFoundException("Package not found"),
      _alpm_errno_t.ALPM_ERR_PKG_IGNORED => new Exception("Package is in ignorepkg"),
      _alpm_errno_t.ALPM_ERR_PKG_INVALID => new InvalidOperationException("Package is invalid"),
      _alpm_errno_t.ALPM_ERR_PKG_INVALID_CHECKSUM => new Exception("Package has an invalid checksum"),
      _alpm_errno_t.ALPM_ERR_PKG_INVALID_SIG => new Exception("Package has an invalid signature"),
      _alpm_errno_t.ALPM_ERR_PKG_MISSING_SIG => new Exception("Package does not have a signature"),
      _alpm_errno_t.ALPM_ERR_PKG_OPEN => new IOException("Cannot open the package file"),
      _alpm_errno_t.ALPM_ERR_PKG_CANT_REMOVE => new IOException("Failed to remove package files"),
      _alpm_errno_t.ALPM_ERR_PKG_INVALID_NAME => new ArgumentException("Package has an invalid name"),
      _alpm_errno_t.ALPM_ERR_PKG_INVALID_ARCH => new ArgumentException("Package has an invalid architecture"),
      _alpm_errno_t.ALPM_ERR_SIG_MISSING => new Exception("Signatures are missing"),
      _alpm_errno_t.ALPM_ERR_SIG_INVALID => new Exception("Signatures are invalid"),
      _alpm_errno_t.ALPM_ERR_UNSATISFIED_DEPS => new Exception("Dependencies could not be satisfied"),
      _alpm_errno_t.ALPM_ERR_CONFLICTING_DEPS => new Exception("Conflicting dependencies"),
      _alpm_errno_t.ALPM_ERR_FILE_CONFLICTS => new IOException("Files conflict"),
      // "Download setup failed" — introduced with libalpm 16; this mapping was missing.
      _alpm_errno_t.ALPM_ERR_RETRIEVE_PREPARE => new IOException("Download setup failed"),
      _alpm_errno_t.ALPM_ERR_RETRIEVE => new Exception("Download failed"),
      _alpm_errno_t.ALPM_ERR_INVALID_REGEX => new ArgumentException("Invalid Regex"),
      _alpm_errno_t.ALPM_ERR_LIBARCHIVE => new Exception("Error in libarchive"),
      _alpm_errno_t.ALPM_ERR_LIBCURL => new Exception("Error in libcurl"),
      _alpm_errno_t.ALPM_ERR_EXTERNAL_DOWNLOAD => new Exception("Error in external download program"),
      _alpm_errno_t.ALPM_ERR_GPGME => new Exception("Error in gpgme"),
      _alpm_errno_t.ALPM_ERR_MISSING_CAPABILITY_SIGNATURES => new NotSupportedException("Missing compile-time features"),
      _ => Unknown(errno)
    };
  }

  /// <summary>
  /// Fallback for an errno without a specific mapping: carries the raw errno and libalpm's own
  /// message so the failure stays diagnosable.
  /// </summary>
  private static unsafe AlpmException Unknown(_alpm_errno_t errno)
  {
    // alpm_strerror is bounds-checked: out-of-range values come back as "unexpected error".
    var strError = Marshal.PtrToStringUTF8((nint)NativeMethods.alpm_strerror(errno));

    return new AlpmException(
      $"libalpm error {(int)errno} \"{strError}\" has no specific .NET exception mapping.",
      errno,
      strError);
  }
}

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

public static class ErrorHandler
{
  public static Exception? GetException(_alpm_errno_t errno)
  {
    return errno switch
    {
      _alpm_errno_t.ALPM_ERR_OK => null,
      _alpm_errno_t.ALPM_ERR_MEMORY => new OutOfMemoryException(),
      _alpm_errno_t.ALPM_ERR_BADPERMS => new UnauthorizedAccessException(),
      _alpm_errno_t.ALPM_ERR_SYSTEM => new SystemException(),
      _alpm_errno_t.ALPM_ERR_NOT_A_FILE => new ArgumentException("file"),
      _alpm_errno_t.ALPM_ERR_NOT_A_DIR => new ArgumentException("directory"),
      _alpm_errno_t.ALPM_ERR_WRONG_ARGS => new ArgumentException("arguments"),
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
      _alpm_errno_t.ALPM_ERR_RETRIEVE => new Exception("Download failed"),
      _alpm_errno_t.ALPM_ERR_INVALID_REGEX => new ArgumentException("Invalid Regex"),
      _alpm_errno_t.ALPM_ERR_LIBARCHIVE => new Exception("Error in libarchive"),
      _alpm_errno_t.ALPM_ERR_LIBCURL => new Exception("Error in libcurl"),
      _alpm_errno_t.ALPM_ERR_EXTERNAL_DOWNLOAD => new Exception("Error in external download program"),
      _alpm_errno_t.ALPM_ERR_GPGME => new Exception("Error in gpgme"),
      _alpm_errno_t.ALPM_ERR_MISSING_CAPABILITY_SIGNATURES => new NotSupportedException("Missing compile-time features"),
      _ => null
    };
  }
}

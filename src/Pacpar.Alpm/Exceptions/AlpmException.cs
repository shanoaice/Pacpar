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

/// <summary>
/// An error concerning a package (<c>ALPM_ERR_PKG_*</c>).
/// </summary>
/// <remarks>
/// <see cref="AlpmPackageException.Package"/> is set when the failing call already knew the package it was operating on;
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

namespace Pacpar.Alpm;

/// <summary>Specifies the source or origin of a package.</summary>
/// <remarks>
/// A projection of libalpm's own <c>_alpm_pkgfrom_t</c>, which starts at 1, so there is no zero
/// member. ADR 0014.
/// </remarks>
public enum PackageOrigin : uint
{
  /// <summary>Loaded from a package file (<c>ALPM_PKG_FROM_FILE</c>).</summary>
  File = 1,

  /// <summary>From the local database (<c>ALPM_PKG_FROM_LOCALDB</c>).</summary>
  LocalDatabase = 2,

  /// <summary>From a sync database (<c>ALPM_PKG_FROM_SYNCDB</c>).</summary>
  SyncDatabase = 3
}

/// <summary>Why a package was installed on the system.</summary>
/// <remarks>
/// A projection of libalpm's own <c>_alpm_pkgreason_t</c>. This one does have a zero member,
/// because libalpm defines one. ADR 0014.
/// </remarks>
public enum PackageReason : uint
{
  /// <summary>Explicitly installed by the user (<c>ALPM_PKG_REASON_EXPLICIT</c>).</summary>
  Explicit = 0,

  /// <summary>Installed as a dependency (<c>ALPM_PKG_REASON_DEPEND</c>).</summary>
  Dependency = 1,

  /// <summary>
  /// The installation reason could not be determined, for example after a parse failure in the
  /// local database (<c>ALPM_PKG_REASON_UNKNOWN</c>).
  /// </summary>
  Unknown = 2
}

/// <summary>
///  Method used to validate a package.
/// </summary>
/// <remarks>
/// A projection of libalpm's own <c>alpm_pkgvalidation_t</c> bit values: the numbering is
/// libalpm's, and <see cref="None"/> is <c>1</c> rather than <c>0</c> because that is where
/// libalpm puts it. The zero member is <see cref="Unknown"/>. ADR 0014.
/// </remarks>
[Flags]
public enum PackageValidation : uint
{
  /// <summary>
  ///  The package's validation type is unknown (<c>ALPM_PKG_VALIDATION_UNKNOWN</c>).
  /// </summary>
  Unknown = 0,

  /// <summary>
  ///  The package does not have any validation (<c>ALPM_PKG_VALIDATION_NONE</c>).
  /// </summary>
  None = 1,

  /// <summary>
  ///  The package is validated with md5 (<c>ALPM_PKG_VALIDATION_MD5SUM</c>).
  /// </summary>
  Md5Sum = 2,

  /// <summary>
  ///  The package is validated with sha256 (<c>ALPM_PKG_VALIDATION_SHA256SUM</c>).
  /// </summary>
  Sha256Sum = 4,

  /// <summary>
  ///  The package is validated with a PGP signature (<c>ALPM_PKG_VALIDATION_SIGNATURE</c>).
  /// </summary>
  Signature = 8,
}

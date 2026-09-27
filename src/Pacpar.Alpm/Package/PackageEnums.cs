namespace Pacpar.Alpm;

/// <summary>Where a package handle comes from (libalpm's <c>_alpm_pkgfrom_t</c>).</summary>
public enum PackageOrigin : uint
{
  /// <summary>Loaded from a package file.</summary>
  File = 1,

  /// <summary>From the local database.</summary>
  LocalDatabase = 2,

  /// <summary>From a sync database.</summary>
  SyncDatabase = 3
}

/// <summary>Why a package is installed (libalpm's <c>_alpm_pkgreason_t</c>).</summary>
public enum PackageReason : uint
{
  /// <summary>Explicitly installed by the user.</summary>
  Explicit = 0,

  /// <summary>Installed as a dependency.</summary>
  Dependency = 1,

  /// <summary>libalpm could not determine the reason.</summary>
  Unknown = 2
}

// ReSharper disable InconsistentNaming
/// <summary>
///  Method used to validate a package.
/// </summary>
[Flags]
public enum PackageValidation : uint
{
  /// <summary>
  ///  The package's validation type is unknown
  /// </summary>
  ALPM_PKG_VALIDATION_UNKNOWN = 0,

  /// <summary>
  ///  The package does not have any validation
  /// </summary>
  ALPM_PKG_VALIDATION_NONE = 1,

  /// <summary>
  ///  The package is validated with md5
  /// </summary>
  ALPM_PKG_VALIDATION_MD5SUM = 2,

  /// <summary>
  ///  The package is validated with sha256
  /// </summary>
  ALPM_PKG_VALIDATION_SHA256SUM = 4,

  /// <summary>
  ///  The package is validated with a PGP signature
  /// </summary>
  ALPM_PKG_VALIDATION_SIGNATURE = 8,
}
// ReSharper restore InconsistentNaming

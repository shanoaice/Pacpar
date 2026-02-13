namespace Pacpar.Alpm.Bindings;

/// <summary>
///  PGP signature verification options
/// </summary>
[Flags]
public enum SigLevel : uint
{
  /// <summary>
  ///  Packages require a signature
  /// </summary>
  ALPM_SIG_PACKAGE = 1,
  /// <summary>
  ///  Packages do not require a signature,
  ///  but check packages that do have signatures
  /// </summary>
  ALPM_SIG_PACKAGE_OPTIONAL = 2,
  /// <summary>
  ///  Packages do not require a signature,
  ///  but check packages that do have signatures
  /// </summary>
  ALPM_SIG_PACKAGE_MARGINAL_OK = 4,
  /// <summary>
  ///  Allow packages with signatures that are unknown trust
  /// </summary>
  ALPM_SIG_PACKAGE_UNKNOWN_OK = 8,
  /// <summary>
  ///  Databases require a signature
  /// </summary>
  ALPM_SIG_DATABASE = 1024,
  /// <summary>
  ///  Databases do not require a signature,
  ///  but check databases that do have signatures
  /// </summary>
  ALPM_SIG_DATABASE_OPTIONAL = 2048,
  /// <summary>
  ///  Allow databases with signatures that are marginal trust
  /// </summary>
  ALPM_SIG_DATABASE_MARGINAL_OK = 4096,
  /// <summary>
  ///  Allow databases with signatures that are unknown trust
  /// </summary>
  ALPM_SIG_DATABASE_UNKNOWN_OK = 8192,
  /// <summary>
  ///  The Default siglevel
  /// </summary>
  ALPM_SIG_USE_DEFAULT = 1073741824,
}

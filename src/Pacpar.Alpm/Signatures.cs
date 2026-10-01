namespace Pacpar.Alpm;

/// <summary>
///  PGP signature verification options
/// </summary>
[Flags]
public enum SigLevel : uint
{
  /// <summary>
  ///  Packages require a signature
  /// </summary>
  AlpmSigPackage = 1,
  /// <summary>
  ///  Packages do not require a signature,
  ///  but check packages that do have signatures
  /// </summary>
  AlpmSigPackageOptional = 2,
  /// <summary>
  ///  Allow packages with signatures that have marginal trust.
  /// </summary>
  AlpmSigPackageMarginalOk = 4,
  /// <summary>
  ///  Allow packages with signatures that are unknown trust
  /// </summary>
  AlpmSigPackageUnknownOk = 8,
  /// <summary>
  ///  Databases require a signature
  /// </summary>
  AlpmSigDatabase = 1024,
  /// <summary>
  ///  Databases do not require a signature,
  ///  but check databases that do have signatures
  /// </summary>
  AlpmSigDatabaseOptional = 2048,
  /// <summary>
  ///  Allow databases with signatures that are marginal trust
  /// </summary>
  AlpmSigDatabaseMarginalOk = 4096,
  /// <summary>
  ///  Allow databases with signatures that are unknown trust
  /// </summary>
  AlpmSigDatabaseUnknownOk = 8192,
  /// <summary>
  ///  The Default siglevel
  /// </summary>
  AlpmSigUseDefault = 1073741824,
}

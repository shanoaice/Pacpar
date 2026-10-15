namespace Pacpar.Alpm;

/// <summary>
///  PGP signature verification options
/// </summary>
/// <remarks>
/// A projection of libalpm's own <c>alpm_siglevel_t</c> bit values, so the numbering is libalpm's
/// and there is no zero member: libalpm defines none. Session options, database registration and
/// package loading all consume this enum, which is why it has a file of its own (ADR 0014).
/// <para>
/// The type name stays singular and close to the native typedef; the member names drop the
/// <c>AlpmSig</c> prefix, and each member records the constant it projects. ADR 0014.
/// </para>
/// </remarks>
[Flags]
public enum SigLevel : uint
{
  /// <summary>
  ///  Packages require a signature (<c>ALPM_SIG_PACKAGE</c>).
  /// </summary>
  Package = 1,
  /// <summary>
  ///  Packages do not require a signature,
  ///  but check packages that do have signatures (<c>ALPM_SIG_PACKAGE_OPTIONAL</c>).
  /// </summary>
  PackageOptional = 2,
  /// <summary>
  ///  Allow packages with signatures that have marginal trust (<c>ALPM_SIG_PACKAGE_MARGINAL_OK</c>).
  /// </summary>
  PackageMarginalOk = 4,
  /// <summary>
  ///  Allow packages with signatures that are unknown trust (<c>ALPM_SIG_PACKAGE_UNKNOWN_OK</c>).
  /// </summary>
  PackageUnknownOk = 8,
  /// <summary>
  ///  Databases require a signature (<c>ALPM_SIG_DATABASE</c>).
  /// </summary>
  Database = 1024,
  /// <summary>
  ///  Databases do not require a signature,
  ///  but check databases that do have signatures (<c>ALPM_SIG_DATABASE_OPTIONAL</c>).
  /// </summary>
  DatabaseOptional = 2048,
  /// <summary>
  ///  Allow databases with signatures that are marginal trust (<c>ALPM_SIG_DATABASE_MARGINAL_OK</c>).
  /// </summary>
  DatabaseMarginalOk = 4096,
  /// <summary>
  ///  Allow databases with signatures that are unknown trust (<c>ALPM_SIG_DATABASE_UNKNOWN_OK</c>).
  /// </summary>
  DatabaseUnknownOk = 8192,
  /// <summary>
  ///  The Default siglevel (<c>ALPM_SIG_USE_DEFAULT</c>).
  /// </summary>
  UseDefault = 1073741824,
}

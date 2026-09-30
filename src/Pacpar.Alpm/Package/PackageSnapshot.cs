namespace Pacpar.Alpm;

/// <summary>
/// A detached copy of the metadata a <see cref="PackageBase"/> reads, made by
/// <see cref="PackageBase.ToSnapshot"/>.
/// </summary>
/// <remarks>
/// A <see cref="PackageView"/> is a view over memory libalpm owns, so it stops being safe to read as
/// soon as the database, transaction or handle behind it goes away - from that point on it throws
/// <see cref="AlpmLifetimeException"/> instead of returning data. A snapshot copies every value out when
/// it is constructed and keeps no native pointer at all: it stays readable after the owning database is
/// unregistered, after the handle is released, and while a transaction commits.
/// <para>
/// The copy costs what an eager read costs - roughly 27 times a lazy scan of the local database (audit
/// report, section 4.1) - which is why it is opt-in per package. Scan through the view, and snapshot
/// only what has to outlive the scan.
/// </para>
/// <para>
/// Values are captured once. The fields libalpm mutates - <see cref="Reason"/>, <see cref="InstallDate"/>
/// and <see cref="Validation"/> - are frozen at the moment of the copy, which is the point of a
/// snapshot, but two snapshots taken at different times can therefore disagree.
/// </para>
/// <para>
/// The file list is the one optional part: it is by far the most expensive thing to copy, so it is read
/// only when <c>includeFiles</c> asks for it and is <c>null</c> otherwise.
/// </para>
/// </remarks>
public sealed class PackageSnapshot
{
  /// <summary>
  /// Copies <paramref name="package"/>; when <paramref name="includeFiles"/> is set, the file list is
  /// copied too. Prefer <see cref="PackageBase.ToSnapshot"/>.
  /// </summary>
  public PackageSnapshot(PackageBase package, bool includeFiles = false)
  {
    ArgumentNullException.ThrowIfNull(package);

    Name = package.Name;
    Version = package.Version;
    Filename = package.Filename;
    Base = package.Base;
    Description = package.Description;
    Url = package.Url;
    BuildDate = package.BuildDate;
    InstallDate = package.InstallDate;
    Packager = package.Packager;
    Md5Sum = package.Md5Sum;
    Sha256Sum = package.Sha256Sum;
    Arch = package.Arch;
    Size = package.Size;
    InstalledSize = package.InstalledSize;
    Origin = package.Origin;
    Reason = package.Reason;
    Validation = package.Validation;
    Licenses = [.. package.Licenses];
    Groups = [.. package.Groups];
    Depends = [.. package.Depends];
    OptionalDepends = [.. package.OptionalDepends];
    Conflicts = [.. package.Conflicts];
    Provides = [.. package.Provides];
    Replaces = [.. package.Replaces];
    Backup = [.. package.Backup];

    if (includeFiles) Files = [.. package.Files];
  }

  /// <summary>The package name; libalpm always sets one.</summary>
  public string Name { get; }

  public PackageVersion Version { get; }

  public string? Filename { get; }

  public string? Base { get; }

  public string? Description { get; }

  public string? Url { get; }

  public DateTimeOffset BuildDate { get; }

  /// <summary>When the package was installed, or <c>null</c> when it is not installed.</summary>
  public DateTimeOffset? InstallDate { get; }

  public string? Packager { get; }

  public string? Md5Sum { get; }

  public string? Sha256Sum { get; }

  public string? Arch { get; }

  /// <summary>The package size in bytes, as libalpm reports it.</summary>
  public long Size { get; }

  /// <summary>The installed size in bytes, as libalpm reports it.</summary>
  public long InstalledSize { get; }

  public PackageOrigin Origin { get; }

  /// <summary>Why the package is installed; frozen at the moment of the copy.</summary>
  public PackageReason Reason { get; }

  /// <summary>Which validations libalpm had performed; frozen at the moment of the copy.</summary>
  public PackageValidation Validation { get; }

  public IReadOnlyList<string> Licenses { get; }

  public IReadOnlyList<string> Groups { get; }

  public IReadOnlyList<Depend> Depends { get; }

  public IReadOnlyList<Depend> OptionalDepends { get; }

  public IReadOnlyList<Depend> Conflicts { get; }

  public IReadOnlyList<Depend> Provides { get; }

  public IReadOnlyList<Depend> Replaces { get; }

  public IReadOnlyList<Backup> Backup { get; }

  /// <summary>
  /// The copied file list, or <c>null</c> when the snapshot was taken without it.
  /// </summary>
  public IReadOnlyList<PackageFile>? Files { get; }
}

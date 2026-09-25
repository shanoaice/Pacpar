using Pacpar.Alpm;
using AlpmVersion = Pacpar.Alpm.Version;
using AlpmFile = Pacpar.Alpm.File;

namespace Pacpar.Benchmarks;

/// <summary>Eager metadata snapshot: copies every scalar and dependency property at construction.</summary>
public sealed class PackageMetadataSnapshot
{
  public string Name { get; }
  public AlpmVersion Version { get; }
  public string? Filename { get; }
  public string? Base { get; }
  public string? Description { get; }
  public string? Url { get; }
  public DateTimeOffset BuildDate { get; }
  public DateTimeOffset? InstallDate { get; }
  public string? Packager { get; }
  public string? Md5Sum { get; }
  public string? Sha256Sum { get; }
  public string? Arch { get; }
  public long Size { get; }
  public long InstalledSize { get; }
  public PackageOrigin Origin { get; }
  public PackageReason Reason { get; }
  public PackageValidation Validation { get; }
  public string[] Licenses { get; }
  public string[] Groups { get; }
  public Depend[] Depends { get; }
  public Depend[] OptionalDepends { get; }
  public Depend[] Conflicts { get; }
  public Depend[] Provides { get; }
  public Depend[] Replaces { get; }
  public Backup[] Backup { get; }

  public PackageMetadataSnapshot(PackageBase pkg)
  {
    Name = pkg.Name;
    Version = pkg.Version;
    Filename = pkg.Filename;
    Base = pkg.Base;
    Description = pkg.Description;
    Url = pkg.Url;
    BuildDate = pkg.BuildDate;
    InstallDate = pkg.InstallDate;
    Packager = pkg.Packager;
    Md5Sum = pkg.Md5Sum;
    Sha256Sum = pkg.Sha256Sum;
    Arch = pkg.Arch;
    Size = pkg.Size.Value;
    InstalledSize = pkg.InstalledSize.Value;
    Origin = pkg.Origin;
    Reason = pkg.Reason;
    Validation = pkg.Validation;
    Licenses = [.. pkg.Licenses];
    Groups = [.. pkg.Groups];
    Depends = [.. pkg.Depends];
    OptionalDepends = [.. pkg.OptionalDepends];
    Conflicts = [.. pkg.Conflicts];
    Provides = [.. pkg.Provides];
    Replaces = [.. pkg.Replaces];
    Backup = [.. pkg.Backup];
  }
}

/// <summary>Full eager snapshot including the file list.</summary>
public sealed class PackageFullSnapshot
{
  public PackageMetadataSnapshot Metadata { get; }
  public AlpmFile[] Files { get; }

  public PackageFullSnapshot(PackageBase pkg)
  {
    Metadata = new PackageMetadataSnapshot(pkg);
    Files = [.. pkg.Files];
  }
}

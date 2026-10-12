# Getting Started with Pacpar.Alpm

This guide walks through initializing `Pacpar.Alpm`, querying package databases, and understanding lifetime expectations.

## Installation

Add a project reference to `Pacpar.Alpm`:

```xml
<ItemGroup>
  <ProjectReference Include="Pacpar.Alpm" Version="1.0.0" />
</ItemGroup>
```

Make sure you have libalpm installed on you machine. If you are on Arch Linux or any distro that uses pacman as a package manager, you already do.  
If you want to read the MTree info of local packages, you will also need libarchive. libarchive is a dependency of pacman on Arch Linux, so you should also probably have them.

## Initializing an ALPM Session

The primary entry point is the `AlpmSession` class:

```csharp
using Pacpar.Alpm;

// Initialize an ALPM handle with the system root and default database path
using var alpm = new AlpmSession(root: "/", dbpath: "/var/lib/pacman");
```

`AlpmSession` implements `IDisposable`. Always dispose the instance when finished, or wrap it in a `using` declaration.

## Querying the Local Database

The local database contains installed packages on the system:

```csharp
var localDb = alpm.GetLocalDatabase();

// 1. Find a specific package
var glibc = localDb.GetPackage("glibc");
if (glibc != null)
{
    Console.WriteLine($"Installed: {glibc.Name} {glibc.Version} (Size: {glibc.InstalledSize} bytes)");
}

// 2. High-performance iteration over all installed packages
foreach (var pkg in localDb.LoadPackageCache())
{
    Console.WriteLine($"{pkg.Name,-30} {pkg.Version}");
}
```

## Understanding Lifetime and Snapshots

Packages returned by `LoadPackageCache()` or `GetPackage()` are **borrowed views** (`PackageView`). They wrap raw unmanaged pointers and remain valid only while the parent `AlpmSession` handle is active.

If you need to keep package metadata across scopes or pass it to background threads:

```csharp
PackageSnapshot snapshot;

using (var alpm = new AlpmSession("/", "/var/lib/pacman"))
{
    var localDb = alpm.GetLocalDatabase();
    var pkg = localDb.GetPackage("linux")!;
    
    // Detaches unmanaged pointers into pure managed memory
    snapshot = pkg.ToSnapshot(includeFiles: false);
} // alpm is disposed here

// Safe: PackageSnapshot survives handle disposal
Console.WriteLine($"Retained Linux version: {snapshot.Version}");
```

For in-depth details on how borrowed views and lifetime safety are guaranteed, see the [Lifetime and GC Safety Reference](design-docs/lifetime-and-gc-safety.md).

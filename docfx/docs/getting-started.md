# Getting Started with Pacpar.Alpm

This guide walks through initializing `Pacpar.Alpm`, querying package databases, and understanding lifetime expectations.

## Installation

Add a project reference to `Pacpar.Alpm`:

```xml
<ItemGroup>
  <ProjectReference Include="..\Pacpar.Alpm\Pacpar.Alpm.csproj" />
</ItemGroup>
```

Make sure the native `libalpm.so` library (provided by the `pacman` package on Arch Linux and derivatives) is present on your system library path.

## Initializing an ALPM Handle

The primary entry point is the `Alpm` class, which manages an unmanaged `alpm_handle_t`:

```csharp
using Pacpar.Alpm;

// Initialize an ALPM handle with the system root and default database path
using var alpm = new Alpm(root: "/", dbpath: "/var/lib/pacman");
```

`Alpm` implements `IDisposable`. Always dispose the instance when finished, or wrap it in a `using` declaration.

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
foreach (var pkg in localDb.GetPackageCache())
{
    Console.WriteLine($"{pkg.Name,-30} {pkg.Version}");
}
```

## Understanding Lifetime and Snapshots

Packages returned by `GetPackageCache()` or `GetPackage()` are **borrowed views** (`PackageView`). They wrap raw unmanaged pointers and remain valid only while the parent `Alpm` handle is active.

If you need to keep package metadata across scopes or pass it to background threads:

```csharp
PackageSnapshot snapshot;

using (var alpm = new Alpm("/", "/var/lib/pacman"))
{
    var localDb = alpm.GetLocalDatabase();
    var pkg = localDb.GetPackage("linux")!;
    
    // Detaches unmanaged pointers into pure managed memory
    snapshot = pkg.ToSnapshot(includeFiles: false);
} // alpm is disposed here

// Safe: PackageSnapshot survives handle disposal
Console.WriteLine($"Retained Linux version: {snapshot.Version}");
```

For in-depth details on how borrowed views and lifetime safety are guaranteed, see the [Architecture & Lifetime Tokens Reference](lifetime-tokens.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Pacpar.Alpm;
using Spectre.Console;
using AlpmVersion = Pacpar.Alpm.PackageVersion;
using AlpmFile = Pacpar.Alpm.PackageFile;

namespace Pacpar.CLI;

/// <summary>
/// Eager metadata snapshot that copies all scalar and dependency properties at construction time,
/// excluding the potentially huge file list.
/// </summary>
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

/// <summary>
/// Full eager snapshot including the file list.
/// </summary>
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

public struct BenchmarkResult
{
  public string Name { get; set; }
  public double ElapsedMs { get; set; }
  public double AllocatedMB { get; set; }
  public int Gen0 { get; set; }
  public int Gen1 { get; set; }
  public int Gen2 { get; set; }
  public int ItemCount { get; set; }
  public string Notes { get; set; }
}

internal class Program
{
  private const string RootDir = "/";
  private const string DbPath = "/var/lib/pacman";

  static void Main(string[] args)
  {
    AnsiConsole.Write(new Rule("[yellow]Pacpar.Alpm Real-World Benchmark Suite[/]").RuleStyle("grey"));
    AnsiConsole.MarkupLine("[grey]Measuring Lazy View vs Eager Snapshot across OS package management scenarios[/]\n");

    if (!Directory.Exists(DbPath))
    {
      AnsiConsole.MarkupLine($"[red]Error:[/] Pacman database directory '{DbPath}' not found.");
      return;
    }

    using var alpm = new Pacpar.Alpm.Alpm(RootDir, DbPath);
    var localDb = alpm.GetLocalDatabase();
    var localPkgs = localDb.GetPackageCache();
    var localCount = localPkgs.Count();

    // Register sync database if available
    Database? extraDb = null;
    if (System.IO.File.Exists(Path.Combine(DbPath, "sync", "extra.db")))
    {
      extraDb = alpm.RegisterSyncDatabase("extra", SigLevel.ALPM_SIG_PACKAGE_OPTIONAL);
    }
    else if (System.IO.File.Exists(Path.Combine(DbPath, "sync", "core.db")))
    {
      extraDb = alpm.RegisterSyncDatabase("core", SigLevel.ALPM_SIG_PACKAGE_OPTIONAL);
    }

    var syncCount = extraDb != null ? extraDb.GetPackageCache().Count() : 0;

    AnsiConsole.MarkupLine($"[green]Environment:[/] Local DB installed packages: [bold]{localCount:N0}[/], Sync ({extraDb?.Name ?? "none"}) packages: [bold]{syncCount:N0}[/]\n");

    var results = new List<BenchmarkResult>();

    // -------------------------------------------------------------------------
    // Scenario 1: pacman -Q (List all installed package names and versions)
    // -------------------------------------------------------------------------
    AnsiConsole.MarkupLine("[bold cyan]Scenario 1: pacman -Q (Listing all installed packages by name + version)[/]");
    {
      // 1A: Lazy View (only Name and Version read)
      results.Add(RunBenchmark("1A. Lazy View (read Name & Version)", () =>
      {
        int count = 0;
        foreach (var pkg in localDb.GetPackageCache())
        {
          var name = pkg.Name;
          var ver = pkg.Version.ToString();
          if (name.Length > 0 && ver.Length > 0) count++;
        }
        return count;
      }));

      // 1B: Eager Metadata Snapshot at constructor
      results.Add(RunBenchmark("1B. Eager Metadata Snapshot (all metadata)", () =>
      {
        var snapshots = new List<PackageMetadataSnapshot>(localCount);
        foreach (var pkg in localDb.GetPackageCache())
        {
          snapshots.Add(new PackageMetadataSnapshot(pkg));
        }
        return snapshots.Count;
      }, "Forces opening & parsing /desc for all 2.4k packages"));

      // 1C: Full Eager Snapshot including Files
      results.Add(RunBenchmark("1C. Full Eager Snapshot (all metadata + Files)", () =>
      {
        var snapshots = new List<PackageFullSnapshot>(localCount);
        foreach (var pkg in localDb.GetPackageCache())
        {
          snapshots.Add(new PackageFullSnapshot(pkg));
        }
        return snapshots.Count;
      }, "Forces opening & parsing /desc AND /files for all packages"));
    }

    // -------------------------------------------------------------------------
    // Scenario 2: pacman -Qs (Search installed packages by name & description)
    // -------------------------------------------------------------------------
    AnsiConsole.MarkupLine("\n[bold cyan]Scenario 2: pacman -Qs (Search installed packages by name & description)[/]");
    {
      const string query = "system";

      // 2A: Lazy View (only Name and Description read)
      results.Add(RunBenchmark("2A. Lazy View (search Name & Desc)", () =>
      {
        int matches = 0;
        foreach (var pkg in localDb.GetPackageCache())
        {
          if (pkg.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
              (pkg.Description != null && pkg.Description.Contains(query, StringComparison.OrdinalIgnoreCase)))
          {
            matches++;
          }
        }
        return matches;
      }));

      // 2B: Eager Metadata Snapshot
      results.Add(RunBenchmark("2B. Eager Metadata Snapshot (search)", () =>
      {
        var snapshots = new List<PackageMetadataSnapshot>(localCount);
        foreach (var pkg in localDb.GetPackageCache())
        {
          snapshots.Add(new PackageMetadataSnapshot(pkg));
        }
        return snapshots.Count(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                    (p.Description != null && p.Description.Contains(query, StringComparison.OrdinalIgnoreCase)));
      }));
    }

    // -------------------------------------------------------------------------
    // Scenario 3: pacman -Ss (Search sync repository packages)
    // -------------------------------------------------------------------------
    if (extraDb != null)
    {
      AnsiConsole.MarkupLine($"\n[bold cyan]Scenario 3: pacman -Ss (Search {extraDb.Name} sync repository: {syncCount:N0} packages)[/]");
      const string query = "python";

      // 3A: Lazy View
      results.Add(RunBenchmark($"3A. Lazy View (Search in {extraDb.Name})", () =>
      {
        int matches = 0;
        foreach (var pkg in extraDb.GetPackageCache())
        {
          if (pkg.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
              (pkg.Description != null && pkg.Description.Contains(query, StringComparison.OrdinalIgnoreCase)))
          {
            matches++;
          }
        }
        return matches;
      }));

      // 3B: Eager Metadata Snapshot
      results.Add(RunBenchmark($"3B. Eager Metadata Snapshot (all {syncCount:N0} pkgs)", () =>
      {
        var snapshots = new List<PackageMetadataSnapshot>(syncCount);
        foreach (var pkg in extraDb.GetPackageCache())
        {
          snapshots.Add(new PackageMetadataSnapshot(pkg));
        }
        return snapshots.Count(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                    (p.Description != null && p.Description.Contains(query, StringComparison.OrdinalIgnoreCase)));
      }, $"Snapshots all {syncCount:N0} packages into managed heap"));
    }

    // -------------------------------------------------------------------------
    // Scenario 4: pacman -Qi (Single package detailed inspection)
    // -------------------------------------------------------------------------
    AnsiConsole.MarkupLine("\n[bold cyan]Scenario 4: pacman -Qi (Single package detailed inspection)[/]");
    {
      var samplePkgName = localPkgs.FirstOrDefault()?.Name ?? "bash";
      var targetPkg = localDb.GetPackage(samplePkgName);

      if (targetPkg != null)
      {
        results.Add(RunBenchmark($"4A. Lazy View ({samplePkgName} full read)", () =>
        {
          var _ = targetPkg.Name;
          var __ = targetPkg.Version.ToString();
          var ___ = targetPkg.Description;
          var ____ = targetPkg.Depends.Count();
          var _____ = targetPkg.Files.Count;
          return 1;
        }, iterations: 100));

        results.Add(RunBenchmark($"4B. Eager Snapshot ({samplePkgName})", () =>
        {
          var snap = new PackageFullSnapshot(targetPkg);
          return snap.Files.Length;
        }, iterations: 100));
      }
    }

    // -------------------------------------------------------------------------
    // Scenario 5: Repeated property reads & the Null-miss loop
    // -------------------------------------------------------------------------
    AnsiConsole.MarkupLine("\n[bold cyan]Scenario 5: Repeated property access & Null-miss impact[/]");
    {
      // Find a package where Filename and Base64Signature are null (typical for local DB)
      var pkg = localPkgs.FirstOrDefault(p => p.Filename == null);
      if (pkg != null)
      {
        results.Add(RunBenchmark("5A. Read Null property 50,000 times (Lazy field ??=)", () =>
        {
          int nullCount = 0;
          for (int i = 0; i < 50_000; i++)
          {
            if (pkg.Filename == null) nullCount++;
          }
          return nullCount;
        }, iterations: 5, notes: "Shows cost of repeated unmanaged P/Invoke when null"));

        results.Add(RunBenchmark("5B. Read Non-Null property 50,000 times (Cached field)", () =>
        {
          int hitCount = 0;
          for (int i = 0; i < 50_000; i++)
          {
            if (pkg.Name != null) hitCount++;
          }
          return hitCount;
        }, iterations: 5, notes: "Hits pure managed cached string"));
      }
    }

    // -------------------------------------------------------------------------
    // Render Results Table
    // -------------------------------------------------------------------------
    AnsiConsole.WriteLine();
    var table = new Table();
    table.Border(TableBorder.Rounded);
    table.Title("[bold green]Benchmark Summary Results[/]");
    table.AddColumn("Benchmark Scenario");
    table.AddColumn(new TableColumn("Time (ms)").RightAligned());
    table.AddColumn(new TableColumn("Allocated").RightAligned());
    table.AddColumn(new TableColumn("GC 0/1/2").Centered());
    table.AddColumn("Notes / Root Cause");

    foreach (var r in results)
    {
      string timeColor = r.ElapsedMs > 1000 ? "red" : (r.ElapsedMs > 200 ? "yellow" : "green");
      string allocColor = r.AllocatedMB > 50 ? "red" : (r.AllocatedMB > 10 ? "yellow" : "green");

      table.AddRow(
        r.Name,
        $"[{timeColor}]{r.ElapsedMs:F2} ms[/]",
        $"[{allocColor}]{r.AllocatedMB:F2} MB[/]",
        $"{r.Gen0}/{r.Gen1}/{r.Gen2}",
        $"[grey]{r.Notes}[/]"
      );
    }

    AnsiConsole.Write(table);
  }

  private static BenchmarkResult RunBenchmark(
    string name,
    Func<int> action,
    string notes = "",
    int iterations = 1)
  {
    // Warmup
    action();

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    int gen0Before = GC.CollectionCount(0);
    int gen1Before = GC.CollectionCount(1);
    int gen2Before = GC.CollectionCount(2);
    long bytesBefore = GC.GetAllocatedBytesForCurrentThread();

    var sw = Stopwatch.StartNew();
    int count = 0;
    for (int i = 0; i < iterations; i++)
    {
      count = action();
    }
    sw.Stop();

    long bytesAfter = GC.GetAllocatedBytesForCurrentThread();
    int gen0After = GC.CollectionCount(0);
    int gen1After = GC.CollectionCount(1);
    int gen2After = GC.CollectionCount(2);

    return new BenchmarkResult
    {
      Name = name,
      ElapsedMs = sw.Elapsed.TotalMilliseconds / iterations,
      AllocatedMB = (bytesAfter - bytesBefore) / (1024.0 * 1024.0) / iterations,
      Gen0 = gen0After - gen0Before,
      Gen1 = gen1After - gen1Before,
      Gen2 = gen2After - gen2Before,
      ItemCount = count,
      Notes = notes
    };
  }
}

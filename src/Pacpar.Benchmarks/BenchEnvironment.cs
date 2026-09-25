using System;
using System.IO;
using Pacpar.Alpm;
using AlpmHandle = Pacpar.Alpm.Alpm;

namespace Pacpar.Benchmarks;

/// <summary>
/// Where the benchmarks find the pacman database they measure against. The defaults target a
/// normal Arch-based system; other hosts can point the suite at any valid database through the
/// environment variables below (read-only - the benchmarks never modify the database).
/// </summary>
internal static class BenchEnvironment
{
  /// <summary>Root directory handed to libalpm. Override with <c>PACPAR_BENCH_ROOT</c>.</summary>
  internal static string RootDir { get; } =
    Environment.GetEnvironmentVariable("PACPAR_BENCH_ROOT") ?? "/";

  /// <summary>Pacman database path. Override with <c>PACPAR_BENCH_DBPATH</c>.</summary>
  internal static string DbPath { get; } =
    Environment.GetEnvironmentVariable("PACPAR_BENCH_DBPATH") ?? "/var/lib/pacman";

  /// <summary>Opens a fresh libalpm handle, failing with a clear message when no database exists.</summary>
  internal static AlpmHandle Open()
  {
    if (!Directory.Exists(DbPath))
    {
      throw new InvalidOperationException(
        $"These benchmarks need a pacman database at '{DbPath}'. " +
        "Run them on an Arch-based system, or set PACPAR_BENCH_DBPATH (and PACPAR_BENCH_ROOT) " +
        "to a valid database. The benchmarks are read-only.");
    }
    return new AlpmHandle(RootDir, DbPath);
  }

  /// <summary>Registers the first available sync database (<c>extra</c>, then <c>core</c>).</summary>
  internal static Database RegisterSyncDatabase(AlpmHandle alpm)
  {
    var syncDir = Path.Combine(DbPath, "sync");
    foreach (var name in new[] { "extra", "core" })
    {
      if (System.IO.File.Exists(Path.Combine(syncDir, name + ".db")))
      {
        return alpm.RegisterSyncDatabase(name, SigLevel.ALPM_SIG_PACKAGE_OPTIONAL);
      }
    }
    throw new InvalidOperationException(
      $"The sync-database benchmarks need 'extra.db' or 'core.db' under '{syncDir}'.");
  }
}

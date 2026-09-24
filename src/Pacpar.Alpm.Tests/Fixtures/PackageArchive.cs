using System.Formats.Tar;
using System.Text;

namespace Pacpar.Alpm.Tests.Fixtures;

/// <summary>
/// Builds the smallest package <c>alpm_pkg_load</c> accepts, so tests that need a real package file
/// do not need the integration container.
/// </summary>
/// <remarks>
/// A <c>.PKGINFO</c> plus, optionally, one file the package owns is enough. The archive stays
/// uncompressed - libalpm reads a plain tar, so no <c>zstd</c> is involved - and no <c>.MTREE</c> is
/// required, not even for a <c>full</c> load or for reaching the commit-time file-conflict check;
/// <c>.dsh-scratch/audit-probe/pkgs/pkg-audit-f-*.pkg.tar.zst</c> is the measured example of that.
/// </remarks>
internal static class PackageArchive
{
  /// <summary>The version every generated package uses, so assertions can spell the names out.</summary>
  internal const string Version = "1.0-1";

  /// <summary>Writes a package and returns the path it was written to.</summary>
  /// <param name="directory">Where to write; it must already exist.</param>
  /// <param name="name">The package name (<c>pkgname</c>).</param>
  /// <param name="arch">The architecture recorded in <c>.PKGINFO</c>.</param>
  /// <param name="depend">Optional <c>depend</c> entry.</param>
  /// <param name="conflict">Optional <c>conflict</c> entry.</param>
  /// <param name="file">Optional file the package owns, for example <c>usr/bin/probe-file</c>.</param>
  internal static string Create(string directory, string name, string arch = "x86_64", string? depend = null,
    string? conflict = null, string? file = null)
  {
    var info = new StringBuilder()
      .AppendLine($"pkgname = {name}")
      .AppendLine($"pkgbase = {name}")
      .AppendLine($"pkgver = {Version}")
      .AppendLine("pkgdesc = test package")
      .AppendLine("url = https://example.invalid")
      .AppendLine("builddate = 0")
      .AppendLine("packager = test")
      .AppendLine("size = 1")
      .AppendLine($"arch = {arch}");

    if (depend != null) info.AppendLine($"depend = {depend}");
    if (conflict != null) info.AppendLine($"conflict = {conflict}");

    var path = Path.Combine(directory, $"{name}-{Version}-{arch}.pkg.tar");

    using (var stream = System.IO.File.Create(path))
    using (var writer = new TarWriter(stream, TarEntryFormat.Pax))
    {
      writer.WriteEntry(TextEntry(".PKGINFO", info.ToString()));

      if (file != null)
      {
        writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "usr/"));
        writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "usr/bin/"));
        writer.WriteEntry(TextEntry(file, "packaged contents"));
      }
    }

    return path;
  }

  private static PaxTarEntry TextEntry(string name, string contents)
    => new(TarEntryType.RegularFile, name)
    {
      DataStream = new MemoryStream(Encoding.UTF8.GetBytes(contents)),
    };
}

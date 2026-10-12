using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>
/// A file entry from a package's installed mtree manifest (<c>.MTREE</c>).
/// </summary>
public sealed record MtreeEntry
{
  /// <summary>
  /// The relative path of the file within the package (with any leading <c>./</c> stripped).
  /// </summary>
  public required string Path { get; init; }

  /// <summary>
  /// The POSIX file mode and permissions. It includes the file type bits: <c>0o100644</c> marks
  /// a regular file, <c>0o120644</c> a symlink. Mask with <c>0o7777</c> to get the permissions only.
  /// </summary>
  public uint Mode { get; init; }

  /// <summary>
  /// The size of the file in bytes. The value is <c>0</c> when the mtree records no size.
  /// </summary>
  public long Size { get; init; }

  /// <summary>
  /// The last modification time in seconds since Unix epoch.
  /// </summary>
  public long ModifiedTime { get; init; }

  /// <summary>
  /// The user ID of the file owner.
  /// </summary>
  public long Uid { get; init; }

  /// <summary>
  /// The group ID of the file owner.
  /// </summary>
  public long Gid { get; init; }

  /// <summary>
  /// The symlink target path, or <c>null</c> if the entry is not a symlink.
  /// </summary>
  public string? SymlinkTarget { get; init; }

  /// <summary>
  /// The SHA-256 digest of the file contents as a lowercase hexadecimal string, or <c>null</c> if not present.
  /// </summary>
  public string? Sha256 { get; init; }

  /// <summary>
  /// The MD5 digest of the file contents as a lowercase hexadecimal string, or <c>null</c> if not present.
  /// </summary>
  public string? Md5 { get; init; }

  internal static unsafe MtreeEntry FromNative(archive_entry* entry)
  {
    var rawPath = NativeString.FromNative((nint)NativeMethods.archive_entry_pathname(entry)) ?? "";
    var path = rawPath.StartsWith("./", StringComparison.Ordinal) ? rawPath[2..] : rawPath;

    var sha256Ptr = NativeMethods.archive_entry_digest(entry, NativeMethods.ARCHIVE_ENTRY_DIGEST_SHA256);
    var sha256 = ToDigestHex(sha256Ptr, 32);

    var md5Ptr = NativeMethods.archive_entry_digest(entry, NativeMethods.ARCHIVE_ENTRY_DIGEST_MD5);
    var md5 = ToDigestHex(md5Ptr, 16);

    var symlink = NativeString.FromNative((nint)NativeMethods.archive_entry_symlink(entry));

    return new MtreeEntry
    {
      Path = path,
      Mode = NativeMethods.archive_entry_mode(entry),
      Size = NativeMethods.archive_entry_size(entry),
      ModifiedTime = NativeMethods.archive_entry_mtime(entry),
      Uid = NativeMethods.archive_entry_uid(entry),
      Gid = NativeMethods.archive_entry_gid(entry),
      SymlinkTarget = symlink,
      Sha256 = sha256,
      Md5 = md5
    };
  }

  private static unsafe string? ToDigestHex(byte* digest, int length)
  {
    if (digest == null) return null;
    var bytes = new ReadOnlySpan<byte>(digest, length);
    // libarchive reports a digest the mtree never set as an all-zero buffer and offers no
    // "is set" query, so an all-zero buffer means absent. A real digest of all zero bits is
    // not a usable hash output, so the ambiguity is only theoretical.
    return bytes.ContainsAnyExcept((byte)0) ? Convert.ToHexStringLower(bytes) : null;
  }
}

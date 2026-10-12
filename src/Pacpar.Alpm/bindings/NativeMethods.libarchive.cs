using System.Runtime.InteropServices;

namespace Pacpar.Alpm.Bindings;

internal static unsafe partial class NativeMethods
{
  private const string __LibArchiveDllName = "libarchive";

  internal const int ARCHIVE_ENTRY_DIGEST_MD5 = 1;
  internal const int ARCHIVE_ENTRY_DIGEST_SHA256 = 4;

  [DllImport(__LibArchiveDllName, EntryPoint = "archive_entry_pathname", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
  internal static extern byte* archive_entry_pathname(archive_entry* entry);

  [DllImport(__LibArchiveDllName, EntryPoint = "archive_entry_mode", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
  internal static extern uint archive_entry_mode(archive_entry* entry);

  [DllImport(__LibArchiveDllName, EntryPoint = "archive_entry_size", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
  internal static extern long archive_entry_size(archive_entry* entry);

  [DllImport(__LibArchiveDllName, EntryPoint = "archive_entry_mtime", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
  internal static extern long archive_entry_mtime(archive_entry* entry);

  [DllImport(__LibArchiveDllName, EntryPoint = "archive_entry_uid", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
  internal static extern long archive_entry_uid(archive_entry* entry);

  [DllImport(__LibArchiveDllName, EntryPoint = "archive_entry_gid", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
  internal static extern long archive_entry_gid(archive_entry* entry);

  [DllImport(__LibArchiveDllName, EntryPoint = "archive_entry_symlink", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
  internal static extern byte* archive_entry_symlink(archive_entry* entry);

  [DllImport(__LibArchiveDllName, EntryPoint = "archive_entry_digest", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
  internal static extern byte* archive_entry_digest(archive_entry* entry, int type);
}

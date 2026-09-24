using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.Options;

namespace Pacpar.Alpm;

/// <summary>
/// Options for the ALPM library.
/// Options of array type are queried and modified through properties exposed as <see cref="ICollection{T}"/>s.
/// Options of scalar type are queried and modified through properties with overridden accessors that calls underlying ALPM functions.
/// </summary>
public class AlpmOptions
{
  private readonly unsafe _alpm_handle_t* _handle;

  internal unsafe AlpmOptions(_alpm_handle_t* handle)
  {
    _handle = handle;
  }

  /// <summary>
  /// Throws for a non-zero libalpm return value, using the handle's current <c>errno</c>.
  /// </summary>
  private unsafe void ThrowIfError(int err)
  {
    if (err != 0) throw ErrorHandler.ToException(NativeMethods.alpm_errno(_handle));
  }

  /// <summary>
  /// Marshals <paramref name="value"/> for a string option setter, releases the buffer afterwards
  /// and throws on failure.
  /// </summary>
  /// <remarks>
  /// The setter arrives as a <c>delegate* managed</c> because <c>delegate* unmanaged[Cdecl]</c>
  /// cannot point at a <c>[DllImport]</c> method (CS8786).
  /// </remarks>
  private unsafe void SetStringOption(string? value, delegate* managed<_alpm_handle_t*, byte*, int> setter)
  {
    var ptr = NativeString.ToNative(value);
    try
    {
      ThrowIfError(setter(_handle, ptr));
    }
    finally
    {
      Marshal.FreeHGlobal((nint)ptr);
    }
  }

  public unsafe ICollection<string> Architectures => new Options.Architecture(_handle);

  public unsafe ICollection<Depend> AssumeInstalled => new AssumeInstalled(_handle);

  public unsafe ICollection<string> CacheDirectories => new CacheDirectories(_handle);

  public unsafe ICollection<string> OverwritableFiles => new OverwritableFiles(_handle);

  public unsafe ICollection<string> HookDirectories => new HookDirectories(_handle);

  public unsafe ICollection<string> IgnoreGroups => new IgnoreGroups(_handle);

  public unsafe ICollection<string> IgnorePackages => new IgnorePackages(_handle);

  public unsafe ICollection<string> NoExtract => new NoExtractOptionCollection(_handle);

  public unsafe ICollection<string> NoUpgrade => new NoUpgrade(_handle);

  public unsafe bool CheckSpace
  {
    get => NativeMethods.alpm_option_get_checkspace(_handle) != 0;
    set => ThrowIfError(NativeMethods.alpm_option_set_checkspace(_handle, value ? 1 : 0));
  }

  public unsafe string DatabaseExtension
  {
    get => NativeString.FromNative((nint)NativeMethods.alpm_option_get_dbext(_handle))!;
    set => SetStringOption(value, &NativeMethods.alpm_option_set_dbext);
  }

  public unsafe string DatabasePath => NativeString.FromNative((nint)NativeMethods.alpm_option_get_dbpath(_handle))!;

  public unsafe string Root => NativeString.FromNative((nint)NativeMethods.alpm_option_get_root(_handle))!;

  public unsafe SigLevel DefaultSigLevel
  {
    get => (SigLevel)NativeMethods.alpm_option_get_default_siglevel(_handle);
    set => ThrowIfError(NativeMethods.alpm_option_set_default_siglevel(_handle, (int)value));
  }

  public unsafe SigLevel LocalFileSigLevel
  {
    get => (SigLevel)NativeMethods.alpm_option_get_local_file_siglevel(_handle);
    set => ThrowIfError(NativeMethods.alpm_option_set_local_file_siglevel(_handle, (int)value));
  }

  public unsafe SigLevel RemoteFileSigLevel
  {
    get => (SigLevel)NativeMethods.alpm_option_get_remote_file_siglevel(_handle);
    set => ThrowIfError(NativeMethods.alpm_option_set_remote_file_siglevel(_handle, (int)value));
  }

  public unsafe int ParallelDownloads
  {
    get => NativeMethods.alpm_option_get_parallel_downloads(_handle);
    set => ThrowIfError(NativeMethods.alpm_option_set_parallel_downloads(_handle, (uint)value));
  }

  /// <summary>
  /// The logfile path, or <c>null</c> when libalpm has none configured (its default).
  /// </summary>
  public unsafe string? LogFile
  {
    get => NativeString.FromNative((nint)NativeMethods.alpm_option_get_logfile(_handle));
    set => SetStringOption(value, &NativeMethods.alpm_option_set_logfile);
  }

  public unsafe bool UseSyslog
  {
    get => NativeMethods.alpm_option_get_usesyslog(_handle) != 0;
    set => ThrowIfError(NativeMethods.alpm_option_set_usesyslog(_handle, value ? 1 : 0));
  }

  public unsafe string Lockfile => NativeString.FromNative((nint)NativeMethods.alpm_option_get_lockfile(_handle))!;

  /// <summary>
  /// libalpm's GnuPG home directory, or <c>null</c> when it has none configured (its default).
  /// </summary>
  public unsafe string? GpgDirectory
  {
    get => NativeString.FromNative((nint)NativeMethods.alpm_option_get_gpgdir(_handle));
    set => SetStringOption(value, &NativeMethods.alpm_option_set_gpgdir);
  }

  // alpm_option_get_disable_dl_timeout and alpm_option_set_disable_dl_timeout are bound in
  // NativeMethods.libalpm.g.cs, but AlpmOptions exposes no property for them.

  // The sandbox options (alpm_option_get/set_disable_sandbox, _disable_sandbox_filesystem,
  // _disable_sandbox_network, _disable_sandbox_syscalls and _sandboxuser) are bound in
  // NativeMethods.libalpm.g.cs, but AlpmOptions exposes no properties for them.
}

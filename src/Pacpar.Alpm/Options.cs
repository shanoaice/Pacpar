using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.options;

namespace Pacpar.Alpm;

/// <summary>
/// Options for the ALPM library.
/// Options of array type are queried and modified through properties exposed as <see cref="ICollection{T}"/>s.
/// Options of scalar type are queried and modified through properties with overridden accessors that calls underlying ALPM functions.
/// </summary>
public class AlpmOptions
{
  private readonly unsafe byte* _handle;

  internal unsafe AlpmOptions(byte* handle)
  {
    _handle = handle;
  }

  public unsafe ICollection<string> Architectures => new ArchitectureOptionCollection(_handle);

  public unsafe ICollection<Depend> AssumeInstalled => new AssumeInstalledOptionCollection(_handle);

  public unsafe ICollection<string> CacheDirectories => new CacheDirectoriesOptionCollection(_handle);

  public unsafe ICollection<string> OverwritableFiles => new OverwritableFilesOptionCollection(_handle);

  public unsafe ICollection<string> HookDirectories => new HookDirectoriesOptionCollection(_handle);

  public unsafe ICollection<string> IgnoreGroups => new IgnoreGroupsOptionCollection(_handle);

  public unsafe ICollection<string> IgnorePackages => new IgnorePackagesOptionCollection(_handle);

  public unsafe ICollection<string> NoExtract => new NoExtractOptionCollection(_handle);

  public unsafe ICollection<string> NoUpgrade => new NoUpgradeOptionCollection(_handle);

  public unsafe bool CheckSpace
  {
    get => NativeMethods.alpm_option_get_checkspace(_handle) != 0;
    set
    {
      var err = NativeMethods.alpm_option_set_checkspace(_handle, value ? 1 : 0);
      if (err != 0) throw ErrorHandler.GetException(NativeMethods.alpm_errno(_handle))!;
    }
  }

  public unsafe string DatabaseExtension
  {
    get => Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_option_get_dbext(_handle))!;
    set
    {
      var ptr = Marshal.StringToHGlobalAnsi(value);
      try
      {
        var err = NativeMethods.alpm_option_set_dbext(_handle, (byte*)ptr);
        if (err != 0) throw ErrorHandler.GetException(NativeMethods.alpm_errno(_handle))!;
      }
      finally
      {
        Marshal.FreeHGlobal(ptr);
      }
    }
  }

  public unsafe string DatabasePath => Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_option_get_dbpath(_handle))!;

  public unsafe string Root => Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_option_get_root(_handle))!;

  public unsafe SigLevel DefaultSigLevel
  {
    get => (SigLevel)NativeMethods.alpm_option_get_default_siglevel(_handle);
    set
    {
      var err = NativeMethods.alpm_option_set_default_siglevel(_handle, (int)value);
      if (err != 0) throw ErrorHandler.GetException(NativeMethods.alpm_errno(_handle))!;
    }
  }

  public unsafe SigLevel LocalFileSigLevel
  {
    get => (SigLevel)NativeMethods.alpm_option_get_local_file_siglevel(_handle);
    set
    {
      var err = NativeMethods.alpm_option_set_local_file_siglevel(_handle, (int)value);
      if (err != 0) throw ErrorHandler.GetException(NativeMethods.alpm_errno(_handle))!;
    }
  }

  public unsafe SigLevel RemoteFileSigLevel
  {
    get => (SigLevel)NativeMethods.alpm_option_get_remote_file_siglevel(_handle);
    set
    {
      var err = NativeMethods.alpm_option_set_remote_file_siglevel(_handle, (int)value);
      if (err != 0) throw ErrorHandler.GetException(NativeMethods.alpm_errno(_handle))!;
    }
  }

  public unsafe int ParallelDownloads
  {
    get => NativeMethods.alpm_option_get_parallel_downloads(_handle);
    set
    {
      var err = NativeMethods.alpm_option_set_parallel_downloads(_handle, (uint)value);
      if (err != 0) throw ErrorHandler.GetException(NativeMethods.alpm_errno(_handle))!;
    }
  }

  public unsafe string LogFile
  {
    get => Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_option_get_logfile(_handle))!;
    set
    {
      var ptr = Marshal.StringToHGlobalAnsi(value);
      try
      {
        var err = NativeMethods.alpm_option_set_logfile(_handle, (byte*)ptr);
        if (err != 0) throw ErrorHandler.GetException(NativeMethods.alpm_errno(_handle))!;
      }
      finally
      {
        Marshal.FreeHGlobal(ptr);
      }
    }
  }

  public unsafe bool UseSyslog
  {
    get => NativeMethods.alpm_option_get_usesyslog(_handle) != 0;
    set
    {
      var err = NativeMethods.alpm_option_set_usesyslog(_handle, value ? 1 : 0);
      if (err != 0) throw ErrorHandler.GetException(NativeMethods.alpm_errno(_handle))!;
    }
  }

  public unsafe string Lockfile => Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_option_get_lockfile(_handle))!;

  public unsafe string GpgDirectory
  {
    get => Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_option_get_gpgdir(_handle))!;
    set
    {
      var ptr = Marshal.StringToHGlobalAnsi(value);
      try
      {
        var err = NativeMethods.alpm_option_set_gpgdir(_handle, (byte*)ptr);
        if (err != 0) throw ErrorHandler.GetException(NativeMethods.alpm_errno(_handle))!;
      }
      finally
      {
        Marshal.FreeHGlobal(ptr);
      }
    }
  }

  // alpm_option_get_disable_dl_timeout is not included in g.cs

  // Sandbox related parts are also incomplete.
  // We will add them when we need them, or later when I had time to diagnose bindgen issues.
}

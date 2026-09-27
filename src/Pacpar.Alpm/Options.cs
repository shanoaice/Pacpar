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
  private readonly SafeAlpmHandle _handle;

  // The ALPM handle's root lifetime token. The option collections are thin, per-access views over
  // libalpm's handle state: they carry the root token so every native call they make is guarded,
  // and the lists they hand out retire together with the handle.
  private readonly Lifetime _lifetime;

  internal AlpmOptions(SafeAlpmHandle handle, Lifetime lifetime)
  {
    _handle = handle;
    _lifetime = lifetime;
  }

  /// <summary>
  /// Throws for a non-zero libalpm return value, using the handle's current <c>errno</c>.
  /// </summary>
  private void ThrowIfError(int err)
  {
    if (err != 0)
    {
      var ex = ErrorHandler.ToException(NativeMethods.alpm_errno(_handle));
      GC.KeepAlive(this);
      throw ex;
    }
  }
  /// <summary>
  /// Marshals <paramref name="value"/> for a string option setter and throws on failure.
  /// </summary>
  /// <remarks>
  /// Option values can be paths or URLs (logfile, gpgdir, dbext), hence the 256-byte scratch; the
  /// buffer is released by the same frame that allocated it.
  /// <para>
  /// The setter arrives as a <c>delegate* managed</c> because <c>delegate* unmanaged[Cdecl]</c>
  /// cannot point at a <c>[DllImport]</c> method (CS8786).
  /// </para>
  /// </remarks>
  private unsafe void SetStringOption(string? value, delegate* managed<SafeAlpmHandle, byte*, int> setter)
  {
    Span<byte> scratch = stackalloc byte[256];
    using var buffer = new Utf8Buffer(value, scratch);
    ThrowIfError(setter(_handle, buffer.Ptr));
  }

  public ICollection<string> Architectures => new Options.Architecture(_handle, _lifetime);

  public ICollection<Depend> AssumeInstalled => new AssumeInstalled(_handle, _lifetime);

  public ICollection<string> CacheDirectories => new CacheDirectories(_handle, _lifetime);

  public ICollection<string> OverwritableFiles => new OverwritableFiles(_handle, _lifetime);

  public ICollection<string> HookDirectories => new HookDirectories(_handle, _lifetime);

  public ICollection<string> IgnoreGroups => new IgnoreGroups(_handle, _lifetime);

  public ICollection<string> IgnorePackages => new IgnorePackages(_handle, _lifetime);

  public ICollection<string> NoExtract => new NoExtractOptionCollection(_handle, _lifetime);

  public ICollection<string> NoUpgrade => new NoUpgrade(_handle, _lifetime);

  public bool CheckSpace
  {
    get
    {
      var val = NativeMethods.alpm_option_get_checkspace(_handle) != 0;
      GC.KeepAlive(this);
      return val;
    }
    set
    {
      ThrowIfError(NativeMethods.alpm_option_set_checkspace(_handle, value ? 1 : 0));
      GC.KeepAlive(this);
    }
  }

  public unsafe string DatabaseExtension
  {
    get
    {
      var val = NativeString.FromNative((nint)NativeMethods.alpm_option_get_dbext(_handle))!;
      GC.KeepAlive(this);
      return val;
    }
    set => SetStringOption(value, &NativeMethods.alpm_option_set_dbext);
  }

  public unsafe string DatabasePath
  {
    get
    {
      var val = NativeString.FromNative((nint)NativeMethods.alpm_option_get_dbpath(_handle))!;
      GC.KeepAlive(this);
      return val;
    }
  }

  public unsafe string Root
  {
    get
    {
      var val = NativeString.FromNative((nint)NativeMethods.alpm_option_get_root(_handle))!;
      GC.KeepAlive(this);
      return val;
    }
  }

  public SigLevel DefaultSigLevel
  {
    get
    {
      var val = (SigLevel)NativeMethods.alpm_option_get_default_siglevel(_handle);
      GC.KeepAlive(this);
      return val;
    }
    set
    {
      ThrowIfError(NativeMethods.alpm_option_set_default_siglevel(_handle, (int)value));
      GC.KeepAlive(this);
    }
  }

  public SigLevel LocalFileSigLevel
  {
    get
    {
      var val = (SigLevel)NativeMethods.alpm_option_get_local_file_siglevel(_handle);
      GC.KeepAlive(this);
      return val;
    }
    set
    {
      ThrowIfError(NativeMethods.alpm_option_set_local_file_siglevel(_handle, (int)value));
      GC.KeepAlive(this);
    }
  }

  public SigLevel RemoteFileSigLevel
  {
    get
    {
      var val = (SigLevel)NativeMethods.alpm_option_get_remote_file_siglevel(_handle);
      GC.KeepAlive(this);
      return val;
    }
    set
    {
      ThrowIfError(NativeMethods.alpm_option_set_remote_file_siglevel(_handle, (int)value));
      GC.KeepAlive(this);
    }
  }

  public int ParallelDownloads
  {
    get
    {
      var val = NativeMethods.alpm_option_get_parallel_downloads(_handle);
      GC.KeepAlive(this);
      return val;
    }
    set
    {
      ThrowIfError(NativeMethods.alpm_option_set_parallel_downloads(_handle, (uint)value));
      GC.KeepAlive(this);
    }
  }

  /// <summary>
  /// The logfile path, or <c>null</c> when libalpm has none configured (its default).
  /// </summary>
  public unsafe string? LogFile
  {
    get
    {
      var val = NativeString.FromNative((nint)NativeMethods.alpm_option_get_logfile(_handle));
      GC.KeepAlive(this);
      return val;
    }
    set => SetStringOption(value, &NativeMethods.alpm_option_set_logfile);
  }

  public bool UseSyslog
  {
    get
    {
      var val = NativeMethods.alpm_option_get_usesyslog(_handle) != 0;
      GC.KeepAlive(this);
      return val;
    }
    set
    {
      ThrowIfError(NativeMethods.alpm_option_set_usesyslog(_handle, value ? 1 : 0));
      GC.KeepAlive(this);
    }
  }

  public unsafe string Lockfile
  {
    get
    {
      var val = NativeString.FromNative((nint)NativeMethods.alpm_option_get_lockfile(_handle))!;
      GC.KeepAlive(this);
      return val;
    }
  }

  /// <summary>
  /// libalpm's GnuPG home directory, or <c>null</c> when it has none configured (its default).
  /// </summary>
  public unsafe string? GpgDirectory
  {
    get
    {
      var val = NativeString.FromNative((nint)NativeMethods.alpm_option_get_gpgdir(_handle));
      GC.KeepAlive(this);
      return val;
    }
    set => SetStringOption(value, &NativeMethods.alpm_option_set_gpgdir);
  }
  // alpm_option_get_disable_dl_timeout and alpm_option_set_disable_dl_timeout are bound in
  // NativeMethods.libalpm.g.cs, but AlpmOptions exposes no property for them.

  // The sandbox options (alpm_option_get/set_disable_sandbox, _disable_sandbox_filesystem,
  // _disable_sandbox_network, _disable_sandbox_syscalls and _sandboxuser) are bound in
  // NativeMethods.libalpm.g.cs, but AlpmOptions exposes no properties for them.
}

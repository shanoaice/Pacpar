using System.Diagnostics.CodeAnalysis;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.Options;

namespace Pacpar.Alpm;

/// <summary>
/// Options for the ALPM library.
/// Options of array type are queried and modified through properties exposed as <see cref="ICollection{T}"/>s.
/// Options of scalar type are queried and modified through properties with overridden accessors that calls underlying ALPM functions.
/// </summary>
/// <summary>
/// Represents the aggregate state of the libalpm sandbox.
/// </summary>
public enum SandboxState
{
  /// <summary>All sandbox components are enabled.</summary>
  Enabled = 0,

  /// <summary>Some sandbox components are disabled.</summary>
  PartiallyDisabled = 1,

  /// <summary>All sandbox components are disabled.</summary>
  Disabled = 2,
}

public unsafe class AlpmOptions
{
  private readonly SafeAlpmHandle _handle;

  // The ALPM handle's root lifetime token. The option collections are thin, per-access views over
  // libalpm's handle state: they carry the root token so every native call they make is guarded,
  // and the lists they hand out retire together with the handle.
  private readonly RootLifetime _lifetime;
  private readonly LifetimeStamp _stamp;

  internal AlpmOptions(SafeAlpmHandle handle, RootLifetime lifetime)
  {
    _handle = handle;
    _lifetime = lifetime;
    _stamp = lifetime.Capture();
  }

  /// <summary>
  /// Throws for a non-zero libalpm return value, reading the handle's errno right here.
  /// </summary>
  /// <param name="err">The value the option setter returned; zero means success.</param>
  /// <param name="operation">
  /// The member that issued the call, so the failure names the option it was setting. Callers pass
  /// nothing; the compiler fills it in.
  /// </param>
  private void ThrowIfError(int err, [System.Runtime.CompilerServices.CallerMemberName] string? operation = null)
  {
    if (err == 0) return;
    GC.KeepAlive(this);
    throw NativeCall.Failure(_handle, $"set {operation ?? "option"}").ToException();
  }

  private T Read<T>(delegate* managed<SafeAlpmHandle, T> reader)
  {
    _stamp.ThrowIfStale();
    var val = reader(_handle);
    GC.KeepAlive(this);
    return val;
  }

  private string? ReadString(delegate* managed<SafeAlpmHandle, byte*> reader)
  {
    _stamp.ThrowIfStale();
    var val = NativeString.FromNative((nint)reader(_handle));
    GC.KeepAlive(this);
    return val;
  }

  private void Write(delegate* managed<SafeAlpmHandle, int, int> writer, int value, [System.Runtime.CompilerServices.CallerMemberName] string? operation = null)
  {
    _stamp.ThrowIfStale();
    ThrowIfError(writer(_handle, value), operation);
    GC.KeepAlive(this);
  }

  private void WriteUInt(delegate* managed<SafeAlpmHandle, uint, int> writer, uint value, [System.Runtime.CompilerServices.CallerMemberName] string? operation = null)
  {
    _stamp.ThrowIfStale();
    ThrowIfError(writer(_handle, value), operation);
    GC.KeepAlive(this);
  }

  private void WriteUShort(delegate* managed<SafeAlpmHandle, ushort, int> writer, ushort value, [System.Runtime.CompilerServices.CallerMemberName] string? operation = null)
  {
    _stamp.ThrowIfStale();
    ThrowIfError(writer(_handle, value), operation);
    GC.KeepAlive(this);
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
  private void SetStringOption(string value, delegate* managed<SafeAlpmHandle, byte*, int> setter,
    [System.Runtime.CompilerServices.CallerMemberName] string? operation = null)
  {
    ArgumentNullException.ThrowIfNull(value);
    _stamp.ThrowIfStale();
    Span<byte> scratch = stackalloc byte[256];
    using var buffer = new Utf8Buffer(value, scratch);
    ThrowIfError(setter(_handle, buffer.Ptr), operation);
  }

  public ICollection<string> Architectures => new Architecture(_handle, _lifetime);

  public ICollection<Depend> AssumeInstalled => new AssumeInstalled(_handle, _lifetime);

  public ICollection<string> CacheDirectories => new CacheDirectories(_handle, _lifetime);

  public ICollection<string> OverwritableFiles => new OverwritableFiles(_handle, _lifetime);

  public ICollection<string> HookDirectories => new HookDirectories(_handle, _lifetime);

  public ICollection<string> IgnoreGroups => new IgnoreGroups(_handle, _lifetime);

  public ICollection<string> IgnorePackages => new IgnorePackages(_handle, _lifetime);

  public ICollection<string> NoExtract => new NoExtract(_handle, _lifetime);

  public ICollection<string> NoUpgrade => new NoUpgrade(_handle, _lifetime);

  public bool CheckSpace
  {
    get => Read(&NativeMethods.alpm_option_get_checkspace) != 0;
    set => Write(&NativeMethods.alpm_option_set_checkspace, value ? 1 : 0);
  }

  public string DatabaseExtension
  {
    get => ReadString(&NativeMethods.alpm_option_get_dbext)!;
    set => SetStringOption(value, &NativeMethods.alpm_option_set_dbext);
  }

  public string DatabasePath
  {
    get => ReadString(&NativeMethods.alpm_option_get_dbpath)!;
  }

  public string Root
  {
    get => ReadString(&NativeMethods.alpm_option_get_root)!;
  }

  public SigLevel DefaultSigLevel
  {
    get => (SigLevel)Read(&NativeMethods.alpm_option_get_default_siglevel);
    set => Write(&NativeMethods.alpm_option_set_default_siglevel, (int)value);
  }

  public SigLevel LocalFileSigLevel
  {
    get => (SigLevel)Read(&NativeMethods.alpm_option_get_local_file_siglevel);
    set => Write(&NativeMethods.alpm_option_set_local_file_siglevel, (int)value);
  }

  public SigLevel RemoteFileSigLevel
  {
    get => (SigLevel)Read(&NativeMethods.alpm_option_get_remote_file_siglevel);
    set => Write(&NativeMethods.alpm_option_set_remote_file_siglevel, (int)value);
  }

  public int ParallelDownloads
  {
    get => Read(&NativeMethods.alpm_option_get_parallel_downloads);
    set => WriteUInt(&NativeMethods.alpm_option_set_parallel_downloads, (uint)value);
  }

  /// <summary>
  /// The logfile path, or <c>null</c> when libalpm has none configured (its default).
  /// </summary>
  /// <remarks>
  /// Once configured, libalpm does not support unsetting the logfile back to <c>null</c>.
  /// Attempting to assign <c>null</c> throws an <see cref="ArgumentNullException"/>.
  /// </remarks>
  [DisallowNull]
  public string? LogFile
  {
    get => ReadString(&NativeMethods.alpm_option_get_logfile);
    set => SetStringOption(value, &NativeMethods.alpm_option_set_logfile);
  }

  public bool UseSyslog
  {
    get => Read(&NativeMethods.alpm_option_get_usesyslog) != 0;
    set => Write(&NativeMethods.alpm_option_set_usesyslog, value ? 1 : 0);
  }

  public string Lockfile
  {
    get => ReadString(&NativeMethods.alpm_option_get_lockfile)!;
  }

  /// <summary>
  /// libalpm's GnuPG home directory, or <c>null</c> when it has none configured (its default).
  /// </summary>
  /// <remarks>
  /// Once configured, libalpm does not support unsetting the GnuPG home directory back to <c>null</c>.
  /// Attempting to assign <c>null</c> throws an <see cref="ArgumentNullException"/>.
  /// </remarks>
  [DisallowNull]
  public string? GpgDirectory
  {
    get => ReadString(&NativeMethods.alpm_option_get_gpgdir);
    set => SetStringOption(value, &NativeMethods.alpm_option_set_gpgdir);
  }

  /// <summary>
  /// Enables or disables the download timeout.
  /// </summary>
  public bool DisableDownloadTimeout
  {
    get => Read(&NativeMethods.alpm_option_get_disable_dl_timeout) != 0;
    set => WriteUShort(&NativeMethods.alpm_option_set_disable_dl_timeout, (ushort)(value ? 1 : 0));
  }

  /// <summary>
  /// Enables or disables the filesystem portion of the sandbox.
  /// </summary>
  public bool DisableSandboxFilesystem
  {
    get => Read(&NativeMethods.alpm_option_get_disable_sandbox_filesystem) != 0;
    set => WriteUShort(&NativeMethods.alpm_option_set_disable_sandbox_filesystem, (ushort)(value ? 1 : 0));
  }

  /// <summary>
  /// Enables or disables the network portion of the sandbox.
  /// </summary>
  public bool DisableSandboxNetwork
  {
    get => Read(&NativeMethods.alpm_option_get_disable_sandbox_network) != 0;
    set => WriteUShort(&NativeMethods.alpm_option_set_disable_sandbox_network, (ushort)(value ? 1 : 0));
  }

  /// <summary>
  /// Enables or disables the syscalls portion of the sandbox.
  /// </summary>
  public bool DisableSandboxSyscalls
  {
    get => Read(&NativeMethods.alpm_option_get_disable_sandbox_syscalls) != 0;
    set => WriteUShort(&NativeMethods.alpm_option_set_disable_sandbox_syscalls, (ushort)(value ? 1 : 0));
  }

  /// <summary>
  /// Gets the aggregate state of the sandbox.
  /// </summary>
  public SandboxState Sandbox
  {
    get
    {
      var fs = DisableSandboxFilesystem;
      var net = DisableSandboxNetwork;
      var sys = DisableSandboxSyscalls;

      if (!fs && !net && !sys)
      {
        return SandboxState.Enabled;
      }

      if (fs && net && sys)
      {
        return SandboxState.Disabled;
      }

      return SandboxState.PartiallyDisabled;
    }
  }

  /// <summary>
  /// Gets or sets the user to switch to for sandboxed operations, or <c>null</c> when none is set.
  /// </summary>
  public string? SandboxUser
  {
    get => ReadString(&NativeMethods.alpm_option_get_sandboxuser);
    set
    {
      _stamp.ThrowIfStale();
      if (value is null)
      {
        ThrowIfError(NativeMethods.alpm_option_set_sandboxuser(_handle, null));
        GC.KeepAlive(this);
        return;
      }

      Span<byte> scratch = stackalloc byte[64];
      using var buffer = new Utf8Buffer(value, scratch);
      var err = NativeMethods.alpm_option_set_sandboxuser(_handle, buffer.Ptr);
      if (err == 1)
      {
        throw new ArgumentException($"The user '{value}' is not known on this system.", nameof(value));
      }
      ThrowIfError(err);
      GC.KeepAlive(this);
    }
  }
}

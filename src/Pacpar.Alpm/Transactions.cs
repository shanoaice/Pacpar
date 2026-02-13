using Pacpar.Alpm.list;

namespace Pacpar.Alpm;

/// <summary>
///  Transaction flags
/// </summary>
[Flags]
public enum TransactionFlags : uint
{
  /// <summary>
  ///  Ignore dependency checks.
  /// </summary>
  ALPM_TRANS_FLAG_NODEPS = 1,
  /// <summary>
  ///  Delete files even if they are tagged as backup.
  /// </summary>
  ALPM_TRANS_FLAG_NOSAVE = 4,
  /// <summary>
  ///  Ignore version numbers when checking dependencies.
  /// </summary>
  ALPM_TRANS_FLAG_NODEPVERSION = 8,
  /// <summary>
  ///  Remove also any packages depending on a package being removed.
  /// </summary>
  ALPM_TRANS_FLAG_CASCADE = 16,
  /// <summary>
  ///  Remove packages and their unneeded deps (not explicitly installed).
  /// </summary>
  ALPM_TRANS_FLAG_RECURSE = 32,
  /// <summary>
  ///  Modify database but do not commit changes to the filesystem.
  /// </summary>
  ALPM_TRANS_FLAG_DBONLY = 64,
  /// <summary>
  ///  Do not run hooks during a transaction
  /// </summary>
  ALPM_TRANS_FLAG_NOHOOKS = 128,
  /// <summary>
  ///  Use ALPM_PKG_REASON_DEPEND when installing packages.
  /// </summary>
  ALPM_TRANS_FLAG_ALLDEPS = 256,
  /// <summary>
  ///  Only download packages and do not actually install.
  /// </summary>
  ALPM_TRANS_FLAG_DOWNLOADONLY = 512,
  /// <summary>
  ///  Do not execute install scriptlets after installing.
  /// </summary>
  ALPM_TRANS_FLAG_NOSCRIPTLET = 1024,
  /// <summary>
  ///  Ignore dependency conflicts.
  /// </summary>
  ALPM_TRANS_FLAG_NOCONFLICTS = 2048,
  /// <summary>
  ///  Do not install a package if it is already installed and up to date.
  /// </summary>
  ALPM_TRANS_FLAG_NEEDED = 8192,
  /// <summary>
  ///  Use ALPM_PKG_REASON_EXPLICIT when installing packages.
  /// </summary>
  ALPM_TRANS_FLAG_ALLEXPLICIT = 16384,
  /// <summary>
  ///  Do not remove a package if it is needed by another one.
  /// </summary>
  ALPM_TRANS_FLAG_UNNEEDED = 32768,
  /// <summary>
  ///  Remove also explicitly installed unneeded deps (use with ALPM_TRANS_FLAG_RECURSE).
  /// </summary>
  ALPM_TRANS_FLAG_RECURSEALL = 65536,
  /// <summary>
  ///  Do not lock the database during the operation.
  /// </summary>
  ALPM_TRANS_FLAG_NOLOCK = 131072,
}

public class Transactions : IDisposable
{
  private bool _released;

  private readonly Alpm _library;

  internal unsafe Transactions(Alpm alpmLibrary, TransactionFlags flags)
  {
    _library = alpmLibrary;
    var err = NativeMethods.alpm_trans_init((byte*)_library.Handle, (int)flags);
    if (err != 0)
    {
      throw _library.GetCurrentError()!;
    }
  }

  public unsafe AlpmList<DepMissing> Prepare()
  {
    var depmissing = new AlpmList<DepMissing>(&DepMissing.Factory);
    fixed (_alpm_list_t** list = &depmissing.AlpmListNative)
    {
      var err = NativeMethods.alpm_trans_prepare((byte*)_library.Handle, list);
      if (err != 0)
      {
        throw _library.GetCurrentError()!;
      }
      return depmissing;
    }
  }

  public unsafe void AddPackage(Package pkg)
  {
    var err = NativeMethods.alpm_add_pkg((byte*)_library.Handle, pkg.BackingStruct);
    if (err != 0)
    {
      throw new PackageException($"Failed to add package: {pkg.Name}", pkg, _library.Errno);
    }
  }

  public unsafe void RemovePackage(Package pkg)
  {
    var err = NativeMethods.alpm_remove_pkg((byte*)_library.Handle, pkg.BackingStruct);
    if (err != 0)
    {
      throw new PackageException($"Failed to remove package: {pkg.Name}", pkg, _library.Errno);
    }
  }

  public unsafe void SystemUpgrade(bool enableDowngrade)
  {
    var err = NativeMethods.alpm_sync_sysupgrade((byte*)_library.Handle, enableDowngrade ? 1 : 0);
    if (err != 0)
    {
      throw _library.GetCurrentError()!;
    }
  }

  public unsafe void Interrupt()
  {
    var err = NativeMethods.alpm_trans_interrupt((byte*)_library.Handle);
    if (err != 0)
    {
      throw _library.GetCurrentError()!;
    }
  }

  public unsafe AlpmStringList Commit()
  {
    var errorMessages = new AlpmStringList(AlpmStringListAllocPattern.FFI);
    fixed (_alpm_list_t** list = &errorMessages.AlpmListNative)
    {
      var err = NativeMethods.alpm_trans_commit((byte*)_library.Handle, list);
      if (err != 0)
      {
        throw _library.GetCurrentError()!;
      }
      return errorMessages;
    }
  }

  public unsafe AlpmList<Package> GetAddedPackages() => new(NativeMethods.alpm_trans_get_add((byte*)_library.Handle), &Package.FactoryFromDatabase);

  public unsafe TransactionFlags GetFlags() => (TransactionFlags)NativeMethods.alpm_trans_get_flags((byte*)_library.Handle);

  public unsafe AlpmList<Package> GetRemovedPackages() => new(NativeMethods.alpm_trans_get_remove((byte*)_library.Handle), &Package.FactoryFromDatabase);

  public void Dispose()
  {
    Dispose(disposing: true);
    GC.SuppressFinalize(this);
  }

  ~Transactions()
  {
    Dispose(disposing: false);
  }

  protected virtual unsafe void Dispose(bool disposing)
  {
    if (!_released)
    {
      if (disposing)
      {
        // dispose managed state (managed objects)
      }
      _ = NativeMethods.alpm_trans_release((byte*)_library.Handle);
      _released = true;
    }
  }
}


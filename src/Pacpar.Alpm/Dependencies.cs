using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

public unsafe class Depend(_alpm_depend_t* backingStruct) : IDisposable
{
  internal readonly _alpm_depend_t* BackingStruct = backingStruct;

  public static Depend Factory(void* ptr) => new((_alpm_depend_t*)ptr);

  public static AlpmDisposableList<Depend> ListFactory(_alpm_list_t* alpmList) => new(alpmList, &Factory);

  private string? _description;
  private string? _name;
  private string? _version;

  private bool _disposed;

  protected void ThrowIfDisposed()
  {
    throw new ObjectDisposedException(GetType().FullName);
  }

  public string? Description
  {
    get
    {
      ThrowIfDisposed();
      _description ??= Marshal.PtrToStringUTF8((IntPtr)BackingStruct->desc);
      return _description;
    }
  }
  public string? Name
  {
    get
    {
      ThrowIfDisposed();
      _name ??= Marshal.PtrToStringUTF8((IntPtr)BackingStruct->name);
      return _name;
    }
  }
  public string? Version
  {
    get
    {
      ThrowIfDisposed();
      _version ??= Marshal.PtrToStringUTF8((IntPtr)BackingStruct->version);
      return _version;
    }
  }

  public _alpm_depmod_t Depmod => BackingStruct->mod_;

  public void Dispose()
  {
    GC.SuppressFinalize(this);
    Dispose(disposing: true);
  }

  protected virtual void Dispose(bool disposing)
  {
    if (!_disposed)
    {
      if (disposing)
      {
        // dispose managed state (managed objects)
      }
      NativeMethods.alpm_dep_free(BackingStruct);
      _disposed = true;
    }
  }

  ~Depend() => Dispose(disposing: false);
}

public unsafe class DepMissing(_alpm_depmissing_t* backingStruct) : IDisposable
{
  internal _alpm_depmissing_t* BackingStruct = backingStruct;

  public static DepMissing Factory(void* ptr) => new((_alpm_depmissing_t*)ptr);

  private string? _causingPkg;
  public readonly Depend? Depend = new(backingStruct->depend);
  private string? _target;

  private bool _disposed;

  protected void ThrowIfDisposed()
  {
    throw new ObjectDisposedException(GetType().FullName);
  }

  public string? CausingPkg
  {
    get
    {
      ThrowIfDisposed();
      _causingPkg ??= Marshal.PtrToStringUTF8((IntPtr)BackingStruct->causingpkg);
      return _causingPkg;
    }
  }
  public string? Target
  {
    get
    {
      ThrowIfDisposed();
      _target ??= Marshal.PtrToStringUTF8((IntPtr)BackingStruct->target);
      return _target;
    }
  }

  public void Dispose()
  {
    GC.SuppressFinalize(this);
    Dispose(disposing: true);
  }

  protected virtual void Dispose(bool disposing)
  {
    if (!_disposed)
    {
      if (disposing)
      {
        // dispose managed state (managed objects)
        Depend?.Dispose();
      }
      NativeMethods.alpm_depmissing_free(BackingStruct);
      _disposed = true;
    }
  }

  ~DepMissing() => Dispose(disposing: false);
}

public unsafe class FileConflict(_alpm_fileconflict_t* backingStruct) : IDisposable
{
  internal _alpm_fileconflict_t* BackingStruct = backingStruct;

  public static FileConflict Factory(void* ptr) => new((_alpm_fileconflict_t*)ptr);

  private string? _ctarget;
  private string? _file;
  private string? _target;

  private bool _disposed;

  protected void ThrowIfDisposed()
  {
    throw new ObjectDisposedException(GetType().FullName);
  }

  public string? Ctarget
  {
    get
    {
      ThrowIfDisposed();
      _ctarget ??= Marshal.PtrToStringUTF8((IntPtr)BackingStruct->ctarget);
      return _ctarget;
    }
  }
  public string? File
  {
    get
    {
      ThrowIfDisposed();
      _file ??= Marshal.PtrToStringUTF8((IntPtr)BackingStruct->file);
      return _file;
    }
  }
  public string? Target
  {
    get
    {
      ThrowIfDisposed();
      _target ??= Marshal.PtrToStringUTF8((IntPtr)BackingStruct->target);
      return _target;
    }
  }

  public void Dispose()
  {
    GC.SuppressFinalize(this);
    Dispose(disposing: true);
  }

  protected virtual void Dispose(bool disposing)
  {
    if (!_disposed)
    {
      if (disposing)
      {
        // dispose managed state (managed objects)
      }
      NativeMethods.alpm_fileconflict_free(BackingStruct);
      _disposed = true;
    }
  }

  ~FileConflict() => Dispose(disposing: false);
}

public unsafe class Conflict(_alpm_conflict_t* backingStruct) : IDisposable
{
  internal _alpm_conflict_t* BackingStruct = backingStruct;

  public static Conflict Factory(void* ptr) => new((_alpm_conflict_t*)ptr);

  public Package Package1 = new(backingStruct->package1);
  public Package Package2 = new(backingStruct->package2);
  public Depend Reason = new(backingStruct->reason);

  private bool _disposed;

  protected void ThrowIfDisposed()
  {
    throw new ObjectDisposedException(GetType().FullName);
  }


  public void Dispose()
  {
    GC.SuppressFinalize(this);
    Dispose(disposing: true);
  }

  protected virtual void Dispose(bool disposing)
  {
    if (!_disposed)
    {
      if (disposing)
      {
        // dispose managed state (managed objects)
        Package1.Dispose();
        Package2.Dispose();
        Reason.Dispose();
      }
      NativeMethods.alpm_conflict_free(BackingStruct);
      _disposed = true;
    }
  }

  ~Conflict() => Dispose(disposing: false);
}

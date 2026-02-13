using System;
using System.Runtime.InteropServices;
using Pacpar.Alpm.list;

namespace Pacpar.Alpm;

public unsafe class Depend(_alpm_depend_t* backingStruct) : IDisposable
{
  internal unsafe _alpm_depend_t* _backing_struct = backingStruct;

  public static unsafe Depend Factory(void* ptr) => new((_alpm_depend_t*)ptr);

  public static unsafe AlpmDisposableList<Depend> ListFactory(_alpm_list_t* alpmList) => new(alpmList, &Factory);

  private string? _description;
  private string? _name;
  private string? _version;

  private bool _disposed;

  public unsafe string? Description
  {
    get
    {
      _description ??= Marshal.PtrToStringUTF8((IntPtr)_backing_struct->desc);
      return _description;
    }
  }
  public unsafe string? Name
  {
    get
    {
      _name ??= Marshal.PtrToStringUTF8((IntPtr)_backing_struct->name);
      return _name;
    }
  }
  public unsafe string? Version
  {
    get
    {
      _version ??= Marshal.PtrToStringUTF8((IntPtr)_backing_struct->version);
      return _version;
    }
  }

  public unsafe _alpm_depmod_t Depmod => _backing_struct->mod_;

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
      NativeMethods.alpm_dep_free(_backing_struct);
      _disposed = true;
    }
  }

  ~Depend() => Dispose(disposing: false);
}

public unsafe class DepMissing(_alpm_depmissing_t* backingStruct) : IDisposable
{
  internal unsafe _alpm_depmissing_t* _backing_struct = backingStruct;

  public static DepMissing Factory(void* ptr) => new((_alpm_depmissing_t*)ptr);

  private string? _causingPkg;
  public readonly unsafe Depend? Depend = new(backingStruct->depend);
  private string? _target;

  private bool _disposed;

  public unsafe string? CausingPkg
  {
    get
    {
      _causingPkg ??= Marshal.PtrToStringUTF8((IntPtr)_backing_struct->causingpkg);
      return _causingPkg;
    }
  }
  public unsafe string? Target
  {
    get
    {
      _target ??= Marshal.PtrToStringUTF8((IntPtr)_backing_struct->target);
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
      NativeMethods.alpm_depmissing_free(_backing_struct);
      _disposed = true;
    }
  }

  ~DepMissing() => Dispose(disposing: false);
}

public unsafe class FileConflict(_alpm_fileconflict_t* backingStruct) : IDisposable
{
  internal unsafe _alpm_fileconflict_t* _backing_struct = backingStruct;

  public static FileConflict Factory(void* ptr) => new((_alpm_fileconflict_t*)ptr);

  private string? _ctarget;
  private string? _file;
  private string? _target;

  private bool _disposed;

  public unsafe string? Ctarget
  {
    get
    {
      _ctarget ??= Marshal.PtrToStringUTF8((IntPtr)_backing_struct->ctarget);
      return _ctarget;
    }
  }
  public unsafe string? File
  {
    get
    {
      _file ??= Marshal.PtrToStringUTF8((IntPtr)_backing_struct->file);
      return _file;
    }
  }
  public unsafe string? Target
  {
    get
    {
      _target ??= Marshal.PtrToStringUTF8((IntPtr)_backing_struct->target);
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
      NativeMethods.alpm_fileconflict_free(_backing_struct);
      _disposed = true;
    }
  }

  ~FileConflict() => Dispose(disposing: false);
}

public unsafe class Conflict(_alpm_conflict_t* backing_struct) : IDisposable
{
  internal unsafe _alpm_conflict_t* _backing_struct = backing_struct;

  public static Conflict Factory(void* ptr) => new((_alpm_conflict_t*)ptr);

  public unsafe Package Package1 = new(backing_struct->package1);
  public unsafe Package Package2 = new(backing_struct->package2);
  public unsafe Depend Reason = new(backing_struct->reason);

  private bool _disposed;

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
      NativeMethods.alpm_conflict_free(_backing_struct);
      _disposed = true;
    }
  }

  ~Conflict() => Dispose(disposing: false);
}

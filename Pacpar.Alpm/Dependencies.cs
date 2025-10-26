using System;
using System.Runtime.InteropServices;

namespace Pacpar.Alpm;

public unsafe class AlpmDepend(_alpm_depend_t* backingStruct)
{
  internal unsafe _alpm_depend_t* _backing_struct = backingStruct;

  public static AlpmDepend Factory(void* ptr) => new((_alpm_depend_t*)ptr);

  private string? _description;
  private string? _name;
  private string? _version;

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
}

public unsafe class AlpmDepMissing(_alpm_depmissing_t* backingStruct)
{
  internal unsafe _alpm_depmissing_t* _backing_struct = backingStruct;

  public static AlpmDepMissing Factory(void* ptr) => new((_alpm_depmissing_t*)ptr);

  private string? _causingPkg;
  public readonly unsafe AlpmDepend? Depend = new(backingStruct->depend);
  private string? _target;

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
}

public unsafe class AlpmFileConflict(_alpm_fileconflict_t* backingStruct)
{
  internal unsafe _alpm_fileconflict_t* _backing_struct = backingStruct;

  public static AlpmFileConflict Factory(void* ptr) => new((_alpm_fileconflict_t*)ptr);

  private string? _ctarget;
  private string? _file;
  private string? _target;

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
}

public unsafe class AlpmConflict(_alpm_conflict_t* backing_struct)
{
  internal unsafe _alpm_conflict_t* _backing_struct = backing_struct;

  public static AlpmConflict Factory(void* ptr) => new((_alpm_conflict_t*)ptr);

  public unsafe AlpmPackage Package1 = new(backing_struct->package1);
  public unsafe AlpmPackage Package2 = new(backing_struct->package2);
  public unsafe AlpmDepend Reason = new(backing_struct->reason);
}

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

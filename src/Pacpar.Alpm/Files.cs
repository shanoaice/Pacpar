using System.Collections;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

public unsafe class Backup(_alpm_backup_t* backingStruct)
{
  public static Backup Factory(void* ptr) => new((_alpm_backup_t*)ptr);

  public static AlpmList<Backup> ListFactory(_alpm_list_t* ptr) => new(ptr, &Factory);

  public string? Name => field ??= Marshal.PtrToStringAnsi((nint)backingStruct->name);
}

public unsafe struct File(_alpm_file_t* backingStruct)
{
  public static File Factory(void* ptr) => new((_alpm_file_t*)ptr);

  public uint Mode => backingStruct->mode;
  public CLong Size => backingStruct->size;

  public string? Name => field ??= Marshal.PtrToStringAnsi((nint)backingStruct->name);
}

public unsafe class FileList(_alpm_filelist_t* backingStruct) : IReadOnlyList<File>
{
  public int Count => (int)backingStruct->count;

  public File this[int index] =>
    new((_alpm_file_t*)(new Span<nint>(backingStruct->files, (int)backingStruct->count))[index]);

  private class FileListEnumerator(FileList fileList) : IEnumerator<File>
  {
    // ReSharper disable once RedundantDefaultMemberInitializer
    private int _index = 0;
    public File Current => fileList[_index];

    object IEnumerator.Current => Current;

    public bool MoveNext() => _index++ < fileList.Count;
    public void Reset() => _index = 0;

    public void Dispose()
    {
      GC.SuppressFinalize(this);
    }
  }

  public IEnumerator<File> GetEnumerator() => new FileListEnumerator(this);
  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

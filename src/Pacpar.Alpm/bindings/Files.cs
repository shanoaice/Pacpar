using System.Collections;
using System.Runtime.InteropServices;
using Pacpar.Alpm.list;

namespace Pacpar.Alpm.Bindings;

public unsafe class Backup(_alpm_backup_t* backingStruct)
{
  public static Backup Factory(void* ptr) => new((_alpm_backup_t*)ptr);

  public static AlpmList<Backup> ListFactory(_alpm_list_t* ptr) => new(ptr, &Factory);

  private string? _name;

  public string? Name => _name ??= Marshal.PtrToStringAnsi((nint)backingStruct->name);
}

public unsafe class File(_alpm_file_t* backingStruct)
{
  public static File Factory(void* ptr) => new((_alpm_file_t*)ptr);

  public readonly uint Mode = backingStruct->mode;
  public readonly CLong Size = backingStruct->size;

  private string? _name;

  public string? Name => _name ??= Marshal.PtrToStringAnsi((nint)backingStruct->name);
}

public unsafe class FileList(_alpm_filelist_t* backingStruct) : IReadOnlyList<File>
{
  private readonly File[] _files = new File[backingStruct->count];

  private readonly int _count = (int)backingStruct->count;
  int IReadOnlyCollection<File>.Count => _count;

  public File this[int index] => _files[index] ??= new(backingStruct->files + index * sizeof(_alpm_file_t));

  private class FileListEnumerator(FileList fileList) : IEnumerator<File>
  {
    // ReSharper disable once RedundantDefaultMemberInitializer
    private int _index = 0;
    public File Current => fileList[_index];

    object IEnumerator.Current => Current;

    public bool MoveNext() => _index++ < fileList._count;
    public void Reset() => _index = 0;
    public void Dispose()
    {
      GC.SuppressFinalize(this);
    }
  }

  public IEnumerator<File> GetEnumerator() => new FileListEnumerator(this);
  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

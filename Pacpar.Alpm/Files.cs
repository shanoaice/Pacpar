using System.Collections;
using System.Runtime.InteropServices;

namespace Pacpar.Alpm;

public unsafe class Backup(_alpm_backup_t* _backing_struct)
{
  public static Backup Factory(void* ptr) => new((_alpm_backup_t*)ptr);

  private string? name;

  public string? Name => name ??= Marshal.PtrToStringAnsi((nint)_backing_struct->name);
}

public unsafe class File(_alpm_file_t* _backing_struct)
{
  public static File Factory(void* ptr) => new((_alpm_file_t*)ptr);

  public readonly uint Mode = _backing_struct->mode;
  public readonly CLong Size = _backing_struct->size;

  private string? name;

  public string? Name => name ??= Marshal.PtrToStringAnsi((nint)_backing_struct->name);
}

public unsafe class FileList(_alpm_filelist_t* _backing_struct) : IReadOnlyList<File>
{
  private readonly File[] _files = new File[_backing_struct->count];

  public readonly int Count = (int)_backing_struct->count;
  int IReadOnlyCollection<File>.Count => Count;

  public File this[int index] => _files[index] ??= new(_backing_struct->files + index * sizeof(_alpm_file_t));

  public class FileListEnumerator(FileList fileList) : IEnumerator<File>
  {
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
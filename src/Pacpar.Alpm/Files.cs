using System.Collections;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

public unsafe class Backup
{
  private readonly _alpm_backup_t* backingStruct;

  internal Backup(_alpm_backup_t* backingStruct)
  {
    this.backingStruct = backingStruct;
  }

  internal static Backup Factory(void* ptr) => new((_alpm_backup_t*)ptr);

  /// <summary>
  /// Borrowed view over a backup-entry list owned by libalpm (for example
  /// <c>alpm_pkg_get_backup</c>).
  /// </summary>
  internal static AlpmList<Backup> ListFactory(_alpm_list_t* ptr) => AlpmList<Backup>.Borrow(ptr, &Factory);

  public string? Name => field ??= NativeString.FromNative((nint)backingStruct->name);
}

public unsafe struct File
{
  private readonly _alpm_file_t* backingStruct;

  internal File(_alpm_file_t* backingStruct)
  {
    this.backingStruct = backingStruct;
  }

  internal static File Factory(void* ptr) => new((_alpm_file_t*)ptr);

  public uint Mode => backingStruct->mode;
  public CLong Size => backingStruct->size;

  public string? Name => field ??= NativeString.FromNative((nint)backingStruct->name);
}

public unsafe class FileList : IReadOnlyList<File>
{
  private readonly _alpm_filelist_t* backingStruct;

  internal FileList(_alpm_filelist_t* backingStruct)
  {
    this.backingStruct = backingStruct;
  }

  public int Count => (int)backingStruct->count;

  public File this[int index] =>
    new((_alpm_file_t*)(new Span<nint>(backingStruct->files, (int)backingStruct->count))[index]);

  public struct Enumerator(FileList fileList) : IEnumerator<File>
  {
    private int _index = 0;
    private bool _started = false;

    public File Current => !_started ? throw new InvalidOperationException() : fileList[_index];

    object IEnumerator.Current => Current;

    public bool MoveNext()
    {
      if (_index >= fileList.Count - 1) return false;

      if (!_started)
      {
        _started = true;
        return true;
      }

      ++_index;
      return true;
    }

    public void Reset()
    {
      _started = false;
      _index = 0;
    }

    public void Dispose()
    {
    }
  }

  public Enumerator GetEnumerator() => new Enumerator(this);

  IEnumerator<File> IEnumerable<File>.GetEnumerator() => GetEnumerator();
  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

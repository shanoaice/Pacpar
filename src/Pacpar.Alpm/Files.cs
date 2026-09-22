using System.Collections;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

/// <summary>
/// A backup entry (<c>alpm_backup_t</c>).
/// </summary>
/// <remarks>
/// A managed snapshot: the name is copied on construction, so the entry stays readable after the
/// package it came from is gone. <see cref="Package.Backup"/> still hands out a borrowed
/// <see cref="AlpmList{T}"/> - the list itself belongs to the package - but every element it yields
/// is one of these copies.
/// </remarks>
public class Backup
{
  internal unsafe Backup(_alpm_backup_t* backingStruct)
  {
    Name = NativeString.FromNative((nint)backingStruct->name);
  }

  internal static unsafe Backup Factory(void* ptr) => new((_alpm_backup_t*)ptr);

  /// <summary>
  /// Borrowed view over a backup-entry list owned by libalpm (for example
  /// <c>alpm_pkg_get_backup</c>).
  /// </summary>
  internal static unsafe AlpmList<Backup> ListFactory(_alpm_list_t* ptr) => AlpmList<Backup>.Borrow(ptr, &Factory);

  public string? Name { get; }
}

/// <summary>
/// One entry of a package's file list (<c>alpm_file_t</c>).
/// </summary>
/// <remarks>
/// A managed snapshot: mode, size and name are copied out of the borrowed array when the entry is
/// read, so a <see cref="File"/> taken from <see cref="Package.Files"/> stays valid.
/// </remarks>
public readonly struct File
{
  internal unsafe File(_alpm_file_t* backingStruct)
  {
    Mode = backingStruct->mode;
    Size = backingStruct->size;
    Name = NativeString.FromNative((nint)backingStruct->name);
  }

  internal static unsafe File Factory(void* ptr) => new((_alpm_file_t*)ptr);

  public uint Mode { get; }

  public CLong Size { get; }

  public string? Name { get; }
}

/// <summary>
/// A package's file list (<c>alpm_filelist_t</c>).
/// </summary>
/// <remarks>
/// A borrowed view: the array belongs to the package and this type never frees it. It is kept as a
/// view rather than copied because a package's file list routinely holds tens of thousands of
/// entries, and every entry it yields is already a <see cref="File"/> snapshot.
/// </remarks>
public unsafe class FileList : IReadOnlyList<File>
{
  private readonly _alpm_filelist_t* backingStruct;

  internal FileList(_alpm_filelist_t* backingStruct)
  {
    this.backingStruct = backingStruct;
  }

  public int Count => (int)backingStruct->count;

  /// <remarks>
  /// The indexer used to read through <c>new Span&lt;nint&gt;(files, count)[index]</c>, which strides
  /// by <c>nint</c> rather than by an entry: only index 0 landed on an entry, and every later index
  /// read a pointer out of the middle of a neighbouring one. A malformed entry then reached
  /// <see cref="File"/>'s constructor as a bogus name pointer.
  /// </remarks>
  public File this[int index]
  {
    get
    {
      if ((nuint)index >= backingStruct->count) throw new ArgumentOutOfRangeException(nameof(index));

      return new File(&backingStruct->files[index]);
    }
  }

  /// <summary>Forward-only enumerator over the borrowed array of file entries.</summary>
  /// <remarks>
  /// The enumerator used to test "is there another element" by comparing the current index with
  /// <c>Count - 1</c> <i>before</i> advancing, which ended every sequence one element early: a
  /// one-entry list yielded nothing at all and a longer list silently dropped its last file. It is
  /// index-based, so it now leaves the index one past the last element, as the contract requires.
  /// </remarks>
  public struct Enumerator(FileList fileList) : IEnumerator<File>
  {
    private int _index = -1;

    public File Current => _index < 0 || _index >= fileList.Count
      ? throw new InvalidOperationException()
      : fileList[_index];

    object IEnumerator.Current => Current;

    public bool MoveNext() => ++_index < fileList.Count;

    public void Reset() => _index = -1;

    public void Dispose()
    {
    }
  }

  public Enumerator GetEnumerator() => new(this);

  IEnumerator<File> IEnumerable<File>.GetEnumerator() => GetEnumerator();
  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

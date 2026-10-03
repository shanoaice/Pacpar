using System.Collections;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>
/// A package's file list (<c>alpm_filelist_t</c>).
/// </summary>
/// <remarks>
/// A borrowed view: the array belongs to the package and this type never frees it. It is kept as a
/// view rather than copied because a package's file list routinely holds tens of thousands of
/// entries, and every entry it yields is already a <see cref="PackageFile"/> snapshot.
/// <para>
/// The package's lifetime token guards every dereference: <see cref="Count"/>, the indexer and
/// <see cref="GetEnumerator"/> throw <see cref="AlpmLifetimeException"/> once the package has been
/// released, instead of reading the freed array.
/// </para>
/// </remarks>
public unsafe class FileList : IReadOnlyList<PackageFile>
{
  private readonly _alpm_filelist_t* _backingStruct;
  private readonly LifetimeStamp? _lifetime;

  /// <param name="backingStruct">The package-owned file list.</param>
  /// <param name="lifetime">Domain of the package that owns the array; <c>null</c> disables the guard.</param>
  internal FileList(_alpm_filelist_t* backingStruct, Lifetime? lifetime)
  {
    _backingStruct = backingStruct;
    _lifetime = lifetime?.Capture();
  }

  public int Count
  {
    get
    {
      _lifetime?.ThrowIfStale();
      return (int)_backingStruct->count;
    }
  }

  /// <remarks>
  /// The indexer used to read through <c>new Span&lt;nint&gt;(files, count)[index]</c>, which strides
  /// by <c>nint</c> rather than by an entry: only index 0 landed on an entry, and every later index
  /// read a pointer out of the middle of a neighbouring one. A malformed entry then reached
  /// <see cref="PackageFile"/>'s constructor as a bogus name pointer.
  /// </remarks>
  public PackageFile this[int index]
  {
    get
    {
      _lifetime?.ThrowIfStale();
      if ((nuint)index >= _backingStruct->count) throw new ArgumentOutOfRangeException(nameof(index));

      var file = new PackageFile(&_backingStruct->files[index]);
      GC.KeepAlive(this);
      return file;
    }
  }
  /// <summary>Forward-only enumerator over the borrowed array of file entries.</summary>
  /// <remarks>
  /// <c>Count - 1</c> <i>before</i> advancing, which ended every sequence one element early: a
  /// one-entry list yielded nothing at all and a longer list silently dropped its last file. It is
  /// index-based, so it now leaves the index one past the last element, as the contract requires.
  /// </remarks>
  public struct Enumerator(FileList fileList) : IEnumerator<PackageFile>
  {
    private int _index = -1;

    public PackageFile Current => _index < 0 || _index >= fileList.Count
      ? throw new InvalidOperationException()
      : fileList[_index];

    object IEnumerator.Current => Current;

    public bool MoveNext() => ++_index < fileList.Count;

    public void Reset() => _index = -1;

    public void Dispose()
    {
    }
  }

  public Enumerator GetEnumerator()
  {
    _lifetime?.ThrowIfStale();
    return new Enumerator(this);
  }

  IEnumerator<PackageFile> IEnumerable<PackageFile>.GetEnumerator() => GetEnumerator();
  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

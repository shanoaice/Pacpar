using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Covers the non-string branch of the option-collection template (design report §K):
/// <see cref="Depend"/> elements are borrowed rather than marshalled, compared by identity, and a
/// detached snapshot must be rejected by <c>Add</c> while the read-only operations answer
/// <c>false</c> for it.
/// </summary>
/// <remarks>
/// Like <see cref="OptionCollectionTests"/>, this builds an isolated libalpm handle under
/// <see cref="Path.GetTempPath()"/> so it stays in the non-integration set.
/// </remarks>
public sealed unsafe class AssumeInstalledOptionTests : IDisposable
{
  private readonly string _workspaceRoot;
  private readonly Alpm _alpm;

  public AssumeInstalledOptionTests()
  {
    _workspaceRoot = Path.Combine(Path.GetTempPath(), "pacpar-assume-tests", Guid.NewGuid().ToString("n"));
    var root = Path.Combine(_workspaceRoot, "root");
    var dbpath = Path.Combine(_workspaceRoot, "var", "lib", "pacman");

    Directory.CreateDirectory(root);
    Directory.CreateDirectory(Path.Combine(dbpath, "local"));
    Directory.CreateDirectory(Path.Combine(root, "tmp"));
    Directory.CreateDirectory(Path.Combine(root, "var", "cache", "pacman", "pkg"));

    _alpm = new Alpm(root, dbpath);
  }

  public void Dispose()
  {
    _alpm.Dispose();

    if (Directory.Exists(_workspaceRoot))
    {
      Directory.Delete(_workspaceRoot, recursive: true);
    }
  }

  private static _alpm_depend_t* ParseDepend(string spec)
  {
    var specPtr = NativeString.ToNative(spec);
    try
    {
      var depend = NativeMethods.alpm_dep_from_string(specPtr);
      Assert.True(depend != null, $"libalpm could not parse the dependency spec '{spec}'.");
      return depend;
    }
    finally
    {
      Marshal.FreeHGlobal((nint)specPtr);
    }
  }

  [Fact]
  public void Add_BorrowsTheDependency_AndStoresACopy()
  {
    // libalpm 16 only accepts DepMod.Any/Equal for assume-installed (verified against the .so),
    // which is why the spec uses "=" and not ">=".
    var native = ParseDepend("foobar=1.0");
    try
    {
      var collection = _alpm.Options.AssumeInstalled;
      Assert.Empty(collection);

      collection.Add(Depend.Factory(native));

      var stored = Assert.Single(collection);
      Assert.Equal("foobar", stored.Name);
      Assert.Equal("1.0", stored.Version);
    }
    finally
    {
      NativeMethods.alpm_dep_free(native);
    }
  }

  /// <summary>
  /// <c>Contains</c> uses <c>alpm_list_find_ptr</c>, so only the element actually stored in the
  /// list (the copy libalpm made) matches; the caller's original struct does not.
  /// </summary>
  [Fact]
  public void Contains_MatchesOnlyTheStoredElement()
  {
    var native = ParseDepend("foobar");
    try
    {
      var collection = _alpm.Options.AssumeInstalled;
      collection.Add(Depend.Factory(native));

      var stored = Assert.Single(collection);
      Assert.True(collection.Contains(stored));
      Assert.False(collection.Contains(Depend.Factory(native)));
    }
    finally
    {
      NativeMethods.alpm_dep_free(native);
    }
  }

  /// <summary>
  /// Probed against libalpm 16.0.1: <b>every</b> <c>alpm_option_remove_*</c> returns 1 when it
  /// removed the entry, 0 when it found nothing and -1 on error, while its own header documents
  /// "0 on success, -1 on error" for all of them. The wrapper has always tested that result against
  /// 0, so <see cref="ICollection{T}.Remove"/> answers with the inverse of its contract for every
  /// option collection, not just this one. This test pins the current behaviour so the refactor is
  /// provably neutral; report item N fixes it and will have to change these two assertions.
  /// </summary>
  [Fact]
  public void Remove_ReturnsTheInvertedResult_PendingReportItemN()
  {
    var native = ParseDepend("foobar=1.0");
    try
    {
      var collection = _alpm.Options.AssumeInstalled;
      collection.Add(Depend.Factory(native));

      // The entry does go away...
      Assert.False(collection.Remove(Depend.Factory(native)));
      Assert.Empty(collection);

      // ...and the result is "true" once there is nothing left to remove.
      Assert.True(collection.Remove(Depend.Factory(native)));
    }
    finally
    {
      NativeMethods.alpm_dep_free(native);
    }
  }

  [Fact]
  public void Clear_RemovesEveryDependency()
  {
    var native = ParseDepend("foobar=1.0");
    try
    {
      var collection = _alpm.Options.AssumeInstalled;
      collection.Add(Depend.Factory(native));
      collection.Add(Depend.Factory(native));
      Assert.Equal(2, collection.Count);

      collection.Clear();

      Assert.Empty(collection);
    }
    finally
    {
      NativeMethods.alpm_dep_free(native);
    }
  }

  /// <summary>
  /// The bug from design report §D was that <c>Add</c> accepted a detached snapshot and handed
  /// libalpm a null pointer instead of throwing.
  /// </summary>
  [Fact]
  public void DetachedSnapshot_IsRejectedByAddAndAbsentFromReads()
  {
    var native = ParseDepend("foobar");
    try
    {
      var detached = Depend.Snapshot(native);
      var collection = _alpm.Options.AssumeInstalled;

      Assert.Throws<ArgumentException>(() => collection.Add(detached));
      Assert.False(collection.Contains(detached));
      Assert.False(collection.Remove(detached));
      Assert.Empty(collection);
    }
    finally
    {
      NativeMethods.alpm_dep_free(native);
    }
  }
}

using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Covers the non-string branch of the option-collection template (design report §K) now that
/// <see cref="Depend"/> is a managed snapshot: <c>Add</c> materialises the snapshot for libalpm,
/// while <c>Contains</c> and <c>Remove</c> resolve the snapshot to the element libalpm stored by
/// comparing values.
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

  /// <summary>
  /// Parses a dependency with libalpm, then frees the native struct and hands back the snapshot: from
  /// this point on the test only holds managed values.
  /// </summary>
  private static Depend Parse(string spec)
  {
    var specPtr = NativeString.ToNative(spec);
    try
    {
      var native = NativeMethods.alpm_dep_from_string(specPtr);
      Assert.True(native != null, $"libalpm could not parse the dependency spec '{spec}'.");

      try
      {
        return Depend.Snapshot(native);
      }
      finally
      {
        NativeMethods.alpm_dep_free(native);
      }
    }
    finally
    {
      Marshal.FreeHGlobal((nint)specPtr);
    }
  }

  /// <summary>
  /// Builds a dependency through the same struct layout <see cref="Depend.ToNative"/> uses, so a test
  /// can describe a value libalpm's parser cannot produce - a dependency with a description.
  /// </summary>
  private static Depend HandBuilt(string name, string? version, string? description, DepMod mod)
  {
    var native = (_alpm_depend_t*)Marshal.AllocHGlobal(sizeof(_alpm_depend_t));
    native->name = NativeString.ToNative(name);
    native->version = NativeString.ToNative(version);
    native->desc = NativeString.ToNative(description);
    native->name_hash = default;
    native->mod_ = (_alpm_depmod_t)(uint)mod;

    try
    {
      return Depend.Snapshot(native);
    }
    finally
    {
      Depend.FreeNative(native);
    }
  }

  [Fact]
  public void Add_StoresACopyOfTheSnapshot()
  {
    var collection = _alpm.Options.AssumeInstalled;
    Assert.Empty(collection);

    collection.Add(Parse("foobar=1.0"));

    var stored = Assert.Single(collection);
    Assert.Equal("foobar", stored.Name);
    Assert.Equal("1.0", stored.Version);
    Assert.Equal(DepMod.EQUAL, stored.Depmod);

    // The snapshot is a value, not a pointer into the list, and the list is not the snapshot's owner.
    collection.Clear();
    Assert.Equal("foobar", stored.Name);
  }

  [Fact]
  public void Add_KeepsTheDescription()
  {
    var collection = _alpm.Options.AssumeInstalled;

    collection.Add(HandBuilt("foobar", "1.0", "needed by a test", DepMod.EQUAL));

    Assert.Equal("needed by a test", Assert.Single(collection).Description);
  }

  [Fact]
  public void Add_AcceptsADependencyWithoutAVersion()
  {
    var collection = _alpm.Options.AssumeInstalled;

    collection.Add(Parse("foobar"));

    var stored = Assert.Single(collection);
    Assert.Equal("foobar", stored.Name);
    Assert.Null(stored.Version);
    Assert.Equal(DepMod.ANY, stored.Depmod);
    Assert.True(collection.Contains(Parse("foobar")));
  }

  /// <summary>
  /// The identity comparison <c>Contains</c> used to rely on cannot answer this: libalpm stores its
  /// own copy of the dependency (probed with <c>alpm_list_find_ptr</c>), so a snapshot that was never
  /// in the list is the only thing a caller ever has.
  /// </summary>
  [Fact]
  public void Contains_ComparesByValue()
  {
    var collection = _alpm.Options.AssumeInstalled;
    collection.Add(Parse("foobar=1.0"));

    Assert.True(collection.Contains(Parse("foobar=1.0")));
    Assert.False(collection.Contains(Parse("foobar=2.0")));
    Assert.False(collection.Contains(Parse("barfoo=1.0")));
  }

  /// <summary>
  /// Probed against libalpm 16.0.1: removing with a dependency whose description differs still
  /// removes the entry, so the description must not take part in the comparison this layer uses
  /// either - otherwise <c>Contains</c> would answer <c>false</c> for an entry <c>Remove</c> removes.
  /// </summary>
  [Fact]
  public void Contains_IgnoresTheDescription()
  {
    var collection = _alpm.Options.AssumeInstalled;
    collection.Add(HandBuilt("foobar", "1.0", "stored description", DepMod.EQUAL));

    Assert.True(collection.Contains(HandBuilt("foobar", "1.0", "another description", DepMod.EQUAL)));
    Assert.True(collection.Contains(Parse("foobar=1.0")));
  }

  /// <summary>
  /// Probed against libalpm 16.0.1: <b>every</b> <c>alpm_option_remove_*</c> returns 1 when it
  /// removed the entry, 0 when it found nothing and -1 on error, while its own header documents
  /// "0 on success, -1 on error" for all of them. Testing that result against 0, as this wrapper
  /// used to, made <see cref="ICollection{T}.Remove"/> answer with the inverse of its contract for
  /// every option collection; report item N moved the interpretation into the base class.
  /// </summary>
  [Fact]
  public void Remove_ReportsWhetherTheDependencyWasThere()
  {
    var collection = _alpm.Options.AssumeInstalled;
    collection.Add(Parse("foobar=1.0"));

    // The entry does go away although the argument is a different snapshot of the same value.
    Assert.True(collection.Remove(Parse("foobar=1.0")));
    Assert.Empty(collection);

    // Nothing left to remove.
    Assert.False(collection.Remove(Parse("foobar=1.0")));
  }

  [Fact]
  public void Remove_OnDuplicateValues_RemovesOneEntry()
  {
    var collection = _alpm.Options.AssumeInstalled;
    collection.Add(Parse("foobar=1.0"));
    collection.Add(Parse("foobar=1.0"));
    Assert.Equal(2, collection.Count);

    Assert.True(collection.Remove(Parse("foobar=1.0")));

    Assert.Equal("foobar", Assert.Single(collection).Name);
  }

  [Fact]
  public void Clear_RemovesEveryDependency()
  {
    var collection = _alpm.Options.AssumeInstalled;
    collection.Add(Parse("foobar=1.0"));
    collection.Add(Parse("barfoo=2.0"));
    Assert.Equal(2, collection.Count);

    collection.Clear();

    Assert.Empty(collection);
  }

  /// <summary>
  /// Probed against libalpm 16.0.1: <c>alpm_option_add_assumeinstalled</c> answers -1 with
  /// <c>ALPM_ERR_WRONG_ARGS</c> for any modifier other than "any" and "=", while its header documents
  /// no such restriction. The wrapper must surface that instead of storing something libalpm would
  /// never match.
  /// </summary>
  [Fact]
  public void Add_RejectsAModifierLibalpmDoesNotSupport()
  {
    var collection = _alpm.Options.AssumeInstalled;

    var exception = Assert.Throws<AlpmException>(
      () => collection.Add(HandBuilt("foobar", "1.0", null, DepMod.GREATER_OR_EQUAL)));

    Assert.Equal(_alpm_errno_t.ALPM_ERR_WRONG_ARGS, exception.Errno);
    Assert.Empty(collection);
  }
}

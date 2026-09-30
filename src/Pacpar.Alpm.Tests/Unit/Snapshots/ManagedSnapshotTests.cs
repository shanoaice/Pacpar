using System.Runtime.InteropServices;
using System.Text;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit.Snapshots;

/// <summary>
/// Locks in the snapshot contract for the value types that used to be borrowed views over
/// libalpm-owned memory (report item F9, and the snapshot pass that followed it): the fields are
/// copied at construction, so overwriting - or releasing - the structure they came from cannot
/// change what they answer.
/// </summary>
/// <remarks>
/// Every test writes the source structure itself and then overwrites it, which makes the failure
/// deterministic: a view reads the new bytes, a snapshot still answers with the old ones. Nothing
/// here depends on freed memory happening to stay readable, and no libalpm handle is needed.
/// </remarks>
public sealed unsafe class ManagedSnapshotTests
{
  /// <summary>
  /// Overwrites the content of a buffer allocated for <paramref name="original"/>, leaving its NUL
  /// terminator in place so a view would read a well-formed but different string instead of running
  /// off the allocation.
  /// </summary>
  private static void Scramble(byte* buffer, string original)
  {
    var length = Encoding.UTF8.GetByteCount(original);

    for (var i = 0; i < length; ++i)
    {
      buffer[i] = (byte)'X';
    }
  }

  [Fact]
  public void Version_CopiesItsText()
  {
    var buffer = NativeString.ToNative("1.2.3-4");

    try
    {
      var version = new PackageVersion(buffer);

      Scramble(buffer, "1.2.3-4");

      Assert.Equal("1.2.3-4", version.ToString());
    }
    finally
    {
      NativeMemory.Free((void*)(nint)buffer);
    }
  }

  /// <summary>
  /// <c>alpm_pkg_vercmp</c> only accepts native strings, so the comparison marshals its operands.
  /// It must therefore keep working after the memory the versions came from is gone.
  /// </summary>
  /// <remarks>
  /// Probed against libalpm 16.0.1: a version without a package release compares <b>equal</b> to the
  /// same version with one (<c>vercmp("1.0", "1.0-1") == 0</c>), because libalpm compares the release
  /// only when both sides have one. The pair below therefore varies the release on both sides.
  /// </remarks>
  [Fact]
  public void Version_ComparesWithoutItsSource()
  {
    var olderText = NativeString.ToNative("1.0-1");
    var newerText = NativeString.ToNative("1.0-2");

    try
    {
      var older = new PackageVersion(olderText);
      var newer = new PackageVersion(newerText);

      Scramble(olderText, "1.0-1");
      NativeMemory.Free((void*)(nint)olderText);
      olderText = null;

      Scramble(newerText, "1.0-2");
      NativeMemory.Free((void*)(nint)newerText);
      newerText = null;

      Assert.True(older.CompareTo(newer) < 0, "1.0-1 must sort before 1.0-2");
      Assert.True(newer.CompareTo(older) > 0);

      var sameText = NativeString.ToNative("1.0-1");
      try
      {
        Assert.Equal(0, older.CompareTo(new PackageVersion(sameText)));
        Assert.Equal(1, older.CompareTo(null));
      }
      finally
      {
        NativeMemory.Free((void*)(nint)sameText);
      }
    }
    finally
    {
      if (olderText != null) NativeMemory.Free((void*)(nint)olderText);
      if (newerText != null) NativeMemory.Free((void*)(nint)newerText);
    }
  }

  [Fact]
  public void Backup_CopiesItsName()
  {
    var name = NativeString.ToNative("etc/pacman.conf");
    var hash = NativeString.ToNative("deadbeef");
    var native = (_alpm_backup_t*)NativeMemory.Alloc((nuint)sizeof(_alpm_backup_t));

    try
    {
      native->name = name;
      native->hash = hash;

      // Backup is an eager snapshot: the factory takes the list's token for shape compatibility,
      // but this test's memory is test-owned, so no context guards it.
      var backup = Backup.Factory(native, null);

      Scramble(name, "etc/pacman.conf");
      native->name = null;

      Assert.Equal("etc/pacman.conf", backup.Name);
    }
    finally
    {
      NativeMemory.Free((void*)(nint)name);
      NativeMemory.Free((void*)(nint)hash);
      NativeMemory.Free((void*)(nint)native);
    }
  }

  [Fact]
  public void File_CopiesNameModeAndSize()
  {
    var name = NativeString.ToNative("usr/bin/probe");
    var native = (_alpm_file_t*)NativeMemory.Alloc((nuint)sizeof(_alpm_file_t));

    try
    {
      native->name = name;
      native->mode = 0b111_101_101;
      native->size = new CLong(4096);

      var file = PackageFile.Factory(native, null);

      Scramble(name, "usr/bin/probe");
      native->name = null;
      native->mode = 0;
      native->size = default;

      Assert.Equal("usr/bin/probe", file.Name);
      Assert.Equal(0b111_101_101u, file.Mode);
      Assert.Equal(4096L, file.Size);
    }
    finally
    {
      NativeMemory.Free((void*)(nint)name);
      NativeMemory.Free((void*)(nint)native);
    }
  }

  /// <summary>
  /// The enumerator used to stop one entry early, so a one-entry list yielded nothing and every
  /// longer list dropped its last file. The entries themselves are snapshots.
  /// </summary>
  [Fact]
  public void FileList_YieldsEveryEntry_AndEveryEntryIsASnapshot()
  {
    string[] names = ["a", "bb", "ccc"];
    var buffers = new byte*[names.Length];
    var entries = (_alpm_file_t*)NativeMemory.Alloc((nuint)(sizeof(_alpm_file_t) * names.Length));
    var fileList = new _alpm_filelist_t();

    try
    {
      for (var i = 0; i < names.Length; ++i)
      {
        buffers[i] = NativeString.ToNative(names[i]);
        entries[i] = new _alpm_file_t
        {
          name = buffers[i],
          mode = (uint)i,
          size = new CLong(i),
        };
      }

      fileList.count = (nuint)names.Length;
      fileList.files = entries;

      // The FileList token guards Count / indexer / enumeration; this list is test-owned, so a
      // null token means "no owning context to outlive".
      var list = new FileList(&fileList, null);

      Assert.Equal(names.Length, list.Count);
      Assert.Equal(names, list.Select(file => file.Name));

      var first = list[0];
      Scramble(buffers[0], names[0]);

      Assert.Equal("a", first.Name);
    }
    finally
    {
      foreach (var buffer in buffers)
      {
        if (buffer != null) NativeMemory.Free((void*)(nint)buffer);
      }

      NativeMemory.Free((void*)(nint)entries);
    }
  }

  /// <summary>
  /// A group's member list is callback-independent library memory that can go away - the cache is
  /// rebuilt, the database is unregistered - so the snapshot owns a copy of the list too.
  /// </summary>
  [Fact]
  public void Group_CopiesItsNameAndMemberList()
  {
    var name = NativeString.ToNative("base-devel");
    var native = new _alpm_group_t();
    var members = NativeMethods.alpm_list_add(null, null);

    try
    {
      native.name = name;
      native.packages = members;

      // Group's ctor dereferences the native struct and enumerates the member list under the
      // staleness guard, so it needs a live token - unlike the pure snapshot types above.
      var lifetime = Lifetime.CreateRoot(new object(), "a test handle");

      var group = Group.Factory(&native, lifetime);

      Scramble(name, "base-devel");
      native.name = null;
      NativeMethods.alpm_list_free(members);
      members = null;
      native.packages = null;

      Assert.Equal("base-devel", group.Name);
      Assert.Single(group.Packages);
    }
    finally
    {
      NativeMemory.Free((void*)(nint)name);
      if (members != null) NativeMethods.alpm_list_free(members);
    }
  }

  /// <summary>
  /// libalpm builds the event union on the calling thread's stack; the probe in
  /// <c>.dsh-scratch/audit-probe/events.c</c> read zeros from it once the callback returned.
  /// </summary>
  [Fact]
  public void EventPayload_CopiesItsFieldsOutOfTheCallbackUnion()
  {
    var line = NativeString.ToNative(":: running post-transaction hooks...");
    var native = (_alpm_event_t*)NativeMemory.Alloc((nuint)sizeof(_alpm_event_t));

    try
    {
      native->type_ = _alpm_event_type_t.ALPM_EVENT_SCRIPTLET_INFO;
      native->scriptlet_info.line = line;

      // FromUnion takes the handle's root token, which any package views in the payload are
      // snapshotted against; this branch carries none.
      var payload = AlpmEvent.FromUnion(native, Lifetime.CreateRoot(new object(), "a test handle"));

      // Exactly what libalpm does to the union as soon as the callback returns.
      *native = default;
      NativeMemory.Free((void*)(nint)line);
      line = null;

      Assert.Equal(":: running post-transaction hooks...", Assert.IsType<AlpmEvent.ScriptletInfo>(payload).Line);
    }
    finally
    {
      if (line != null) NativeMemory.Free((void*)(nint)line);
      NativeMemory.Free((void*)(nint)native);
    }
  }

  [Fact]
  public void EventPayload_CopiesScalarFieldsOutOfTheCallbackUnion()
  {
    var native = (_alpm_event_t*)NativeMemory.Alloc((nuint)sizeof(_alpm_event_t));

    try
    {
      native->type_ = _alpm_event_type_t.ALPM_EVENT_HOOK_RUN_START;
      native->hook_run.position = 2;
      native->hook_run.total = 5;

      var payload = AlpmEvent.FromUnion(native, Lifetime.CreateRoot(new object(), "a test handle"));

      *native = default;

      var hookRun = Assert.IsType<AlpmEvent.HookRunStart>(payload);
      Assert.Equal((nuint)2, hookRun.Position);
      Assert.Equal((nuint)5, hookRun.Total);
    }
    finally
    {
      NativeMemory.Free((void*)(nint)native);
    }
  }

  [Fact]
  public void QuestionPayload_CopiesItsFieldsOutOfTheCallbackUnion()
  {
    using var env = new IsolatedAlpmEnvironment();
    var pkgDir = Path.Combine(Path.GetTempPath(), "pacpar-snapshot-" + Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(pkgDir);

    try
    {
      using var oldPkg = env.Alpm.LoadPackage(PackageArchive.Create(pkgDir, "pacpar-old"), full: false, SigLevel.ALPM_SIG_USE_DEFAULT);
      using var newPkg = env.Alpm.LoadPackage(PackageArchive.Create(pkgDir, "pacpar-new"), full: false, SigLevel.ALPM_SIG_USE_DEFAULT);
      var db = env.Alpm.RegisterSyncDatabase("core", SigLevel.ALPM_SIG_USE_DEFAULT);

      var native = (_alpm_question_t*)NativeMemory.Alloc((nuint)sizeof(_alpm_question_t));
      try
      {
        native->type_ = _alpm_question_type_t.ALPM_QUESTION_REPLACE_PKG;
        native->replace.replace = 1;
        native->replace.oldpkg = oldPkg.BackingStruct;
        native->replace.newpkg = newPkg.BackingStruct;
        native->replace.newdb = db.BackingStruct;

        var payload = AlpmQuestion.FromUnion(native, env.Alpm.BindingConfig);

        *native = default;

        var replace = Assert.IsType<AlpmQuestion.ReplacePackage>(payload);
        Assert.True(replace.Replace);
        Assert.Equal("pacpar-old", replace.OldPackage.Name);
        Assert.Equal("pacpar-new", replace.NewPackage.Name);
        Assert.Equal("core", replace.NewDatabase);
      }
      finally
      {
        NativeMemory.Free((void*)(nint)native);
      }
    }
    finally
    {
      if (Directory.Exists(pkgDir))
      {
        Directory.Delete(pkgDir, recursive: true);
      }
    }
  }

  [Fact]
  public void InstallIgnoredPackage_CopiesItsFieldsOutOfTheCallbackUnion()
  {
    using var env = new IsolatedAlpmEnvironment();
    var pkgDir = Path.Combine(Path.GetTempPath(), "pacpar-ignorepkg-" + Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(pkgDir);

    try
    {
      using var pkg = env.Alpm.LoadPackage(PackageArchive.Create(pkgDir, "ignored-pkg"), full: false, SigLevel.ALPM_SIG_USE_DEFAULT);

      var native = (_alpm_question_t*)NativeMemory.Alloc((nuint)sizeof(_alpm_question_t));
      try
      {
        native->type_ = _alpm_question_type_t.ALPM_QUESTION_INSTALL_IGNOREPKG;
        native->install_ignorepkg.install = 1;
        native->install_ignorepkg.pkg = pkg.BackingStruct;

        var payload = AlpmQuestion.FromUnion(native, env.Alpm.BindingConfig);

        *native = default;

        var question = Assert.IsType<AlpmQuestion.InstallIgnoredPackage>(payload);
        Assert.True(question.Install);
        Assert.Equal("ignored-pkg", question.Package.Name);
      }
      finally
      {
        NativeMemory.Free((void*)(nint)native);
      }
    }
    finally
    {
      if (Directory.Exists(pkgDir))
      {
        Directory.Delete(pkgDir, recursive: true);
      }
    }
  }

  [Fact]
  public void ConflictPackage_CopiesItsFieldsOutOfTheCallbackUnion()
  {
    using var env = new IsolatedAlpmEnvironment();
    var pkgDir = Path.Combine(Path.GetTempPath(), "pacpar-conflict-" + Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(pkgDir);

    try
    {
      using var pkg1 = env.Alpm.LoadPackage(PackageArchive.Create(pkgDir, "pkg-one"), full: false, SigLevel.ALPM_SIG_USE_DEFAULT);
      using var pkg2 = env.Alpm.LoadPackage(PackageArchive.Create(pkgDir, "pkg-two"), full: false, SigLevel.ALPM_SIG_USE_DEFAULT);

      var conflictStruct = (_alpm_conflict_t*)NativeMemory.Alloc((nuint)sizeof(_alpm_conflict_t));
      var dependStruct = (_alpm_depend_t*)NativeMemory.Alloc((nuint)sizeof(_alpm_depend_t));
      var nameBuf = NativeString.ToNative("dep-name");
      var verBuf = NativeString.ToNative("1.0");
      var descBuf = NativeString.ToNative("dep-desc");
      var native = (_alpm_question_t*)NativeMemory.Alloc((nuint)sizeof(_alpm_question_t));

      try
      {
        dependStruct->name = nameBuf;
        dependStruct->version = verBuf;
        dependStruct->desc = descBuf;

        conflictStruct->package1 = pkg1.BackingStruct;
        conflictStruct->package2 = pkg2.BackingStruct;
        conflictStruct->reason = dependStruct;

        native->type_ = _alpm_question_type_t.ALPM_QUESTION_CONFLICT_PKG;
        native->conflict.remove = 1;
        native->conflict.conflict = conflictStruct;

        var payload = AlpmQuestion.FromUnion(native, env.Alpm.BindingConfig);

        *native = default;
        *conflictStruct = default;
        *dependStruct = default;

        var conflict = Assert.IsType<AlpmQuestion.ConflictPkg>(payload);
        Assert.True(conflict.Remove);
        Assert.Equal("pkg-one", conflict.Package1.Name);
        Assert.Equal("pkg-two", conflict.Package2.Name);
        Assert.Equal("dep-name", conflict.Name);
        Assert.Equal("1.0", conflict.Version);
        Assert.Equal("dep-desc", conflict.Description);
      }
      finally
      {
        NativeMemory.Free((void*)(nint)nameBuf);
        NativeMemory.Free((void*)(nint)verBuf);
        NativeMemory.Free((void*)(nint)descBuf);
        NativeMemory.Free((void*)(nint)dependStruct);
        NativeMemory.Free((void*)(nint)conflictStruct);
        NativeMemory.Free((void*)(nint)native);
      }
    }
    finally
    {
      if (Directory.Exists(pkgDir))
      {
        Directory.Delete(pkgDir, recursive: true);
      }
    }
  }

  /// <summary>
  /// The member list of a question lives in the same callback-scoped memory as the union, so it is
  /// copied as well - and so are the packages it points at, which belong to the transaction or the
  /// database rather than to the callback.
  /// </summary>
  [Fact]
  public void QuestionPayload_CopiesTheMemberList()
  {
    using var env = new IsolatedAlpmEnvironment();
    var pkgDir = Path.Combine(Path.GetTempPath(), "pacpar-member-list-" + Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(pkgDir);

    try
    {
      using var pkg = env.Alpm.LoadPackage(PackageArchive.Create(pkgDir, "member-list"),
        full: false, SigLevel.ALPM_SIG_USE_DEFAULT);
      var native = (_alpm_question_t*)NativeMemory.Alloc((nuint)sizeof(_alpm_question_t));
      var members = NativeMethods.alpm_list_add(null, pkg.BackingStruct);

      try
      {
        native->type_ = _alpm_question_type_t.ALPM_QUESTION_REMOVE_PKGS;
        native->remove_pkgs.skip = 0;
        native->remove_pkgs.packages = members;

        // The REMOVE_PKGS branch traverses the member list and copies each package out of it,
        // so the resulting payload retains no native memory.
        var payload = AlpmQuestion.FromUnion(native, env.Alpm.BindingConfig);

        *native = default;
        NativeMethods.alpm_list_free(members);
        members = null;
        pkg.Dispose();

        var remove = Assert.IsType<AlpmQuestion.RemovePkgs>(payload);
        Assert.False(remove.Skip);
        Assert.Equal("member-list", Assert.Single(remove.Packages).Name);
      }
      finally
      {
        if (members != null) NativeMethods.alpm_list_free(members);
        NativeMemory.Free((void*)(nint)native);
      }
    }
    finally
    {
      if (Directory.Exists(pkgDir))
      {
        Directory.Delete(pkgDir, recursive: true);
      }
    }
  }

  [Fact]
  public void DownloadPayload_CopiesItsFieldsOutOfTheCallbackData()
  {
    var data = (_alpm_download_event_completed_t*)NativeMemory.Alloc((nuint)sizeof(_alpm_download_event_completed_t));

    try
    {
      data->total = new CLong(1024);
      data->result = 0;

      var payload = AlpmDownloadEvent.FromUnion(_alpm_download_event_type_t.ALPM_DOWNLOAD_COMPLETED, data);

      data->total = default;
      data->result = -1;

      var completed = Assert.IsType<AlpmDownloadEvent.Completed>(payload);
      Assert.Equal(1024L, completed.Total);
      Assert.Equal(0, completed.Result);
      Assert.True(completed.IsSuccessful);
      Assert.False(completed.IsError);
    }
    finally
    {
      NativeMemory.Free((void*)(nint)data);
    }
  }
}

using System.Runtime.InteropServices;
using System.Text;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm.Tests.Unit;

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
      var version = new Version(buffer);

      Scramble(buffer, "1.2.3-4");

      Assert.Equal("1.2.3-4", version.ToString());
    }
    finally
    {
      Marshal.FreeHGlobal((nint)buffer);
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
      var older = new Version(olderText);
      var newer = new Version(newerText);

      Scramble(olderText, "1.0-1");
      Marshal.FreeHGlobal((nint)olderText);
      olderText = null;

      Scramble(newerText, "1.0-2");
      Marshal.FreeHGlobal((nint)newerText);
      newerText = null;

      Assert.True(older.CompareTo(newer) < 0, "1.0-1 must sort before 1.0-2");
      Assert.True(newer.CompareTo(older) > 0);

      var sameText = NativeString.ToNative("1.0-1");
      try
      {
        Assert.Equal(0, older.CompareTo(new Version(sameText)));
        Assert.Equal(1, older.CompareTo(null));
      }
      finally
      {
        Marshal.FreeHGlobal((nint)sameText);
      }
    }
    finally
    {
      if (olderText != null) Marshal.FreeHGlobal((nint)olderText);
      if (newerText != null) Marshal.FreeHGlobal((nint)newerText);
    }
  }

  [Fact]
  public void Backup_CopiesItsName()
  {
    var name = NativeString.ToNative("etc/pacman.conf");
    var hash = NativeString.ToNative("deadbeef");
    var native = (_alpm_backup_t*)Marshal.AllocHGlobal(sizeof(_alpm_backup_t));

    try
    {
      native->name = name;
      native->hash = hash;

      var backup = Backup.Factory(native);

      Scramble(name, "etc/pacman.conf");
      native->name = null;

      Assert.Equal("etc/pacman.conf", backup.Name);
    }
    finally
    {
      Marshal.FreeHGlobal((nint)name);
      Marshal.FreeHGlobal((nint)hash);
      Marshal.FreeHGlobal((nint)native);
    }
  }

  [Fact]
  public void File_CopiesNameModeAndSize()
  {
    var name = NativeString.ToNative("usr/bin/probe");
    var native = (_alpm_file_t*)Marshal.AllocHGlobal(sizeof(_alpm_file_t));

    try
    {
      native->name = name;
      native->mode = 0b111_101_101;
      native->size = new CLong(4096);

      var file = File.Factory(native);

      Scramble(name, "usr/bin/probe");
      native->name = null;
      native->mode = 0;
      native->size = default;

      Assert.Equal("usr/bin/probe", file.Name);
      Assert.Equal(0b111_101_101u, file.Mode);
      Assert.Equal(4096L, file.Size.Value);
    }
    finally
    {
      Marshal.FreeHGlobal((nint)name);
      Marshal.FreeHGlobal((nint)native);
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
    var entries = (_alpm_file_t*)Marshal.AllocHGlobal(sizeof(_alpm_file_t) * names.Length);
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

      var list = new FileList(&fileList);

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
        if (buffer != null) Marshal.FreeHGlobal((nint)buffer);
      }

      Marshal.FreeHGlobal((nint)entries);
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

      var group = Group.Factory(&native);

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
      Marshal.FreeHGlobal((nint)name);
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
    var native = (_alpm_event_t*)Marshal.AllocHGlobal(sizeof(_alpm_event_t));

    try
    {
      native->type_ = _alpm_event_type_t.ALPM_EVENT_SCRIPTLET_INFO;
      native->scriptlet_info.line = line;

      var payload = EventType.FromUnion(native);

      // Exactly what libalpm does to the union as soon as the callback returns.
      *native = default;
      Marshal.FreeHGlobal((nint)line);
      line = null;

      Assert.Equal(":: running post-transaction hooks...", Assert.IsType<EventType.ScriptletInfo>(payload).Line);
    }
    finally
    {
      if (line != null) Marshal.FreeHGlobal((nint)line);
      Marshal.FreeHGlobal((nint)native);
    }
  }

  [Fact]
  public void EventPayload_CopiesScalarFieldsOutOfTheCallbackUnion()
  {
    var native = (_alpm_event_t*)Marshal.AllocHGlobal(sizeof(_alpm_event_t));

    try
    {
      native->type_ = _alpm_event_type_t.ALPM_EVENT_HOOK_RUN_START;
      native->hook_run.position = 2;
      native->hook_run.total = 5;

      var payload = EventType.FromUnion(native);

      *native = default;

      var hookRun = Assert.IsType<EventType.HookRunStart>(payload);
      Assert.Equal((nuint)2, hookRun.Position);
      Assert.Equal((nuint)5, hookRun.Total);
    }
    finally
    {
      Marshal.FreeHGlobal((nint)native);
    }
  }

  [Fact]
  public void QuestionPayload_CopiesItsFieldsOutOfTheCallbackUnion()
  {
    var oldPackage = NativeString.ToNative("pacpar-old");
    var newPackage = NativeString.ToNative("pacpar-new");
    var newDatabase = NativeString.ToNative("core");
    var native = (_alpm_question_t*)Marshal.AllocHGlobal(sizeof(_alpm_question_t));

    try
    {
      native->type_ = _alpm_question_type_t.ALPM_QUESTION_REPLACE_PKG;
      native->replace.replace = 1;
      native->replace.oldpkg = (_alpm_pkg_t*)oldPackage;
      native->replace.newpkg = (_alpm_pkg_t*)newPackage;
      native->replace.newdb = (_alpm_db_t*)newDatabase;

      var payload = QuestionType.FromUnion(native);

      *native = default;
      Marshal.FreeHGlobal((nint)oldPackage);
      oldPackage = null;
      Marshal.FreeHGlobal((nint)newPackage);
      newPackage = null;
      Marshal.FreeHGlobal((nint)newDatabase);
      newDatabase = null;

      var replace = Assert.IsType<QuestionType.ReplacePackage>(payload);
      Assert.True(replace.Replace);
      Assert.Equal("pacpar-old", replace.OldPackage);
      Assert.Equal("pacpar-new", replace.NewPackage);
      Assert.Equal("core", replace.NewDatabase);
    }
    finally
    {
      if (oldPackage != null) Marshal.FreeHGlobal((nint)oldPackage);
      if (newPackage != null) Marshal.FreeHGlobal((nint)newPackage);
      if (newDatabase != null) Marshal.FreeHGlobal((nint)newDatabase);
      Marshal.FreeHGlobal((nint)native);
    }
  }

  /// <summary>
  /// The member list of a question lives in the same callback-scoped memory as the union, so it is
  /// copied as well - a view would answer with an empty or dangling list here.
  /// </summary>
  [Fact]
  public void QuestionPayload_CopiesTheMemberList()
  {
    var native = (_alpm_question_t*)Marshal.AllocHGlobal(sizeof(_alpm_question_t));
    var members = NativeMethods.alpm_list_add(null, null);

    try
    {
      native->type_ = _alpm_question_type_t.ALPM_QUESTION_REMOVE_PKGS;
      native->remove_pkgs.skip = 0;
      native->remove_pkgs.packages = members;

      var payload = QuestionType.FromUnion(native);

      *native = default;
      NativeMethods.alpm_list_free(members);
      members = null;

      Assert.Single(Assert.IsType<QuestionType.RemovePkgs>(payload).Packages);
    }
    finally
    {
      if (members != null) NativeMethods.alpm_list_free(members);
      Marshal.FreeHGlobal((nint)native);
    }
  }

  [Fact]
  public void DownloadPayload_CopiesItsFieldsOutOfTheCallbackData()
  {
    var data = (_alpm_download_event_completed_t*)Marshal.AllocHGlobal(sizeof(_alpm_download_event_completed_t));

    try
    {
      data->total = new CLong(1024);
      data->result = 0;

      var payload = DownloadEventType.FromUnion(_alpm_download_event_type_t.ALPM_DOWNLOAD_COMPLETED, data);

      data->total = default;
      data->result = -1;

      var completed = Assert.IsType<DownloadEventType.Completed>(payload);
      Assert.Equal(1024L, completed.Total.Value);
      Assert.Equal(0, completed.Result);
      Assert.True(completed.IsSuccessful);
      Assert.False(completed.IsError);
    }
    finally
    {
      Marshal.FreeHGlobal((nint)data);
    }
  }
}

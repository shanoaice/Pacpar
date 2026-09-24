using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>When a hook runs (libalpm's <c>_alpm_hook_when_t</c>).</summary>
public enum HookWhen : uint
{
  PreTransaction = 1,
  PostTransaction = 2
}

/// <summary>The operation a package event describes (libalpm's <c>_alpm_package_operation_t</c>).</summary>
public enum PackageOperation : uint
{
  Install = 1,
  Upgrade = 2,
  Reinstall = 3,
  Downgrade = 4,
  Remove = 5
}

/// <summary>
/// An event libalpm reports to a callback handler (<c>alpm_event_t</c>).
/// </summary>
/// <remarks>
/// Every case is a managed snapshot: its fields are copied out of libalpm's union while the callback
/// runs, which is the only time that union exists (probed: libalpm builds it on the calling thread's
/// stack and overwrites it as soon as the callback returns, so a view read afterwards silently
/// returned zeros - report item F9). Keeping a case and reading it after the callback is therefore
/// safe, which the borrowed views this replaces were not.
/// <para>
/// The packages a case exposes remain <see cref="Package"/> views, because that is what a package
/// always is: libalpm owns it and this library reads through its accessors. They outlive the
/// callback (they belong to the transaction or a database), unlike the event union itself.
/// </para>
/// </remarks>
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public abstract class EventType
{
  internal static unsafe EventType FromUnion(_alpm_event_t* backingStruct)
  {
    return backingStruct->type_ switch
    {
      _alpm_event_type_t.ALPM_EVENT_CHECKDEPS_START => new CheckDepsStart(),
      _alpm_event_type_t.ALPM_EVENT_CHECKDEPS_DONE => new CheckDepsDone(),
      _alpm_event_type_t.ALPM_EVENT_FILECONFLICTS_START => new FileConflictsStart(),
      _alpm_event_type_t.ALPM_EVENT_FILECONFLICTS_DONE => new FileConflictsDone(),
      _alpm_event_type_t.ALPM_EVENT_RESOLVEDEPS_START => new ResolveDepsStart(),
      _alpm_event_type_t.ALPM_EVENT_RESOLVEDEPS_DONE => new ResolveDepsDone(),
      _alpm_event_type_t.ALPM_EVENT_INTERCONFLICTS_START => new InterConflictsStart(),
      _alpm_event_type_t.ALPM_EVENT_INTERCONFLICTS_DONE => new InterConflictsDone(),
      _alpm_event_type_t.ALPM_EVENT_TRANSACTION_START => new TransactionStart(),
      _alpm_event_type_t.ALPM_EVENT_TRANSACTION_DONE => new TransactionDone(),
      _alpm_event_type_t.ALPM_EVENT_PACKAGE_OPERATION_START => new PackageOperationStart(backingStruct),
      _alpm_event_type_t.ALPM_EVENT_PACKAGE_OPERATION_DONE => new PackageOperationDone(backingStruct),
      _alpm_event_type_t.ALPM_EVENT_INTEGRITY_START => new IntegrityStart(),
      _alpm_event_type_t.ALPM_EVENT_INTEGRITY_DONE => new IntegrityDone(),
      _alpm_event_type_t.ALPM_EVENT_LOAD_START => new LoadStart(),
      _alpm_event_type_t.ALPM_EVENT_LOAD_DONE => new LoadDone(),
      _alpm_event_type_t.ALPM_EVENT_SCRIPTLET_INFO => new ScriptletInfo(backingStruct),
      _alpm_event_type_t.ALPM_EVENT_DB_RETRIEVE_START => new RetrieveStart(),
      _alpm_event_type_t.ALPM_EVENT_DB_RETRIEVE_DONE => new RetrieveDone(),
      _alpm_event_type_t.ALPM_EVENT_DB_RETRIEVE_FAILED => new RetrieveFailed(),
      _alpm_event_type_t.ALPM_EVENT_DISKSPACE_START => new DiskSpaceStart(),
      _alpm_event_type_t.ALPM_EVENT_DISKSPACE_DONE => new DiskSpaceDone(),
      _alpm_event_type_t.ALPM_EVENT_OPTDEP_REMOVAL => new OptionalDependencyRemoval(backingStruct),
      _alpm_event_type_t.ALPM_EVENT_DATABASE_MISSING => new DatabaseMissing(backingStruct),
      _alpm_event_type_t.ALPM_EVENT_KEYRING_START => new KeyringStart(),
      _alpm_event_type_t.ALPM_EVENT_KEYRING_DONE => new KeyringDone(),
      _alpm_event_type_t.ALPM_EVENT_KEY_DOWNLOAD_START => new KeyDownloadStart(),
      _alpm_event_type_t.ALPM_EVENT_KEY_DOWNLOAD_DONE => new KeyDownloadDone(),
      _alpm_event_type_t.ALPM_EVENT_PACNEW_CREATED => new PacnewCreated(backingStruct),
      _alpm_event_type_t.ALPM_EVENT_PACSAVE_CREATED => new PacsaveCreated(backingStruct),
      _alpm_event_type_t.ALPM_EVENT_HOOK_START => new HookStart(backingStruct),
      _alpm_event_type_t.ALPM_EVENT_HOOK_DONE => new HookDone(backingStruct),
      _alpm_event_type_t.ALPM_EVENT_HOOK_RUN_START => new HookRunStart(backingStruct),
      _alpm_event_type_t.ALPM_EVENT_HOOK_RUN_DONE => new HookRunDone(backingStruct),
      _alpm_event_type_t.ALPM_EVENT_PKG_RETRIEVE_START => new PackageRetrieveStart(backingStruct),
      _alpm_event_type_t.ALPM_EVENT_PKG_RETRIEVE_DONE => new PackageRetrieveDone(backingStruct),
      _alpm_event_type_t.ALPM_EVENT_PKG_RETRIEVE_FAILED => new PackageRetrieveFailed(backingStruct),
      _ => throw new ArgumentException($"Unknown event type: {backingStruct->type_}"),
    };
  }

  public class CheckDepsStart : EventType
  {
  }

  public class CheckDepsDone : EventType
  {
  }

  public class FileConflictsStart : EventType
  {
  }

  public class FileConflictsDone : EventType
  {
  }

  public class ResolveDepsStart : EventType
  {
  }

  public class ResolveDepsDone : EventType
  {
  }

  public class InterConflictsStart : EventType
  {
  }

  public class InterConflictsDone : EventType
  {
  }

  public class TransactionStart : EventType
  {
  }

  public class TransactionDone : EventType
  {
  }

  public class PackageOperationStart : EventType
  {
    internal unsafe PackageOperationStart(_alpm_event_t* native)
    {
      NewPackage = new Package(native->package_operation.newpkg);
      OldPackage = new Package(native->package_operation.oldpkg);
      Operation = (PackageOperation)(uint)native->package_operation.operation;
    }

    public Package NewPackage { get; }
    public Package OldPackage { get; }
    public PackageOperation Operation { get; }
  }

  public class PackageOperationDone : EventType
  {
    internal unsafe PackageOperationDone(_alpm_event_t* native)
    {
      NewPackage = new Package(native->package_operation.newpkg);
      OldPackage = new Package(native->package_operation.oldpkg);
      Operation = (PackageOperation)(uint)native->package_operation.operation;
    }

    public Package NewPackage { get; }
    public Package OldPackage { get; }
    public PackageOperation Operation { get; }
  }

  public class IntegrityStart : EventType
  {
  }

  public class IntegrityDone : EventType
  {
  }

  public class LoadStart : EventType
  {
  }

  public class LoadDone : EventType
  {
  }

  public class ScriptletInfo : EventType
  {
    internal unsafe ScriptletInfo(_alpm_event_t* native)
    {
      Line = NativeString.FromNative((nint)native->scriptlet_info.line) ?? "";
    }

    public string Line { get; }
  }

  public class RetrieveStart : EventType
  {
  }

  public class RetrieveDone : EventType
  {
  }

  public class RetrieveFailed : EventType
  {
  }

  public class DiskSpaceStart : EventType
  {
  }

  public class DiskSpaceDone : EventType
  {
  }

  public class OptionalDependencyRemoval : EventType
  {
    internal unsafe OptionalDependencyRemoval(_alpm_event_t* native)
    {
      OptionalDependency = new Depend(native->optdep_removal.optdep);
      Package = new Package(native->optdep_removal.pkg);
    }

    public Depend OptionalDependency { get; }
    public Package Package { get; }
  }

  public class DatabaseMissing : EventType
  {
    internal unsafe DatabaseMissing(_alpm_event_t* native)
    {
      DatabaseName = NativeString.FromNative((nint)native->database_missing.dbname) ?? "";
    }

    public string DatabaseName { get; }
  }

  public class KeyringStart : EventType
  {
  }

  public class KeyringDone : EventType
  {
  }

  public class KeyDownloadStart : EventType
  {
  }

  public class KeyDownloadDone : EventType
  {
  }

  public class PacnewCreated : EventType
  {
    internal unsafe PacnewCreated(_alpm_event_t* native)
    {
      FromNoUpgrade = native->pacnew_created.from_noupgrade != 0;
      OldPackage = new Package(native->pacnew_created.oldpkg);
      NewPackage = new Package(native->pacnew_created.newpkg);
      File = NativeString.FromNative((nint)native->pacnew_created.file) ?? "";
    }

    public bool FromNoUpgrade { get; }
    public Package OldPackage { get; }
    public Package NewPackage { get; }
    public string File { get; }
  }

  public class PacsaveCreated : EventType
  {
    internal unsafe PacsaveCreated(_alpm_event_t* native)
    {
      OldPackage = new Package(native->pacsave_created.oldpkg);
      File = NativeString.FromNative((nint)native->pacsave_created.file) ?? "";
    }

    public Package OldPackage { get; }
    public string File { get; }
  }

  public class HookStart : EventType
  {
    internal unsafe HookStart(_alpm_event_t* native)
    {
      When = (HookWhen)(uint)native->hook.when;
    }

    public HookWhen When { get; }
  }

  public class HookDone : EventType
  {
    internal unsafe HookDone(_alpm_event_t* native)
    {
      When = (HookWhen)(uint)native->hook.when;
    }

    public HookWhen When { get; }
  }

  public class HookRunStart : EventType
  {
    internal unsafe HookRunStart(_alpm_event_t* native)
    {
      Name = NativeString.FromNative((nint)native->hook_run.name) ?? "";
      Description = NativeString.FromNative((nint)native->hook_run.desc) ?? "";
      Position = native->hook_run.position;
      Total = native->hook_run.total;
    }

    public string Name { get; }
    public string Description { get; }
    public nuint Position { get; }
    public nuint Total { get; }
  }

  public class HookRunDone : EventType
  {
    internal unsafe HookRunDone(_alpm_event_t* native)
    {
      Name = NativeString.FromNative((nint)native->hook_run.name) ?? "";
      Description = NativeString.FromNative((nint)native->hook_run.desc) ?? "";
      Position = native->hook_run.position;
      Total = native->hook_run.total;
    }

    public string Name { get; }
    public string Description { get; }
    public nuint Position { get; }
    public nuint Total { get; }
  }

  public class PackageRetrieveStart : EventType
  {
    internal unsafe PackageRetrieveStart(_alpm_event_t* native)
    {
      PackageCount = native->pkg_retrieve.num;
      TotalSize = native->pkg_retrieve.total_size;
    }

    public nuint PackageCount { get; }
    public CLong TotalSize { get; }
  }

  public class PackageRetrieveDone : EventType
  {
    internal unsafe PackageRetrieveDone(_alpm_event_t* native)
    {
      PackageCount = native->pkg_retrieve.num;
      TotalSize = native->pkg_retrieve.total_size;
    }

    public nuint PackageCount { get; }
    public CLong TotalSize { get; }
  }

  public class PackageRetrieveFailed : EventType
  {
    internal unsafe PackageRetrieveFailed(_alpm_event_t* native)
    {
      PackageCount = native->pkg_retrieve.num;
      TotalSize = native->pkg_retrieve.total_size;
    }

    public nuint PackageCount { get; }
    public CLong TotalSize { get; }
  }
}

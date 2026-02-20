using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public abstract class EventType
{
  public static unsafe EventType FromUnion(_alpm_event_t* backingStruct)
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

  public unsafe class PackageOperationStart(_alpm_event_t* backingStruct) : EventType
  {
    public Package NewPackage => new(backingStruct->package_operation.newpkg);
    public Package OldPackage => new(backingStruct->package_operation.oldpkg);
    public _alpm_package_operation_t Operation => backingStruct->package_operation.operation;
  }

  public unsafe class PackageOperationDone(_alpm_event_t* backingStruct) : EventType
  {
    public Package NewPackage => new(backingStruct->package_operation.newpkg);
    public Package OldPackage => new(backingStruct->package_operation.oldpkg);
    public _alpm_package_operation_t Operation => backingStruct->package_operation.operation;
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

  public unsafe class ScriptletInfo(_alpm_event_t* backingStruct) : EventType
  {
    public string Line => field ??= (Marshal.PtrToStringAnsi((nint)backingStruct->scriptlet_info.line) ?? "");
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

  public unsafe class OptionalDependencyRemoval(_alpm_event_t* backingStruct) : EventType
  {
    public Depend OptionalDependency => new(backingStruct->optdep_removal.optdep);
    public Package Package => new(backingStruct->optdep_removal.pkg);
  }

  public unsafe class DatabaseMissing(_alpm_event_t* backingStruct) : EventType
  {
    public string DatabaseName =>
      field ??= (Marshal.PtrToStringAnsi((nint)backingStruct->database_missing.dbname) ?? "");
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

  public unsafe class PacnewCreated(_alpm_event_t* backingStruct) : EventType
  {
    public bool FromNoUpgrade => backingStruct->pacnew_created.from_noupgrade != 0;
    public Package OldPackage => new(backingStruct->pacnew_created.oldpkg);
    public Package NewPackage => new(backingStruct->pacnew_created.newpkg);
    public string File => field ??= (Marshal.PtrToStringAnsi((nint)backingStruct->pacnew_created.file) ?? "");
  }

  public unsafe class PacsaveCreated(_alpm_event_t* backingStruct) : EventType
  {
    public Package OldPackage => new(backingStruct->pacsave_created.oldpkg);
    public string File => field ??= (Marshal.PtrToStringAnsi((nint)backingStruct->pacsave_created.file) ?? "");
  }

  public unsafe class HookStart(_alpm_event_t* backingStruct) : EventType
  {
    public _alpm_hook_when_t When => backingStruct->hook.when;
  }

  public unsafe class HookDone(_alpm_event_t* backingStruct) : EventType
  {
    public _alpm_hook_when_t When => backingStruct->hook.when;
  }

  public unsafe class HookRunStart(_alpm_event_t* backingStruct) : EventType
  {
    public string Name => field ??= (Marshal.PtrToStringAnsi((nint)backingStruct->hook_run.name) ?? "");
    public string Description => field ??= (Marshal.PtrToStringAnsi((nint)backingStruct->hook_run.desc) ?? "");
    public nuint Position => backingStruct->hook_run.position;
    public nuint Total => backingStruct->hook_run.total;
  }

  public unsafe class HookRunDone(_alpm_event_t* backingStruct) : EventType
  {
    public string Name => field ??= (Marshal.PtrToStringAnsi((nint)backingStruct->hook_run.name) ?? "");
    public string Description => field ??= (Marshal.PtrToStringAnsi((nint)backingStruct->hook_run.desc) ?? "");
    public nuint Position => backingStruct->hook_run.position;
    public nuint Total => backingStruct->hook_run.total;
  }

  public unsafe class PackageRetrieveStart(_alpm_event_t* backingStruct) : EventType
  {
    public nuint PackageCount => backingStruct->pkg_retrieve.num;
    public CLong TotalSize => backingStruct->pkg_retrieve.total_size;
  }

  public unsafe class PackageRetrieveDone(_alpm_event_t* backingStruct) : EventType
  {
    public nuint PackageCount => backingStruct->pkg_retrieve.num;
    public CLong TotalSize => backingStruct->pkg_retrieve.total_size;
  }

  public unsafe class PackageRetrieveFailed(_alpm_event_t* backingStruct) : EventType
  {
    public nuint PackageCount => backingStruct->pkg_retrieve.num;
    public CLong TotalSize => backingStruct->pkg_retrieve.total_size;
  }
}

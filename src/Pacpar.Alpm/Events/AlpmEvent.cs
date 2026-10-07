using System.Diagnostics.CodeAnalysis;
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
/// An event reported by libalpm to an event callback handler (<c>alpm_event_t</c>).
/// </summary>
/// <remarks>
/// Event payloads carrying package views are borrowed references valid only for the duration of the
/// callback execution frame. Callers wishing to retain package data after the callback returns must
/// call <see cref="PackageBase.ToSnapshot"/> to create an independent managed snapshot.
/// </remarks>
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public abstract class AlpmEvent
{
  internal static unsafe AlpmEvent FromUnion(_alpm_event_t* backingStruct, ChildLifetime? lifetime)
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
      _alpm_event_type_t.ALPM_EVENT_PACKAGE_OPERATION_START => new PackageOperationStart(backingStruct, lifetime),
      _alpm_event_type_t.ALPM_EVENT_PACKAGE_OPERATION_DONE => new PackageOperationDone(backingStruct, lifetime),
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
      _alpm_event_type_t.ALPM_EVENT_OPTDEP_REMOVAL => new OptionalDependencyRemoval(backingStruct, lifetime),
      _alpm_event_type_t.ALPM_EVENT_DATABASE_MISSING => new DatabaseMissing(backingStruct),
      _alpm_event_type_t.ALPM_EVENT_KEYRING_START => new KeyringStart(),
      _alpm_event_type_t.ALPM_EVENT_KEYRING_DONE => new KeyringDone(),
      _alpm_event_type_t.ALPM_EVENT_KEY_DOWNLOAD_START => new KeyDownloadStart(),
      _alpm_event_type_t.ALPM_EVENT_KEY_DOWNLOAD_DONE => new KeyDownloadDone(),
      _alpm_event_type_t.ALPM_EVENT_PACNEW_CREATED => new PacnewCreated(backingStruct, lifetime),
      _alpm_event_type_t.ALPM_EVENT_PACSAVE_CREATED => new PacsaveCreated(backingStruct, lifetime),
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

  /// <summary>Triggered when dependency checking begins.</summary>
  public class CheckDepsStart : AlpmEvent;

  /// <summary>Triggered when dependency checking completes.</summary>
  public class CheckDepsDone : AlpmEvent;

  /// <summary>Triggered when file conflict checking begins.</summary>
  public class FileConflictsStart : AlpmEvent;

  /// <summary>Triggered when file conflict checking completes.</summary>
  public class FileConflictsDone : AlpmEvent;

  /// <summary>Triggered when dependency resolution begins.</summary>
  public class ResolveDepsStart : AlpmEvent;

  /// <summary>Triggered when dependency resolution completes.</summary>
  public class ResolveDepsDone : AlpmEvent;

  /// <summary>Triggered when inter-package conflict checking begins.</summary>
  public class InterConflictsStart : AlpmEvent;

  /// <summary>Triggered when inter-package conflict checking completes.</summary>
  public class InterConflictsDone : AlpmEvent;

  /// <summary>Triggered when transaction processing begins.</summary>
  public class TransactionStart : AlpmEvent;

  /// <summary>Triggered when transaction processing completes.</summary>
  public class TransactionDone : AlpmEvent;

  /// <summary>Triggered when a package operation (install, upgrade, downgrade, or remove) begins.</summary>
  public class PackageOperationStart : AlpmEvent
  {
    internal unsafe PackageOperationStart(_alpm_event_t* native, ChildLifetime? lifetime)
    {
      var newpkg = native->package_operation.newpkg;
      var oldpkg = native->package_operation.oldpkg;
      NewPackage = newpkg == null ? null : new PackageView(newpkg, lifetime);
      OldPackage = oldpkg == null ? null : new PackageView(oldpkg, lifetime);
      Operation = (PackageOperation)(uint)native->package_operation.operation;
    }

    /// <summary>Gets the package being installed or upgraded to, or <c>null</c> if this operation is a package removal.</summary>
    public PackageView? NewPackage { get; }
    /// <summary>Gets the package being upgraded from or removed, or <c>null</c> if this operation is a package installation.</summary>
    public PackageView? OldPackage { get; }
    /// <summary>Gets the type of package operation being performed.</summary>
    public PackageOperation Operation { get; }
  }

  /// <summary>Triggered when a package operation completes.</summary>
  public class PackageOperationDone : AlpmEvent
  {
    internal unsafe PackageOperationDone(_alpm_event_t* native, ChildLifetime? lifetime)
    {
      var newpkg = native->package_operation.newpkg;
      var oldpkg = native->package_operation.oldpkg;
      NewPackage = newpkg == null ? null : new PackageView(newpkg, lifetime);
      OldPackage = oldpkg == null ? null : new PackageView(oldpkg, lifetime);
      Operation = (PackageOperation)(uint)native->package_operation.operation;
    }

    /// <summary>Gets the package being installed or upgraded to, or <c>null</c> if this operation is a package removal.</summary>
    public PackageView? NewPackage { get; }
    /// <summary>Gets the package being upgraded from or removed, or <c>null</c> if this operation is a package installation.</summary>
    public PackageView? OldPackage { get; }
    /// <summary>Gets the type of package operation that completed.</summary>
    public PackageOperation Operation { get; }
  }

  /// <summary>Triggered when package integrity checking begins.</summary>
  public class IntegrityStart : AlpmEvent;

  /// <summary>Triggered when package integrity checking completes.</summary>
  public class IntegrityDone : AlpmEvent;

  /// <summary>Triggered when package loading begins.</summary>
  public class LoadStart : AlpmEvent;

  /// <summary>Triggered when package loading completes.</summary>
  public class LoadDone : AlpmEvent;

  /// <summary>Carries output from an install or remove scriptlet.</summary>
  public class ScriptletInfo : AlpmEvent
  {
    internal unsafe ScriptletInfo(_alpm_event_t* native)
    {
      Line = NativeString.FromNative((nint)native->scriptlet_info.line) ?? "";
    }

    /// <summary>Gets the output line emitted by the scriptlet.</summary>
    public string Line { get; }
  }

  /// <summary>Triggered when package file retrieval begins.</summary>
  public class RetrieveStart : AlpmEvent;

  /// <summary>Triggered when package file retrieval completes successfully.</summary>
  public class RetrieveDone : AlpmEvent;

  /// <summary>Triggered when package file retrieval fails.</summary>
  public class RetrieveFailed : AlpmEvent;

  /// <summary>Triggered when disk space checking begins.</summary>
  public class DiskSpaceStart : AlpmEvent;

  /// <summary>Triggered when disk space checking completes.</summary>
  public class DiskSpaceDone : AlpmEvent;

  /// <summary>Triggered when an optional dependency is being removed.</summary>
  public class OptionalDependencyRemoval : AlpmEvent
  {
    internal unsafe OptionalDependencyRemoval(_alpm_event_t* native, ChildLifetime? lifetime)
    {
      OptionalDependency = new Depend(native->optdep_removal.optdep);
      Package = new PackageView(native->optdep_removal.pkg, lifetime);
    }

    /// <summary>Gets the optional dependency being removed.</summary>
    public Depend OptionalDependency { get; }
    /// <summary>Gets the package that referenced the optional dependency.</summary>
    public PackageView Package { get; }
  }

  /// <summary>Triggered when a requested sync database file is missing.</summary>
  public class DatabaseMissing : AlpmEvent
  {
    internal unsafe DatabaseMissing(_alpm_event_t* native)
    {
      DatabaseName = NativeString.FromNative((nint)native->database_missing.dbname) ?? "";
    }

    /// <summary>Gets the name of the missing database.</summary>
    public string DatabaseName { get; }
  }

  /// <summary>Triggered when keyring verification or initialization begins.</summary>
  public class KeyringStart : AlpmEvent;

  /// <summary>Triggered when keyring verification or initialization completes.</summary>
  public class KeyringDone : AlpmEvent;

  /// <summary>Triggered when downloading a PGP key begins.</summary>
  public class KeyDownloadStart : AlpmEvent;

  /// <summary>Triggered when downloading a PGP key completes.</summary>
  public class KeyDownloadDone : AlpmEvent;

  /// <summary>Triggered when a .pacnew configuration file is created during package extraction.</summary>
  public class PacnewCreated : AlpmEvent
  {
    internal unsafe PacnewCreated(_alpm_event_t* native, ChildLifetime? lifetime)
    {
      var oldpkg = native->pacnew_created.oldpkg;
      var newpkg = native->pacnew_created.newpkg;
      FromNoUpgrade = native->pacnew_created.from_noupgrade != 0;
      OldPackage = oldpkg == null ? null : new PackageView(oldpkg, lifetime);
      NewPackage = newpkg == null ? null : new PackageView(newpkg, lifetime);
      File = NativeString.FromNative((nint)native->pacnew_created.file) ?? "";
    }

    /// <summary>Gets whether the .pacnew file was generated due to a NoUpgrade directive.</summary>
    public bool FromNoUpgrade { get; }
    /// <summary>Gets the existing installed package view, if available.</summary>
    public PackageView? OldPackage { get; }
    /// <summary>Gets the new package view providing the updated file, if available.</summary>
    public PackageView? NewPackage { get; }
    /// <summary>Gets the filesystem path to the file.</summary>
    public string File { get; }
  }

  /// <summary>Triggered when a .pacsave backup file is created during package removal.</summary>
  public class PacsaveCreated : AlpmEvent
  {
    internal unsafe PacsaveCreated(_alpm_event_t* native, ChildLifetime? lifetime)
    {
      var oldpkg = native->pacsave_created.oldpkg;
      OldPackage = oldpkg == null ? null : new PackageView(oldpkg, lifetime);
      File = NativeString.FromNative((nint)native->pacsave_created.file) ?? "";
    }

    /// <summary>Gets the package being removed that owned the file, if available.</summary>
    public PackageView? OldPackage { get; }
    /// <summary>Gets the filesystem path to the file.</summary>
    public string File { get; }
  }

  /// <summary>Triggered when transaction hooks begin executing.</summary>
  public class HookStart : AlpmEvent
  {
    internal unsafe HookStart(_alpm_event_t* native)
    {
      When = (HookWhen)(uint)native->hook.when;
    }

    /// <summary>Gets the hook execution stage (pre- or post-transaction).</summary>
    public HookWhen When { get; }
  }

  /// <summary>Triggered when transaction hooks finish executing.</summary>
  public class HookDone : AlpmEvent
  {
    internal unsafe HookDone(_alpm_event_t* native)
    {
      When = (HookWhen)(uint)native->hook.when;
    }

    /// <summary>Gets the hook execution stage (pre- or post-transaction).</summary>
    public HookWhen When { get; }
  }

  /// <summary>Triggered when a specific hook starts running.</summary>
  public class HookRunStart : AlpmEvent
  {
    internal unsafe HookRunStart(_alpm_event_t* native)
    {
      Name = NativeString.FromNative((nint)native->hook_run.name) ?? "";
      Description = NativeString.FromNative((nint)native->hook_run.desc) ?? "";
      Position = native->hook_run.position;
      Total = native->hook_run.total;
    }

    /// <summary>Gets the name of the hook.</summary>
    public string Name { get; }
    /// <summary>Gets the description of the hook, if available.</summary>
    public string Description { get; }
    /// <summary>Gets the 1-based index of the running hook within the current stage.</summary>
    public nuint Position { get; }
    /// <summary>Gets the total number of hooks scheduled to run in the current stage.</summary>
    public nuint Total { get; }
  }

  /// <summary>Triggered when a specific hook finishes running.</summary>
  public class HookRunDone : AlpmEvent
  {
    internal unsafe HookRunDone(_alpm_event_t* native)
    {
      Name = NativeString.FromNative((nint)native->hook_run.name) ?? "";
      Description = NativeString.FromNative((nint)native->hook_run.desc) ?? "";
      Position = native->hook_run.position;
      Total = native->hook_run.total;
    }

    /// <summary>Gets the name of the hook.</summary>
    public string Name { get; }
    /// <summary>Gets the description of the hook, if available.</summary>
    public string Description { get; }
    /// <summary>Gets the 1-based index of the completed hook within the current stage.</summary>
    public nuint Position { get; }
    /// <summary>Gets the total number of hooks scheduled to run in the current stage.</summary>
    public nuint Total { get; }
  }

  /// <summary>Triggered when package retrieval for a transaction begins.</summary>
  public class PackageRetrieveStart : AlpmEvent
  {
    internal unsafe PackageRetrieveStart(_alpm_event_t* native)
    {
      PackageCount = native->pkg_retrieve.num;
      TotalSize = native->pkg_retrieve.total_size.Value;
    }

    /// <summary>Gets the number of packages to be retrieved.</summary>
    public nuint PackageCount { get; }
    /// <summary>Gets the total download size in bytes across all packages to be retrieved.</summary>
    public long TotalSize { get; }
  }

  /// <summary>Triggered when package retrieval for a transaction completes successfully.</summary>
  public class PackageRetrieveDone : AlpmEvent
  {
    internal unsafe PackageRetrieveDone(_alpm_event_t* native)
    {
      PackageCount = native->pkg_retrieve.num;
      TotalSize = native->pkg_retrieve.total_size.Value;
    }

    /// <summary>Gets the number of packages retrieved.</summary>
    public nuint PackageCount { get; }
    /// <summary>Gets the total download size in bytes across all retrieved packages.</summary>
    public long TotalSize { get; }
  }

  /// <summary>Triggered when package retrieval for a transaction fails.</summary>
  public class PackageRetrieveFailed : AlpmEvent
  {
    internal unsafe PackageRetrieveFailed(_alpm_event_t* native)
    {
      PackageCount = native->pkg_retrieve.num;
      TotalSize = native->pkg_retrieve.total_size.Value;
    }

    /// <summary>Gets the number of packages that were to be retrieved.</summary>
    public nuint PackageCount { get; }
    /// <summary>Gets the total download size in bytes across all packages that were to be retrieved.</summary>
    public long TotalSize { get; }
  }
}

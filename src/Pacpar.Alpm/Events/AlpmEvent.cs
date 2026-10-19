using System.Diagnostics.CodeAnalysis;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>When a hook runs (libalpm's <c>_alpm_hook_when_t</c>).</summary>
/// <remarks>
/// A projection of libalpm's own numbering, which starts at 1, so there is no zero member.
/// ADR 0014.
/// </remarks>
public enum HookWhen : uint
{
  /// <summary>Before the transaction runs (<c>ALPM_HOOK_PRE_TRANSACTION</c>).</summary>
  PreTransaction = 1,

  /// <summary>After the transaction runs (<c>ALPM_HOOK_POST_TRANSACTION</c>).</summary>
  PostTransaction = 2
}

/// <summary>The operation a package event describes (libalpm's <c>_alpm_package_operation_t</c>).</summary>
/// <remarks>
/// A projection of libalpm's own numbering, which starts at 1, so there is no zero member.
/// ADR 0014.
/// </remarks>
public enum PackageOperation : uint
{
  /// <summary>The package is being installed (<c>ALPM_PACKAGE_INSTALL</c>).</summary>
  Install = 1,

  /// <summary>The package is being upgraded (<c>ALPM_PACKAGE_UPGRADE</c>).</summary>
  Upgrade = 2,

  /// <summary>The package is being reinstalled (<c>ALPM_PACKAGE_REINSTALL</c>).</summary>
  Reinstall = 3,

  /// <summary>The package is being downgraded (<c>ALPM_PACKAGE_DOWNGRADE</c>).</summary>
  Downgrade = 4,

  /// <summary>The package is being removed (<c>ALPM_PACKAGE_REMOVE</c>).</summary>
  Remove = 5
}

/// <summary>
/// An event reported by libalpm to an event callback handler (<c>alpm_event_t</c>).
/// </summary>
/// <remarks>
/// Event payloads carrying package views are borrowed references valid only for the duration of the
/// callback execution frame. Callers wishing to retain package data after the callback returns must
/// call <see cref="PackageBase.ToSnapshot"/> to create an independent managed snapshot.
/// <para>
/// The set of subclasses is closed: it mirrors <c>_alpm_event_type_t</c>, and
/// <c>FromUnion</c> is the only thing that creates one - every subclass is sealed, so the union
/// cannot grow a member libalpm does not have.
/// </para>
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
  public sealed class CheckDepsStart : AlpmEvent
  {
    internal CheckDepsStart() { }
  }

  /// <summary>Triggered when dependency checking completes.</summary>
  public sealed class CheckDepsDone : AlpmEvent
  {
    internal CheckDepsDone() { }
  }

  /// <summary>Triggered when file conflict checking begins.</summary>
  public sealed class FileConflictsStart : AlpmEvent
  {
    internal FileConflictsStart() { }
  }

  /// <summary>Triggered when file conflict checking completes.</summary>
  public sealed class FileConflictsDone : AlpmEvent
  {
    internal FileConflictsDone() { }
  }

  /// <summary>Triggered when dependency resolution begins.</summary>
  public sealed class ResolveDepsStart : AlpmEvent
  {
    internal ResolveDepsStart() { }
  }

  /// <summary>Triggered when dependency resolution completes.</summary>
  public sealed class ResolveDepsDone : AlpmEvent
  {
    internal ResolveDepsDone() { }
  }

  /// <summary>Triggered when inter-package conflict checking begins.</summary>
  public sealed class InterConflictsStart : AlpmEvent
  {
    internal InterConflictsStart() { }
  }

  /// <summary>Triggered when inter-package conflict checking completes.</summary>
  public sealed class InterConflictsDone : AlpmEvent
  {
    internal InterConflictsDone() { }
  }

  /// <summary>Triggered when transaction processing begins.</summary>
  public sealed class TransactionStart : AlpmEvent
  {
    internal TransactionStart() { }
  }

  /// <summary>Triggered when transaction processing completes.</summary>
  public sealed class TransactionDone : AlpmEvent
  {
    internal TransactionDone() { }
  }

  /// <summary>Triggered when a package operation (install, upgrade, downgrade, or remove) begins.</summary>
  public sealed class PackageOperationStart : AlpmEvent
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
  public sealed class PackageOperationDone : AlpmEvent
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
  public sealed class IntegrityStart : AlpmEvent
  {
    internal IntegrityStart() { }
  }

  /// <summary>Triggered when package integrity checking completes.</summary>
  public sealed class IntegrityDone : AlpmEvent
  {
    internal IntegrityDone() { }
  }

  /// <summary>Triggered when package loading begins.</summary>
  public sealed class LoadStart : AlpmEvent
  {
    internal LoadStart() { }
  }

  /// <summary>Triggered when package loading completes.</summary>
  public sealed class LoadDone : AlpmEvent
  {
    internal LoadDone() { }
  }

  /// <summary>Carries output from an install or remove scriptlet.</summary>
  public sealed class ScriptletInfo : AlpmEvent
  {
    internal unsafe ScriptletInfo(_alpm_event_t* native)
    {
      Line = NativeString.FromNative((nint)native->scriptlet_info.line) ?? "";
    }

    /// <summary>Gets the output line emitted by the scriptlet.</summary>
    public string Line { get; }
  }

  /// <summary>Triggered when package file retrieval begins.</summary>
  public sealed class RetrieveStart : AlpmEvent
  {
    internal RetrieveStart() { }
  }

  /// <summary>Triggered when package file retrieval completes successfully.</summary>
  public sealed class RetrieveDone : AlpmEvent
  {
    internal RetrieveDone() { }
  }

  /// <summary>Triggered when package file retrieval fails.</summary>
  public sealed class RetrieveFailed : AlpmEvent
  {
    internal RetrieveFailed() { }
  }

  /// <summary>Triggered when disk space checking begins.</summary>
  public sealed class DiskSpaceStart : AlpmEvent
  {
    internal DiskSpaceStart() { }
  }

  /// <summary>Triggered when disk space checking completes.</summary>
  public sealed class DiskSpaceDone : AlpmEvent
  {
    internal DiskSpaceDone() { }
  }

  /// <summary>Triggered when an optional dependency is being removed.</summary>
  public sealed class OptionalDependencyRemoval : AlpmEvent
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
  public sealed class DatabaseMissing : AlpmEvent
  {
    internal unsafe DatabaseMissing(_alpm_event_t* native)
    {
      DatabaseName = NativeString.FromNative((nint)native->database_missing.dbname) ?? "";
    }

    /// <summary>Gets the name of the missing database.</summary>
    public string DatabaseName { get; }
  }

  /// <summary>Triggered when keyring verification or initialization begins.</summary>
  public sealed class KeyringStart : AlpmEvent
  {
    internal KeyringStart() { }
  }

  /// <summary>Triggered when keyring verification or initialization completes.</summary>
  public sealed class KeyringDone : AlpmEvent
  {
    internal KeyringDone() { }
  }

  /// <summary>Triggered when downloading a PGP key begins.</summary>
  public sealed class KeyDownloadStart : AlpmEvent
  {
    internal KeyDownloadStart() { }
  }

  /// <summary>Triggered when downloading a PGP key completes.</summary>
  public sealed class KeyDownloadDone : AlpmEvent
  {
    internal KeyDownloadDone() { }
  }

  /// <summary>Triggered when a .pacnew configuration file is created during package extraction.</summary>
  public sealed class PacnewCreated : AlpmEvent
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
  public sealed class PacsaveCreated : AlpmEvent
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
  public sealed class HookStart : AlpmEvent
  {
    internal unsafe HookStart(_alpm_event_t* native)
    {
      When = (HookWhen)(uint)native->hook.when;
    }

    /// <summary>Gets the hook execution stage (pre- or post-transaction).</summary>
    public HookWhen When { get; }
  }

  /// <summary>Triggered when transaction hooks finish executing.</summary>
  public sealed class HookDone : AlpmEvent
  {
    internal unsafe HookDone(_alpm_event_t* native)
    {
      When = (HookWhen)(uint)native->hook.when;
    }

    /// <summary>Gets the hook execution stage (pre- or post-transaction).</summary>
    public HookWhen When { get; }
  }

  /// <summary>Triggered when a specific hook starts running.</summary>
  public sealed class HookRunStart : AlpmEvent
  {
    internal unsafe HookRunStart(_alpm_event_t* native)
    {
      Name = NativeString.FromNative((nint)native->hook_run.name) ?? "";
      Description = NativeString.FromNative((nint)native->hook_run.desc) ?? "";
      Position = (int)native->hook_run.position;
      Total = (int)native->hook_run.total;
    }

    /// <summary>Gets the name of the hook.</summary>
    public string Name { get; }
    /// <summary>Gets the description of the hook, if available.</summary>
    public string Description { get; }
    /// <summary>Gets the 1-based index of the running hook within the current stage.</summary>
    public int Position { get; }
    /// <summary>Gets the total number of hooks scheduled to run in the current stage.</summary>
    public int Total { get; }
  }

  /// <summary>Triggered when a specific hook finishes running.</summary>
  public sealed class HookRunDone : AlpmEvent
  {
    internal unsafe HookRunDone(_alpm_event_t* native)
    {
      Name = NativeString.FromNative((nint)native->hook_run.name) ?? "";
      Description = NativeString.FromNative((nint)native->hook_run.desc) ?? "";
      Position = (int)native->hook_run.position;
      Total = (int)native->hook_run.total;
    }

    /// <summary>Gets the name of the hook.</summary>
    public string Name { get; }
    /// <summary>Gets the description of the hook, if available.</summary>
    public string Description { get; }
    /// <summary>Gets the 1-based index of the completed hook within the current stage.</summary>
    public int Position { get; }
    /// <summary>Gets the total number of hooks scheduled to run in the current stage.</summary>
    public int Total { get; }
  }

  /// <summary>Triggered when package retrieval for a transaction begins.</summary>
  public sealed class PackageRetrieveStart : AlpmEvent
  {
    internal unsafe PackageRetrieveStart(_alpm_event_t* native)
    {
      PackageCount = (int)native->pkg_retrieve.num;
      TotalSize = native->pkg_retrieve.total_size.Value;
    }

    /// <summary>Gets the number of packages to be retrieved.</summary>
    public int PackageCount { get; }
    /// <summary>Gets the total download size in bytes across all packages to be retrieved.</summary>
    public long TotalSize { get; }
  }

  /// <summary>Triggered when package retrieval for a transaction completes successfully.</summary>
  public sealed class PackageRetrieveDone : AlpmEvent
  {
    internal unsafe PackageRetrieveDone(_alpm_event_t* native)
    {
      PackageCount = (int)native->pkg_retrieve.num;
      TotalSize = native->pkg_retrieve.total_size.Value;
    }

    /// <summary>Gets the number of packages retrieved.</summary>
    public int PackageCount { get; }
    /// <summary>Gets the total download size in bytes across all retrieved packages.</summary>
    public long TotalSize { get; }
  }

  /// <summary>Triggered when package retrieval for a transaction fails.</summary>
  public sealed class PackageRetrieveFailed : AlpmEvent
  {
    internal unsafe PackageRetrieveFailed(_alpm_event_t* native)
    {
      PackageCount = (int)native->pkg_retrieve.num;
      TotalSize = native->pkg_retrieve.total_size.Value;
    }

    /// <summary>Gets the number of packages that were to be retrieved.</summary>
    public int PackageCount { get; }
    /// <summary>Gets the total download size in bytes across all packages that were to be retrieved.</summary>
    public long TotalSize { get; }
  }
}

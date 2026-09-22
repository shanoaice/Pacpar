using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

/// <summary>The version comparison a dependency uses (libalpm's <c>_alpm_depmod_t</c>).</summary>
public enum DepMod : uint
{
  /// <summary>Any version satisfies the dependency.</summary>
  ANY = 1,

  /// <summary>=</summary>
  EQUAL = 2,

  /// <summary>&gt;=</summary>
  GREATER_OR_EQUAL = 3,

  /// <summary>&lt;=</summary>
  LESS_OR_EQUAL = 4,

  /// <summary>&gt;</summary>
  GREATER = 5,

  /// <summary>&lt;</summary>
  LESS = 6
}

/// <summary>
/// A dependency (<c>alpm_depend_t</c>).
/// </summary>
/// <remarks>
/// A managed snapshot: construction copies every field out of the native struct, and no pointer to
/// it is retained. The instance stays valid after the package, database or option list it came from
/// is gone, but - like libalpm's own <c>alpm_depend_t</c> - it is a value, not a handle, so
/// <i>changing</i> the dependency means replacing the entry in the list that owns it.
/// <para>
/// Handing a snapshot back to libalpm is explicit: <see cref="ToNative"/> materialises a struct for
/// the calls that take one, and <see cref="AssumeInstalled"/> uses it for <c>Add</c>. Lookups
/// (<c>Contains</c>, <c>Remove</c>) compare by value through <see cref="Matches"/> instead, because
/// libalpm's removal predicate also compares the internal <c>name_hash</c>, which only a struct
/// libalpm built itself carries.
/// </para>
/// </remarks>
public unsafe class Depend
{
  internal Depend(_alpm_depend_t* backingStruct)
  {
    Name = NativeString.FromNative((nint)backingStruct->name);
    Version = NativeString.FromNative((nint)backingStruct->version);
    Description = NativeString.FromNative((nint)backingStruct->desc);
    Depmod = (DepMod)(uint)backingStruct->mod_;
  }

  private Depend(string? name, string? version, string? description, DepMod depmod)
  {
    Name = name;
    Version = version;
    Description = description;
    Depmod = depmod;
  }

  internal static Depend Factory(void* ptr) => new((_alpm_depend_t*)ptr);

  /// <summary>
  /// Borrowed view over a dependency list owned by libalpm (for example
  /// <c>alpm_pkg_get_depends</c>, <c>alpm_option_get_assumeinstalled</c>).
  /// </summary>
  internal static AlpmList<Depend> ListFactory(_alpm_list_t* alpmList)
    => AlpmList<Depend>.Borrow(alpmList, &Factory);

  /// <summary>Creates a detached, fully managed copy of a native dependency.</summary>
  internal static Depend Snapshot(_alpm_depend_t* native)
    => new(NativeString.FromNative((nint)native->name),
      NativeString.FromNative((nint)native->version),
      NativeString.FromNative((nint)native->desc),
      (DepMod)(uint)native->mod_);

  public string? Description { get; }

  public string? Name { get; }

  public string? Version { get; }

  public DepMod Depmod { get; }

  /// <summary>
  /// Value equality as libalpm's own option-list removal predicate sees it, probed against
  /// libalpm 16.0.1: <see cref="Name"/>, <see cref="Version"/> and <see cref="Depmod"/> take part,
  /// <see cref="Description"/> does not.
  /// </summary>
  internal bool Matches(Depend other)
    => string.Equals(Name, other.Name, StringComparison.Ordinal)
       && string.Equals(Version ?? string.Empty, other.Version ?? string.Empty, StringComparison.Ordinal)
       && Depmod == other.Depmod;

  /// <summary>
  /// Materialises a native <c>alpm_depend_t</c> carrying this snapshot, for the libalpm calls that
  /// take a dependency by pointer. The caller owns the result and releases it with
  /// <see cref="FreeNative"/>.
  /// </summary>
  /// <remarks>
  /// <c>name_hash</c> is deliberately left zero. libalpm recomputes it in the copy it stores -
  /// verified with a probe that handed <c>alpm_option_add_assumeinstalled</c> a hand-built struct
  /// with a zero hash and read back libalpm's own hash - while the removal predicate <i>compares</i>
  /// the field, so only a struct libalpm itself produced (or the element it stored) can be removed.
  /// <see cref="AssumeInstalled"/> therefore never passes a materialised struct to <c>Remove</c>.
  /// </remarks>
  internal _alpm_depend_t* ToNative()
  {
    var native = (_alpm_depend_t*)Marshal.AllocHGlobal(sizeof(_alpm_depend_t));
    native->name = NativeString.ToNative(Name);
    native->version = NativeString.ToNative(Version);
    native->desc = NativeString.ToNative(Description);
    native->name_hash = default;
    native->mod_ = (_alpm_depmod_t)(uint)Depmod;
    return native;
  }

  /// <summary>Releases a struct produced by <see cref="ToNative"/>.</summary>
  internal static void FreeNative(_alpm_depend_t* native)
  {
    if (native == null) return;

    if (native->name != null) Marshal.FreeHGlobal((nint)native->name);
    if (native->version != null) Marshal.FreeHGlobal((nint)native->version);
    if (native->desc != null) Marshal.FreeHGlobal((nint)native->desc);
    Marshal.FreeHGlobal((nint)native);
  }
}

/// <summary>
/// A dependency that is missing from the transaction outcome.
/// </summary>
/// <remarks>
/// Managed snapshot: the list libalpm dumps into <c>alpm_trans_prepare</c>'s output parameter is
/// caller-owned, so <see cref="Transactions.Prepare"/> copies the values out and frees the native
/// memory (list and elements). No native pointer is retained.
/// </remarks>
public sealed class DepMissing
{
  private DepMissing(string? target, string? causingPkg, Depend? depend)
  {
    Target = target;
    CausingPkg = causingPkg;
    Depend = depend;
  }

  internal static unsafe DepMissing FromNative(_alpm_depmissing_t* native)
    => new(NativeString.FromNative((nint)native->target),
      NativeString.FromNative((nint)native->causingpkg),
      native->depend != null ? Depend.Snapshot(native->depend) : null);

  public Depend? Depend { get; }

  public string? CausingPkg { get; }

  public string? Target { get; }
}

public class FileConflict
{
  public string? Ctarget;
  public string? File;
  public string? Target;

  internal unsafe FileConflict(_alpm_fileconflict_t* backingStruct)
  {
    Ctarget = NativeString.FromNative((nint)backingStruct->ctarget);
    File = NativeString.FromNative((nint)backingStruct->file);
    Target = NativeString.FromNative((nint)backingStruct->target);
  }

  internal static unsafe FileConflict Factory(void* ptr) => new((_alpm_fileconflict_t*)ptr);
}

public class Conflict
{
  public string Package1Name;
  public string Package2Name;

  internal unsafe Conflict(_alpm_conflict_t* backingStruct)
  {
    Reason = new Depend(backingStruct->reason);
    Package1Name = new Package(backingStruct->package1).Name;
    Package2Name = new Package(backingStruct->package2).Name;
  }

  internal static unsafe Conflict Factory(void* ptr) => new((_alpm_conflict_t*)ptr);

  /// <summary>
  /// The conflicting dependency. Borrowed from the conflict struct: it is released by
  /// <c>alpm_conflict_free</c>, not by this type.
  /// </summary>
  public Depend Reason;
}

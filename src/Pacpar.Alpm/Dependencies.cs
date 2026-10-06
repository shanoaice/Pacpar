using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

/// <summary>The version comparison a dependency uses (libalpm's <c>_alpm_depmod_t</c>).</summary>
public enum DepMod : uint
{
  /// <summary>Any version satisfies the dependency.</summary>
  Any = 1,

  /// <summary>=</summary>
  Equal = 2,

  /// <summary>&gt;=</summary>
  GreaterOrEqual = 3,

  /// <summary>&lt;=</summary>
  LessOrEqual = 4,

  /// <summary>&gt;</summary>
  Greater = 5,

  /// <summary>&lt;</summary>
  Less = 6
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
/// the calls that take one, and the assume-installed option list uses it for <c>Add</c>. Lookups
/// (<c>Contains</c>, <c>Remove</c>) compare by value through <see cref="Matches"/> instead, because
/// libalpm's removal predicate also compares the internal <c>name_hash</c>, which only a struct
/// libalpm built itself carries.
/// </para>
/// </remarks>
public unsafe class Depend
{
  /// <summary>Creates a detached, fully managed copy of a native dependency.</summary>
  internal Depend(_alpm_depend_t* backingStruct)
  {
    Name = NativeString.FromNative((nint)backingStruct->name);
    Version = NativeString.FromNative((nint)backingStruct->version);
    Description = NativeString.FromNative((nint)backingStruct->desc);
    Depmod = (DepMod)(uint)backingStruct->mod_;
  }

  /// <summary>
  /// Creates a new copy of <see cref="Depend"/> from managed components
  /// </summary>
  private Depend(string? name, string? version, string? description, DepMod depmod)
  {
    Name = name;
    Version = version;
    Description = description;
    Depmod = depmod;
  }

  internal static Depend Factory(void* ptr, Lifetime? lifetime) => new((_alpm_depend_t*)ptr);

  /// <summary>
  /// Borrowed view over a dependency list owned by libalpm (for example
  /// <c>alpm_pkg_get_depends</c>, <c>alpm_option_get_assumeinstalled</c>).
  /// </summary>
  /// <param name="alpmList">The borrowed list. May be <c>null</c>.</param>
  /// <param name="lifetime">Token of the context that owns the list; guards traversal.</param>
  internal static AlpmList<Depend> ListFactory(_alpm_list_t* alpmList, Lifetime? lifetime)
    => AlpmList<Depend>.Borrow(alpmList, &Factory, lifetime);

  /// <summary>
  /// An optional description of why this dependency is required or recommended.
  /// </summary>
  public string? Description { get; }

  /// <summary>
  /// The package name required by this dependency.
  /// </summary>
  public string? Name { get; }

  /// <summary>
  /// The version constraint for this dependency, or <c>null</c> if any version satisfies it.
  /// </summary>
  public string? Version { get; }

  /// <summary>
  /// The comparison operator applied to <see cref="Version"/>.
  /// </summary>
  public DepMod Depmod { get; }

  /// <summary>
  /// Determines whether this dependency matches <paramref name="other"/> by name, version, and version modifier.
  /// </summary>
  internal bool Matches(Depend other)
    => string.Equals(Name, other.Name, StringComparison.Ordinal)
       && string.Equals(Version ?? string.Empty, other.Version ?? string.Empty, StringComparison.Ordinal)
       && Depmod == other.Depmod;

  /// <summary>
  /// Materialises a native <c>alpm_depend_t</c> carrying this snapshot for native calls that
  /// accept a dependency pointer.
  /// </summary>
  /// <remarks>
  /// The caller owns the returned pointer and must release it using <see cref="FreeNative"/>.
  /// </remarks>
  internal _alpm_depend_t* ToNative()
  {
    var native = (_alpm_depend_t*)NativeMemory.Alloc((nuint)sizeof(_alpm_depend_t));
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

    if (native->name != null) NativeMemory.Free(native->name);
    if (native->version != null) NativeMemory.Free(native->version);
    if (native->desc != null) NativeMemory.Free(native->desc);
    NativeMemory.Free(native);
  }
}

/// <summary>
/// A dependency that is missing from the transaction outcome.
/// </summary>
/// <remarks>
/// Managed snapshot: the list libalpm dumps into <c>alpm_trans_prepare</c>'s output parameter is
/// caller-owned, so <see cref="Transaction.Prepare"/> copies the values out and frees the native
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

  // Token parameter unused: a DepMissing is an eager snapshot of caller-owned failure data.
  internal static unsafe DepMissing Factory(void* native, Lifetime? lifetime)
    => new(NativeString.FromNative((nint)((_alpm_depmissing_t*)native)->target),
      NativeString.FromNative((nint)((_alpm_depmissing_t*)native)->causingpkg),
      ((_alpm_depmissing_t*)native)->depend != null ? new Depend(((_alpm_depmissing_t*)native)->depend) : null);

  public Depend? Depend { get; }

  public string? CausingPkg { get; }

  public string? Target { get; }
}

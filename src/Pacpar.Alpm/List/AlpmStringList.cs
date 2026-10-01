using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm.List;

/// <summary>
/// Read-only view over an <c>alpm_list_t</c> of C strings.
/// </summary>
/// <remarks>
/// This is a borrowed view only: it never frees the list nor any of its strings, so callers must
/// not dispose it (it is not disposable). Strings libalpm owns internally — for example the
/// results of <c>alpm_db_get_servers</c>, <c>alpm_pkg_get_licenses</c> or
/// <c>alpm_option_get_cachedirs</c> — are the normal case.
/// <para>
/// Lists whose strings the caller must free are not exposed as this type. They are copied into a
/// managed collection and freed by the method that produced them; see
/// <see cref="TakeOwned"/>.
/// </para>
/// </remarks>
public sealed class AlpmStringList : AlpmList<string>
{
  /// <param name="alpmList">The borrowed list of C strings. May be <c>null</c>.</param>
  /// <param name="lifetime">
  /// Token of the native context that owns the strings; guards traversal. <c>null</c> only for the
  /// empty list.
  /// </param>
  internal unsafe AlpmStringList(_alpm_list_t* alpmList, Lifetime? lifetime) : base(alpmList, &StringFactory, lifetime)
  {
  }

  public unsafe AlpmStringList() : base(null, &StringFactory, null)
  {
  }

  // The token parameter exists to match the factory delegate signature; string elements are copied
  // eagerly, so an issued string never points into native memory and needs no element token.
  private static unsafe string StringFactory(void* data, Lifetime? lifetime)
    => NativeString.FromNative((nint)data) ?? string.Empty;

  /// <summary>
  /// Takes ownership of a list libalpm allocated for the caller, copies its strings into a managed
  /// collection, then frees the list and (through <paramref name="innerFree"/>) its strings.
  /// </summary>
  /// <param name="list">List the caller owns. May be <c>null</c>.</param>
  /// <param name="innerFree">
  /// Required destructor for the strings. Pass <c>MemoryManagement.CFreeExtern</c> for strings
  /// libalpm allocated with <c>malloc</c>/<c>strdup</c> — this is the <c>FREELIST</c> recipe from
  /// <c>man 3 libalpm_list</c>.
  /// </param>
  internal static unsafe IReadOnlyList<string> TakeOwned(_alpm_list_t* list, delegate* unmanaged[Cdecl]<void*, void> innerFree)
  {
    using var owned = new AlpmOwnedList<string>(list, &StringFactory, innerFree, null);
    return owned.ToArray();
  }
}

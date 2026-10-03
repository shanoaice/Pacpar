using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>
/// A package libalpm owns: a database package, a transaction member, or one reached through a group.
/// </summary>
/// <remarks>
/// A package view is a lightweight reference to memory owned by libalpm.
/// If you need package information to persist after the database, transaction, or ALPM handle is disposed,
/// call <see cref="PackageBase.ToSnapshot"/> to create an independent managed snapshot.
/// </remarks>
public sealed unsafe class PackageView : PackageBase
{
  /// <param name="backingStruct">The libalpm-owned package. Not dereferenced here.</param>
  /// <param name="lifetime">Domain of the context that owns the package memory, if any.</param>
  internal PackageView(_alpm_pkg_t* backingStruct, Lifetime? lifetime) : base(backingStruct)
  {
    Lifetime = lifetime?.Capture();
  }

  internal static PackageView Factory(void* ptr, Lifetime? lifetime) => new((_alpm_pkg_t*)ptr, lifetime);
}

using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>
/// A package libalpm owns: a database package, a transaction member, or one reached through a group.
/// </summary>
/// <remarks>
/// Nothing here frees it, which is why this type is deliberately not <see cref="IDisposable"/>; a
/// package this library loaded from a file is a <see cref="LoadedPackage"/> instead, and the two do
/// not convert to one another (see <see cref="PackageBase"/>).
/// <para>
/// Being a view over libalpm's memory is exactly what makes bulk scans cheap, so keep it that way: read
/// the view while scanning, and call <see cref="ToSnapshot"/> only for the packages that must outlive
/// the scan. The lifetime token the issuing context passed in guards the view: once that context is
/// released, every read throws <see cref="AlpmLifetimeException"/> instead of touching freed memory.
/// </para>
/// </remarks>
public sealed unsafe class PackageView : PackageBase
{
  /// <param name="backingStruct">The libalpm-owned package. Not dereferenced here.</param>
  /// <param name="lifetime">Token of the context that owns the package memory, if any.</param>
  internal PackageView(_alpm_pkg_t* backingStruct, Lifetime? lifetime) : base(backingStruct)
  {
    Lifetime = lifetime;
  }

  internal static PackageView Factory(void* ptr, Lifetime? lifetime) => new((_alpm_pkg_t*)ptr, lifetime);
}

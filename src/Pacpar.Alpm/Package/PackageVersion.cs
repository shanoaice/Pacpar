using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>
/// A package version (<c>alpm_pkg_get_version</c>), ordered by libalpm's own
/// <c>alpm_pkg_vercmp</c>.
/// </summary>
/// <remarks>
/// A managed snapshot: the version text is copied on construction, so a value taken from
/// <see cref="PackageBase.Version"/> stays valid after the package that produced it is disposed.
/// <para>
/// The type has value semantics. <see cref="Equals(PackageVersion)"/> and the <c>==</c> operator
/// compare the copied text, so two instances that report the same text are interchangeable - which
/// matters because every read of <see cref="PackageBase.Version"/> builds a new instance.
/// </para>
/// <para>
/// That equality is a different relation from <see cref="CompareTo(PackageVersion)"/> returning 0,
/// and the two cannot be made to agree. <c>alpm_pkg_vercmp</c> skips the package release when
/// either side omits it, and it compares numeric segments numerically - probed against libalpm
/// 16.0.1, <c>"1.0"</c> compares equal to <c>"1.0-1"</c> and to <c>"1.0-2"</c>, while
/// <c>"1.0-1"</c> and <c>"1.0-2"</c> do not compare equal to each other. A relation that behaves
/// that way is not transitive, so it cannot back equality or a hash code. Equality compares the
/// text; <see cref="CompareTo(PackageVersion)"/> answers a question about ordering only.
/// </para>
/// </remarks>
public sealed class PackageVersion : IComparable<PackageVersion>, IEquatable<PackageVersion>
{
  private readonly string _value;

  internal unsafe PackageVersion(byte* version)
  {
    _value = NativeString.FromNative((nint)version) ?? string.Empty;
  }

  /// <summary>
  /// Orders this version against <paramref name="other"/> the way <c>alpm_pkg_vercmp</c> does:
  /// negative when this one is older, positive when it is newer, and 0 when libalpm reports them as
  /// the same version.
  /// </summary>
  /// <remarks>
  /// A <see langword="null"/> <paramref name="other"/> sorts before every version. The result is an
  /// ordering, not an equality test: see the type remarks for why 0 here is looser than
  /// <see cref="Equals(PackageVersion)"/>.
  /// </remarks>
  public unsafe int CompareTo(PackageVersion? other)
  {
    if (other == null) return 1;

    // Versions are short (64 bytes covers realistic epoch/pkgver/pkgrel strings), so both
    // operands marshal onto stack scratch and the comparison never touches the native heap.
    Span<byte> leftScratch = stackalloc byte[64];
    Span<byte> rightScratch = stackalloc byte[64];
    using var left = new Utf8Buffer(_value, leftScratch);
    using var right = new Utf8Buffer(other._value, rightScratch);
    return NativeMethods.alpm_pkg_vercmp(left.Ptr, right.Ptr);
  }

  /// <summary>
  /// Whether <paramref name="other"/> carries the same version text as this instance.
  /// </summary>
  /// <remarks>
  /// The comparison is ordinal on the copied text and never calls into libalpm. See the type remarks
  /// for why it does not follow <see cref="CompareTo(PackageVersion)"/>.
  /// </remarks>
  public bool Equals(PackageVersion? other) =>
    other is not null && string.Equals(_value, other._value, StringComparison.Ordinal);

  /// <summary>
  /// Whether <paramref name="obj"/> is a <see cref="PackageVersion"/> carrying the same version text
  /// as this instance.
  /// </summary>
  public override bool Equals(object? obj) => Equals(obj as PackageVersion);

  /// <summary>
  /// The ordinal hash of the version text, consistent with <see cref="Equals(PackageVersion)"/>.
  /// </summary>
  public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_value);

  /// <summary>
  /// Whether the two operands carry the same version text, or are both <see langword="null"/>.
  /// </summary>
  /// <remarks>
  /// Two reads of <see cref="PackageBase.Version"/> never produce the same instance, so the operator
  /// compares text rather than object identity.
  /// </remarks>
  public static bool operator ==(PackageVersion? left, PackageVersion? right) =>
    left is null ? right is null : left.Equals(right);

  /// <summary>
  /// Whether the two operands carry different version text, or one of them is
  /// <see langword="null"/>.
  /// </summary>
  public static bool operator !=(PackageVersion? left, PackageVersion? right) => !(left == right);

  /// <summary>
  /// The version text, exactly as libalpm reported it.
  /// </summary>
  public override string ToString() => _value;
}

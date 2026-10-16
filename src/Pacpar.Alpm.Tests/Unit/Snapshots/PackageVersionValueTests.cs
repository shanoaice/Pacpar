using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm.Tests.Unit.Snapshots;

/// <summary>
/// Pins the value semantics of <see cref="PackageVersion"/>: equality is ordinal on the copied
/// version text, and it is deliberately a different relation from
/// <see cref="PackageVersion.CompareTo"/> returning 0.
/// </summary>
/// <remarks>
/// Every version here is built over a buffer that is freed before the assertions run, so nothing in
/// these tests can pass by reading native memory that happens to still be readable. No libalpm
/// handle is needed: <c>alpm_pkg_vercmp</c> takes no handle.
/// </remarks>
public sealed unsafe class PackageVersionValueTests
{
  /// <summary>
  /// Copies <paramref name="text"/> into native memory, snapshots it, and releases the buffer - the
  /// snapshot has to stand on its own from here on.
  /// </summary>
  private static PackageVersion Version(string text)
  {
    var buffer = NativeString.ToNative(text);

    try
    {
      return new PackageVersion(buffer);
    }
    finally
    {
      NativeMemory.Free(buffer);
    }
  }

  [Fact]
  public void SameText_TwoInstances_AreEqual()
  {
    var left = Version("1.2.3-4");
    var right = Version("1.2.3-4");

    Assert.False(ReferenceEquals(left, right), "the precondition: two reads are two instances");

    Assert.True(left.Equals(right));
    Assert.True(left.Equals((object)right));
    Assert.True(left == right);
    Assert.False(left != right);
    Assert.Equal(left, right);
    Assert.Equal(left.GetHashCode(), right.GetHashCode());
  }

  [Fact]
  public void DifferentText_IsNotEqual()
  {
    var older = Version("1.0-1");
    var newer = Version("1.0-2");

    Assert.False(older.Equals(newer));
    Assert.False(older == newer);
    Assert.True(older != newer);
    Assert.NotEqual(older, newer);
  }

  [Fact]
  public void NullAndForeignObjects_AreNotEqual()
  {
    var version = Version("1.0-1");

    Assert.False(version.Equals(null));
    Assert.False(version.Equals((object?)null));
    Assert.False(version.Equals("1.0-1"));
    Assert.False(version == null);
    Assert.False(null == version);
    Assert.True(version != null);
    Assert.True(null != version);

    PackageVersion? leftNull = null;
    PackageVersion? rightNull = null;

    Assert.True(leftNull == rightNull);
    Assert.False(leftNull != rightNull);
  }

  /// <summary>
  /// The payoff of the equality members: a version read from one package finds the entry a version
  /// read from another package put in the collection.
  /// </summary>
  [Fact]
  public void CollectionLookup_FindsAnIndependentInstance()
  {
    var set = new HashSet<PackageVersion> { Version("1.0-1") };
    var counts = new Dictionary<PackageVersion, int> { [Version("1.0-1")] = 1 };

    Assert.Contains(Version("1.0-1"), set);
    Assert.DoesNotContain(Version("1.0-2"), set);
    Assert.Equal(1, counts[Version("1.0-1")]);

    // A second insert of an equal key replaces the entry rather than adding one.
    counts[Version("1.0-1")] = 2;
    Assert.Single(counts);
    Assert.Equal(2, counts[Version("1.0-1")]);
  }

  /// <summary>
  /// Locks the divergence the type remarks document. Probed against libalpm 16.0.1: a missing
  /// package release is not compared, so <c>"1.0"</c> compares equal to <c>"1.0-1"</c> and to
  /// <c>"1.0-2"</c>, while those two do not compare equal to each other. That relation is not
  /// transitive, which is exactly why it cannot be equality.
  /// </summary>
  [Fact]
  public void CompareToZero_DoesNotImplyEquality()
  {
    var noRelease = Version("1.0");
    var firstRelease = Version("1.0-1");
    var secondRelease = Version("1.0-2");

    Assert.Equal(0, noRelease.CompareTo(firstRelease));
    Assert.Equal(0, noRelease.CompareTo(secondRelease));

    Assert.False(noRelease.Equals(firstRelease));
    Assert.False(noRelease == firstRelease);

    // The same pair under an ordering that does compare both releases.
    Assert.True(firstRelease.CompareTo(secondRelease) < 0);
    Assert.False(firstRelease.Equals(secondRelease));
  }

  /// <summary>
  /// Sorting follows <see cref="PackageVersion.CompareTo"/>, not equality, so a collection ordered
  /// by the default comparer still groups by libalpm's ordering.
  /// </summary>
  [Fact]
  public void Sorting_UsesTheOrderingRatherThanEquality()
  {
    var versions = new List<PackageVersion> { Version("1.0-2"), Version("1.0-1"), Version("0.9-9") };

    versions.Sort();

    Assert.Equal(["0.9-9", "1.0-1", "1.0-2"], versions.Select(version => version.ToString()));
  }
}

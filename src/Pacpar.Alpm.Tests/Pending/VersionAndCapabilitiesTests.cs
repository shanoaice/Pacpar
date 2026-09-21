using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Report item M, guide §3.1: libalpm's own version and compile-time capabilities.
/// </summary>
/// <remarks>
/// Neither call takes a handle — libalpm allows both before <c>alpm_initialize</c> — so these tests
/// deliberately build no <see cref="Alpm"/> instance. Each assertion compares the wrapper against
/// the raw binding call, which keeps the test independent of which libalpm build is installed.
/// </remarks>
public sealed unsafe class VersionAndCapabilitiesTests
{
  [Fact]
  public void Version_ReportsTheSameStringAsTheNativeCall()
  {
    var native = Marshal.PtrToStringUTF8((nint)NativeMethods.alpm_version());

    Assert.NotNull(native);
    Assert.Equal(native, Alpm.Version);

    // The installed Arch package reports "16.0.1"; assert the shape rather than the value.
    Assert.Matches(@"^\d+\.\d+", Alpm.Version);
  }

  [Fact]
  public void Capabilities_ReportsTheSameBitmaskAsTheNativeCall()
    => Assert.Equal((Capability)(uint)NativeMethods.alpm_capabilities(), Alpm.Capabilities);

  /// <summary>
  /// A capability libalpm reports but this wrapper does not model must not be dropped: it is
  /// carried through as an undeclared bit. The Arch build reports all three
  /// (<c>NLS|DOWNLOADER|SIGNATURES</c> = 7), but a differently built libalpm may report fewer, so
  /// this test only asserts the invariant.
  /// </summary>
  [Fact]
  public void Capabilities_OnlyContainBitsTheEnumDeclares()
  {
    const Capability declared = Capability.Nls | Capability.Downloader | Capability.Signatures;

    Assert.True((Alpm.Capabilities & ~declared) == 0,
      $"libalpm reported a capability this build does not model: {(uint)Alpm.Capabilities}");
  }
}

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pacpar.Alpm.Tests.Unit.GcAnchor;

/// <summary>
/// The static GC-anchor audit: every place that reads unmanaged memory through a managed owner must
/// prove how the owner stays alive for the duration of the read.
/// </summary>
/// <remarks>
/// The audit is deliberately three-valued. A hazard site is <c>Safe</c> only when one of the
/// mechanism validators proves it, <c>Unsafe</c> when nothing is claimed, and <c>NeedsReview</c>
/// when a mechanism is claimed but its contract is a property of control flow rather than of IL.
/// <c>NeedsReview</c> is resolved against <c>gc-anchor-allowlist.txt</c>, which records the review
/// decision and its reason exactly once so the same case never demands attention again.
/// </remarks>
public sealed class GcAnchorAuditTests
{
  // Recorded baseline: raising this is fine, lowering it is not. The 187 -> 176 drop came from the
  // cutover to SafeHandle-marshalled [LibraryImport] entry points, and is accounted for site by site:
  //  -4  the four DangerousGetHandle sites in Alpm (the Handle escape property, AsHandle(), and the
  //      two raw reads inlined into LoadPackage and GetLocalDatabase) - the escape hatch is gone.
  //  -9  the nine "new Options.X(_handle, _lifetime)" constructions in AlpmOptions: while _handle was
  //      a raw pointer those read as ManagedAllocationFromPointer, and now the constructor takes a
  //      SafeAlpmHandle instead.
  //  +2  the two non-owning "new SafeAlpmHandle(...)" wrappers added in Database..ctor and
  //      PackageBase.LibraryHandle, which wrap a borrowed pointer for the interop boundary.
  private const int ExpectedHazardSiteCount = 174;

  private static AnchorAuditResult Audit()
  {
    var types = typeof(Alpm).Assembly.GetTypes()
      .Where(t => t.Namespace?.StartsWith("Pacpar.Alpm.Bindings", StringComparison.Ordinal) != true)
      .Where(t => !(t.IsAbstract && t.IsSealed))          // static classes have no owner to anchor
      .Where(t => !typeof(SafeHandle).IsAssignableFrom(t)); // ReleaseHandle owns the handle already
    return GcAnchorAudit.Run(types, ReadAllowlist());
  }

  private static string ReadAllowlist([CallerFilePath] string callerPath = "")
  {
    var path = Path.Combine(Path.GetDirectoryName(callerPath)!, "gc-anchor-allowlist.txt");
    return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
  }

  [Fact]
  public void HazardSites_AreEnumerated()
  {
    var result = Audit();
    Assert.NotEmpty(result.Sites);

    // Tripwire only: a jump in the hazard count means new native surface appeared and the anchors
    // should be re-read, not that anything is wrong.
    Assert.True(result.Sites.Count >= ExpectedHazardSiteCount,
      $"hazard site count regressed below the recorded baseline of {ExpectedHazardSiteCount}: {result.Sites.Count}");
  }

  [Fact]
  public void NoHazardSiteIsUnanchored()
  {
    var result = Audit();
    var unanchored = result.Unanchored
      .Where(s => !GcAnchorAudit.IsAllowlisted(result.Allowlist, s))
      .Select(s => $"  - {s.Key} [{s.Kind}] {s.Detail}: {s.Reason}")
      .ToList();

    Assert.True(unanchored.Count == 0,
      "The following hazard sites claim no anchoring mechanism at all. Either call GC.KeepAlive(owner) "
      + "after the unmanaged call on every path, bracket it with DangerousAddRef/DangerousRelease, "
      + "or record a reviewed exception in gc-anchor-allowlist.txt.\n" + string.Join('\n', unanchored));
  }

  [Fact]
  public void EveryClaimedMechanismIsProvenOrAllowlisted()
  {
    var result = Audit();

    var unresolved = result.RequiringReview
      .Where(s => !GcAnchorAudit.IsAllowlisted(result.Allowlist, s))
      .Select(s => $"  - {s.Key} [{s.Claimed}] {s.Detail}: {s.Reason}")
      .ToList();

    Assert.True(unresolved.Count == 0,
      "The following hazard sites claim a mechanism whose contract IL inspection cannot prove. "
      + "Review each one and record the decision in gc-anchor-allowlist.txt with its reason.\n"
      + string.Join('\n', unresolved));

    Assert.True(result.StaleAllowlistEntries.Count == 0,
      "These gc-anchor-allowlist.txt entries no longer match any hazard site and must be removed:\n"
      + string.Join('\n', result.StaleAllowlistEntries.Select(e => $"  - {e}")));
  }
}

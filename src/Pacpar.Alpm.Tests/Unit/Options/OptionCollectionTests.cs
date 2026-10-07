using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Regression tests for the option collection template: <c>Clear()</c> must not
/// keep walking the native list after removing from it, and <c>CopyTo</c> must follow
/// <see cref="ICollection{T}"/> semantics instead of shifting the destination.
/// </summary>
/// <remarks>
/// These tests build their own isolated libalpm handle under <see cref="Path.GetTempPath()"/>.
/// They deliberately do not use <c>AlpmEnvironmentFixture</c>, which refuses to run outside the
/// project container, so they stay in the non-integration test set.
/// </remarks>
public sealed class OptionCollectionTests : IDisposable
{
  private readonly IsolatedAlpmEnvironment _environment = new();

  private ICollection<string> Architectures => _environment.Alpm.Options.Architectures;

  public void Dispose() => _environment.Dispose();

  [Fact]
  public void CopyTo_WithNonZeroArrayIndex_WritesFromThatIndex()
  {
    var architectures = Architectures;
    architectures.Add("x86_64");
    architectures.Add("aarch64");
    architectures.Add("any");

    var destination = new string[5];

    architectures.CopyTo(destination, 1);

    Assert.Null(destination[0]);
    Assert.Equal("x86_64", destination[1]);
    Assert.Equal("aarch64", destination[2]);
    Assert.Equal("any", destination[3]);
    Assert.Null(destination[4]);
  }

  [Fact]
  public void CopyTo_ExactlyFillingTheDestination_Succeeds()
  {
    var architectures = Architectures;
    architectures.Add("x86_64");
    architectures.Add("aarch64");
    architectures.Add("any");

    var destination = new string[3];

    architectures.CopyTo(destination, 0);

    Assert.Equal(["x86_64", "aarch64", "any"], destination);

  }

  [Fact]
  public void CopyTo_NullArray_ThrowsArgumentNullException()
  {
    var architectures = Architectures;
    architectures.Add("x86_64");

    Assert.Throws<ArgumentNullException>(() => architectures.CopyTo(null!, 0));
  }

  [Fact]
  public void CopyTo_NegativeArrayIndex_ThrowsArgumentOutOfRangeException()
  {
    var architectures = Architectures;
    architectures.Add("x86_64");

    Assert.Throws<ArgumentOutOfRangeException>(() => architectures.CopyTo(new string[1], -1));
  }

  /// <summary>
  /// The old implementation never checked <c>array.Length</c>: it wrote <c>Count</c> items starting
  /// at <c>array[0]</c> and ran off the end (or overwrote the wrong slots) whenever the destination
  /// was too small.
  /// </summary>
  [Theory]
  [InlineData(3, 1)] // needs 3 free slots, only 2 remain
  [InlineData(2, 0)] // needs 3 free slots, only 2 exist
  [InlineData(5, 3)] // needs 3 free slots, only 2 remain
  public void CopyTo_DestinationTooSmall_ThrowsArgumentException(int arrayLength, int arrayIndex)
  {
    var architectures = Architectures;
    architectures.Add("x86_64");
    architectures.Add("aarch64");
    architectures.Add("any");

    var exception = Assert.Throws<ArgumentException>(() => architectures.CopyTo(new string[arrayLength], arrayIndex));

    Assert.Equal("array", exception.ParamName);
  }

  /// <summary>
  /// The old implementation pre-advanced the enumerator and then read <c>Current</c> in a
  /// <c>do/while</c>, so copying an empty collection threw <see cref="InvalidOperationException"/>.
  /// </summary>
  [Fact]
  public void CopyTo_EmptyCollection_IsANoOp()
  {
    var architectures = Architectures;
    Assert.Empty(architectures);

    var destination = new[] { "keep" };

    architectures.CopyTo(destination, 1);

    Assert.Equal(["keep"], destination);
  }
  [Fact]
  public void Clear_RemovesEveryItem_AndSucceedsOnEmptyCollection()
  {
    var architectures = Architectures;

    // Clear on empty collection must not throw
    architectures.Clear();
    Assert.Empty(architectures);

    architectures.Add("x86_64");
    architectures.Add("aarch64");
    architectures.Add("any");
    Assert.Equal(3, architectures.Count);

    architectures.Clear();
    Assert.Empty(architectures);

    // The handle must still be usable after Clear().
    architectures.Add("riscv64");
    Assert.Equal("riscv64", Assert.Single(architectures));
  }

  /// <summary>
  /// Every <c>alpm_option_remove_*</c> returns 1 when it removed the entry and 0
  /// when it found nothing, while its header documents "0 on success, -1 on error". The wrapper
  /// used to test that result against 0, which inverted the result of
  /// <see cref="ICollection{T}.Remove"/> for all nine option collections.
  /// </summary>
  [Fact]
  public void Remove_ReportsWhetherTheItemWasThere()
  {
    var architectures = Architectures;
    architectures.Add("x86_64");

    Assert.True(architectures.Remove("x86_64"));
    Assert.Empty(architectures);

    Assert.False(architectures.Remove("x86_64"));

    // An item that was never there must answer false without disturbing the list.
    architectures.Add("aarch64");
    Assert.False(architectures.Remove("riscv64"));
    Assert.Equal("aarch64", Assert.Single(architectures));
  }

  /// <summary>
  /// <c>Contains</c> used to compare libalpm's finder result with the needle it had just allocated.
  /// <c>alpm_list_find_str</c> answers with the element stored in the list - probed: a list holding
  /// its own <c>strdup("abc")</c> returned that pointer, not the equal buffer the caller passed - so
  /// the comparison held for no string list at all and <c>Contains</c> answered <c>false</c> for
  /// every item that *was* present. Nothing covered it: <c>Remove</c> never used the comparison, and
  /// <c>Clear</c> ignores what <c>Remove</c> returns.
  /// </summary>
  [Fact]
  public void Contains_ReportsItemsThatArePresent()
  {
    var architectures = Architectures;
    architectures.Add("x86_64");
    architectures.Add("aarch64");

    Assert.True(architectures.Contains("x86_64"));
    Assert.True(architectures.Contains("aarch64"));
    Assert.False(architectures.Contains("riscv64"));

    // Removing one must not make the other unreachable.
    Assert.True(architectures.Remove("x86_64"));
    Assert.False(architectures.Contains("x86_64"));
    Assert.True(architectures.Contains("aarch64"));
  }

  [Fact]
  public void ScalarOptions_ThrowAlpmLifetimeException_AfterSessionDisposed()
  {
    var options = _environment.Alpm.Options;
    _environment.Alpm.Dispose();

    Assert.Throws<AlpmLifetimeException>(() => options.CheckSpace);
    Assert.Throws<AlpmLifetimeException>(() => options.CheckSpace = true);
    Assert.Throws<AlpmLifetimeException>(() => options.DatabaseExtension);
    Assert.Throws<AlpmLifetimeException>(() => options.DatabasePath);
    Assert.Throws<AlpmLifetimeException>(() => options.Root);
    Assert.Throws<AlpmLifetimeException>(() => options.DefaultSigLevel);
    Assert.Throws<AlpmLifetimeException>(() => options.DefaultSigLevel = SigLevel.AlpmSigPackage);
    Assert.Throws<AlpmLifetimeException>(() => options.LocalFileSigLevel);
    Assert.Throws<AlpmLifetimeException>(() => options.RemoteFileSigLevel);
    Assert.Throws<AlpmLifetimeException>(() => options.ParallelDownloads);
    Assert.Throws<AlpmLifetimeException>(() => options.ParallelDownloads = 5);
    Assert.Throws<AlpmLifetimeException>(() => options.LogFile);
    Assert.Throws<AlpmLifetimeException>(() => options.UseSyslog);
    Assert.Throws<AlpmLifetimeException>(() => options.Lockfile);
    Assert.Throws<AlpmLifetimeException>(() => options.GpgDirectory);
    Assert.Throws<AlpmLifetimeException>(() => options.DisableDownloadTimeout);
    Assert.Throws<AlpmLifetimeException>(() => options.DisableSandboxFilesystem);
    Assert.Throws<AlpmLifetimeException>(() => options.Sandbox);
    Assert.Throws<AlpmLifetimeException>(() => options.SandboxUser);
  }
}

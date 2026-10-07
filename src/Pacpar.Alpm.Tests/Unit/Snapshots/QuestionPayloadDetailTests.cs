using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.Tests.Fixtures;
using AlpmHandle = Pacpar.Alpm.Alpm;

namespace Pacpar.Alpm.Tests.Unit.Snapshots;

/// <summary>
/// The question payload borrows package views under the callback frame's lifetime.
/// These tests verify reading package properties within the frame, invalidation when the frame completes,
/// and that explicit ToSnapshot() captures an independent copy that outlives the package.
/// </summary>
public sealed unsafe class QuestionPayloadDetailTests : IDisposable
{
  private readonly IsolatedAlpmEnvironment _environment = new();

  private readonly string _packageDirectory =
    Path.Combine(Path.GetTempPath(), "pacpar-payload-detail", Guid.NewGuid().ToString("n"));

  public QuestionPayloadDetailTests()
  {
    Directory.CreateDirectory(_packageDirectory);
  }

  public void Dispose()
  {
    _environment.Dispose();

    if (Directory.Exists(_packageDirectory))
    {
      Directory.Delete(_packageDirectory, recursive: true);
    }
  }

  private static AlpmQuestion.InstallIgnoredPackage QuestionFor(
    AlpmHandle alpm, _alpm_pkg_t* package, ChildLifetime? lifetime = null)
  {
    var native = (_alpm_question_t*)NativeMemory.AllocZeroed((nuint)sizeof(_alpm_question_t));

    try
    {
      native->type_ = _alpm_question_type_t.ALPM_QUESTION_INSTALL_IGNOREPKG;
      native->install_ignorepkg.install = 1;
      native->install_ignorepkg.pkg = package;

      return Assert.IsType<AlpmQuestion.InstallIgnoredPackage>(
        AlpmQuestion.FromUnion(native, lifetime));
    }
    finally
    {
      NativeMemory.Free((void*)(nint)native);
    }
  }

  [Fact]
  public void Payload_ReadsPackagePropertiesWithinTheCallbackFrame()
  {
    using var pkg = Load("payload-default", file: "usr/bin/probe");
    var frameLifetime = _environment.Alpm.RootLifetime.CreateChild("test frame");

    var question = QuestionFor(_environment.Alpm, pkg.BackingStruct, frameLifetime);

    Assert.Equal("payload-default", question.Package.Name);
    Assert.NotNull(question.Package.Files);
    Assert.Contains(question.Package.Files, file => file.Name == "usr/bin/probe");
  }

  [Fact]
  public void Payload_DiesWhenCallbackFrameCompletes()
  {
    using var pkg = Load("payload-frame-dies", file: "usr/bin/probe");
    var frameLifetime = _environment.Alpm.RootLifetime.CreateChild("test frame");

    var question = QuestionFor(_environment.Alpm, pkg.BackingStruct, frameLifetime);
    Assert.Equal("payload-frame-dies", question.Package.Name);

    // Frame ends
    frameLifetime.Invalidate("test frame completed");

    // Reading package or modifying answer now throws AlpmLifetimeException
    Assert.Throws<AlpmLifetimeException>(() => question.Package.Name);
    Assert.Throws<AlpmLifetimeException>(() => question.Install = false);
  }

  [Fact]
  public void ExplicitSnapshot_OutlivesThePackageAndTheFrame()
  {
    PackageSnapshot snapshot;
    var frameLifetime = _environment.Alpm.RootLifetime.CreateChild("test frame");

    using (var pkg = Load("payload-detached", file: "usr/bin/probe"))
    {
      var question = QuestionFor(_environment.Alpm, pkg.BackingStruct, frameLifetime);
      snapshot = question.Package.ToSnapshot(includeFiles: true);
      Assert.Equal("payload-detached", snapshot.Name);
    }

    // Invalidate frame
    frameLifetime.Invalidate("test frame completed");

    // The explicit snapshot stays valid independently of native memory and frame lifetime
    Assert.Equal("payload-detached", snapshot.Name);
    Assert.Equal(PackageArchive.Version, snapshot.Version.ToString());
    Assert.NotNull(snapshot.Files);
    Assert.Contains(snapshot.Files, file => file.Name == "usr/bin/probe");
  }

  private LoadedPackage Load(string name, string file)
    => _environment.Alpm.LoadPackage(
      PackageArchive.Create(_packageDirectory, name, file: file),
      full: true,
      SigLevel.AlpmSigUseDefault);
}

using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.Tests.Fixtures;
using AlpmHandle = Pacpar.Alpm.Alpm;

namespace Pacpar.Alpm.Tests.Unit.Snapshots;

/// <summary>
/// The question payload copies packages instead of lending views, and
/// <see cref="AlpmBindingConfig.QuestionPayloadIncludeFiles"/> decides whether the file list is copied. These tests
/// pin the default, the opt-in, the per-handle scope of the setting and the one property that makes the
/// copy worth its cost: the payload stays readable after the package it came from is gone.
/// </summary>
public sealed unsafe class QuestionPayloadDetailTests : IDisposable
{
  private readonly IsolatedAlpmEnvironment _environment = new();

  private readonly string _packageDirectory =
    Path.Combine(Path.GetTempPath(), "pacpar-payload-detail", Guid.NewGuid().ToString("n"));

  private readonly string _otherPackageDirectory =
    Path.Combine(Path.GetTempPath(), "pacpar-payload-detail", Guid.NewGuid().ToString("n"));

  public QuestionPayloadDetailTests()
  {
    Directory.CreateDirectory(_packageDirectory);
    Directory.CreateDirectory(_otherPackageDirectory);
  }

  public void Dispose()
  {
    _environment.Dispose();

    foreach (var directory in new[] { _packageDirectory, _otherPackageDirectory })
    {
      if (Directory.Exists(directory))
      {
        Directory.Delete(directory, recursive: true);
      }
    }
  }

  /// <summary>
  /// Builds an INSTALL_IGNOREPKG payload the way the callback thunk does - the union is freed before
  /// the assertion runs, so every value the payload answers with must have been copied.
  /// </summary>
  private static AlpmQuestion.InstallIgnoredPackage QuestionFor(AlpmHandle alpm, _alpm_pkg_t* package)
  {
    var native = (_alpm_question_t*)NativeMemory.Alloc((nuint)sizeof(_alpm_question_t));

    try
    {
      native->type_ = _alpm_question_type_t.ALPM_QUESTION_INSTALL_IGNOREPKG;
      native->install_ignorepkg.install = 1;
      native->install_ignorepkg.pkg = package;

      return Assert.IsType<AlpmQuestion.InstallIgnoredPackage>(
        AlpmQuestion.FromUnion(native, alpm.BindingConfig));
    }
    finally
    {
      NativeMemory.Free((void*)(nint)native);
    }
  }

  [Fact]
  public void Default_LeavesTheFileListOut()
  {
    using var pkg = Load("payload-default", file: "usr/bin/probe");

    Assert.False(_environment.Alpm.BindingConfig.QuestionPayloadIncludeFiles);

    var question = QuestionFor(_environment.Alpm, pkg.BackingStruct);

    Assert.Equal("payload-default", question.Package.Name);
    Assert.Null(question.Package.Files);
  }

  [Fact]
  public void WithFiles_CopiesTheFileList()
  {
    using var pkg = Load("payload-files", file: "usr/bin/probe");

    _environment.Alpm.BindingConfig.QuestionPayloadIncludeFiles = true;

    var files = QuestionFor(_environment.Alpm, pkg.BackingStruct).Package.Files;

    Assert.NotNull(files);
    Assert.Contains(files, file => file.Name == "usr/bin/probe");
  }

  [Fact]
  public void Payload_OutlivesThePackageItWasCopiedFrom()
  {
    AlpmQuestion.InstallIgnoredPackage question;

    using (var pkg = Load("payload-detached", file: "usr/bin/probe"))
    {
      question = QuestionFor(_environment.Alpm, pkg.BackingStruct);
      Assert.Equal("payload-detached", question.Package.Name);
    }

    // The package's native memory is freed by the disposal above and the question union is already gone.
    Assert.Equal("payload-detached", question.Package.Name);
    Assert.Equal(PackageArchive.Version, question.Package.Version.ToString());
  }

  /// <summary>
  /// The setting hangs off the handle, not the process: a second handle keeps the default while the
  /// first opts in, which is what lets a CLI and a TUI share the process with different payload costs.
  /// </summary>
  [Fact]
  public void Binding_IsPerHandle()
  {
    using var other = new IsolatedAlpmEnvironment();
    using var configured = Load("payload-configured", file: "usr/bin/probe");
    using var untouched = LoadInto(other, _otherPackageDirectory, "payload-untouched", file: "usr/bin/probe");

    _environment.Alpm.BindingConfig.QuestionPayloadIncludeFiles = true;

    Assert.NotNull(QuestionFor(_environment.Alpm, configured.BackingStruct).Package.Files);
    Assert.Null(QuestionFor(other.Alpm, untouched.BackingStruct).Package.Files);
  }

  private LoadedPackage Load(string name, string file)
    => LoadInto(_environment, _packageDirectory, name, file);

  private static LoadedPackage LoadInto(IsolatedAlpmEnvironment environment, string directory, string name,
    string file)
    => environment.Alpm.LoadPackage(PackageArchive.Create(directory, name, file: file),
      full: true, SigLevel.AlpmSigUseDefault);
}

using System.Runtime.InteropServices;
using System.Text;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Report item H: libalpm's string contract is UTF-8, so both the marshalling helpers and one
/// end-to-end option round trip must carry non-ASCII text through unchanged.
/// </summary>
/// <remarks>
/// On Linux this was already true of the old ANSI calls (<see cref="Encoding.Default"/> *is* UTF-8
/// there), so these tests pin the encoding choice rather than a behaviour change. They are the
/// portable half of the sweep: on a Windows host the old code would fail them.
/// </remarks>
public sealed unsafe class Utf8StringTests : IDisposable
{
  private const string NonAscii = "中文 / ünïcødé";

  private readonly string _workspaceRoot;
  private readonly Alpm _alpm;

  public Utf8StringTests()
  {
    _workspaceRoot = Path.Combine(Path.GetTempPath(), "pacpar-utf8-tests", Guid.NewGuid().ToString("n"));
    var root = Path.Combine(_workspaceRoot, "root");
    var dbpath = Path.Combine(_workspaceRoot, "var", "lib", "pacman");

    Directory.CreateDirectory(root);
    Directory.CreateDirectory(Path.Combine(dbpath, "local"));
    Directory.CreateDirectory(Path.Combine(root, "tmp"));
    Directory.CreateDirectory(Path.Combine(root, "var", "cache", "pacman", "pkg"));

    _alpm = new Alpm(root, dbpath);
  }

  public void Dispose()
  {
    _alpm.Dispose();

    if (Directory.Exists(_workspaceRoot))
    {
      Directory.Delete(_workspaceRoot, recursive: true);
    }
  }

  [Fact]
  public void ToNative_WritesUtf8BytesAndNulTerminator()
  {
    var expected = Encoding.UTF8.GetBytes(NonAscii);
    var ptr = NativeString.ToNative(NonAscii);
    try
    {
      Assert.Equal(expected, new ReadOnlySpan<byte>(ptr, expected.Length).ToArray());
      Assert.Equal((byte)0, ptr[expected.Length]);
      Assert.Equal(NonAscii, NativeString.FromNative((nint)ptr));
    }
    finally
    {
      Marshal.FreeHGlobal((nint)ptr);
    }
  }

  [Fact]
  public void ToNative_Null_IsANullPointer()
  {
    Assert.True(NativeString.ToNative(null) == null);
  }

  [Fact]
  public void FromNative_NullPointer_IsNull()
  {
    Assert.Null(NativeString.FromNative(0));
  }

  [Fact]
  public void NonAsciiOption_RoundTripsThroughLibalpm()
  {
    var architectures = _alpm.Options.Architectures;

    architectures.Add(NonAscii);

    Assert.Equal(NonAscii, Assert.Single(architectures));
  }
}

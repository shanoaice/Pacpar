using System.Reflection;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Guards the hand-written <c>[LibraryImport]</c> layer in
/// <c>src/Pacpar.Alpm/Interop/NativeMethods.SafeHandle.cs</c> against drift in the csbindgen-generated
/// <c>[DllImport]</c> declarations.
/// </summary>
/// <remarks>
/// Both sets of declarations live on the same partial class <see cref="NativeMethods"/>, so
/// reflection sees them together and the two can be paired by their <c>EntryPoint</c>.
/// <see cref="DllImportAttribute.EntryPoint"/> is populated on the generated methods and
/// <see cref="LibraryImportAttribute.EntryPoint"/> survives on the hand-written ones, which is what
/// makes the pairing possible without any native library present: reflection never invokes the
/// methods, so this suite runs everywhere the rest of the host unit tests do.
/// </remarks>
public sealed class SafeBindingPairingTests
{
  /// <summary>
  /// Parameters of this CLR type in a hand-written overload stand for a raw pointer of this type in
  /// the generated declaration, because the <c>[LibraryImport]</c> SafeHandle marshaller turns the
  /// handle into its underlying pointer for the duration of the call.
  /// </summary>
  private static readonly Dictionary<Type, Type> HandleSubstitutions = new()
  {
    [typeof(SafeAlpmHandle)] = typeof(_alpm_handle_t*),
  };

  /// <summary>
  /// Entry points that must keep the raw generated declaration because a <c>SafeHandle</c> cannot be
  /// marshalled from inside its own <c>ReleaseHandle</c>: <c>alpm_release</c> is the native release
  /// routine itself, called by <see cref="SafeAlpmHandle.ReleaseHandle"/> with the raw handle value.
  /// </summary>
  private static readonly string[] RawOnlyEntryPoints = ["alpm_release"];

  /// <summary>
  /// Hand-written overloads that deliberately deviate from their generated declaration: entry point
  /// to the indices of the parameters whose types intentionally differ. The log callback keeps its
  /// <c>va_list</c> argument opaque (<c>void*</c> instead of the generated <c>__va_list_tag*</c>)
  /// so that regenerating the bindings can never re-materialise a typed <c>va_list</c> in the
  /// native thunk signature: <c>Callback.LogAgent</c> forwards it unchanged to the native
  /// <c>vasprintf</c> shim (see <c>LogMessageFormatter</c>). Everything else about the overload -
  /// entry point, arity, the other parameter types, the return type - is still paired and checked.
  /// </summary>
  private static readonly Dictionary<string, int[]> IntentionalParameterDeviations = new()
  {
    ["alpm_option_set_logcb"] = [1],
  };

  private static readonly MethodInfo[] GeneratedDeclarations =
    [.. Declarations(typeof(DllImportAttribute))];

  private static readonly MethodInfo[] SafeOverloads =
    [.. Declarations(typeof(LibraryImportAttribute))];

  private static IEnumerable<MethodInfo> Declarations(Type marker) =>
    typeof(NativeMethods)
      .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
      .Where(method => method.GetCustomAttribute(marker) is not null);

  private static string EntryPointOf(MethodInfo method) =>
    method.GetCustomAttribute<DllImportAttribute>()?.EntryPoint
    ?? method.GetCustomAttribute<LibraryImportAttribute>()?.EntryPoint
    ?? method.Name;

  private static string Describe(MethodInfo method) =>
    $"{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))}) -> {method.ReturnType.Name}";



  /// <summary>
  /// Every hand-written overload must match the generated declaration it mirrors: same entry point,
  /// same arity, same parameter types (with <see cref="SafeAlpmHandle"/> standing in for the raw
  /// handle pointer), and the same return type. This is the check that catches libalpm signature
  /// drift, which would otherwise silently corrupt the stack at the call boundary. The only
  /// tolerated departures are the deliberate ones whitelisted in
  /// <see cref="IntentionalParameterDeviations"/>.
  /// </summary>
  [Fact]
  public void EverySafeHandleOverload_MatchesItsGeneratedSignature()
  {
    var problems = new List<string>();

    foreach (var safe in SafeOverloads)
    {
      var generated = GeneratedDeclarations.FirstOrDefault(g => EntryPointOf(g) == EntryPointOf(safe));
      if (generated is null)
      {
        problems.Add($"{Describe(safe)}: no generated declaration carries this EntryPoint.");
        continue;
      }

      var expected = generated.GetParameters();
      var actual = safe.GetParameters();

      if (expected.Length != actual.Length)
      {
        problems.Add($"{Describe(safe)}: arity {actual.Length} but the generated declaration has {expected.Length}.");
        continue;
      }

      for (var i = 0; i < expected.Length; i++)
      {
        if (IntentionalParameterDeviations.TryGetValue(EntryPointOf(safe), out var deviated) && deviated.Contains(i))
        {
          continue;
        }

        var wanted = HandleSubstitutions.TryGetValue(actual[i].ParameterType, out var substituted)
          ? substituted
          : actual[i].ParameterType;
        if (wanted != expected[i].ParameterType)
        {
          problems.Add(
            $"{Describe(safe)}: parameter {i} is {actual[i].ParameterType.Name} " +
            $"but the generated declaration has {expected[i].ParameterType.Name}.");
        }
      }

      if (safe.ReturnType != generated.ReturnType)
      {
        problems.Add(
          $"{Describe(safe)}: returns {safe.ReturnType.Name} " +
          $"but the generated declaration returns {generated.ReturnType.Name}.");
      }
    }

    Assert.True(
      problems.Count == 0,
      $"""
      {problems.Count} hand-written overload(s) do not match their generated declaration:
      {string.Join(Environment.NewLine, problems)}
      """);
  }

  /// <summary>
  /// Both directions are only meaningful while the layers actually exist: if the generated
  /// declarations ever stop being visible to reflection (for example because csbindgen changed its
  /// class shape), the pairing assertions above would vacuously pass.
  /// </summary>
  [Fact]
  public void BothDeclarationLayers_AreVisible()
  {
    Assert.NotEmpty(GeneratedDeclarations);
    Assert.NotEmpty(SafeOverloads);
  }

  [Fact]
  public unsafe void LibalpmVersion_IsAvailable()
  {
    var versionPtr = NativeMethods.alpm_version();
    Assert.True(versionPtr != null);
    var version = Marshal.PtrToStringUTF8((nint)versionPtr);
    Assert.NotNull(version);
    Assert.Matches(@"^[0-9]+.[0-9]+.[0-9]+", version);
  }
}

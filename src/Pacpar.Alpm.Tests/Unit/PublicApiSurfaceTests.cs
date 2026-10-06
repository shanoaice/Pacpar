using System.Reflection;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// No public member may take or return a raw native pointer, and the generated binding
/// layer must not be reachable from the public surface at all.
/// </summary>
/// <remarks>
/// The public API used to expose constructors such as <c>Database(byte*)</c> and factories such as
/// <c>PackageView.Factory(void*, Lifetime?)</c>, so a consumer holding a pointer had to enter an
/// <c>unsafe</c>
/// context and name libalpm's binding types to wrap it. Instances are meant to come from
/// <see cref="Alpm"/>, <see cref="Database"/>, <see cref="Transaction"/> and the types that hang
/// off them. That surface no longer exposes a raw handle at all: the wrapper reaches libalpm
/// through <see cref="SafeAlpmHandle"/>, a type-parameterised <c>SafeHandle</c> that owns the
/// native handle and is what the <c>[LibraryImport]</c> entry points accept.
/// <para>
/// Since ADR 0006 the generated <c>Pacpar.Alpm.Bindings</c> namespace is <c>internal</c>, so the
/// pointer check no longer skips it - every exported type is inspected. A second test pins the
/// namespace itself as unexported, so a libalpm upgrade plus a csbindgen regeneration cannot quietly
/// widen the public surface back into an escape hatch.
/// </para>
/// </remarks>
public sealed class PublicApiSurfaceTests
{
  [Fact]
  public void PublicApi_DoesNotExposeRawPointers()
  {
    var offenders = new List<string>();

    foreach (var type in ExportedTypes)
    {
      foreach (var member in type.GetMembers(
                 BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
      {
        switch (member)
        {
          case MethodBase method:
            offenders.AddRange(method.GetParameters()
              .Where(parameter => parameter.ParameterType.IsPointer)
              .Select(parameter => $"{type.FullName}.{method.Name}({parameter.ParameterType.Name})"));
            break;
          case FieldInfo field when field.FieldType.IsPointer:
            offenders.Add($"{type.FullName}.{field.Name} (field)");
            break;
          case PropertyInfo property when property.PropertyType.IsPointer:
            offenders.Add($"{type.FullName}.{property.Name} (property)");
            break;
        }
      }
    }

    Assert.Empty(offenders);
  }

  /// <summary>
  /// ADR 0006: the generated bindings are an implementation detail. Nothing in
  /// <c>Pacpar.Alpm.Bindings</c> may be exported - no enum, struct, fixed buffer or helper class.
  /// </summary>
  [Fact]
  public void PublicApi_DoesNotExportTheGeneratedBindingNamespace()
  {
    var leaked = ExportedTypes
      .Where(type => type.Namespace?.StartsWith("Pacpar.Alpm.Bindings", StringComparison.Ordinal) == true)
      .Select(type => type.FullName!)
      .OrderBy(name => name, StringComparer.Ordinal)
      .ToArray();

    Assert.Empty(leaked);
  }

  private static IEnumerable<Type> ExportedTypes => typeof(Alpm).Assembly.GetExportedTypes();
}

using System.Reflection;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Report item I: no hand-written public member may take or return a raw native pointer.
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
/// The generated <c>Pacpar.Alpm.Bindings</c> namespace is excluded: it is bindgen output whose
/// whole purpose is to mirror the C signatures, pointers included.
/// </para>
/// </remarks>
public sealed class PublicApiSurfaceTests
{
  [Fact]
  public void PublicApi_DoesNotExposeRawPointers()
  {
    var offenders = new List<string>();

    foreach (var type in typeof(Alpm).Assembly.GetExportedTypes())
    {
      if (type.Namespace?.StartsWith("Pacpar.Alpm.Bindings", StringComparison.Ordinal) == true) continue;

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
}

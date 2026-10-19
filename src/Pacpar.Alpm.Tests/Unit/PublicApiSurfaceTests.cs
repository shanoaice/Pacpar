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
/// <see cref="AlpmSession"/>, <see cref="Database"/>, <see cref="Transaction"/> and the types that hang
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

  /// <summary>
  /// The three callback unions are closed: <c>FromUnion</c> is the only producer of a payload, so no
  /// payload may be constructible by a consumer.
  /// </summary>
  /// <remarks>
  /// Most payloads always carried an <c>internal</c> constructor. The ones written as
  /// <c>public sealed class CheckDepsStart : AlpmEvent;</c> did not declare one at all, so C# gave
  /// them a public parameterless one and a consumer could build an event libalpm never reported.
  /// Sealing does not close that hole - sealing stops derivation, not construction - so the
  /// constructors have to be declared, and this test keeps it that way.
  /// </remarks>
  [Fact]
  public void PublicApi_DoesNotExposeConstructorsOnCallbackPayloads()
  {
    Type[] unions = [typeof(AlpmEvent), typeof(AlpmQuestion), typeof(AlpmDownloadEvent)];

    var offenders = ExportedTypes
      .Where(type => unions.Contains(type.BaseType))
      .SelectMany(type => type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
      .Select(ctor => $"{ctor.DeclaringType!.FullName}.{ctor.Name}({ctor.GetParameters().Length} params)")
      .ToArray();

    Assert.Empty(offenders);
  }

  /// <summary>
  /// ADR 0016: the public surface does not expose native-sized integers. A count is an <c>int</c>
  /// and a byte size is a <c>long</c>; <c>nuint</c> and <c>nint</c> surface as <see cref="UIntPtr"/>
  /// and <see cref="IntPtr"/> in metadata.
  /// </summary>
  /// <remarks>
  /// The check walks generic arguments because a native-sized integer can hide inside a delegate
  /// type such as <c>Action&lt;ProgressType, string, int, nuint, nuint&gt;</c>, where no member type
  /// is <see cref="UIntPtr"/> itself. It also unwraps arrays and by-ref types.
  /// </remarks>
  [Fact]
  public void PublicApi_DoesNotExposeNativeSizedIntegers()
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
              .Where(parameter => ContainsNativeSizedInteger(parameter.ParameterType))
              .Select(parameter => $"{type.FullName}.{method.Name}({parameter.Name})"));
            if (method is MethodInfo { ReturnType: var returnType } && ContainsNativeSizedInteger(returnType))
            {
              offenders.Add($"{type.FullName}.{method.Name} (return)");
            }
            break;
          case FieldInfo field when ContainsNativeSizedInteger(field.FieldType):
            offenders.Add($"{type.FullName}.{field.Name} (field)");
            break;
          case PropertyInfo property when ContainsNativeSizedInteger(property.PropertyType):
            offenders.Add($"{type.FullName}.{property.Name} (property)");
            break;
          case EventInfo @event when ContainsNativeSizedInteger(@event.EventHandlerType!):
            offenders.Add($"{type.FullName}.{@event.Name} (event)");
            break;
        }
      }
    }

    Assert.Empty(offenders);
  }

  private static bool ContainsNativeSizedInteger(Type type)
  {
    if (type == typeof(IntPtr) || type == typeof(UIntPtr))
    {
      return true;
    }

    if (type.IsArray || type.IsByRef || type.IsPointer)
    {
      var element = type.GetElementType();
      return element is not null && ContainsNativeSizedInteger(element);
    }

    return type.IsGenericType && type.GetGenericArguments().Any(ContainsNativeSizedInteger);
  }

  private static IEnumerable<Type> ExportedTypes => typeof(AlpmSession).Assembly.GetExportedTypes();
}

using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace Pacpar.Alpm.Tests.Unit.GcAnchor;

/// <summary>How a hazard site is claimed to be protected.</summary>
internal enum AnchorMechanism
{
  /// <summary>No mechanism claimed: the site is unprotected.</summary>
  None,
  /// <summary>M1: <c>GC.KeepAlive(owner)</c> after the call, on all paths.</summary>
  KeepAlive,
  /// <summary>M2: the P/Invoke takes a SafeHandle and the CLR stub refcounts it.</summary>
  SafeHandleParameter,
  /// <summary>M3: a <c>DangerousAddRef</c>/<c>DangerousRelease</c> region brackets the call.</summary>
  AddRefRelease,
  /// <summary>M4: <c>this</c> is live after the call and transitively owns the native memory.</summary>
  ReceiverOnStack,
  /// <summary>M5: the anchor is the managed caller of the synchronous libalpm callback.</summary>
  SyncCallbackFrame,
  /// <summary>M6: the native pointer is a parameter, so the caller, not this method, must anchor it.</summary>
  DelegatedToCaller,
  /// <summary>Doc form R2: the constructed object receives the owner's token, so it anchors itself.</summary>
  CarriesOwnAnchor,
}

/// <summary>Three-valued audit verdict. <see cref="NeedsReview"/> is the false-positive escape valve.</summary>
internal enum AnchorVerdict
{
  /// <summary>At least one validator proved the site safe.</summary>
  Safe,
  /// <summary>A mechanism is claimed but its contract is not statically provable.</summary>
  NeedsReview,
  /// <summary>No mechanism is claimed, or a claim is disproved.</summary>
  Unsafe,
}

/// <summary>How strongly a type transitively owns the native memory a hazard touches.</summary>
internal enum OwnershipClass
{
  /// <summary>Type holds a SafeHandle field: anchoring <c>this</c> provably keeps the memory alive.</summary>
  Strong,
  /// <summary>Type holds only a <c>Lifetime</c> token: anchoring <c>this</c> suffices only when the token is non-null.</summary>
  Token,
  /// <summary>Type owns nothing (snapshot types): anchoring <c>this</c> protects nothing.</summary>
  None,
}

internal enum HazardKind
{
  /// <summary>H1: a call into <c>Pacpar.Alpm.Bindings.NativeMethods</c>.</summary>
  NativeCall,
  /// <summary>H2: a <c>SafeHandle.DangerousGetHandle()</c> result.</summary>
  DangerousGetHandle,
  /// <summary>H3: a managed object materialised from a raw pointer.</summary>
  ManagedAllocationFromPointer,
}

internal readonly record struct HazardSite(
  string Type,
  string Member,
  int IlOffset,
  HazardKind Kind,
  string Detail,
  AnchorMechanism Claimed,
  AnchorVerdict Verdict,
  string Reason)
{
  public string Key => $"{Type}.{Member}";
}

/// <summary>The discovered hazard sites, their claimed mechanisms and their verdicts.</summary>
internal sealed class AnchorAuditResult(
  IReadOnlyList<HazardSite> sites,
  IReadOnlyDictionary<string, string> allowlist,
  IReadOnlyList<string> staleAllowlistEntries)
{
  public IReadOnlyList<HazardSite> Sites { get; } = sites;
  public IReadOnlyDictionary<string, string> Allowlist { get; } = allowlist;
  public IReadOnlyList<string> StaleAllowlistEntries { get; } = staleAllowlistEntries;

  public IEnumerable<HazardSite> Unanchored => Sites.Where(s => s.Verdict == AnchorVerdict.Unsafe);
  public IEnumerable<HazardSite> RequiringReview => Sites.Where(s => s.Verdict == AnchorVerdict.NeedsReview);
}

/// <summary>
/// Discovers GC-anchor hazard sites in <c>Pacpar.Alpm</c> and classifies how each one is protected.
/// </summary>
/// <remarks>
/// Mechanism identification is kept strictly separate from the safety decision: each mechanism has
/// its own validator with its own sufficient condition, and a site is only <c>Safe</c> when a
/// validator <em>proves</em> it. Anything that cannot be proven falls to <c>NeedsReview</c> and is
/// resolved against the checked-in allowlist, which is the only thing standing between static
/// analysis and an endless stream of false positives.
/// </remarks>
internal static class GcAnchorAudit
{
  private const string NativeMethodsTypeName = "Pacpar.Alpm.Bindings.NativeMethods";

  private static readonly MethodInfo KeepAliveMethod =
    typeof(GC).GetMethod(nameof(GC.KeepAlive), [typeof(object)])!;
  private static readonly MethodInfo DangerousGetHandleMethod =
    typeof(SafeHandle).GetMethod(nameof(SafeHandle.DangerousGetHandle), Type.EmptyTypes)!;
  private static readonly MethodInfo DangerousAddRefMethod =
    typeof(SafeHandle).GetMethod(nameof(SafeHandle.DangerousAddRef), [typeof(bool).MakeByRefType()])!;
  private static readonly MethodInfo DangerousReleaseMethod =
    typeof(SafeHandle).GetMethod(nameof(SafeHandle.DangerousRelease), Type.EmptyTypes)!;

  public static AnchorAuditResult Run(IEnumerable<Type> types, string allowlistText)
  {
    var allowlist = ParseAllowlist(allowlistText);
    var sites = new List<HazardSite>();
    foreach (var type in types)
    {
      var ownership = ClassifyOwnership(type);
      // Nested types report under their declaring type. The snapshot classes inside AlpmEvent and
      // AlpmQuestion are only ever constructed by their owner's FromUnion on the callback path, so
      // reviewing the owner reviews them; qualifying the name keeps that to one allowlist entry
      // instead of one per snapshot case.
      var qualified = type.DeclaringType is { } outer ? outer.Name + "." + type.Name : type.Name;
      foreach (var method in GetScannableMethods(type))
      {
        foreach (var site in ScanMethod(type, method, ownership))
        {
          sites.Add(site.Type == type.Name ? site with { Type = qualified } : site);
        }
      }
    }

    var seen = sites.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
    var stale = allowlist.Keys
      .Where(k => !seen.Contains(k) && !seen.Any(s => s.StartsWith(k + ".", StringComparison.Ordinal)))
      .ToList();
    return new AnchorAuditResult(sites, allowlist, stale);
  }

  private static IEnumerable<MethodBase> GetScannableMethods(Type type) =>
    // Constructors are part of the surface. GetMethods never returns them, and the snapshot types
    // do their native reads inside theirs: PackageFile copies mode/size/name out of the entry,
    // ConflictPkg calls alpm_pkg_get_name on the packages inside the conflict, Group materialises
    // its package list. Leaving constructors out hid exactly the sites this audit exists to find.
    type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Cast<MethodBase>()
        .Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        .Where(m => !m.IsAbstract && m.GetMethodBody() != null)
        // AlpmOptionList<T>'s private protected primitives (GetList, AddNative, Acquire, Release,
        // View, FindIn) are reachable only from the base class itself, which anchors them for the
        // whole family. Scanning only its public entry points keeps that single obligation audited
        // once instead of re-reporting it as a defect on every concrete option-list type.
        .Where(m => !IsAlpmOptionList(type) || m is MethodInfo { IsPublic: true });

  /// <summary>True when the type derives from <c>AlpmOptionList&lt;T&gt;</c>.</summary>
  private static bool IsAlpmOptionList(Type type)
  {
    for (var cur = type; cur != null && cur != typeof(object); cur = cur.BaseType)
    {
      if (cur.IsGenericType && cur.GetGenericTypeDefinition().Name.StartsWith("AlpmOptionList", StringComparison.Ordinal))
        return true;
    }
    return false;
  }

  private static IEnumerable<HazardSite> ScanMethod(Type type, MethodBase method, OwnershipClass ownership)
  {
    var graph = IlControlFlowGraph.Build(method);
    if (graph == null) yield break;

    var instructions = graph.Instructions;
    var module = method.Module;

    for (int i = 0; i < instructions.Count; i++)
    {
      var hazard = instructions[i];

      // calli invokes through a function pointer, so it carries no method token to resolve, yet it
      // is a real native call: the AlpmOptions setters forward to libalpm as
      // set => SetStringOption(value, &NativeMethods.alpm_option_set_dbext).
      if (hazard.OpCode == OpCodes.Calli)
      {
        yield return Classify(module, type, method, graph, i,
          HazardKind.NativeCall, "calli (function pointer)", ownership, null);
        continue;
      }

      // ldftn and ldvirtftn only load a method address; the invocation they feed is the calli
      // handled above. Counting them as hazards reports the three AlpmOptions setters as unanchored
      // while never reaching the call that actually enters native code.
      if (hazard.OpCode == OpCodes.Ldftn || hazard.OpCode == OpCodes.Ldvirtftn) continue;

      int methodToken = hazard.MethodToken;
      if (methodToken == 0) continue;

      MethodBase? target;
      try { target = module.ResolveMethod(methodToken); }
      catch { continue; }
      if (target == null) continue;

      HazardKind kind;
      string detail;

      if (target.DeclaringType?.FullName == NativeMethodsTypeName)
      {
        kind = HazardKind.NativeCall;
        detail = target.Name;
        if (target.GetParameters().Any(p => typeof(SafeHandle).IsAssignableFrom(p.ParameterType)))
        {
          yield return new HazardSite(type.Name, method.Name, hazard.Offset, kind, detail,
            AnchorMechanism.SafeHandleParameter, AnchorVerdict.Safe,
            "the CLR P/Invoke stub refcounts the SafeHandle parameter");
          continue;
        }
      }
      else if (target == DangerousGetHandleMethod)
      {
        kind = HazardKind.DangerousGetHandle;
        detail = "SafeHandle.DangerousGetHandle";
      }
      // H3: a managed object materialised from a raw pointer. Ref-struct wrappers such as Span<T>
      // are excluded -- they own nothing, cannot be stored, and cannot outlive the expression that
      // created them, so there is no owner for the GC to collect underneath them.
      else if (target is ConstructorInfo ctor
               && ctor.GetParameters().Any(p => p.ParameterType.IsPointer)
               && ctor.DeclaringType is { IsByRefLike: false })
      {
        kind = HazardKind.ManagedAllocationFromPointer;
        detail = $"new {target.DeclaringType?.Name}";
      }
      else
      {
        continue;
      }

      var constructed = kind == HazardKind.ManagedAllocationFromPointer ? target.DeclaringType : null;
      yield return Classify(module, type, method, graph, i, kind, detail, ownership, constructed);
    }
  }

  private static HazardSite Classify(
    Module module, Type type, MethodBase method, IlControlFlowGraph graph, int hazardIndex,
    HazardKind kind, string detail, OwnershipClass ownership, Type? constructedType)
  {
    var instructions = graph.Instructions;
    var hazard = instructions[hazardIndex];
    bool isStatic = method.IsStatic;

    // M6: the raw handle or pointer leaves this frame, so the anchoring obligation is the caller's.
    // That is exactly the SafeHandle contract: DangerousGetHandle is legal only when the caller has
    // "otherwise ensured" the handle is not released.
    if (kind == HazardKind.DangerousGetHandle || PointerCameFromParameter(module, method, instructions, hazardIndex))
    {
      return new HazardSite(type.Name, method.Name, hazard.Offset, kind, detail,
        AnchorMechanism.DelegatedToCaller, AnchorVerdict.NeedsReview,
        kind == HazardKind.DangerousGetHandle
          ? "DangerousGetHandle hands a raw handle out of this frame: the caller must keep the SafeHandle alive"
          : "the native pointer is a method parameter: the anchoring obligation belongs to the call site");
    }

    // Doc form R2 (subsequent token/receiver argument): the constructed object receives the raw
    // pointer together with the lifetime token or the SafeHandle, so it anchors itself.
    if (constructedType != null && ReceivesAnchorArgument(constructedType))
    {
      var resultOwnership = ClassifyOwnership(constructedType);
      return Decide(type, method, hazard, kind, detail, AnchorMechanism.CarriesOwnAnchor,
        "the constructed " + constructedType.Name + " receives the owner's lifetime token or SafeHandle as a live constructor argument",
        resultOwnership == OwnershipClass.None ? OwnershipClass.Token : resultOwnership);
    }

    if (!isStatic && FindKeepAlive(module, instructions, hazardIndex, graph) >= 0)
    {
      return Decide(type, method, hazard, kind, detail, AnchorMechanism.KeepAlive,
        "GC.KeepAlive(this) post-dominates the hazard", ownership);
    }

    if (HasAddRefReleaseRegion(module, instructions, hazardIndex))
    {
      return new HazardSite(type.Name, method.Name, hazard.Offset, kind, detail,
        AnchorMechanism.AddRefRelease, AnchorVerdict.NeedsReview,
        "DangerousAddRef/DangerousRelease pairing is a path property and cannot be proven by IL inspection");
    }

    if (!isStatic
        && PointerCameFromInstanceField(module, instructions, hazardIndex, type)
        && (UsesThisAfter(module, instructions, hazardIndex, type)
            || ThisIsOnEvaluationStack(module, instructions, hazardIndex)))
    {
      return Decide(type, method, hazard, kind, detail, AnchorMechanism.ReceiverOnStack,
        UsesThisAfter(module, instructions, hazardIndex, type)
          ? "the receiver is live after the hazard"
          : "the receiver is on the evaluation stack while the hazard fires",
        ownership);
    }

    if (IsCallbackSnapshot(type, method))
    {
      return new HazardSite(type.Name, method.Name, hazard.Offset, kind, detail,
        AnchorMechanism.SyncCallbackFrame, AnchorVerdict.NeedsReview,
        "the anchor is the managed caller of the synchronous callback, outside this method body");
    }

    // Nothing was claimed. Reporting a defect requires two things: `this` actually produced the
    // pointer, and `this` is a type that owns native memory. A managed snapshot such as
    // PackageVersion holds only strings, so nothing a finalizer does could free what this call
    // reads, and attributing ownership of a native pointer to it would be a false positive.
    var ownedByThis = ownership != OwnershipClass.None
                      && !isStatic
                      && PointerCameFromInstanceField(module, instructions, hazardIndex, type);
    if (ownedByThis)
    {
      return new HazardSite(type.Name, method.Name, hazard.Offset, kind, detail,
        AnchorMechanism.None, AnchorVerdict.Unsafe,
        "`this` owns the native pointer but nothing keeps `this` alive past the hazard");
    }
    return new HazardSite(type.Name, method.Name, hazard.Offset, kind, detail,
      AnchorMechanism.None, AnchorVerdict.NeedsReview,
      ownership == OwnershipClass.None
        ? "the type owns no native memory, so no finalizer of it could free what this call reads"
        : string.Format(
            "no mechanism claimed; provenance did not tie the pointer to the instance "
            + "(owns={0}, fromParameter={1}, fromThis={2}, isStatic={3})",
            ownership,
            PointerCameFromParameter(module, method, instructions, hazardIndex),
            PointerCameFromInstanceField(module, instructions, hazardIndex, type),
            isStatic)
          + " window=[" + string.Join(" ", instructions
              .Skip(Math.Max(0, hazardIndex - 12))
              .Take(hazardIndex - Math.Max(0, hazardIndex - 12))
              .Select(i => i.OpCode.Name))
          + "]");
  }

  /// <summary>
  /// Turns a reachability claim into a verdict. Reachability of <c>this</c> only implies memory
  /// safety when <c>this</c> transitively owns the memory, so a snapshot type is never safe here.
  /// </summary>
  private static HazardSite Decide(
    Type type, MethodBase method, IlInstruction hazard, HazardKind kind, string detail,
    AnchorMechanism mechanism, string proof, OwnershipClass ownership)
  {
    var verdict = ownership switch
    {
      OwnershipClass.Strong => AnchorVerdict.Safe,
      OwnershipClass.Token => AnchorVerdict.NeedsReview,
      // A mechanism IS claimed but IL cannot show it roots the memory that was read.
      _ => AnchorVerdict.NeedsReview,
    };
    var reason = ownership switch
    {
      OwnershipClass.Strong => proof,
      OwnershipClass.Token => proof + "; ownership reaches the native memory only through a Lifetime? token that may be null",
      _ => proof + "; but `this` owns no SafeHandle and no lifetime token, so keeping it alive may not keep the memory alive",
    };
    return new HazardSite(type.Name, method.Name, hazard.Offset, kind, detail, mechanism, verdict, reason);
  }

  private static int FindKeepAlive(
    Module module, IReadOnlyList<IlInstruction> instructions, int hazardIndex, IlControlFlowGraph graph)
  {
    var hazard = instructions[hazardIndex];
    for (int j = hazardIndex + 1; j < instructions.Count; j++)
    {
      var candidate = instructions[j];
      if (candidate.MethodToken == 0) continue;
      if (Resolve(module, candidate.MethodToken) != KeepAliveMethod) continue;
      if (j == 0 || !IsLoadThis(instructions[j - 1])) continue;
      if (graph.PostDominates(hazard.Offset, candidate.Offset)) return j;
    }
    return -1;
  }

  private static MethodBase? Resolve(Module module, int token)
  {
    try { return module.ResolveMethod(token); }
    catch { return null; }
  }

  private static bool IsLoadThis(IlInstruction instruction) =>
    instruction.OpCode.Value is unchecked(0x02) // ldarg.0
    || (instruction.OpCode.Value is unchecked(0x0E) && instruction.VariableIndex == 0); // ldarg.s 0

  private static bool UsesThisAfter(
    Module module, IReadOnlyList<IlInstruction> instructions, int hazardIndex, Type type)
  {
    for (int j = hazardIndex + 1; j < instructions.Count; j++)
    {
      if (IsLoadThis(instructions[j])) return true;

      // Doc form R1 (subsequent field assignment "field ??= NativeString.FromNative(...)"): a store
      // into one of this's own fields after the hazard forces the JIT to keep this live across it.
      // A store into a FOREIGN object's field must not count: that is the stale-value trap.
      if (instructions[j].FieldToken != 0)
      {
        var field = ResolveField(module, instructions[j].FieldToken);
        if (field is { IsStatic: false } && field.DeclaringType is { } declaring
            && declaring.IsAssignableFrom(type))
        {
          return true;
        }
      }
    }
    return false;
  }

  /// <summary>
  /// Doc form R4: <c>this</c> was pushed before the hazard and is still on the evaluation stack
  /// when it fires, so the GC sees it even though this method never reads it again.
  /// </summary>
  /// <remarks>
  /// <c>AlpmOptions.SetStringOption</c> is the shape that needs this: it compiles to
  /// <c>ThrowIfError(setter(_handle, buffer.Ptr))</c>, which pushes <c>this</c> as the pending
  /// receiver, evaluates the arguments, fires the calli, and only afterwards spends the receiver.
  /// A forward scan for a later use of <c>this</c> finds nothing and reports the site unanchored.
  /// The walk runs each candidate load of <c>this</c> forward to the hazard and requires the depth
  /// never to fall back to it. Any branch, switch, throw, or instruction whose stack effect cannot
  /// be resolved abandons the candidate instead of guessing.
  /// </remarks>
  private static bool ThisIsOnEvaluationStack(
    Module module, IReadOnlyList<IlInstruction> instructions, int hazardIndex)
  {
    for (int c = hazardIndex - 1; c >= 0 && c >= hazardIndex - 24; c--)
    {
      if (!IsLoadThis(instructions[c])) continue;

      int depth = 1;
      bool consumed = false;
      for (int j = c + 1; j < hazardIndex; j++)
      {
        var op = instructions[j].OpCode;
        if (op.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch
            or FlowControl.Return or FlowControl.Throw)
        {
          consumed = true;
          break;
        }

        var pop = StackPop(module, instructions[j]);
        var push = StackPush(module, instructions[j]);
        if (pop == null || push == null) { consumed = true; break; }

        depth += push.Value - pop.Value;
        if (depth <= 0) { consumed = true; break; }
      }

      if (!consumed && depth >= 1) return true;
    }
    return false;
  }

  /// <summary>Values an instruction removes from the evaluation stack, or null when unresolvable.</summary>
  private static int? StackPop(Module module, IlInstruction ins) =>
    ins.OpCode.StackBehaviourPop switch
    {
      StackBehaviour.Pop0 => 0,
      StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
      StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi
        or StackBehaviour.Popi_popi8 or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8
        or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
      StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi
        or StackBehaviour.Popref_popi_pop1 => 3,
      StackBehaviour.Varpop => ResolvedCallArity(module, ins),
      _ => null,
    };

  /// <summary>
  /// The throw-expression rule: no call inside a <c>throw</c> expression may reach native memory.
  /// </summary>
  /// <remarks>
  /// A <c>GC.KeepAlive(owner)</c> keeps the owner alive only up to its own instruction. When the
  /// native read happens inside the exception-construction expression, an anchor written before that
  /// expression does not cover it, and the whole expression is one sequencing problem that the
  /// per-call-site audit cannot see: the read lives in another method, whose obligation is recorded
  /// as "delegated to the caller" without anyone checking that the caller discharged it.
  /// <para>
  /// A call is treated as reaching native memory when it targets a binding entry point, or when any
  /// of its parameters is a pointer. The second half matters because the trap appeared through a
  /// helper (<c>NativeCall.Failure(_alpm_handle_t*, string)</c>), not through a direct binding call -
  /// "no <c>NativeMethods.</c> call in a throw" would have missed it. That overload is gone now; this
  /// rule keeps the shape from coming back.
  /// </para>
  /// </remarks>
  public static IReadOnlyList<string> FindPointerCallsInsideThrowExpressions(IEnumerable<Type> types)
  {
    var offenders = new List<string>();
    foreach (var type in types)
    {
      foreach (var method in GetScannableMethods(type))
      {
        var graph = IlControlFlowGraph.Build(method);
        if (graph == null) continue;

        var instructions = graph.Instructions;
        var module = method.Module;

        for (int i = 0; i < instructions.Count; i++)
        {
          if (instructions[i].OpCode != OpCodes.Throw) continue;

          // Walk back over the expression that produced the thrown value: it starts out needing the
          // one value the throw consumes, and ends when every value it uses is accounted for.
          int need = 1;
          for (int j = i - 1; j >= 0 && need > 0; j--)
          {
            var current = instructions[j];
            if (current.IsBranch) break;

            var pop = StackPop(module, current);
            var push = StackPush(module, current);
            if (pop == null || push == null) break;

            if (current.IsCall && CallReachesNativeMemory(module, current))
            {
              offenders.Add($"{type.Name}.{method.Name}(IL_{current.Offset:x4})");
            }

            need = need - push.Value + pop.Value;
          }
        }
      }
    }

    return offenders;
  }

  /// <summary>Whether a call can read native memory: a binding entry point, or any pointer parameter.</summary>
  private static bool CallReachesNativeMemory(Module module, IlInstruction ins)
  {
    if (ins.MethodToken == 0) return false;
    if (Resolve(module, ins.MethodToken) is not MethodBase target) return false;

    if (target.DeclaringType?.FullName?.StartsWith("Pacpar.Alpm.Bindings.NativeMethods", StringComparison.Ordinal) == true)
    {
      return true;
    }

    return target.GetParameters().Any(p => p.ParameterType.IsPointer);
  }

  /// <summary>Values an instruction leaves on the evaluation stack, or null when unresolvable.</summary>
  private static int? StackPush(Module module, IlInstruction ins) =>
    ins.OpCode.StackBehaviourPush switch
    {
      StackBehaviour.Push0 => 0,
      StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8
        or StackBehaviour.Pushr4 or StackBehaviour.Pushr8 or StackBehaviour.Pushref => 1,
      StackBehaviour.Push1_push1 => 2,
      StackBehaviour.Varpush => ins.MethodToken == 0
        ? null
        : Resolve(module, ins.MethodToken) is MethodInfo target
            && target.ReturnType != typeof(void) ? 1 : 0,
      _ => null,
    };

  /// <summary>
  /// Argument count for <c>call</c>/<c>callvirt</c>/<c>newobj</c>, counting the receiver, or null
  /// when the target cannot be resolved. <c>ret</c> and <c>throw</c> also report VarPop but carry
  /// no method token, so they fall out as null and the candidate is abandoned.
  /// </summary>
  private static int? ResolvedCallArity(Module module, IlInstruction ins)
  {
    if (ins.MethodToken == 0) return null;
    var target = Resolve(module, ins.MethodToken);
    return target switch
    {
      ConstructorInfo ctor => ctor.GetParameters().Length,
      MethodInfo method => method.GetParameters().Length + (method.IsStatic ? 0 : 1),
      _ => null,
    };
  }

  /// <summary>
  /// True when the hazard's native pointer was produced by loading an instance field of <c>this</c>.
  /// </summary>
  private static bool PointerCameFromInstanceField(
    Module module, IReadOnlyList<IlInstruction> instructions, int hazardIndex, Type type)
  {
    var fieldNames = InstanceFieldNames(type).ToHashSet(StringComparer.Ordinal);

    for (int j = hazardIndex - 1; j >= 0 && j >= hazardIndex - 12; j--)
    {
      // Only an IMMEDIATELY preceding ldarg.0 pairs with an ldfld: IL pushes the object reference
      // and consumes it on the next instruction. Pairing across a gap mismatches the two (seen on
      // Database.GetPackage, whose window is "ldarg.0 ldfld backingStruct ldloc.1 ldfld"): the scan
      // reached nameBuf's ldfld first and judged it against Database's fields, returning false
      // before ever reaching the real backingStruct pair.
      if (instructions[j].FieldToken != 0)
      {
        if (j > 0 && IsLoadThis(instructions[j - 1]))
        {
          var field = ResolveField(module, instructions[j].FieldToken);
          if (field != null && fieldNames.Contains(field.Name)) return true;
        }
        continue;
      }

      // The pointer may instead come from a parameterless instance accessor on `this`, such as
      // `Alpm.Handle`, which reads the SafeHandle field inside its own body. Following that one hop
      // keeps provenance; `newobj` never matches because it resolves to a ConstructorInfo.
      if (instructions[j].MethodToken == 0) continue;
      var target = Resolve(module, instructions[j].MethodToken);
      if (target is MethodInfo { IsStatic: false } accessor
          && accessor.GetParameters().Length == 0
          && accessor.DeclaringType is { } declaring
          && declaring.IsAssignableFrom(type)
          && j > 0 && IsLoadThis(instructions[j - 1]))
      {
        return true;
      }
    }
    return false;
  }

  /// <summary>
  /// True when the hazard's native pointer arrived as a method parameter rather than being read
  /// from an instance field of <c>this</c>. The anchoring obligation is then the caller's.
  /// </summary>
  private static bool PointerCameFromParameter(
    Module module, MethodBase method, IReadOnlyList<IlInstruction> instructions, int hazardIndex)
  {
    var parameters = method.GetParameters();
    int thisOffset = method.IsStatic ? 0 : 1;

    for (int j = hazardIndex - 1; j >= 0 && j >= hazardIndex - 12; j--)
    {
      var value = instructions[j].OpCode.Value;
      int argIndex = value switch
      {
        _ when value == unchecked(0x02) => 0, // ldarg.0
        _ when value == unchecked(0x03) => 1, // ldarg.1
        _ when value == unchecked(0x04) => 2, // ldarg.2
        _ when value == unchecked(0x05) => 3, // ldarg.3
        // ldarg.s / ldarga.s carry the index in the operand byte.
        _ when value == unchecked(0x0E) || value == unchecked(0x0F) => instructions[j].VariableIndex,
        _ => -1,
      };
      if (argIndex < 0) continue;

      // The obligation moves to the call site only when the POINTER itself arrived as a parameter.
      // Seeing a string or a flags argument beside the native call proves nothing about ownership.
      int parameterIndex = argIndex - thisOffset;
      if (parameterIndex >= 0 && parameterIndex < parameters.Length
          && parameters[parameterIndex].ParameterType.IsPointer)
      {
        return true;
      }
    }
    return false;
  }

  /// <summary>
  /// True when one of the type's constructors takes the owner's lifetime token or SafeHandle, so
  /// that argument is a live stack reference for the whole constructor body even if the result
  /// later drops it (doc form R2: a subsequent token/receiver argument).
  /// </summary>
  private static bool ReceivesAnchorArgument(Type constructedType) =>
    constructedType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
      .Any(c => c.GetParameters().Any(p =>
        typeof(SafeHandle).IsAssignableFrom(p.ParameterType)
        || typeof(Lifetime).IsAssignableFrom(p.ParameterType)
        || p.ParameterType.Name.StartsWith("Lifetime", StringComparison.Ordinal)
        || p.ParameterType.Name.EndsWith("Lifetime", StringComparison.Ordinal)));

  private static IEnumerable<string> InstanceFieldNames(Type type)
  {
    for (var cur = type; cur != null && cur != typeof(object); cur = cur.BaseType)
    {
      foreach (var f in cur.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
      {
        yield return f.Name;
      }
    }
  }

  private static FieldInfo? ResolveField(Module module, int token)
  {
    try { return module.ResolveField(token); }
    catch { return null; }
  }

  /// <summary>
  /// True when a <c>DangerousAddRef</c>/<c>DangerousRelease</c> region surrounds the hazard.
  /// Pairing is a path property, so this only ever <em>claims</em> the mechanism.
  /// </summary>
  private static bool HasAddRefReleaseRegion(
    Module module, IReadOnlyList<IlInstruction> instructions, int hazardIndex)
  {
    bool addRef = false, releaseAfter = false;
    for (int j = 0; j < instructions.Count; j++)
    {
      int token = instructions[j].MethodToken;
      if (token == 0) continue;
      var target = Resolve(module, token);
      if (target == DangerousAddRefMethod && j < hazardIndex) addRef = true;
      if (target == DangerousReleaseMethod && j > hazardIndex) releaseAfter = true;
    }
    return addRef && releaseAfter;
  }

  private static bool IsCallbackSnapshot(Type type, MethodBase method) =>
    method.IsConstructor
    && type.Namespace?.StartsWith("Pacpar.Alpm.Events", StringComparison.Ordinal) == true;

  /// <summary>Transitive ownership strength of a type.</summary>
  public static OwnershipClass ClassifyOwnership(Type type)
  {
    bool hasSafeHandle = false, hasRootedToken = false, hasNullableToken = false;
    for (var cur = type; cur != null && cur != typeof(object); cur = cur.BaseType)
    {
      foreach (var f in cur.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
      {
        if (typeof(SafeHandle).IsAssignableFrom(f.FieldType)) hasSafeHandle = true;
        if (typeof(Lifetime).IsAssignableFrom(f.FieldType)
            || f.FieldType.Name.StartsWith("Lifetime", StringComparison.Ordinal)
            || f.FieldType.Name.EndsWith("Lifetime", StringComparison.Ordinal))
        {
          if (IsNrtNullable(f)) hasNullableToken = true;
          else hasRootedToken = true;
        }
      }
    }
    if (hasSafeHandle) return OwnershipClass.Strong;
    // A non-nullable token is as good as the handle itself. Lifetime's own documentation states the
    // invariant: "any live token keeps the owner reachable in one hop" (Alpm.cs) - a token field that
    // can never be null makes that hop unconditional, so reaching `this` reaches the SafeHandle.
    if (hasRootedToken) return OwnershipClass.Strong;
    if (hasNullableToken) return OwnershipClass.Token;
    return OwnershipClass.None;
  }

  private static readonly NullabilityInfoContext Nullability = new();

  /// <summary>
  /// True when a reference-typed field is annotated <c>Lifetime?</c>. Reflection reports the same
  /// <see cref="Type"/> either way, because nullability of a reference type is metadata only;
  /// <see cref="NullabilityInfoContext"/> is the only surface that still carries the annotation.
  /// </summary>
  private static bool IsNrtNullable(FieldInfo field) =>
    !field.FieldType.IsValueType && Nullability.Create(field).WriteState != NullabilityState.NotNull;

  /// <summary>
  /// Parses <c>gc-anchor-allowlist.txt</c>. Each line is <c>site | mechanism | reason</c>, where
  /// <c>site</c> is <c>Type</c> or <c>Type.Member</c>. Blank lines and <c>#</c> comments are ignored.
  /// </summary>
  public static Dictionary<string, string> ParseAllowlist(string text)
  {
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var raw in text.Replace("\r", "").Split('\n'))
    {
      var line = raw.Trim();
      if (line.Length == 0 || line.StartsWith('#')) continue;
      var parts = line.Split('|', 3);
      if (parts.Length < 3) continue;
      result[parts[0].Trim()] = $"{parts[1].Trim()}: {parts[2].Trim()}";
    }
    return result;
  }

  /// <summary>
  /// True when the allowlist covers this site. A member-level entry names the site exactly and
  /// excuses any verdict. A type-level entry records a type-wide invariant, so it excuses only
  /// NeedsReview: a site that claims no mechanism at all must be named member by member, which
  /// stops a newly added unanchored member from hiding behind its type.
  /// </summary>
  public static bool IsAllowlisted(IReadOnlyDictionary<string, string> allowlist, HazardSite site)
  {
    if (allowlist.ContainsKey(site.Key)) return true;
    if (site.Verdict == AnchorVerdict.Unsafe) return false;
    int dot = site.Key.IndexOf('.');
    return dot > 0 && allowlist.ContainsKey(site.Key[..dot]);
  }
}

using System.Reflection;
using System.Reflection.Emit;

namespace Pacpar.Alpm.Tests.Unit.GcAnchor;

/// <summary>
/// A decoded IL instruction: its offset, opcode, and the exact bytes of its operand.
/// </summary>
/// <remarks>
/// The GC anchor audit needs instruction-level offsets because the byte-level scan it replaces
/// cannot tell an opcode from an operand byte: the constant <c>0x200</c> encodes as
/// <c>20 00 02 00 00</c>, and the <c>0x02</c> in the middle is an operand byte of <c>ldc.i4</c>,
/// not <c>ldarg.0</c>. Decoding removes that whole class of misreads.
/// </remarks>
internal readonly record struct IlInstruction(
  int Offset,
  OpCode OpCode,
  int OperandOffset,
  int OperandSize,
  byte[] OperandBytes)
{
  /// <summary>Offset of the instruction that follows this one.</summary>
  public int NextOffset => OperandOffset + OperandSize;

  /// <summary>The metadata token for a method (call, callvirt, newobj, jmp, calli, ldftn), or 0.</summary>
  public int MethodToken => OpCode.OperandType == OperandType.InlineMethod ? Token : 0;

  /// <summary>The metadata token for a field (ldfld, stfld, ldsfld, stsfld, ldflda, ldsflda), or 0.</summary>
  public int FieldToken => OpCode.OperandType == OperandType.InlineField ? Token : 0;

  /// <summary>True when this instruction actually transfers control to another method body.</summary>
  public bool IsCall => OpCode.FlowControl == FlowControl.Call;

  /// <summary>True for unconditional branches (br, leave) and conditional branches (brtrue, beq, ...).</summary>
  public bool IsBranch => OpCode.OperandType is OperandType.InlineBrTarget or OperandType.ShortInlineBrTarget;

  /// <summary>
  /// Absolute offset of the branch target, or <c>-1</c> when this is not a single-target branch.
  /// </summary>
  public int BranchTarget => OpCode.OperandType switch
  {
    OperandType.ShortInlineBrTarget => NextOffset + unchecked((sbyte)OperandBytes[0]),
    OperandType.InlineBrTarget => NextOffset + BitConverter.ToInt32(OperandBytes, 0),
    _ => -1,
  };

  /// <summary>True when control can fall through to the next instruction.</summary>
  public bool FallsThrough => OpCode.FlowControl is not (FlowControl.Branch or FlowControl.Return or FlowControl.Throw);

  /// <summary>
  /// Targets of an <c>InlineSwitch</c> opcode, or an empty span for anything else.
  /// </summary>
  public ReadOnlySpan<int> SwitchTargets()
  {
    if (OpCode.OperandType != OperandType.InlineSwitch) return ReadOnlySpan<int>.Empty;
    int count = BitConverter.ToInt32(OperandBytes, 0);
    if (count < 0 || count > 4096) return ReadOnlySpan<int>.Empty;
    var targets = new int[count];
    int baseOffset = NextOffset;
    for (int i = 0; i < count; i++)
    {
      targets[i] = baseOffset + BitConverter.ToInt32(OperandBytes, 4 + 4 * i);
    }
    return targets;
  }

  /// <summary>The raw 4-byte metadata token, for token-bearing opcodes.</summary>
  private int Token => OperandSize >= 4 ? BitConverter.ToInt32(OperandBytes, 0) : 0;

  /// <summary>The short argument or local index, for <c>ldarg.s</c>/<c>stloc.s</c>-style opcodes.</summary>
  public int VariableIndex => OperandSize >= 1 ? OperandBytes[0] : 0;
}

/// <summary>
/// Decodes a method's IL byte array into instructions whose operand sizes are exact.
/// </summary>
/// <remarks>
/// The opcode-to-operand-size table is derived from <see cref="System.Reflection.Emit.OpCodes"/>
/// itself at startup rather than hand-written, so it can never drift from the runtime's encoding.
/// </remarks>
internal static class IlDecoder
{
  private static readonly Dictionary<int, (OpCode Op, int Size)> Table = BuildTable();

  private static Dictionary<int, (OpCode Op, int Size)> BuildTable()
  {
    var table = new Dictionary<int, (OpCode, int)>(512);
    foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
    {
      if (field.FieldType != typeof(OpCode)) continue;
      var op = (OpCode)field.GetValue(null)!;
      table[KeyOf(op)] = (op, OperandSizeOf(op.OperandType));
    }
    return table;
  }

  private static int OperandSizeOf(OperandType type) => type switch
  {
    OperandType.InlineNone => 0,
    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar or OperandType.ShortInlineR => 1,
    OperandType.InlineVar => 2,
    OperandType.InlineI8 => 8,
    OperandType.InlineSwitch => -1, // sized at decode time from the trailing count
    OperandType.InlineBrTarget or OperandType.InlineI or OperandType.InlineField or OperandType.InlineMethod
      or OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType
      or OperandType.InlineR => 4,
    _ => 0,
  };

  private static int KeyOf(OpCode op) =>
    op.Size == 1 ? (op.Value & 0xFF) : ((0xFE << 8) | (op.Value & 0xFF));

  /// <summary>
  /// Decodes <paramref name="il"/> into instructions. An undecodable region ends decoding there,
  /// so a malformed method degrades to "cannot prove" rather than "proven safe".
  /// </summary>
  public static List<IlInstruction> Decode(byte[] il)
  {
    var result = new List<IlInstruction>(Math.Max(4, il.Length / 3));
    int offset = 0;
    while (offset < il.Length)
    {
      int start = offset;
      int b = il[offset++];
      int key = b;
      if (b == 0xFE)
      {
        if (offset >= il.Length) break;
        key = (0xFE << 8) | il[offset++];
      }

      if (!Table.TryGetValue(key, out var entry)) break;

      int size = entry.Size;
      if (size == -1)
      {
        if (offset + 4 > il.Length) break;
        int count = BitConverter.ToInt32(il, offset);
        if (count < 0 || count > 4096) break;
        size = 4 + 4 * count;
      }

      if (offset + size > il.Length) break;

      var operand = new byte[size];
      Array.Copy(il, offset, operand, 0, size);
      result.Add(new IlInstruction(start, entry.Op, offset, size, operand));
      offset += size;
    }
    return result;
  }
}

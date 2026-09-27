using System.Reflection;
using System.Reflection.Emit;

namespace Pacpar.Alpm.Tests.Unit.GcAnchor;

/// <summary>
/// A basic block of IL: a maximal run of instructions with a single entry point.
/// </summary>
internal sealed class IlBlock(int index, int startOffset)
{
  public int Index { get; } = index;
  public int StartOffset { get; } = startOffset;
  public int EndOffset { get; set; }
  /// <summary>Indices of successor blocks. Duplicate edges are harmless to post-dominance.</summary>
  public List<int> Successors { get; } = [];
  /// <summary>True when the block's last instruction returns or throws.</summary>
  public bool ExitsMethod { get; set; }
  /// <summary>IL offsets of the instructions in this block, ascending.</summary>
  public List<int> InstructionOffsets { get; } = [];
}

/// <summary>
/// Control-flow graph over a method body, used to decide whether an anchor post-dominates a hazard.
/// </summary>
/// <remarks>
/// Post-dominance is the honest generalisation of "the anchor is on every path after the hazard".
/// Exception-handler edges are included from every instruction inside a protected region, which
/// makes the graph <em>harder</em> to prove things about. That is the safe direction: the audit
/// reports <c>NeedsReview</c> instead of a false <c>Safe</c> whenever control flow is exotic.
/// </remarks>
internal sealed class IlControlFlowGraph
{
  private readonly List<IlInstruction> _instructions;
  private readonly Dictionary<int, IlBlock> _blocksByOffset = [];
  private readonly List<IlBlock> _blocks = [];
  private readonly Dictionary<int, IlInstruction> _byOffset = [];
  private readonly HashSet<int>[] _postDominators;

  public IReadOnlyList<IlBlock> Blocks => _blocks;
  public IReadOnlyList<IlInstruction> Instructions => _instructions;

  private IlControlFlowGraph(List<IlInstruction> instructions, MethodBody body)
  {
    _instructions = instructions;
    foreach (var instruction in instructions) _byOffset[instruction.Offset] = instruction;
    BuildBlocks(body);
    _postDominators = ComputePostDominators();
  }

  public static IlControlFlowGraph? Build(MethodBase method)
  {
    var body = method.GetMethodBody();
    var il = body?.GetILAsByteArray();
    if (il == null) return null;
    var instructions = IlDecoder.Decode(il);
    if (instructions.Count == 0) return null;
    return new IlControlFlowGraph(instructions, body!);
  }

  /// <summary>The block containing <paramref name="offset"/>, or -1 when the offset is not decoded.</summary>
  public int BlockIndexOf(int offset)
  {
    foreach (var block in _blocks)
    {
      if (offset >= block.StartOffset && offset < block.EndOffset) return block.Index;
    }
    return -1;
  }

  /// <summary>
  /// True when every path from <paramref name="from"/> to the end of the method passes through
  /// <paramref name="through"/>. Same block is trivially true.
  /// </summary>
  public bool PostDominates(int from, int through)
  {
    int a = BlockIndexOf(from), b = BlockIndexOf(through);
    if (a < 0 || b < 0) return false;
    return _postDominators[a].Contains(b);
  }

  private void BuildBlocks(MethodBody body)
  {
    var leaders = new SortedSet<int> { 0 };
    foreach (var instruction in _instructions)
    {
      int target = instruction.BranchTarget;
      if (target >= 0) leaders.Add(target);
      foreach (var t in instruction.SwitchTargets()) leaders.Add(t);
      if (!instruction.FallsThrough || instruction.IsBranch) leaders.Add(instruction.NextOffset);
    }
    foreach (var clause in body.ExceptionHandlingClauses)
    {
      leaders.Add(clause.TryOffset);
      leaders.Add(clause.HandlerOffset);
      leaders.Add(clause.TryOffset + clause.TryLength);
      if (clause.Flags == ExceptionHandlingClauseOptions.Filter) leaders.Add(clause.FilterOffset);
    }

    var instructionOffsets = new HashSet<int>(_instructions.Select(i => i.Offset));
    var validLeaders = new HashSet<int>(leaders.Where(l => l == 0 || instructionOffsets.Contains(l)));

    int blockIndex = 0;
    IlBlock? current = null;
    foreach (var instruction in _instructions)
    {
      if (current == null || validLeaders.Contains(instruction.Offset))
      {
        current = new IlBlock(blockIndex++, instruction.Offset);
        _blocks.Add(current);
        _blocksByOffset[current.StartOffset] = current;
      }
      current.InstructionOffsets.Add(instruction.Offset);
      current.EndOffset = instruction.NextOffset;
    }

    for (int i = 0; i < _blocks.Count; i++)
    {
      var block = _blocks[i];
      var last = _byOffset[block.InstructionOffsets[^1]];

      if (last.IsBranch && last.BranchTarget >= 0 && _blocksByOffset.TryGetValue(last.BranchTarget, out var branchTarget))
      {
        block.Successors.Add(branchTarget.Index);
      }
      foreach (var target in last.SwitchTargets())
      {
        if (_blocksByOffset.TryGetValue(target, out var t)) block.Successors.Add(t.Index);
      }

      // Fall-through exists for straight-line code, conditional branches and switches.
      bool fallsThrough = last.OpCode.FlowControl is FlowControl.Cond_Branch
        || (last.FallsThrough && !last.IsBranch && last.OpCode.OperandType != OperandType.InlineSwitch);
      if (fallsThrough && i + 1 < _blocks.Count) block.Successors.Add(_blocks[i + 1].Index);

      if (last.OpCode.FlowControl is FlowControl.Return or FlowControl.Throw) block.ExitsMethod = true;
    }

    // Conservative exception edges: anything inside a protected region can reach its handler.
    foreach (var clause in body.ExceptionHandlingClauses)
    {
      int tryEnd = clause.TryOffset + clause.TryLength;
      var handlerBlock = BlockAtOrAfter(clause.HandlerOffset);
      if (handlerBlock == null) continue;
      foreach (var block in _blocks)
      {
        if (block.StartOffset >= clause.TryOffset && block.StartOffset < tryEnd)
        {
          block.Successors.Add(handlerBlock.Index);
        }
      }
    }
  }

  private IlBlock? BlockAtOrAfter(int offset) =>
    _blocks.FirstOrDefault(b => b.StartOffset >= offset);

  private HashSet<int>[] ComputePostDominators()
  {
    int n = _blocks.Count;
    var sets = new HashSet<int>[n];
    var all = new HashSet<int>(Enumerable.Range(0, n));
    for (int i = 0; i < n; i++) sets[i] = new HashSet<int>(all);

    bool changed = true;
    int guard = 0;
    while (changed && guard++ < 64)
    {
      changed = false;
      for (int i = 0; i < n; i++)
      {
        if (_blocks[i].ExitsMethod)
        {
          var onlySelf = new HashSet<int> { i };
          if (!onlySelf.SetEquals(sets[i])) { sets[i] = onlySelf; changed = true; }
          continue;
        }

        HashSet<int>? intersection = null;
        foreach (var s in _blocks[i].Successors)
        {
          intersection = intersection == null
            ? new HashSet<int>(sets[s])
            : intersection.Intersect(sets[s]).ToHashSet();
        }
        var next = intersection ?? new HashSet<int>();
        next.Add(i);
        if (!next.SetEquals(sets[i])) { sets[i] = next; changed = true; }
      }
    }
    return sets;
  }
}

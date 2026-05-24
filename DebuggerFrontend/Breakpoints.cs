using LSLib.LS.Story.Compiler;
using LSTools.StoryCompiler;
using System.Runtime.InteropServices;

namespace LSTools.DebuggerFrontend;

public enum LineType
{
    NodeLine,
    RuleActionLine,
    GoalInitActionLine,
    GoalExitActionLine
}

public class LineDebugInfo
{
    public LineType Type { get; set; }
    public NodeDebugInfo? Node { get; set; }
    public required GoalDebugInfo Goal { get; set; }
    public uint ActionIndex { get; set; }
    public uint Line { get; set; }
}

public class GoalLineMap
{
    public required GoalDebugInfo Goal { get; set; }
    public Dictionary<uint, LineDebugInfo> LineMap { get; set; } = [];
}

public class CodeLocationTranslator(StoryDebugInfo debugInfo)
{
    private readonly Dictionary<string, GoalLineMap> _goalMap = [];

    public void Initialize() => BuildLineMap();

    public LineDebugInfo? LocationToNode(string goalName, uint line)
    {
        return _goalMap.TryGetValue(goalName, out var goalLineMap) &&
               goalLineMap.LineMap.TryGetValue(line, out var lineInfo)
               ? lineInfo
               : null;
    }

    private void AddLineMapping(LineType type, GoalDebugInfo goal, NodeDebugInfo? node, uint index, uint line)
    {
        ref var goalLineMap = ref CollectionsMarshal.GetValueRefOrAddDefault(_goalMap, goal.Name, out bool exists);
        if (!exists)
        {
            goalLineMap = new GoalLineMap { Goal = goal };
        }

        goalLineMap!.LineMap[line] = new LineDebugInfo
        {
            Type = type,
            Goal = goal,
            Node = node,
            ActionIndex = index,
            Line = line
        };
    }

    private void BuildLineMap(GoalDebugInfo goal)
    {
        for (var index = 0; index < goal.InitActions.Count; index++)
        {
            AddLineMapping(LineType.GoalInitActionLine, goal, null, (uint)index, goal.InitActions[index].Line);
        }

        for (var index = 0; index < goal.ExitActions.Count; index++)
        {
            AddLineMapping(LineType.GoalExitActionLine, goal, null, (uint)index, goal.ExitActions[index].Line);
        }
    }

    private void BuildLineMap(NodeDebugInfo node)
    {
        if (node.RuleId == 0) return;

        var rule = debugInfo.Rules[node.RuleId];
        var goal = debugInfo.Goals[rule.GoalId];

        var resolvedWireType = (NodeType)node.Type;

        if (node.Line != 0 && resolvedWireType != NodeType.Rule)
        {
            AddLineMapping(LineType.NodeLine, goal, node, 0, (uint)node.Line);
        }

        if (resolvedWireType == NodeType.Rule)
        {
            for (var index = 0; index < rule.Actions.Count; index++)
            {
                AddLineMapping(LineType.RuleActionLine, goal, node, (uint)index, rule.Actions[index].Line);
            }
        }
    }

    private void BuildLineMap()
    {
        foreach (var (_, goal) in debugInfo.Goals)
        {
            BuildLineMap(goal);
        }

        foreach (var (_, node) in debugInfo.Nodes)
        {
            BuildLineMap(node);
        }
    }
}

public class Breakpoint
{
    public uint Id { get; set; }
    public required DAPSource Source { get; set; }
    public string GoalName { get; set; } = string.Empty;
    public uint Line { get; set; }
    public LineDebugInfo? LineInfo { get; set; }
    public bool PermanentlyInvalid { get; set; }
    public bool Verified { get; set; }
    public string ErrorReason { get; set; } = string.Empty;

    public DAPBreakpoint ToDAP() => new()
    {
        Id = (int)Id,
        Verified = Verified,
        Message = ErrorReason,
        Source = Source,
        Line = (int)Line
    };
}

public class BreakpointManager(DebuggerClient client)
{
    private CodeLocationTranslator? _locationTranslator;
    private Dictionary<uint, Breakpoint> _breakpoints = [];

    private uint NextBreakpointId { get => field++; set; } = 1;

    public List<Breakpoint> DebugInfoLoaded(StoryDebugInfo debugInfo)
    {
        _locationTranslator = new CodeLocationTranslator(debugInfo);
        _locationTranslator.Initialize();

        var changes = RevalidateBreakpoints();
        UpdateBreakpointsOnBackend();
        return changes;
    }

    public List<Breakpoint> DebugInfoUnloaded()
    {
        _locationTranslator = null;
        return RevalidateBreakpoints();
    }

    public void ClearGoalBreakpoints(string goalName)
    {
        _breakpoints = _breakpoints
            .Where(kv => kv.Value.GoalName != goalName)
            .ToDictionary(static kv => kv.Key, static kv => kv.Value);
    }

    public Breakpoint AddBreakpoint(DAPSource source, DAPSourceBreakpoint breakpoint)
    {
        var bp = new Breakpoint
        {
            Id = NextBreakpointId,
            Source = source,
            GoalName = Path.GetFileNameWithoutExtension(source.Name) ?? string.Empty,
            Line = (uint)breakpoint.Line,
            PermanentlyInvalid = false
        };
        _breakpoints.Add(bp.Id, bp);

        if (breakpoint.Condition is { Length: > 0 } || breakpoint.HitCondition is { Length: > 0 })
        {
            bp.PermanentlyInvalid = true;
            bp.ErrorReason = "Conditional breakpoints are not supported";
        }

        ValidateBreakpoint(bp);
        return bp;
    }

    public void UpdateBreakpointsOnBackend()
    {
        List<Breakpoint> activeBreakpoints = [.. _breakpoints.Values.Where(static bp => bp.Verified)];
        client.SendSetBreakpoints(activeBreakpoints);
    }

    public bool ValidateBreakpoint(Breakpoint bp)
    {
        bool oldVerified = bp.Verified;
        string oldReason = bp.ErrorReason;
        var oldLineInfo = bp.LineInfo;

        if (bp.PermanentlyInvalid)
        {
            bp.Verified = false;
            return oldVerified != bp.Verified || oldReason != bp.ErrorReason;
        }

        if (_locationTranslator is null)
        {
            bp.Verified = false;
            bp.LineInfo = null;
            bp.ErrorReason = "No debug data loaded / story compilation state is inactive.";
            return oldVerified != bp.Verified || oldReason != bp.ErrorReason || oldLineInfo != bp.LineInfo;
        }

        var nodeInfo = _locationTranslator.LocationToNode(bp.GoalName, bp.Line);
        if (nodeInfo is null)
        {
            bp.Verified = false;
            bp.LineInfo = null;
            bp.ErrorReason = $"Line {bp.Line} could not be resolved to a valid structural Osiris query or action node block.";
        }
        else
        {
            bp.Verified = true;
            bp.LineInfo = nodeInfo;
            bp.ErrorReason = string.Empty;
        }

        return oldVerified != bp.Verified || oldReason != bp.ErrorReason || oldLineInfo != bp.LineInfo;
    }

    private List<Breakpoint> RevalidateBreakpoints()
    {
        List<Breakpoint> changes = [];
        foreach (var (_, bp) in _breakpoints)
        {
            if (ValidateBreakpoint(bp))
            {
                changes.Add(bp);
            }
        }
        return changes;
    }
}
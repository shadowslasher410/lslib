using LSLib.LS.Story;
using LSLib.LS.Story.Compiler;
using System.Text.RegularExpressions;

namespace LSTools.DebuggerFrontend;

public class DebugVariable
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public required MsgTypedValue TypedValue { get; set; }
}

public class CoalescedFrame
{
    public string Name { get; set; } = string.Empty;
    public string? File { get; set; }
    public int Line { get; set; }
    public required MsgFrame Frame { get; set; }
    public List<DebugVariable> Variables { get; set; } = [];
    public MsgTuple? CallArguments { get; set; }
    public RuleDebugInfo? Rule { get; set; }
}

public partial class StackTracePrinter(StoryDebugInfo debugInfo, ValueFormatter formatter)
{
    public bool MergeFrames { get; set; } = true;
    public string? ModUuid { get; set; }

    [GeneratedRegex(@".*\.pak:/Mods/.*/Story/RawFiles/Goals/(.*)\.txt", RegexOptions.IgnoreCase)]
    private static partial Regex ModPathRegex();

    private List<DebugVariable> TupleToVariables(MsgFrame frame)
    {
        var columns = frame.Tuple.Column;
        List<DebugVariable> variables = new(columns.Count);

        NodeDebugInfo? node = frame.NodeId != 0 ? debugInfo.Nodes[frame.NodeId] : null;
        RuleDebugInfo? rule = node is { RuleId: > 0 } ? debugInfo.Rules[node.RuleId] : null;

        for (var i = 0; i < columns.Count; i++)
        {
            var value = columns[i];

            string resolvedTypeName = (Value.Type)value.TypeId switch
            {
                Value.Type.None => "None",
                Value.Type.Integer => "Integer",
                Value.Type.Integer64 => "Integer64",
                Value.Type.Float => "Float",
                Value.Type.String => "String",
                Value.Type.GuidString => "GuidString",
                _ => $"Unknown({value.TypeId})"
            };

            variables.Add(new DebugVariable
            {
                Name = ValueFormatter.TupleVariableIndexToName(rule, node, i),
                Type = resolvedTypeName,
                Value = ValueFormatter.ValueToString(value),
                TypedValue = value
            });
        }

        return variables;
    }

    private CoalescedFrame MsgFrameToLocal(MsgFrame frame)
    {
        var outFrame = new CoalescedFrame
        {
            Frame = frame,
            Name = formatter.GetFrameDebugName(frame)
        };

        if (frame.Type is FrameType.GoalInitAction or FrameType.GoalExitAction)
        {
            var goal = debugInfo.Goals[frame.GoalId];
            outFrame.File = goal.Path;
            outFrame.Line = frame.Type == FrameType.GoalInitAction
                ? (int)goal.InitActions[(int)frame.ActionIndex].Line
                : (int)goal.ExitActions[(int)frame.ActionIndex].Line;
        }
        else if (frame.NodeId != 0)
        {
            var node = debugInfo.Nodes[frame.NodeId];
            if (node.RuleId != 0)
            {
                var rule = debugInfo.Rules[node.RuleId];
                var goal = debugInfo.Goals[rule.GoalId];
                outFrame.File = goal.Path;

                outFrame.Line = frame.Type switch
                {
                    FrameType.Pushdown when node.Type == Node.Type.Rule => (int)rule.ActionsStartLine,
                    FrameType.RuleAction => (int)rule.Actions[(int)frame.ActionIndex].Line,
                    _ => node.Line
                };
            }
        }

        outFrame.Variables = TupleToVariables(frame);

        if (outFrame.File is { Length: > 0 } && ModUuid is { Length: > 0 })
        {
            var match = ModPathRegex().Match(outFrame.File);
            if (match.Success)
            {
                outFrame.File = $"divinity:/{ModUuid}/{match.Groups[1].Value}.divgoal";
            }
        }

        return outFrame;
    }

    private static List<List<CoalescedFrame>> DetermineFrameRanges(List<CoalescedFrame> frames)
    {
        List<List<CoalescedFrame>> ranges = [];
        List<CoalescedFrame> currentFrames = [];

        foreach (var frame in frames)
        {
            if (frame.Frame.Type is FrameType.GoalInitAction or FrameType.GoalExitAction)
            {
                if (currentFrames.Count > 0)
                {
                    ranges.Add(currentFrames);
                    currentFrames = [];
                }

                ranges.Add([frame]);
            }
            else
            {
                if (frame.Frame.Type is FrameType.Insert or FrameType.Delete)
                {
                    if (currentFrames.Count > 0)
                    {
                        ranges.Add(currentFrames);
                        currentFrames = [];
                    }
                }

                currentFrames.Add(frame);

                if (frame.Frame.Type == FrameType.RuleAction)
                {
                    ranges.Add(currentFrames);
                    currentFrames = [];
                }
            }
        }

        if (currentFrames.Count > 0)
        {
            ranges.Add(currentFrames);
        }

        return ranges;
    }

    private CoalescedFrame MergeFrame(List<CoalescedFrame> range)
    {
        if (range.Count == 0)
        {
            throw new ArgumentException("Cannot resolve and merge an empty stack trace collection frame range.", nameof(range));
        }

        var frame = new CoalescedFrame
        {
            Frame = range[0].Frame
        };

        foreach (var node in range)
        {
            if (node.Line != 0)
            {
                frame.File = node.File;
                frame.Line = node.Line;
            }

            if (frame.Rule is null && node.Frame.NodeId != 0)
            {
                var storyNode = debugInfo.Nodes[node.Frame.NodeId];
                if (storyNode.RuleId != 0)
                {
                    frame.Rule = debugInfo.Rules[storyNode.RuleId];
                }
            }

            if (node.Frame.Type is FrameType.Pushdown
                                or FrameType.Insert
                                or FrameType.Delete)
            {
                frame.Variables = node.Variables;
            }

            if (node.Frame.Type is FrameType.Insert or FrameType.Delete)
            {
                frame.CallArguments = node.Frame.Tuple;
            }
        }

        frame.Variables ??= [];
        frame.Name = formatter.GetFrameName(frame.Frame, frame.CallArguments);

        if (range.Count >= 2
            && range[0].Frame.Type is FrameType.Insert or FrameType.Delete
            && range[1].Frame.Type is FrameType.Pushdown or FrameType.PushdownDelete)
        {
            var pushdownNode = debugInfo.Nodes[range[1].Frame.NodeId];
            if (range[0].Frame.NodeId != pushdownNode.ParentNodeId)
            {
                frame.Name = $"(Database Propagation) {frame.Name}";
            }
        }

        return frame;
    }

    private List<CoalescedFrame> MergeCallStack(List<CoalescedFrame> nodes)
    {
        var frameRanges = DetermineFrameRanges(nodes);
        return [.. frameRanges.Select(MergeFrame)];
    }

    public List<CoalescedFrame> BreakpointToStack(BkBreakpointTriggered message)
    {
        var rawFrames = message.CallStack.Select(MsgFrameToLocal).ToList();

        List<CoalescedFrame> mergedFrames = MergeFrames
            ? MergeCallStack(rawFrames)
            : rawFrames;

        mergedFrames.Reverse();
        return mergedFrames;
    }
}
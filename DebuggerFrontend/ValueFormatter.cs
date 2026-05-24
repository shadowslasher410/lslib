using LSLib.LS.Story;
using LSLib.LS.Story.Compiler;
using System.Text;

namespace LSTools.DebuggerFrontend;

public class ValueFormatter(StoryDebugInfo debugInfo)
{
    public string TupleToString(MsgFrame frame)
    {
        var node = debugInfo.Nodes[frame.NodeId];
        var rule = node.RuleId != 0 ? debugInfo.Rules[node.RuleId] : null;

        var sb = new StringBuilder();

        for (var i = 0; i < frame.Tuple.Column.Count; i++)
        {
            var value = frame.Tuple.Column[i];
            var columnName = TupleVariableIndexToName(rule, node, i);
            var valueStr = ValueToString(value);

            if (columnName is { Length: > 0 })
            {
                sb.Append($"{columnName}={valueStr}, ");
            }
            else
            {
                sb.Append($"{valueStr}, ");
            }
        }

        return sb.ToString();
    }

    public static string TupleToString(MsgTuple tuple) =>
        string.Join(", ", tuple.Column.Select(ValueToString));

    public static string ValueToString(MsgTypedValue value) => (Value.Type)value.TypeId switch
    {
        Value.Type.None => "(None)",
        Value.Type.Integer or
        Value.Type.Integer64 => value.Intval.ToString(),
        Value.Type.Float => value.Floatval.ToString(),
        _ => value.Stringval ?? string.Empty
    };

    public static string TupleVariableIndexToName(RuleDebugInfo? rule, NodeDebugInfo? node, int index)
    {
        if (rule is null)
        {
            return $"#{index}";
        }

        if (node is not null)
        {
            if (index < node.ColumnToVariableMaps.Count)
            {
                var mappedColumnIdx = node.ColumnToVariableMaps[index];
                return mappedColumnIdx < rule.Variables.Count
                    ? rule.Variables[mappedColumnIdx].Name
                    : $"(Bad Variable Idx #{index})";
            }

            return $"(Unknown #{index})";
        }

        return index < rule.Variables.Count
            ? rule.Variables[index].Name
            : $"(Bad Variable Idx #{index})";
    }

    public string GetFrameDebugName(MsgFrame frame)
    {
        string frameType = frame.Type switch
        {
            FrameType.IsValid => nameof(FrameType.IsValid),
            FrameType.Pushdown => nameof(FrameType.Pushdown),
            FrameType.PushdownDelete => nameof(FrameType.PushdownDelete),
            FrameType.Insert => nameof(FrameType.Insert),
            FrameType.Delete => nameof(FrameType.Delete),
            FrameType.RuleAction => nameof(FrameType.RuleAction),
            FrameType.GoalInitAction => nameof(FrameType.GoalInitAction),
            FrameType.GoalExitAction => nameof(FrameType.GoalExitAction),
            _ => throw new InvalidOperationException($"Unsupported frame type: {frame.Type}")
        };

        if (frame.NodeId != 0)
        {
            var node = debugInfo.Nodes[frame.NodeId];
            string dbName = "";

            if (node.DatabaseId != 0)
            {
                dbName = debugInfo.Databases[node.DatabaseId].Name;
            }
            else if (node is { Name.Length: > 0 })
            {
                dbName = node.Name;
            }

            return dbName is { Length: > 0 }
                ? $"{frameType} @ {node.Type} (DB {dbName})"
                : $"{frameType} @ {node.Type}";
        }

        var goal = debugInfo.Goals[frame.GoalId];
        return $"{frameType} @ {goal.Name}";
    }

    public string GetFrameName(MsgFrame frame, MsgTuple? arguments) => frame.Type switch
    {
        FrameType.GoalInitAction => $"{debugInfo.Goals[frame.GoalId].Name} (INIT)",
        FrameType.GoalExitAction => $"{debugInfo.Goals[frame.GoalId].Name} (EXIT)",
        FrameType.Insert or
        FrameType.Delete => GetInsertOrDeleteFrameName(frame, arguments),
        _ => throw new InvalidOperationException($"Unsupported root frame type: {frame.Type}")
    };

    private string GetInsertOrDeleteFrameName(MsgFrame frame, MsgTuple? arguments)
    {
        string argumentsFmt = arguments is not null ? $"({TupleToString(arguments)})" : "";
        var node = debugInfo.Nodes[frame.NodeId];

        if (node.Type == Node.Type.Database)
        {
            var db = debugInfo.Databases[node.DatabaseId];

            string actionTag = frame.Type == FrameType.Insert ? "INSERT" : "DELETE";
            return $"{db.Name}{argumentsFmt} ({actionTag})";
        }

        return $"{node.Name}{argumentsFmt}";
    }
}
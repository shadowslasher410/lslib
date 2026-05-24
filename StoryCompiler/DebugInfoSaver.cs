using LSLib.LS;
using LSLib.LS.Story.Compiler;
using LightProto;
using System.Text;

namespace LSTools.StoryCompiler;

public class DebugInfoSaver
{
    private static DatabaseDebugInfoMsg ToLightProto(DatabaseDebugInfo debugInfo)
    {
        var msg = new DatabaseDebugInfoMsg
        {
            Id = (uint)debugInfo.Id,
            Name = debugInfo.Name
        };

        foreach (var paramType in debugInfo.ParamTypes)
        {
            msg.ParamTypes.Add((uint)paramType);
        }
        return msg;
    }

    private static GoalDebugInfoMsg ToLightProto(GoalDebugInfo debugInfo)
    {
        var msg = new GoalDebugInfoMsg
        {
            Id = (uint)debugInfo.Id,
            Name = debugInfo.Name,
            Path = debugInfo.Path
        };

        foreach (var action in debugInfo.InitActions)
        {
            msg.InitActions.Add(ToLightProto(action));
        }

        foreach (var action in debugInfo.ExitActions)
        {
            msg.ExitActions.Add(ToLightProto(action));
        }

        return msg;
    }

    private static RuleVariableDebugInfoMsg ToLightProto(RuleVariableDebugInfo debugInfo) => new()
    {
        Index = (uint)debugInfo.Index,
        Type = (uint)debugInfo.Type,
        Name = debugInfo.Name,
        Unused = debugInfo.Unused
    };

    private static ActionDebugInfoMsg ToLightProto(ActionDebugInfo debugInfo) => new()
    {
        Line = (uint)debugInfo.Line
    };

    private static RuleDebugInfoMsg ToLightProto(RuleDebugInfo debugInfo)
    {
        var msg = new RuleDebugInfoMsg
        {
            Id = (uint)debugInfo.Id,
            GoalId = (uint)debugInfo.GoalId,
            Name = debugInfo.Name,
            ConditionsStartLine = (uint)debugInfo.ConditionsStartLine,
            ConditionsEndLine = (uint)debugInfo.ConditionsEndLine,
            ActionsStartLine = (uint)debugInfo.ActionsStartLine,
            ActionsEndLine = (uint)debugInfo.ActionsEndLine
        };

        foreach (var variable in debugInfo.Variables)
        {
            msg.Variables.Add(ToLightProto(variable));
        }

        foreach (var action in debugInfo.Actions)
        {
            msg.Actions.Add(ToLightProto(action));
        }

        return msg;
    }

    private static NodeDebugInfoMsg ToLightProto(NodeDebugInfo debugInfo)
    {
        var msg = new NodeDebugInfoMsg
        {
            Id = (uint)debugInfo.Id,
            RuleId = (uint)debugInfo.RuleId,
            Line = (uint)debugInfo.Line,
            DatabaseId = (uint)debugInfo.DatabaseId,
            Name = debugInfo.Name,
            Type = (NodeDebugInfoMsg.NodeType)(int)debugInfo.Type,
            ParentNodeId = (uint)debugInfo.ParentNodeId,
            FunctionName = debugInfo.FunctionName?.Name ?? string.Empty,
            FunctionArity = debugInfo.FunctionName is not null ? (uint)debugInfo.FunctionName.Arity : 0u
        };

        foreach (var (key, value) in debugInfo.ColumnToVariableMaps)
        {
            msg.ColumnMaps.Add((uint)key, (uint)value);
        }

        return msg;
    }

    private static FunctionParamDebugInfoMsg ToLightProto(FunctionParamDebugInfo debugInfo) => new()
    {
        TypeId = (uint)debugInfo.TypeId,
        Name = debugInfo.Name ?? string.Empty,
        Out = debugInfo.Out
    };

    private static FunctionDebugInfoMsg ToLightProto(FunctionDebugInfo debugInfo)
    {
        var msg = new FunctionDebugInfoMsg
        {
            Name = debugInfo.Name,
            TypeId = (uint)debugInfo.TypeId
        };

        foreach (var param in debugInfo.Params)
        {
            msg.Params.Add(ToLightProto(param));
        }

        return msg;
    }

    private static StoryDebugInfoMsg ToLightProto(StoryDebugInfo debugInfo)
    {
        var msg = new StoryDebugInfoMsg
        {
            Version = (uint)debugInfo.Version
        };

        foreach (var db in debugInfo.Databases.Values) msg.Databases.Add(ToLightProto(db));
        foreach (var goal in debugInfo.Goals.Values) msg.Goals.Add(ToLightProto(goal));
        foreach (var rule in debugInfo.Rules.Values) msg.Rules.Add(ToLightProto(rule));
        foreach (var node in debugInfo.Nodes.Values) msg.Nodes.Add(ToLightProto(node));
        foreach (var function in debugInfo.Functions.Values) msg.Functions.Add(ToLightProto(function));

        return msg;
    }

    public static void Save(Stream stream, StoryDebugInfo debugInfo)
    {
        var msg = ToLightProto(debugInfo);

        byte[] proto = msg.ToByteArray();

        var flags = CompressionHelpers.MakeCompressionFlags(CompressionMethod.LZ4, LSCompressionLevel.Fast);
        byte[] compressed = CompressionHelpers.Compress(proto, flags);

        stream.Write(compressed.AsSpan());

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((uint)proto.Length);
    }
}
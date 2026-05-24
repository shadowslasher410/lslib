using LightProto;
using LSLib.LS;
using LSLib.LS.Story.Compiler;
using LSTools.StoryCompiler;
using System.Buffers.Binary;

namespace LSTools.DebuggerFrontend;

class DebugInfoLoader
{
    private static DatabaseDebugInfo FromLightProto(DatabaseDebugInfoMsg msg) => new()
    {
        Id = msg.Id,
        Name = msg.Name,
        ParamTypes = [.. msg.ParamTypes]
    };

    private static ActionDebugInfo FromLightProto(ActionDebugInfoMsg msg) => new()
    {
        Line = msg.Line
    };

    private static GoalDebugInfo FromLightProto(GoalDebugInfoMsg msg) => new()
    {
        Id = msg.Id,
        Name = msg.Name,
        Path = msg.Path,
        InitActions = [.. msg.InitActions.Select(FromLightProto)],
        ExitActions = [.. msg.ExitActions.Select(FromLightProto)]
    };

    private static RuleVariableDebugInfo FromLightProto(RuleVariableDebugInfoMsg msg) => new()
    {
        Index = msg.Index,
        Name = msg.Name,
        Type = msg.Type
    };

    private static RuleDebugInfo FromLightProto(RuleDebugInfoMsg msg) => new()
    {
        Id = msg.Id,
        GoalId = msg.GoalId,
        Name = msg.Name,
        Variables = [.. msg.Variables.Select(FromLightProto)],
        Actions = [.. msg.Actions.Select(FromLightProto)],
        ConditionsStartLine = msg.ConditionsStartLine,
        ConditionsEndLine = msg.ConditionsEndLine,
        ActionsStartLine = msg.ActionsStartLine,
        ActionsEndLine = msg.ActionsEndLine
    };

    private static NodeDebugInfo FromLightProto(NodeDebugInfoMsg msg)
    {
        var debugInfo = new NodeDebugInfo
        {
            Id = msg.Id,
            RuleId = msg.RuleId,
            Line = (int)msg.Line,
            ColumnToVariableMaps = [],
            DatabaseId = msg.DatabaseId,
            Name = msg.Name,
            Type = (LSLib.LS.Story.Node.Type)(int)msg.Type,
            ParentNodeId = msg.ParentNodeId
        };

        if (msg.FunctionName is { Length: > 0 })
        {
            debugInfo.FunctionName = new FunctionNameAndArity(msg.FunctionName, (int)msg.FunctionArity);
        }

        foreach (var map in msg.ColumnMaps)
        {
            debugInfo.ColumnToVariableMaps.Add((int)map.Key, (int)map.Value);
        }

        return debugInfo;
    }

    private static FunctionParamDebugInfo FromLightProto(FunctionParamDebugInfoMsg msg) => new()
    {
        TypeId = msg.TypeId,
        Name = msg.Name,
        Out = msg.Out
    };

    private static FunctionDebugInfo FromLightProto(FunctionDebugInfoMsg msg) => new()
    {
        Name = msg.Name,
        Params = [.. msg.Params.Select(FromLightProto)],
        TypeId = msg.TypeId
    };

    private static StoryDebugInfo FromLightProto(StoryDebugInfoMsg msg)
    {
        var debugInfo = new StoryDebugInfo
        {
            Version = msg.Version
        };

        foreach (var dbMsg in msg.Databases)
        {
            var db = FromLightProto(dbMsg);
            debugInfo.Databases.Add(db.Id, db);
        }

        foreach (var goalMsg in msg.Goals)
        {
            var goal = FromLightProto(goalMsg);
            debugInfo.Goals.Add(goal.Id, goal);
        }

        foreach (var ruleMsg in msg.Rules)
        {
            var rule = FromLightProto(ruleMsg);
            debugInfo.Rules.Add(rule.Id, rule);
        }

        foreach (var nodeMsg in msg.Nodes)
        {
            var node = FromLightProto(nodeMsg);
            debugInfo.Nodes.Add(node.Id, node);
        }

        foreach (var funcMsg in msg.Functions)
        {
            var func = FromLightProto(funcMsg);
            debugInfo.Functions.Add(new FunctionNameAndArity(func.Name, func.Params.Count), func);
        }

        return debugInfo;
    }

    public static StoryDebugInfo Load(ReadOnlySpan<byte> msgPayload)
    {
        ReadOnlySpan<byte> lengthBytes = msgPayload[^4..];
        uint decompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(lengthBytes);

        ReadOnlySpan<byte> compressed = msgPayload[..^4];

        var flags = CompressionHelpers.MakeCompressionFlags(CompressionMethod.LZ4, LSCompressionLevel.Fast);
        byte[] decompressed = CompressionHelpers.Decompress(compressed.ToArray(), (int)decompressedSize, flags);

        var msg = Serializer.Deserialize<StoryDebugInfoMsg>(decompressed);

        return FromLightProto(msg);
    }
}

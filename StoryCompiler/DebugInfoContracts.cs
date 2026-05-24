using LightProto;

namespace LSTools.StoryCompiler;

[ProtoContract]
public partial class DatabaseDebugInfoMsg
{
    [ProtoMember(1)] public uint Id { get; set; }
    [ProtoMember(2)] public string Name { get; set; } = string.Empty;
    [ProtoMember(3)] public List<uint> ParamTypes { get; init; } = [];
}

[ProtoContract]
public partial class ActionDebugInfoMsg
{
    [ProtoMember(1)] public uint Line { get; set; }
}

[ProtoContract]
public partial class GoalDebugInfoMsg
{
    [ProtoMember(1)] public uint Id { get; set; }
    [ProtoMember(2)] public string Name { get; set; } = string.Empty;
    [ProtoMember(3)] public string Path { get; set; } = string.Empty;
    [ProtoMember(4)] public List<ActionDebugInfoMsg> InitActions { get; init; } = [];
    [ProtoMember(5)] public List<ActionDebugInfoMsg> ExitActions { get; init; } = [];
}

[ProtoContract]
public partial class RuleVariableDebugInfoMsg
{
    [ProtoMember(1)] public uint Index { get; set; }
    [ProtoMember(2)] public uint Type { get; set; }
    [ProtoMember(3)] public string Name { get; set; } = string.Empty;
    [ProtoMember(4)] public bool Unused { get; set; }
}

[ProtoContract]
public partial class RuleDebugInfoMsg
{
    [ProtoMember(1)] public uint Id { get; set; }
    [ProtoMember(2)] public uint GoalId { get; set; }
    [ProtoMember(3)] public List<RuleVariableDebugInfoMsg> Variables { get; init; } = [];
    [ProtoMember(4)] public string Name { get; set; } = string.Empty;
    [ProtoMember(5)] public List<ActionDebugInfoMsg> Actions { get; init; } = [];
    [ProtoMember(6)] public uint ConditionsStartLine { get; set; }
    [ProtoMember(7)] public uint ConditionsEndLine { get; set; }
    [ProtoMember(8)] public uint ActionsStartLine { get; set; }
    [ProtoMember(9)] public uint ActionsEndLine { get; set; }
}

[ProtoContract]
public partial class NodeDebugInfoMsg
{
    public enum NodeType
    {
        Unused = 0,
        Database = 1,
        Proc = 2,
        DivQuery = 3,
        And = 4,
        NotAnd = 5,
        RelOp = 6,
        Rule = 7,
        InternalQuery = 8,
        UserQuery = 9
    }

    [ProtoMember(1)] public uint Id { get; set; }
    [ProtoMember(2)] public uint RuleId { get; set; }
    [ProtoMember(3)] public uint Line { get; set; }
    [ProtoMember(4)] public Dictionary<uint, uint> ColumnMaps { get; init; } = [];
    [ProtoMember(5)] public uint DatabaseId { get; set; }
    [ProtoMember(6)] public string Name { get; set; } = string.Empty;
    [ProtoMember(7)] public NodeType Type { get; set; }

    [ProtoMember(8)] public uint ParentNodeId { get; set; }
    [ProtoMember(9)] public string FunctionName { get; set; } = string.Empty;
    [ProtoMember(10)] public uint FunctionArity { get; set; }
}

[ProtoContract]
public partial class FunctionParamDebugInfoMsg
{
    [ProtoMember(1)] public uint TypeId { get; set; }
    [ProtoMember(2)] public string Name { get; set; } = string.Empty;
    [ProtoMember(3)] public bool Out { get; set; }
}

[ProtoContract]
public partial class FunctionDebugInfoMsg
{
    [ProtoMember(1)] public string Name { get; set; } = string.Empty;
    [ProtoMember(2)] public List<FunctionParamDebugInfoMsg> Params { get; init; } = [];
    [ProtoMember(3)] public uint TypeId { get; set; }
}

[ProtoContract]
public partial class StoryDebugInfoMsg
{
    [ProtoMember(1)] public List<DatabaseDebugInfoMsg> Databases { get; init; } = [];
    [ProtoMember(2)] public List<GoalDebugInfoMsg> Goals { get; init; } = [];
    [ProtoMember(3)] public List<RuleDebugInfoMsg> Rules { get; init; } = [];
    [ProtoMember(4)] public List<NodeDebugInfoMsg> Nodes { get; init; } = [];
    [ProtoMember(5)] public List<FunctionDebugInfoMsg> Functions { get; init; } = [];
    [ProtoMember(6)] public uint Version { get; set; }
}
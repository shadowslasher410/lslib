using LightProto;

namespace LSTools.StoryCompiler;

#region Global Enums

public enum NodeType : uint
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

#endregion

#region Protocol Messages

[ProtoContract]
public sealed partial class DatabaseDebugInfoMsg
{
    [ProtoMember(1)] public uint Id { get; set; }
    [ProtoMember(2)] public string Name { get; set; } = string.Empty;
    [ProtoMember(3)] public List<uint> ParamTypes { get; } = [];

    public void Clear()
    {
        Id = 0;
        Name = string.Empty;
        ParamTypes.Clear();
    }
}

[ProtoContract]
public sealed partial class ActionDebugInfoMsg
{
    [ProtoMember(1)] public uint Line { get; set; }

    public void Clear() => Line = 0;
}

[ProtoContract]
public sealed partial class GoalDebugInfoMsg
{
    [ProtoMember(1)] public uint Id { get; set; }
    [ProtoMember(2)] public string Name { get; set; } = string.Empty;
    [ProtoMember(3)] public string Path { get; set; } = string.Empty;
    [ProtoMember(4)] public List<ActionDebugInfoMsg> InitActions { get; } = [];
    [ProtoMember(5)] public List<ActionDebugInfoMsg> ExitActions { get; } = [];

    public void Clear()
    {
        Id = 0;
        Name = string.Empty;
        Path = string.Empty;
        InitActions.Clear();
        ExitActions.Clear();
    }
}

[ProtoContract]
public sealed partial class RuleVariableDebugInfoMsg
{
    [ProtoMember(1)] public uint Index { get; set; }
    [ProtoMember(2)] public uint Type { get; set; }
    [ProtoMember(3)] public string Name { get; set; } = string.Empty;
    [ProtoMember(4)] public bool Unused { get; set; }

    public void Clear()
    {
        Index = 0;
        Type = 0;
        Name = string.Empty;
        Unused = false;
    }
}

[ProtoContract]
public sealed partial class RuleDebugInfoMsg
{
    [ProtoMember(1)] public uint Id { get; set; }
    [ProtoMember(2)] public uint GoalId { get; set; }
    [ProtoMember(3)] public List<RuleVariableDebugInfoMsg> Variables { get; } = [];
    [ProtoMember(4)] public string Name { get; set; } = string.Empty;
    [ProtoMember(5)] public List<ActionDebugInfoMsg> Actions { get; } = [];
    [ProtoMember(6)] public uint ConditionsStartLine { get; set; }
    [ProtoMember(7)] public uint ConditionsEndLine { get; set; }
    [ProtoMember(8)] public uint ActionsStartLine { get; set; }
    [ProtoMember(9)] public uint ActionsEndLine { get; set; }

    public void Clear()
    {
        Id = 0;
        GoalId = 0;
        Variables.Clear();
        Name = string.Empty;
        Actions.Clear();
        ConditionsStartLine = 0;
        ConditionsEndLine = 0;
        ActionsStartLine = 0;
        ActionsEndLine = 0;
    }
}

[ProtoContract]
public sealed partial class NodeDebugInfoMsg
{
    [ProtoMember(1)] public uint Id { get; set; }
    [ProtoMember(2)] public uint RuleId { get; set; }
    [ProtoMember(3)] public uint Line { get; set; }
    [ProtoMember(4)] public Dictionary<uint, uint> ColumnMaps { get; } = [];
    [ProtoMember(5)] public uint DatabaseId { get; set; }
    [ProtoMember(6)] public string Name { get; set; } = string.Empty;
    [ProtoMember(7)] public NodeType Type { get; set; }
    [ProtoMember(8)] public uint ParentNodeId { get; set; }
    [ProtoMember(9)] public string FunctionName { get; set; } = string.Empty;
    [ProtoMember(10)] public uint FunctionArity { get; set; }

    public void Clear()
    {
        Id = 0;
        RuleId = 0;
        Line = 0;
        ColumnMaps.Clear();
        DatabaseId = 0;
        Name = string.Empty;
        Type = NodeType.Unused;
        ParentNodeId = 0;
        FunctionName = string.Empty;
        FunctionArity = 0;
    }
}

[ProtoContract]
public sealed partial class FunctionParamDebugInfoMsg
{
    [ProtoMember(1)] public uint TypeId { get; set; }
    [ProtoMember(2)] public string Name { get; set; } = string.Empty;
    [ProtoMember(3)] public bool Out { get; set; }

    public void Clear()
    {
        TypeId = 0;
        Name = string.Empty;
        Out = false;
    }
}

[ProtoContract]
public sealed partial class FunctionDebugInfoMsg
{
    [ProtoMember(1)] public string Name { get; set; } = string.Empty;
    [ProtoMember(2)] public List<FunctionParamDebugInfoMsg> Params { get; } = [];
    [ProtoMember(3)] public uint TypeId { get; set; }

    public void Clear()
    {
        Name = string.Empty;
        Params.Clear();
        TypeId = 0;
    }
}

[ProtoContract]
public sealed partial class StoryDebugInfoMsg
{
    [ProtoMember(1)] public List<DatabaseDebugInfoMsg> Databases { get; } = [];
    [ProtoMember(2)] public List<GoalDebugInfoMsg> Goals { get; } = [];
    [ProtoMember(3)] public List<RuleDebugInfoMsg> Rules { get; } = [];
    [ProtoMember(4)] public List<NodeDebugInfoMsg> Nodes { get; } = [];
    [ProtoMember(5)] public List<FunctionDebugInfoMsg> Functions { get; } = [];
    [ProtoMember(6)] public uint Version { get; set; }

    public void Clear()
    {
        Databases.Clear();
        Goals.Clear();
        Rules.Clear();
        Nodes.Clear();
        Functions.Clear();
        Version = 0;
    }
}

#endregion
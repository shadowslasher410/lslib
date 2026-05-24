
namespace LSLib.LS.Story.Compiler;

public sealed class DatabaseDebugInfo
{
    // ID of database in generated story file
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<uint> ParamTypes { get; set; } = [];
}

public sealed class ActionDebugInfo
{
    // Location of action in source file
    public uint Line { get; set; }
}

public class GoalDebugInfo
{
    // ID of goal in generated story file
    public uint Id { get; set; }
    // Goal name
    public string Name { get; set; } = string.Empty;
    // Absolute path of goal source file
    public string Path { get; set; } = string.Empty;
    // Actions in INIT section
    public List<ActionDebugInfo> InitActions { get; set; } = [];
    // Actions in EXIT section
    public List<ActionDebugInfo> ExitActions { get; set; } = [];
}

public class RuleVariableDebugInfo
{
    // Index of rule variable in local tuple
    public uint Index { get; set; }
    // Name of rule variable
    public string Name { get; set; } = string.Empty;
    // Type ID of rule variable
    public uint Type { get; set; }
    // Is the variable slot unused? (i.e. not bound to a physical column)
    public bool Unused { get; set; }
}

public class RuleDebugInfo
{
    // Local index of rule (this is not stored in the story file and is only used by the debugger)
    public uint Id { get; set; }
    // ID of parent goal node
    public uint GoalId { get; set; }
    // Generated rule name (usually the name of the first condition)
    public string Name { get; set; } = string.Empty;
    // Rule local variables
    public List<RuleVariableDebugInfo> Variables { get; set; } = [];
    // Actions in THEN-part
    public List<ActionDebugInfo> Actions { get; set; } = [];

    // Line number of the beginning of the "IF" section
    public uint ConditionsStartLine { get; set; }

    // Line number of the end of the "IF" section
    public uint ConditionsEndLine { get; set; }

    // Line number of the beginning of the "THEN" section
    public uint ActionsStartLine { get; set; }

    // Line number of the end of the "THEN" section
    public uint ActionsEndLine { get; set; }
}

public sealed class NodeDebugInfo
{
    // ID of node in generated story file
    public uint Id { get; set; }

    // Index of parent rule
    public uint RuleId { get; set; }

    // Location of action in source file
    public int Line { get; set; }

    // Local tuple to rule variable index mappings
    public Dictionary<int, int> ColumnToVariableMaps { get; set; } = [];

    // ID of associated database node
    public uint DatabaseId { get; set; }

    // Name of node
    public string Name { get; set; } = string.Empty;

    // Type of node
    public Node.Type Type { get; set; }

    // ID of left parent node
    public uint ParentNodeId { get; set; }

    // Function (query, proc, etc.) attached to this node
    public FunctionNameAndArity? FunctionName { get; set; }
}

public sealed class FunctionParamDebugInfo
{
    // Intrinsic type ID
    public uint TypeId { get; set; }

    // Name of parameter
    public string Name { get; set; } = string.Empty;

    // Is an out param (ie. return value)?
    public bool Out { get; set; }
}


public sealed class FunctionDebugInfo
{
    // Name of function
    public string Name { get; set; } = string.Empty;
    
    // Type of node
    public List<FunctionParamDebugInfo> Params { get; set; } = [];
    
    // Function type ID
    public uint TypeId { get; set; }
}

public sealed class StoryDebugInfo
{
    /// <summary>
    /// Story debug info format version. Increment each time the format changes.
    /// </summary>
    public const uint CurrentVersion = 2;

    public uint Version { get; set; } = CurrentVersion;
    public Dictionary<uint, DatabaseDebugInfo> Databases { get; set; } = [];
    public Dictionary<uint, GoalDebugInfo> Goals { get; set; } = [];
    public Dictionary<uint, RuleDebugInfo> Rules { get; set; } = [];
    public Dictionary<uint, NodeDebugInfo> Nodes { get; set; } = [];
    public Dictionary<FunctionNameAndArity, FunctionDebugInfo> Functions { get; set; } = [];
}
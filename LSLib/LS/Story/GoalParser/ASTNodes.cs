using LSLib.LS.Story.Compiler;
using LSLib.Parser;

namespace LSLib.LS.Story.GoalParser;

/// <summary>
/// Goal node - contains everything from a goal file.
/// </summary>
public sealed class ASTGoal
{
    // Facts in the INITSECTION part
    public List<ASTBaseFact> InitSection { get; set; } = [];
    
    // List of all production rules (including procs and queries) from the KBSECTION part
    public List<ASTRule> KBSection { get; set; } = [];
    
    // Facts in the EXITSECTION part
    public List<ASTBaseFact> ExitSection { get; set; } = [];
    
    // Names of parent goals (if any)
    public List<ASTParentTargetEdge> ParentTargetEdges { get; set; } = [];
    
    // Location of node in source code
    public CodeLocation Location { get; set; } = null!;
}

/// <summary>
/// Name of a single parent target edge (i.e. parent goal name).
/// This is discarded during parsing and does not appear in the final AST.
/// </summary>
public sealed class ASTParentTargetEdge
{
    // Location of node in source code
    public CodeLocation Location { get; set; } = null!;
    
    // Parent goal name
    public string Goal { get; set; } = string.Empty;
}

/// <summary>
/// Osiris statement from the INIT or EXIT section.
/// </summary>
public class ASTBaseFact
{
    // Location of fact in source code
    public CodeLocation Location { get; set; } = null!;
}

/// <summary>
/// Osiris fact statement from the INIT or EXIT section.
/// </summary>
public sealed class ASTFact : ASTBaseFact
{
    // Name of database we're inserting into / deleting from
    public string Database { get; set; } = string.Empty;
    
    // Fact negation ("DB_Something(1)" vs. "NOT DB_Something(1)").
    public bool Not { get; set; }
    
    // List of values in the fact tuple
    public List<ASTConstantValue> Elements { get; set; } = [];
}

/// <summary>
/// Osiris GoalCompleted statement from the INIT or EXIT section.
/// </summary>
public sealed class ASTGoalCompletedFact : ASTBaseFact
{
}

/// <summary>
/// Describes a production rule in the KB section
/// </summary>
public sealed class ASTRule
{
    // Location of rule in source code
    public CodeLocation Location { get; set; } = null!;
    
    // Type of rule (if, proc or query)
    public RuleType Type { get; set; }
    
    // Conditions/predicates
    public List<ASTCondition> Conditions { get; set; } = [];
    
    // Actions to execute on tuples that satisfy the conditions
    public List<ASTAction> Actions { get; set; } = [];
}

/// <summary>
/// Production rule condition/predicate.
/// </summary>
public class ASTCondition
{
    // Location of condition in source code
    public CodeLocation Location { get; set; } = null!;
}

/// <summary>
/// "Function call-like" predicate - a div query, a user query or a database filter.
/// (i.e. "AND SomeFunc(1, 2)" or "AND NOT SomeFunc(1, 2)")
/// </summary>
public sealed class ASTFuncCondition : ASTCondition
{
    // Query/Database name
    public string Name { get; set; } = string.Empty;
    
    // Condition negation ("AND DB_Something(1)" vs. "AND NOT DB_Something(1)").
    public bool Not { get; set; }
    
    // List of query parameters / database tuple columns
    public List<ASTRValue> Params { get; set; } = [];
}

/// <summary>
/// Predicate with a binary operator (i.e. "A >= B", "A == B", ...)
/// </summary>
public sealed class ASTBinaryCondition : ASTCondition
{
    // Left-hand value
    public ASTRValue LValue { get; set; } = null!;
    
    // Operator
    public RelOpType Op { get; set; }
    
    // Right-hand value
    public ASTRValue RValue { get; set; } = null!;
}

public class ASTAction
{
    // Location of action in source code
    public CodeLocation Location { get; set; } = null!;
}

public sealed class ASTGoalCompletedAction : ASTAction
{
}

/// <summary>
/// Statement in the THEN part of a rule.
/// This is either a builtin PROC call, user PROC call, or a database insert/delete operation.
/// </summary>
public sealed class ASTStatement : ASTAction
{
    // Proc/Database name
    public string Name { get; set; } = string.Empty;
    
    // Statement negation ("DB_Something(1)" vs. "NOT DB_Something(1)").
    public bool Not { get; set; }
    
    // List of PROC parameters / database tuple columns
    public List<ASTRValue> Params { get; set; } = [];
}

public class ASTRValue
{
    // Location of node in source code
    public CodeLocation Location { get; set; } = null!;
}

/// <summary>
/// Constant scalar value.
/// </summary>
public sealed class ASTConstantValue : ASTRValue
{
    // Type of value, if specified in the code.
    // (e.g. "(INT64)123")
    public string? TypeName { get; set; }
    
    // Internal type of the constant
    public IRConstantType Type { get; set; }
    
    // Value of this constant if the type is Integer.
    public long IntegerValue { get; set; }
    
    // Value of this constant if the type is Float.
    public float FloatValue { get; set; }
    
    // Value of this constant if the type is String or Name.
    public string StringValue { get; set; } = string.Empty;
}

/// <summary>
/// Rule-local variable name.
/// (Any variable that begins with an underscore in the IF or THEN part of a rule)
/// </summary>
public sealed class ASTLocalVar : ASTRValue
{
    // Type of variable, if specified in the code.
    // (e.g. "(ITEMGUID)_Var")
    public string? Type { get; set; }
    
    // Name of variable.
    public string Name { get; set; } = string.Empty;
}
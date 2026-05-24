using LSLib.LS.Story.GoalParser;
using LSLib.Parser;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace LSLib.LS.Story.Compiler;

/// <summary>
/// Parent class for IR (Intermediate Representation) references.
/// These are names that were passed on from the AST, but
/// may not be defined at the time of parsing.
/// </summary>
public abstract class IRReference<TName, TReferenced>
    where TName : class
    where TReferenced : class
{
    public TName? Name { get; set; }
    protected CompilationContext? Context;

    [MemberNotNullWhen(false, nameof(Name))]
    public bool IsNull => Name is null;

    [MemberNotNullWhen(true, nameof(Name))]
    public bool IsValid => Name is not null;

    protected IRReference() { }

    protected IRReference(TName name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public void Bind(CompilationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Context is null)
            Context = context;
        else
            throw new InvalidOperationException("Reference already bound to a compilation context!");
    }

    public abstract TReferenced? Resolve();
}

/// <summary>
/// Named reference to a story goal.
/// </summary>
public sealed class IRGoalRef(string name) : IRReference<string, IRGoal>(name)
{
    public override IRGoal? Resolve()
    {
        if (IsNull || Context is null) return null;
        return Context.LookupGoal(Name);
    }
}

/// <summary>
/// Named reference to a story symbol (proc, query, event).
/// </summary>
public sealed class IRSymbolRef(FunctionNameAndArity name) : IRReference<FunctionNameAndArity, FunctionSignature>(name)
{
    public override FunctionSignature? Resolve()
    {
        if (IsNull || Context is null) return null;
        return Context.LookupSignature(Name);
    }
}

/// <summary>
/// Goal dependency edge from subgial to parent
/// </summary>
public sealed class IRTargetEdge
{
    // Goal name
    public IRGoalRef Goal { get; set; } = null!;
    // Location of code reference
    public CodeLocation? Location { get; set; }
}

/// <summary>
/// Goal node - contains everything from a goal file.
/// </summary>
public sealed class IRGoal
{
    // Goal name (derived from filename)
    public string Name { get; set; } = string.Empty;
    // Facts in the INITSECTION part
    public List<IRFact> InitSection { get; set; } = [];
    // List of all production rules (including procs and queries) from the KBSECTION part
    public List<IRRule> KBSection { get; set; } = [];
    // Facts in the EXITSECTION part
    public List<IRFact> ExitSection { get; set; } = [];
    // Parent goals (if any)
    public List<IRTargetEdge> ParentTargetEdges { get; set; } = [];
    // Location of node in source code
    public CodeLocation? Location { get; set; }
}

/// <summary>
/// Osiris fact statement from the INIT or EXIT section.
/// </summary>
public sealed class IRFact
{
    // Database we're inserting into / deleting from
    public IRSymbolRef? Database { get; set; } = null;
    // Fact negation ("DB_Something(1)" vs. "NOT DB_Something(1)").
    public bool Not { get; set; }
    // List of values in the fact tuple
    public List<IRConstant> Elements { get; set; } = [];
    // Goal that we're completing
    public IRGoal? Goal { get; set; }
    // Location of node in source code
    public CodeLocation? Location { get; set; }
}


/// <summary>
/// Describes a production rule in the KB section
/// </summary>
public sealed class IRRule
{
    public IRGoal? Goal { get; set; }
    // Type of rule (if, proc or query)
    public RuleType Type { get; set; }
    // Conditions/predicates
    public List<IRCondition> Conditions { get; set; } = [];
    // Actions to execute on tuples that satisfy the conditions
    public List<IRStatement> Actions { get; set; } = [];
    // Rule-local variables
    public List<IRRuleVariable> Variables { get; set; } = [];
    // Rule-local variables by name
    // Fix: Using StringComparer.OrdinalIgnoreCase completely bypasses heavy runtime ToLowerInvariant allocations strings cloning
    public Dictionary<string, IRRuleVariable> VariablesByName { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    // Location of node in source code
    public CodeLocation? Location { get; set; }

    public IRRuleVariable FindOrAddVariable(string name, ValueType type)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(type);

        if (name[0] != '_')
        {
            throw new ArgumentException("Local variable name must start with an underscore", nameof(name));
        }

        IRRuleVariable? v = null;
        // Only resolve the variable if it has a name.
        // Unnamed variables are never resolved by name, and all references are assigned 
        // to a separate variable "slot"
        if (name.Length > 1)
        {
            VariablesByName.TryGetValue(name, out v);
        }

        if (v is null)
        {
            // Allocate a new variable slot if no variable with the same name exists
            v = new IRRuleVariable
            {
                Index = Variables.Count,
                Name = name,
                Type = type,
                FirstBindingIndex = -1
            };

            Variables.Add(v);

            if (name.Length > 1)
            {
                VariablesByName.Add(name, v);
            }
        }

        return v;
    }
}

/// <summary>
/// Rule-level local variable.
/// </summary>
public sealed class IRRuleVariable
{
    // Index of the variable within the rule.
    // Indices start from zero.
    public int Index { get; set; }
    // Local name of the variable.
    // This is only used during compilation and is discarded
    // when emitting the final story file.
    public string Name { get; set; } = string.Empty;
    // Type of the rule variable
    public ValueType Type { get; set; } = null!;
    // Index of condition that first bound this variable
    public int FirstBindingIndex { get; set; }

    public bool IsUnused()
    {
        return Name.Length == 1;
    }
}
/// <summary>
/// Production rule condition/predicate.
/// </summary>
public class IRCondition
{
    // Number of columns in the output tuple of this condition.
    public int TupleSize { get; set; }
    // Location of node in source code
    public CodeLocation? Location { get; set; }
}

/// <summary>
/// "Function call-like" predicate - a div query, a user query or a database filter.
/// (i.e. "AND SomeFunc(1, 2)" or "AND NOT SomeFunc(1, 2)")
/// </summary>
public sealed class IRFuncCondition : IRCondition
{
    // Query/Database name
    public IRSymbolRef Func { get; set; } = null!;
    // Condition negation ("AND DB_Something(1)" vs. "AND NOT DB_Something(1)").
    public bool Not { get; set; }
    // List of query parameters / database tuple columns
    public List<IRValue> Params { get; set; } = [];
}


/// <summary>
/// Predicate with a binary operator (i.e. "A >= B", "A == B", ...)
/// </summary>
public sealed class IRBinaryCondition : IRCondition
{
    // Left-hand value
    public IRValue LValue { get; set; } = null!;
    // Operator
    public RelOpType Op { get; set; }
    // Right-hand value
    public IRValue RValue { get; set; } = null!;
}

/// <summary>
/// Statement in the THEN part of a rule.
/// This is either a builtin PROC call, user PROC call, a database insert/delete operation,
/// or a goal completion statement.
/// </summary>
public sealed class IRStatement
{
    // Proc/Database name
    // (We don't know yet whether this is a PROC or a DB - this info will only be
    //  available during phase2 parsing)
    public IRSymbolRef Func { get; set; } = null!;
    // Goal to complete
    // (Reference is empty if this statement doesn't trigger a goal completion)
    public IRGoal? Goal { get; set; }
    // Statement negation ("DB_Something(1)" vs. "NOT DB_Something(1)").
    public bool Not { get; set; }
    // List of PROC parameters / database tuple columns
    public List<IRValue> Params { get; set; } = [];
    // Location of node in source code
    public CodeLocation? Location { get; set; }
}

public class IRValue
{
    // Type of variable, if specified in the code.
    // (e.g. "(ITEMGUID)_Var")
    public ValueType Type { get; set; } = null!;
    // Location of node in source code
    public CodeLocation? Location { get; set; }
}

/// <summary>
/// Constant value type. This is the type we see during story
/// script parsing, which is not necessarily the same as the
/// Osiris type.
/// </summary>
public enum IRConstantType
{
    Unknown = 0,
    Integer = 1,
    Float = 2,
    String = 3,
    Name = 4
}

/// <summary>
/// Constant scalar value.
/// </summary>
public class IRConstant : IRValue
{
    // Internal type of the constant
    // This is not the same as the Osiris type; e.g. a value of type CHARACTERGUID
    // will be stored with a constant type of "Name". It also doesn't differentiate
    // between INT and INT64 as we don't know the exact Osiris type without contextual
    // type inference, which will happen in later stages.
    public IRConstantType ValueType { get; set; }
    // Was the type info retrieved from the AST or inferred?
    public bool InferredType { get; set; }
    // Value of this constant if the type is Integer.
    public long IntegerValue { get; set; }
    // Value of this constant if the type is Float.
    public float FloatValue { get; set; }
    // Value of this constant if the type is String or Name.
    public string StringValue { get; set; } = string.Empty;


    public override string ToString()
    {
        return ValueType switch
        {
            IRConstantType.Unknown => "(unknown)",
            IRConstantType.Integer => IntegerValue.ToString(CultureInfo.InvariantCulture),
            IRConstantType.Float => FloatValue.ToString(CultureInfo.InvariantCulture),
            IRConstantType.String => $"\"{StringValue}\"",
            IRConstantType.Name => StringValue,
            _ => "(unknown type)"
        };
    }
}

/// <summary>
/// Rule-local variable name.
/// (Any variable that begins with an underscore in the IF or THEN part of a rule)
/// </summary>
public sealed class IRVariable : IRValue
{
    // Index of variable in the rule variable list
    public int Index { get; set; }
}

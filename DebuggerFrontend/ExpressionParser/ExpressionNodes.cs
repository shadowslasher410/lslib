using LSLib.LS.Story.Compiler;

namespace LSLib.DebuggerFrontend.ExpressionParser;

/// <summary>
/// Base class for all nodes.
/// </summary>
public abstract class ExpressionNode
{
    // Concrete parameterless constructor for safe child instantiation
    protected ExpressionNode() { }
}

/// <summary>
/// Parameter list of an expression.
/// This is discarded during parsing and does not appear in the final tree.
/// </summary>
public sealed class StatementParamList : ExpressionNode
{
    // Modern target-typed collection expressions [] optimize underlying array layouts natively
    public List<RValue> Params { get; set; } = [];
}

/// <summary>
/// An expression.
/// This is either a PROC call, QRY, or a database insert/delete operation.
/// </summary>
public sealed class Statement : ExpressionNode
{
    public string Name { get; set; } = string.Empty;

    // Statement negation ("DB_Something(1)" vs. "NOT DB_Something(1)").
    public bool Not { get; set; }

    public List<RValue> Params { get; set; } = [];
}

public abstract class RValue : ExpressionNode
{
    protected RValue() { }
}

/// <summary>
/// Constant scalar value.
/// </summary>
public sealed class ConstantValue : RValue
{
    // Type of value, if specified in the code. (e.g. "(INT64)123")
    public string TypeName { get; set; } = string.Empty;

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
/// (Any variable that begins with an underscore)
/// </summary>
public sealed class LocalVar : RValue
{
    // Type of variable, if specified in the code. (e.g. "(ITEMGUID)_Var")
    public string Type { get; set; } = string.Empty;

    // Name of variable.
    public string Name { get; set; } = string.Empty;
}
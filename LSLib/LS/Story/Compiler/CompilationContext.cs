using LSLib.Parser;
using System.Globalization;

namespace LSLib.LS.Story.Compiler;

/// <summary>
/// Determines the game version we're targeting during compilation.
/// </summary>
public enum TargetGame
{
    DOS2,
    DOS2DE,
    BG3
}

/// <summary>
/// Type declaration
/// </summary>
public class ValueType
{
    // Type ID
    public uint TypeId;
    // Osiris builtin type ID
    public Value.Type IntrinsicTypeId;
    // Type name
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Returns whether this type is an alias of the specified type.
    /// </summary>
    public bool IsAliasOf(ValueType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return 
            // The base types match
            IntrinsicTypeId == type.IntrinsicTypeId
            // The alias ID doesn't match
            && TypeId != type.TypeId
            // This type is an alias type
            && TypeId != (uint)IntrinsicTypeId
            // The other type is a base type
            && type.TypeId == (uint)type.IntrinsicTypeId;
    }
}

/// <summary>
/// Parameter direction. 
/// Only relevant for queries, which get both input and output parameters.
/// </summary>
public enum ParamDirection
{
    In,
    Out
}

/// <summary>
/// Osiris internal function type.
/// </summary>
public enum FunctionType
{
    // Osiris items
    // Query defined by the Osiris runtime
    SysQuery,
    // Call defined by the Osiris runtime
    SysCall,

    // Application defined items
    // Event defined by the application (D:OS)
    Event,
    // Query defined by the application (D:OS)
    Query,
    // Call defined by the application (D:OS)
    Call,

    // User-defined items
    // Proc (~call) defined in user code
    Proc,
    // Query defined in user code
    UserQuery,
    // Database defined in user code
    Database
};

/// <summary>
/// Function name
/// In Osiris, multiple functions are allowed with the same name,
/// if they have different arity (number of parameters).
/// </summary>
public class FunctionNameAndArity(string name, int arity) : IEquatable<FunctionNameAndArity>
{
    // Function name
    public string Name { get; init; } = name ?? throw new ArgumentNullException(nameof(name));
    // Number of parameters
    public int Arity { get; init; } = arity;

    public override bool Equals(object? fun)
    {
        return Equals(fun as FunctionNameAndArity);
    }

    public bool Equals(FunctionNameAndArity? fun)
    {
        if (fun == null) return false;
        return string.Equals(Name, fun.Name, StringComparison.OrdinalIgnoreCase)
           && Arity == fun.Arity;
    }

    public override int GetHashCode()
    {
        return string.GetHashCode(Name, StringComparison.OrdinalIgnoreCase) ^ Arity;
    }

    public override string ToString()
    {
        return $"{Name}({Arity})";
    }
}

/// <summary>
/// Function parameter (or database column, depending on the function type)
/// </summary>
public class FunctionParam
{
    // Parameter direction, i.e. either In or Out.
    public ParamDirection Direction;
    // Parameter type
    // For builtin functions this is always taken from the function header.
    // For user defined functions this is inferred from code.
    public ValueType Type { get; set; } = new();
    // Parameter name
    public string Name { get; set; } = string.Empty;
}

public class FunctionSignature
{
    // Type of function (call, query, database, etc.)
    public FunctionType Type;
    // Function name
    public string Name { get; set; } = string.Empty;
    // List of arguments
    public List<FunctionParam> Params { get; set; } = [];
    // Indicates that we were able to infer the type of all parameters
    public bool FullyTyped;
    // Indicates that the database is "inserted into" in at least one place
    public bool Inserted;
    // Indicates that the database is "deleted from" in at least one place
    public bool Deleted;
    // Indicates that the function is "read" in at least one place
    public bool Read;

    public FunctionNameAndArity GetNameAndArity() => new(Name, Params.Count);
}

/// <summary>
/// Metadata for built-in functions
/// </summary>
public class BuiltinFunction
{
    public FunctionSignature Signature { get; set; } = new();
    // Metadata passed from story headers. These aren't used at all during compilation and are only used in the compiled story file.
    public UInt32 Meta1;
    public UInt32 Meta2;
    public UInt32 Meta3;
    public UInt32 Meta4;
}

/// <summary>
/// Diagnostic message level
/// </summary>
public enum MessageLevel
{
    Info,
    Error,
    Warning
}

/// <summary>
/// Holder for compiler diagnostic codes.
/// </summary>
public class DiagnosticCode
{
    /// <summary>
    /// Miscellaenous internal error - should not happen.
    /// </summary>
    public const string InternalError = "E00";
    /// <summary>
    /// A type ID was declared multiple times in the story definition file.
    /// </summary>
    public const string TypeIdAlreadyDefined = "E01";
    /// <summary>
    /// A type name (alias)  was declared multiple times in the story definition file.
    /// </summary>
    public const string TypeNameAlreadyDefined = "E02";
    /// <summary>
    /// The type ID is either an intrinsic ID or is outside the allowed range.
    /// </summary>
    public const string TypeIdInvalid = "E03";
    /// <summary>
    /// The alias type ID doesn't point to a valid intrinsic type ID
    /// </summary>
    public const string IntrinsicTypeIdInvalid = "E04";

    /// <summary>
    /// A function with the same signature already exists.
    /// </summary>
    public const string SignatureAlreadyDefined = "E05";
    /// <summary>
    /// The type of an argument could not be resolved in a builtin function.
    /// (This only occurs when parsing story headers, not in goal code)
    /// </summary>
    public const string UnresolvedTypeInSignature = "E06";
    /// <summary>
    /// A goal with the same name was seen earlier.
    /// </summary>
    public const string GoalAlreadyDefined = "E07";
    /// <summary>
    /// The parent goal specified in the goal script was not found.
    /// </summary>
    public const string UnresolvedGoal = "E08";
    /// <summary>
    /// Failed to infer the type of a rule-local variable.
    /// </summary>
    public const string UnresolvedVariableType = "E09";
    /// <summary>
    /// The function signature (full typed parameter list) of a function
    /// could not be determined. This is likely the result of a failed type inference.
    /// </summary>
    public const string UnresolvedSignature = "E10";
    /// <summary>
    /// The intrinsic type of a function parameter does not match the expected type.
    /// </summary>
    public const string LocalTypeMismatch = "E11";
    /// <summary>
    /// Value with unknown type encountered during IR generation.
    /// </summary>
    public const string UnresolvedType = "E12";
    /// <summary>
    /// PROC/QRY declarations must start with a PROC/QRY name as the first condition.
    /// </summary>
    public const string InvalidProcDefinition = "E13";
    /// <summary>
    /// Fact contains a function that is not callable
    /// (the function is not a call, database or proc).
    /// </summary>
    public const string InvalidSymbolInFact = "E14";
    /// <summary>
    /// Rule action contains a function that is not callable
    /// (the function is not a call, database or proc).
    /// </summary>
    public const string InvalidSymbolInStatement = "E15";
    /// <summary>
    /// "NOT" action contains a non-database function.
    /// </summary>
    public const string CanOnlyDeleteFromDatabase = "E16";
    /// <summary>
    /// Initial PROC/QRY/IF function type differs from allowed type.
    /// </summary>
    public const string InvalidSymbolInInitialCondition = "E17";
    /// <summary>
    /// Condition contains a function that is not a query or database.
    /// </summary>
    public const string InvalidFunctionTypeInCondition = "E18";
    /// <summary>
    /// Function name could not be resolved.
    /// </summary>
    public const string UnresolvedSymbol = "E19";
    /// <summary>
    /// Use of less/greater operators on strings or guidstrings.
    /// </summary>
    public const string StringLtGtComparison = "W20";
    /// <summary>
    /// The alias type of a function parameter does not match the expected type.
    /// </summary>
    public const string GuidAliasMismatch = "E21";
    /// <summary>
    /// Object name GUID is prefixed with a type that is not known.
    /// </summary>
    public const string GuidPrefixNotKnown = "W22";
    /// <summary>
    /// PROC_/QRY_ naming style violation.
    /// </summary>
    public const string RuleNamingStyle = "W23";
    /// <summary>
    /// A rule variable was used in a read context, but was not yet bound.
    /// </summary>
    public const string ParamNotBound = "E24";
    /// <summary>
    /// The database is likely unused or unpopulated.
    /// (Written but not read, or vice versa)
    /// </summary>
    public const string UnusedDatabaseWarning = "W25";
    /// <summary>
    /// The database is likely unused or unpopulated.
    /// (Written but not read, or vice versa)
    /// </summary>
    public const string UnusedDatabaseError = "E25";
    /// <summary>
    /// Database "DB_" naming convention violation.
    /// </summary>
    public const string DbNamingStyle = "W26";
    /// <summary>
    /// Object name GUID could not be resolved to a game object.
    /// </summary>
    public const string UnresolvedGameObjectName = "W27";
    /// <summary>
    /// Type of name GUID differs from type of game object.
    /// </summary>
    public const string GameObjectTypeMismatch = "W28";
    /// <summary>
    /// Name part of name GUID differs from name of game object.
    /// </summary>
    public const string GameObjectNameMismatch = "W29";
    /// <summary>
    /// Multiple definitions seen for the same function with different signatures.
    /// </summary>
    public const string ProcTypeMismatch = "E30";
    /// <summary>
    /// Attempted to cast a type to an unrelated/incompatible type (i.e. STRING to INTEGER)
    /// </summary>
    public const string CastToUnrelatedType = "E31";
    /// <summary>
    /// Attempted to cast an alias to an unrelated alias (i.e. CHARACTERGUID to ITEMGUID)
    /// </summary>
    public const string CastToUnrelatedGuidAlias = "E32";
    /// <summary>
    /// Left-hand side and right-hand side variables are the same in a binary operation.
    /// This will result in an "invalid compare" error in runtime.
    /// </summary>
    public const string BinaryOperationSameRhsLhs = "E33";
    /// <summary>
    /// comparison on types that have known bugs or side effects
    /// (currently this only triggers on GUIDSTRING - STRING comparison)
    /// </summary>
    public const string RiskyComparison = "E34";
    /// <summary>
    /// The database is possibly used in an incorrect way.
    /// (Deleted and read, but not written)
    /// </summary>
    public const string UnwrittenDatabase = "W35";
}

public sealed class Diagnostic(CodeLocation? location, MessageLevel level, string code, string message)
{
    public CodeLocation? Location { get; } = location;
    public MessageLevel Level { get; } = level;
    public string Code { get; } = code ?? throw new ArgumentNullException(nameof(code));
    public string Message { get; } = message ?? throw new ArgumentNullException(nameof(message));
}

public sealed class CompilationLog
{
    public List<Diagnostic> Log { get; set; } = [];
    /// <summary>
    /// Controls whether specific warnings are enabled or disabled.
    /// All are enabled by default.
    /// </summary>
    public Dictionary<string, bool> WarningSwitches { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public CompilationLog()
    {
        WarningSwitches.Add(DiagnosticCode.RuleNamingStyle, false);
        WarningSwitches.Add(DiagnosticCode.UnwrittenDatabase, false);
    }
    public void Warn(CodeLocation? location, string code, string message)
    {
        if (WarningSwitches.TryGetValue(code, out bool enabled) && !enabled) return;

        var diag = new Diagnostic(location, MessageLevel.Warning, code, message);
        Log.Add(diag);
    }
    public void Warn(CodeLocation? location, string code, string format, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(format);
        var compositeFormat = CompositeFormat.Parse(format);
        string message = string.Format(CultureInfo.InvariantCulture, compositeFormat, args);
        Warn(location, code, message);
    }
    public void Error(CodeLocation? location, string code, string message)
    {
        var diag = new Diagnostic(location, MessageLevel.Error, code, message);
        Log.Add(diag);
    }
    public void Error(CodeLocation? location, string code, string format, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(format);
        var compositeFormat = CompositeFormat.Parse(format);
        string message = string.Format(CultureInfo.InvariantCulture, compositeFormat, args);
        Error(location, code, message);
    }
}

public sealed class GameObjectInfo
{
    public string Name { get; set; } = string.Empty;
    public ValueType Type { get; set; } = new();
}

/// <summary>
/// Compilation context that holds input and intermediate data used during the compilation process.
/// </summary>
public sealed class CompilationContext
{
    public const uint MaxIntrinsicTypeId = 5;

    public Dictionary<uint, ValueType> TypesById { get; set; } = [];
    public Dictionary<string, ValueType> TypesByName { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, IRGoal> GoalsByName { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<FunctionNameAndArity, FunctionSignature> Signatures { get; set; } = [];
    public Dictionary<FunctionNameAndArity, object> Functions { get; set; } = [];
    public Dictionary<string, GameObjectInfo> GameObjects { get; set; } = new(StringComparer.Ordinal);
    public CompilationLog Log { get; set; } = new();

    public CompilationContext()
    {
        RegisterIntrinsicTypes();
    }

    /// <summary>
    /// Registers all Osiris builtin types that are not declared in the story header separately
    /// </summary>
    private void RegisterIntrinsicTypes()
    {
        var tUnknown = new ValueType
        {
            Name = "NONE",
            TypeId = 0,
            IntrinsicTypeId = Value.Type.None
        };
        AddType(tUnknown);

        var tInteger = new ValueType
        {
            Name = "INTEGER",
            TypeId = 1,
            IntrinsicTypeId = Value.Type.Integer
        };
        AddType(tInteger);

        var tInteger64 = new ValueType
        {
            Name = "INTEGER64",
            TypeId = 2,
            IntrinsicTypeId = Value.Type.Integer64
        };
        AddType(tInteger64);

        var tReal = new ValueType
        {
            Name = "REAL",
            TypeId = 3,
            IntrinsicTypeId = Value.Type.Float
        };
        AddType(tReal);

        var tString = new ValueType
        {
            Name = "STRING",
            TypeId = 4,
            IntrinsicTypeId = Value.Type.String
        };
        AddType(tString);

        var tGuidString = new ValueType
        {
            Name = "GUIDSTRING",
            TypeId = 5,
            IntrinsicTypeId = Value.Type.GuidString
        };
        AddType(tGuidString);
    }

    private void AddType(ValueType type)
    {
        TypesById.Add(type.TypeId, type);
        TypesByName.Add(type.Name, type);
    }

    public bool RegisterType(ValueType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (TypesById.ContainsKey(type.TypeId))
        {
            Log.Error(null, DiagnosticCode.TypeIdAlreadyDefined, "Type ID already in use");
            return false;
        }

        if (TypesByName.ContainsKey(type.Name))
        {
            Log.Error(null, DiagnosticCode.TypeNameAlreadyDefined, "Type name already in use");
            return false;
        }

        if (type.TypeId < MaxIntrinsicTypeId || type.TypeId > 255)
        {
            Log.Error(null, DiagnosticCode.TypeIdInvalid, "Type ID must be in the range 5..255");
            return false;
        }

        if (type.IntrinsicTypeId <= 0 || (uint)type.IntrinsicTypeId > MaxIntrinsicTypeId)
        {
            Log.Error(null, DiagnosticCode.TypeIdInvalid, "Alias type ID must refer to an intrinsic type");
            return false;
        }

        AddType(type);
        return true;
    }

    public bool RegisterFunction(FunctionSignature signature, object func)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(func);

        var nameAndArity = signature.GetNameAndArity();
        if (Signatures.ContainsKey(nameAndArity))
        {
            Log.Error(null, DiagnosticCode.SignatureAlreadyDefined,
                string.Format(CultureInfo.InvariantCulture, "Signature already registered: {0}({1})", nameAndArity.Name, nameAndArity.Arity));
            return false;
        }

        Signatures.Add(nameAndArity, signature);
        Functions.Add(nameAndArity, func);
        return true;
    }

    public bool RegisterGoal(IRGoal goal)
    {
        ArgumentNullException.ThrowIfNull(goal);

        if (GoalsByName.ContainsKey(goal.Name))
        {
            Log.Error(null, DiagnosticCode.GoalAlreadyDefined,
                string.Format(CultureInfo.InvariantCulture, "Goal already registered: {0}", goal.Name));
            return false;
        }

        GoalsByName.Add(goal.Name, goal);
        return true;
    }


    public ValueType? LookupType(string typeName)
    {
        ArgumentException.ThrowIfNullOrEmpty(typeName);
        return TypesByName.TryGetValue(typeName, out var type) ? type : null;
    }

    public FunctionSignature? LookupSignature(FunctionNameAndArity name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Signatures.TryGetValue(name, out var signature) ? signature : null;
    }

    public object? LookupName(FunctionNameAndArity name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Functions.TryGetValue(name, out var function) ? function : null;
    }

    public IRGoal? LookupGoal(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return GoalsByName.TryGetValue(name, out var goal) ? goal : null;
    }
}

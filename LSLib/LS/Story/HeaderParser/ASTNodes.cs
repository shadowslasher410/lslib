using System;
using System.Collections.Generic;
using LSLib.LS.Story.Compiler;

namespace LSLib.LS.Story.HeaderParser;

/// <summary>
/// Base class for all AST nodes.
/// (This doesn't do anything meaningful, it is needed only to 
/// provide the parser a semantic value base class.)
/// </summary>
public class ASTNode
{
    // Fix: Added backing property tracking line telemetry or literal token context cleanly for Superpower rules integration
    public string Literal { get; set; } = string.Empty;
}

/// <summary>
/// Declarations node - contains every declaration from the story header file.
/// </summary>
public class ASTDeclarations : ASTNode
{
    // Debug options
    public List<string> Options { get; set; } = [];

    // Declared type aliases
    public List<ASTAlias> Aliases { get; set; } = [];

    // Declared functions
    public List<ASTFunction> Functions { get; set; } = [];
}

/// <summary>
/// Function type wrapper node
/// This is discarded during parsing and does not appear in the final AST.
/// </summary>
public class ASTFunctionTypeNode : ASTNode
{
    // Type of function (SysQuery, SysCall, Event, etc.)
    // Fix: Fully qualified the exact global namespace route to completely bypass local class naming ambiguities
    public LSLib.LS.Story.Compiler.FunctionType Type { get; set; }
}

/// <summary>
/// Function meta-information
/// This is discarded during parsing and does not appear in the final AST.
/// </summary>
public class ASTFunctionMetadata : ASTNode
{
    public uint Meta1 { get; set; }
    public uint Meta2 { get; set; }
    public uint Meta3 { get; set; }
    public uint Meta4 { get; set; }
}

/// <summary>
/// Describes a built-in function with its name, number and parameters.
/// </summary>
public class ASTFunction : ASTNode
{
    // Type of function (SysQuery, SysCall, Event, etc.)
    public LSLib.LS.Story.Compiler.FunctionType Type { get; set; }

    // Name of the function
    public string Name { get; set; } = string.Empty;

    // Function parameters
    public List<ASTFunctionParam> Params { get; set; } = [];

    // Function metadata for Osiris internal use - mostly unknown.
    public uint Meta1 { get; set; }
    public uint Meta2 { get; set; }
    public uint Meta3 { get; set; }
    public uint Meta4 { get; set; }
}

/// <summary>
/// List of function parameters
/// This is discarded during parsing and does not appear in the final AST.
/// </summary>
public class ASTFunctionParamList : ASTNode
{
    // Function parameters
    public List<ASTFunctionParam> Params { get; set; } = [];
}

/// <summary>
/// Typed (and optionally direction marked) parameter of a function
/// </summary>
public class ASTFunctionParam : ASTNode
{
    // Parameter name
    public string Name { get; set; } = string.Empty;

    // Parameter type
    public string Type { get; set; } = string.Empty;

    // Parameter direction (IN/OUT)
    // This is only meaningful for Query and SysQuery, for all other types direction is always "IN".
    public ParamDirection Direction { get; set; }
}

/// <summary>
/// Type alias - defines a new type name and type ID, and maps it to an existing base type.
/// </summary>
public class ASTAlias : ASTNode
{
    // Name of the new type
    public string TypeName { get; set; } = string.Empty;

    // ID of the new type (must be a new type ID)
    public uint TypeId { get; set; }

    // ID of the type this type is mapped to (must be an existing type ID)
    public uint AliasId { get; set; }
}

/// <summary>
/// Debug/compiler option
/// This is discarded during parsing and does not appear in the final AST.
/// </summary>
public class ASTOption : ASTNode
{
    // Name of debug option
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// String literal from lexing stage (yytext).
/// This is discarded during parsing and does not appear in the final AST.
/// </summary>
public class ASTLiteral : ASTNode
{
}
using LSLib.LS.Story.HeaderParser;
using Superpower;
using Superpower.Model;
using System.Globalization;

namespace LSLib.LS.Story.Compiler;

/// <summary>
/// Responsible for parsing story header files (story_header.div),
/// and loading header definitions to the compilation context.
/// </summary>
public class StoryHeaderLoader(CompilationContext context)
{
    private readonly CompilationContext _context = context ?? throw new ArgumentNullException(nameof(context));


    /// <summary>
    /// Creates and loads a type alias (e.g. CHARACTERGUID, ITEMGUID, etc.) from an AST node.
    /// </summary>
    private bool LoadAliasFromAST(ASTAlias astAlias)
    {
        ArgumentNullException.ThrowIfNull(astAlias);

        var type = new ValueType
        {
            Name = astAlias.TypeName ?? string.Empty,
            TypeId = astAlias.TypeId,
            IntrinsicTypeId = (Value.Type)astAlias.AliasId
        };
        return _context.RegisterType(type);
    }

    /// <summary>
    /// Creates and loads a function declaration from an AST node.
    /// </summary>
    private bool LoadFunctionFromAST(ASTFunction astFunction)
    {
        ArgumentNullException.ThrowIfNull(astFunction);

        var args = new List<FunctionParam>(astFunction.Params.Count);
        foreach (var astParam in astFunction.Params)
        {
            if (astParam is null) continue;

            var type = _context.LookupType(astParam.Type);
            // Since types and alias types are declared at the beginning of the
            // story header, we should have full type information here, so any
            // unresolved types will be flagged as an error.
            if (type is null)
            {
                _context.Log.Error(null, DiagnosticCode.UnresolvedTypeInSignature,
                    string.Format(CultureInfo.InvariantCulture, "Function \"{0}({1})\" argument \"{2}\" has unresolved type \"{3}\"",
                        astFunction.Name, astFunction.Params.Count, astParam.Name, astParam.Type));
                continue;
            }

            var param = new FunctionParam
            {
                Name = astParam.Name ?? string.Empty,
                Type = type,
                Direction = astParam.Direction
            };
            args.Add(param);
        }

        var signature = new FunctionSignature
        {
            Name = astFunction.Name ?? string.Empty,
            Type = astFunction.Type,
            Params = args,
            FullyTyped = true,
            Inserted = false,
            Deleted = false,
            Read = false
        };

        var func = new BuiltinFunction
        {
            Signature = signature,
            Meta1 = astFunction.Meta1,
            Meta2 = astFunction.Meta2,
            Meta3 = astFunction.Meta3,
            Meta4 = astFunction.Meta4
        };

        return _context.RegisterFunction(signature, func);
    }

    /// <summary>
    /// Parses a story header file into an AST.
    /// </summary>
    public static ASTDeclarations? ParseHeader(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        string fileContent = reader.ReadToEnd();

        if (string.IsNullOrWhiteSpace(fileContent)) return null;
        TokenList<HeaderTokens> tokens = HeaderTokenizer.Instance.Tokenize(fileContent);
        TokenListParserResult<HeaderTokens, ASTDeclarations> result = HeaderCombinatorParser.HeaderFileParser.TryParse(tokens);

        if (!result.HasValue)
        {
            Position errorPos = result.ErrorPosition;
            throw new InvalidDataException($"Story Header syntax fault at line {errorPos.Line}, column {errorPos.Column}: {result.ErrorMessage}");
        }

        return result.Value;
    }

    /// <summary>
    /// Loads all declarations from a story header file.
    /// </summary>
    public void LoadHeader(ASTDeclarations declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        foreach (var alias in declarations.Aliases)
        {
            if (alias is not null) LoadAliasFromAST(alias);
        }

        foreach (var func in declarations.Functions)
        {
            if (func is not null) LoadFunctionFromAST(func);
        }
    }
}
using LSLib.DebuggerFrontend.ExpressionParser;
using LSLib.LS.Story.Compiler;
using Superpower;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LSTools.DebuggerFrontend.ExpressionParser;

internal static class ParserConstants
{
    public static readonly CultureInfo ParserCulture = CultureInfo.ReadOnly(new CultureInfo("en-US"));
}

public class ExpressionParser(string expression)
{
    private readonly string _expression = expression ?? string.Empty;

    private Statement? CurrentSemanticValue { get; set; }

    public bool Parse()
    {
        if (string.IsNullOrWhiteSpace(_expression)) return false;

        try
        {
            var tokens = ExpressionTokenizer.Instance.Tokenize(_expression);
            var result = ExpressionParserCombinators.Expression.TryParse(tokens);

            if (result.HasValue)
            {
                CurrentSemanticValue = result.Value;
                return true;
            }
        }
        catch
        {
            // Suppress
        }

        return false;
    }

    public Statement? GetStatement() => CurrentSemanticValue;

    #region Internal Factory Engine Mapping Methods

    internal static string CleanString(string lit)
    {
        ArgumentNullException.ThrowIfNull(lit);
        if (lit.StartsWith('L')) lit = lit[1..];

        string content = lit.Length >= 2 && lit.StartsWith('"') && lit.EndsWith('"') ? lit[1..^1] : lit;
        return Regex.Unescape(content);
    }

    internal static LocalVar MakeLocalVar(string varName) => new()
    {
        Name = varName ?? string.Empty
    };

    internal static LocalVar MakeLocalVar(string typeName, string varName) => new()
    {
        Type = typeName ?? string.Empty,
        Name = varName ?? string.Empty
    };

    internal static ConstantValue MakeTypedConstant(string typeName, ConstantValue c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new()
        {
            TypeName = typeName ?? string.Empty,
            Type = c.Type,
            StringValue = c.StringValue ?? string.Empty,
            FloatValue = c.FloatValue,
            IntegerValue = c.IntegerValue,
        };
    }

    internal static ConstantValue MakeConstGuidString(string val) => new()
    {
        Type = IRConstantType.Name,
        StringValue = val ?? string.Empty
    };

    internal static ConstantValue MakeConstString(string val) => new()
    {
        Type = IRConstantType.String,
        StringValue = CleanString(val)
    };

    internal static ConstantValue MakeConstInteger(string val) => new()
    {
        Type = IRConstantType.Integer,
        IntegerValue = long.Parse(val, ParserConstants.ParserCulture.NumberFormat)
    };

    internal static ConstantValue MakeConstFloat(string val) => new()
    {
        Type = IRConstantType.Float,
        FloatValue = float.Parse(val, ParserConstants.ParserCulture.NumberFormat)
    };

    internal static object MakeParamList(RValue single) => new StatementParamList
    {
        Params = [single]
    };

    internal static object MakeParamList(object currentList, RValue next)
    {
        if (currentList is StatementParamList existingList)
        {
            existingList.Params.Add(next);
            return existingList;
        }
        return new StatementParamList { Params = [next] };
    }

    #endregion
}
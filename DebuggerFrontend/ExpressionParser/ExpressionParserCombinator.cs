using LSLib.LS.Story.Compiler;
using LSLib.DebuggerFrontend.ExpressionParser;
using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using ExpressionParserType = LSTools.DebuggerFrontend.ExpressionParser.ExpressionParser;

namespace LSTools.DebuggerFrontend;

public static class ExpressionParserCombinators
{
    private static string GetValue(Token<ExpressionTokens> token) => token.Span.ToString();

    private static readonly TokenListParser<ExpressionTokens, ConstantValue> GuidStringParser =
        Token.EqualTo(ExpressionTokens.GUIDSTRING).Select(static t => ExpressionParserType.MakeConstGuidString(GetValue(t)));

    private static readonly TokenListParser<ExpressionTokens, ConstantValue> StringParser =
        Token.EqualTo(ExpressionTokens.STRING).Select(static t => ExpressionParserType.MakeConstString(GetValue(t)));

    private static readonly TokenListParser<ExpressionTokens, ConstantValue> IntegerParser =
        Token.EqualTo(ExpressionTokens.INTEGER).Select(static t => ExpressionParserType.MakeConstInteger(GetValue(t)));

    private static readonly TokenListParser<ExpressionTokens, ConstantValue> FloatParser =
        Token.EqualTo(ExpressionTokens.FLOAT).Select(static t => ExpressionParserType.MakeConstFloat(GetValue(t)));

    private static readonly TokenListParser<ExpressionTokens, ConstantValue> BaseConstant =
        GuidStringParser.Or(StringParser).Or(FloatParser).Or(IntegerParser);

    private static readonly TokenListParser<ExpressionTokens, RValue> TypedConstant =
        (from open in Token.EqualTo(ExpressionTokens.OpenParenthesis)
         from typeId in Token.EqualTo(ExpressionTokens.IDENTIFIER)
         from close in Token.EqualTo(ExpressionTokens.CloseParenthesis)
         from cv in BaseConstant
         select (RValue)ExpressionParserType.MakeTypedConstant(GetValue(typeId), cv))
        .Or(BaseConstant.Select(static cv => (RValue)cv));

    private static readonly TokenListParser<ExpressionTokens, RValue> TypedLocalVar =
        (from open in Token.EqualTo(ExpressionTokens.OpenParenthesis)
         from typeId in Token.EqualTo(ExpressionTokens.IDENTIFIER)
         from close in Token.EqualTo(ExpressionTokens.CloseParenthesis)
         from lvar in Token.EqualTo(ExpressionTokens.LOCAL_VAR)
         select (RValue)ExpressionParserType.MakeLocalVar(GetValue(typeId), GetValue(lvar)))
        .Or(Token.EqualTo(ExpressionTokens.LOCAL_VAR).Select(static t => (RValue)ExpressionParserType.MakeLocalVar(GetValue(t))));

    private static readonly TokenListParser<ExpressionTokens, RValue> Param =
        TypedConstant.Or(TypedLocalVar);

    private static readonly TokenListParser<ExpressionTokens, object> ParamList =
        Param.ManyDelimitedBy(Token.EqualTo(ExpressionTokens.Comma))
            .Select(static arr =>
            {
                if (arr.Length == 0) return MakeParamList();

                object listState = ExpressionParserType.MakeParamList(arr[0]);

                for (int i = 1; i < arr.Length; i++)
                {
                    listState = ExpressionParserType.MakeParamList(listState, arr[i]);
                }

                return listState;
            })
            .OptionalOrDefault(MakeParamList());

    public static readonly TokenListParser<ExpressionTokens, Statement> Expression =
        (from notKeyword in Token.EqualTo(ExpressionTokens.NOT)
         from name in Token.EqualTo(ExpressionTokens.IDENTIFIER)
         from open in Token.EqualTo(ExpressionTokens.OpenParenthesis)
         from pList in ParamList
         from close in Token.EqualTo(ExpressionTokens.CloseParenthesis)
         select MakeStatement(GetValue(name), pList, true))

        .Or(from name in Token.EqualTo(ExpressionTokens.IDENTIFIER)
            from open in Token.EqualTo(ExpressionTokens.OpenParenthesis)
            from pList in ParamList
            from close in Token.EqualTo(ExpressionTokens.CloseParenthesis)
            select MakeStatement(GetValue(name), pList, false))

        .Or(Token.EqualTo(ExpressionTokens.IDENTIFIER).Select(static t => MakeStatement(GetValue(t), false)));

    #region AST Factory Mapping Hooks

    private static Statement MakeStatement(string name, bool isNot) => new()
    {
        Name = name ?? string.Empty,
        Not = isNot,
        Params = []
    };

    private static Statement MakeStatement(string name, object paramList, bool isNot) => new()
    {
        Name = name ?? string.Empty,
        Not = isNot,
        Params = paramList switch
        {
            StatementParamList pList => pList.Params,
            List<RValue> rawList => [.. rawList], // 3. C# 14 collection spread operator mapping
            _ => []
        }
    };

    private static StatementParamList MakeParamList() => new() { Params = [] };

    private static StatementParamList MakeParamList(RValue single) => new() { Params = [single] };

    private static StatementParamList MakeParamList(object currentList, RValue next)
    {
        if (currentList is StatementParamList existingList)
        {
            existingList.Params.Add(next);
            return existingList;
        }
        return new StatementParamList { Params = [next] };
    }

    private static RValue MakeTypedConstant(string typeName, RValue constant)
    {
        if (constant is ConstantValue cVal)
        {
            cVal.TypeName = typeName ?? string.Empty;
            return cVal;
        }
        return constant;
    }

    private static LocalVar MakeLocalVar(string varName) => new()
    {
        Name = varName ?? string.Empty,
        Type = string.Empty
    };

    private static LocalVar MakeLocalVar(string typeName, string varName) => new()
    {
        Type = typeName ?? string.Empty,
        Name = varName ?? string.Empty
    };

    private static ConstantValue MakeConstGuidString(string val) => new()
    {
        Type = IRConstantType.Name,
        StringValue = val ?? string.Empty
    };

    private static ConstantValue MakeConstString(string val) => new()
    {
        Type = IRConstantType.String,
        StringValue = val is { Length: > 1 } && val.StartsWith('"') && val.EndsWith('"') ? val[1..^1] : (val ?? string.Empty)
    };

    private static ConstantValue MakeConstInteger(string val) => new()
    {
        Type = IRConstantType.Integer,
        IntegerValue = long.TryParse(val, out var res) ? res : 0
    };

    private static ConstantValue MakeConstFloat(string val) => new()
    {
        Type = IRConstantType.Float,
        FloatValue = float.TryParse(val, System.Globalization.CultureInfo.InvariantCulture, out var res) ? res : 0f
    };

    #endregion
}
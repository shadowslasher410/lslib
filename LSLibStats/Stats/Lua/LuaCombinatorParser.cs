using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using System.Diagnostics.CodeAnalysis;

namespace LSLibStats.Stats.Lua;

public interface ILuaNode { }
public sealed record LuaReservedNode(string Value) : ILuaNode;
public sealed record LuaIntegerNode(string Value) : ILuaNode;
public sealed record LuaFloatNode(string Value) : ILuaNode;
public sealed record LuaStringNode(string Value) : ILuaNode;
public sealed record LuaDiceRollNode(string Value) : ILuaNode;
public sealed record LuaVariableNode(string Name) : ILuaNode;
public sealed record LuaTableNode(List<LuaFieldNode> Fields) : ILuaNode;
public sealed record LuaFieldNode(ILuaNode? Key, ILuaNode Value) : ILuaNode;
public sealed record LuaBinaryOpNode(string Operator, ILuaNode Left, ILuaNode Right) : ILuaNode;
public sealed record LuaUnaryOpNode(string Operator, ILuaNode Inner) : ILuaNode;
public sealed record LuaFunctionCallNode(ILuaNode Prefix, string? MethodName, List<ILuaNode> Args) : ILuaNode;
public sealed record LuaIndexNode(ILuaNode Prefix, ILuaNode Index) : ILuaNode;
public sealed record LuaMemberAccessNode(ILuaNode Prefix, string Member) : ILuaNode;

public static class LuaCombinatorParser
{
    private static readonly TokenListParser<StatLuaTokens, string> NameParser =
        Token.EqualTo(StatLuaTokens.NAME).Select(t => t.ToStringValue() ?? string.Empty);

    private static readonly TokenListParser<StatLuaTokens, string> IntParser =
        Token.EqualTo(StatLuaTokens.INTEGER).Select(t => t.ToStringValue() ?? string.Empty);

    private static readonly TokenListParser<StatLuaTokens, string> FloatParser =
        Token.EqualTo(StatLuaTokens.FLOAT).Select(t => t.ToStringValue() ?? string.Empty);

    private static readonly TokenListParser<StatLuaTokens, string> StringParser =
        Token.EqualTo(StatLuaTokens.LITERAL_STRING).Select(t => t.ToStringValue() ?? string.Empty);

    private static readonly TokenListParser<StatLuaTokens, string> DiceParser =
        Token.EqualTo(StatLuaTokens.DICE_ROLL).Select(t => t.ToStringValue() ?? string.Empty);

    private static readonly TokenListParser<StatLuaTokens, string> ReservedParser =
        Token.EqualTo(StatLuaTokens.LUA_RESERVED_VAL).Select(t => t.ToStringValue() ?? string.Empty);

    private static readonly TokenListParser<StatLuaTokens, string> BinOpParser =
        Token.EqualTo(StatLuaTokens.BINOP).Or(Token.EqualTo(StatLuaTokens.BIN_OR_UNOP)).Select(t => t.ToStringValue() ?? string.Empty);

    private static readonly TokenListParser<StatLuaTokens, ILuaNode> ExpressionParser =
        ParseRef(() => LExpParser!);

    private static readonly TokenListParser<StatLuaTokens, LuaFieldNode> FieldParser =
        (from open in Token.EqualTo(StatLuaTokens.LEFT_BRACKET)
         from key in ExpressionParser
         from close in Token.EqualTo(StatLuaTokens.RIGHT_BRACKET)
         from eq in Token.EqualTo((StatLuaTokens)'=')
         from val in ExpressionParser
         select new LuaFieldNode(key, val))
        .Or(from name in NameParser
            from eq in Token.EqualTo((StatLuaTokens)'=')
            from val in ExpressionParser
            select new LuaFieldNode(new LuaVariableNode(name), val))
        .Or(ExpressionParser.Select(val => new LuaFieldNode(null, val)));

    private static readonly TokenListParser<StatLuaTokens, ILuaNode> TableConstructorParser =
        from open in Token.EqualTo(StatLuaTokens.LEFT_BRACE)
        from fields in FieldParser.ManyDelimitedBy(Token.EqualTo(StatLuaTokens.COMMA).Or(Token.EqualTo(StatLuaTokens.SEMICOLON))).OptionalOrDefault([])
        from close in Token.EqualTo(StatLuaTokens.RIGHT_BRACE)
        select (ILuaNode)new LuaTableNode([.. fields]);

    private static readonly TokenListParser<StatLuaTokens, List<ILuaNode>> ArgsParser =
        (from open in Token.EqualTo(StatLuaTokens.LEFT_PAREN)
         from list in ExpressionParser.ManyDelimitedBy(Token.EqualTo(StatLuaTokens.COMMA)).OptionalOrDefault([])
         from close in Token.EqualTo(StatLuaTokens.RIGHT_PAREN)
         select list.ToList())
        .Or(TableConstructorParser.Select(t => new List<ILuaNode> { t }))
        .Or(StringParser.Select(s => new List<ILuaNode> { new LuaStringNode(s) }));

    private static readonly TokenListParser<StatLuaTokens, ILuaNode> PrefixBaseParser =
        NameParser.Select(n => (ILuaNode)new LuaVariableNode(n))
        .Or(from open in Token.EqualTo(StatLuaTokens.LEFT_PAREN)
            from inner in ExpressionParser
            from close in Token.EqualTo(StatLuaTokens.RIGHT_PAREN)
            select inner);

    private static readonly TokenListParser<StatLuaTokens, ILuaNode> PrefixExpParser =
        from baseNode in PrefixBaseParser
        from extensions in (
            (from open in Token.EqualTo(StatLuaTokens.LEFT_BRACKET)
             from index in ExpressionParser
             from close in Token.EqualTo(StatLuaTokens.RIGHT_BRACKET)
             select new Func<ILuaNode, ILuaNode>(prefix => new LuaIndexNode(prefix, index)))
            .Or(from dot in Token.EqualTo(StatLuaTokens.DOT)
                from name in NameParser
                select new Func<ILuaNode, ILuaNode>(prefix => new LuaMemberAccessNode(prefix, name)))
            .Or(from colon in Token.EqualTo(StatLuaTokens.COLON)
                from method in NameParser
                from args in ArgsParser
                select new Func<ILuaNode, ILuaNode>(prefix => new LuaFunctionCallNode(prefix, method, args)))
            .Or(from args in ArgsParser
                select new Func<ILuaNode, ILuaNode>(prefix => new LuaFunctionCallNode(prefix, null, args)))
        ).Many()
        select extensions.Aggregate(baseNode, (current, func) => func(current));

    private static readonly TokenListParser<StatLuaTokens, ILuaNode> LExpNoUnOpParser =
        ReservedParser.Select(v => (ILuaNode)new LuaReservedNode(v))
        .Or(IntParser.Select(v => (ILuaNode)new LuaIntegerNode(v)))
        .Or(FloatParser.Select(v => (ILuaNode)new LuaFloatNode(v)))
        .Or(StringParser.Select(v => (ILuaNode)new LuaStringNode(v)))
        .Or(DiceParser.Select(v => (ILuaNode)new LuaDiceRollNode(v)))
        .Or(TableConstructorParser)
        .Or(PrefixExpParser);

    private static readonly TokenListParser<StatLuaTokens, ILuaNode> LExpParser =
        (from unOp in Token.EqualTo(StatLuaTokens.UNOP).Or(Token.EqualTo(StatLuaTokens.BIN_OR_UNOP)).Select(t => t.ToStringValue() ?? string.Empty)
         from inner in ParseRef(() => LExpParser!)
         select (ILuaNode)new LuaUnaryOpNode(unOp, inner))
        .Or(LExpNoUnOpParser).Chain(BinOpParser, LExpNoUnOpParser, (op, left, right) => new LuaBinaryOpNode(op, left, right));


    private static TokenListParser<TToken, TResult> ParseRef<TToken, TResult>(Func<TokenListParser<TToken, TResult>> factory)
    {
        TokenListParser<TToken, TResult>? cache = null;
        return stream => (cache ??= factory())(stream);
    }

    public static bool TryParse(string source, [NotNullWhen(true)] out ILuaNode? rootNode, out string errorLog)
    {
        rootNode = null;
        errorLog = string.Empty;

        var tokens = LuaTokenizer.Tokenize(source);
        if (tokens.Any(t => t.Kind == StatLuaTokens.BAD))
        {
            errorLog = "Lexical analyzer error: Unexpected operators matched inside Lua expression.";
            return false;
        }

        var cleanTokens = tokens.Where(t => t.Kind != StatLuaTokens.BAD).ToArray();
        if (cleanTokens.Length == 0) return false;

        var tokenStream = new TokenList<StatLuaTokens>(cleanTokens);
        var result = ExpressionParser.AtEnd().TryParse(tokenStream);

        if (!result.HasValue)
        {
            errorLog = $"Lua grammar contract restriction break: {result.FormatErrorMessageFragment()}";
            return false;
        }

        rootNode = result.Value;
        return true;
    }
}
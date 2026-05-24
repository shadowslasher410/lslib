using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using System.Diagnostics.CodeAnalysis;

namespace LSLibStats.Stats.Expression;

public interface IExpressionNode { }
public sealed record IntegerNode(string Value) : IExpressionNode;
public sealed record DiceRollNode(string Value) : IExpressionNode;
public sealed record VariableNode(string Name) : IExpressionNode;
public sealed record StatusPropertyNode(string ObjectName, string PropName) : IExpressionNode;
public sealed record ContextPropertyNode(string Context, IExpressionNode Inner) : IExpressionNode;
public sealed record BinaryOpNode(string Operator, IExpressionNode Left, IExpressionNode Right) : IExpressionNode;
public sealed record UnaryOpNode(string Operator, IExpressionNode Inner) : IExpressionNode;
public sealed record FunctionNode(string Name, List<IExpressionNode> Args) : IExpressionNode;

public static class ExpressionCombinatorParser
{
    private static readonly TokenListParser<ExpressionTokens, string> NameParser =
        Token.EqualTo(ExpressionTokens.NAME).Select(t => t.ToStringValue());

    private static readonly TokenListParser<ExpressionTokens, string> IntParser =
        Token.EqualTo(ExpressionTokens.INTEGER).Select(t => t.ToStringValue());

    private static readonly TokenListParser<ExpressionTokens, string> DiceParser =
        Token.EqualTo(ExpressionTokens.DICE_ROLL).Select(t => t.ToStringValue());

    private static readonly TokenListParser<ExpressionTokens, string> BinOpParser =
        Token.EqualTo(ExpressionTokens.BINOP).Or(Token.EqualTo(ExpressionTokens.BIN_OR_UNOP)).Select(t => t.ToStringValue());

    private static readonly TokenListParser<ExpressionTokens, IExpressionNode> ExpressionParser =
        ParseRef(() => LExpParser!);

    private static readonly TokenListParser<ExpressionTokens, IExpressionNode> LVarParser =
        (from name in NameParser
         from dot in Token.EqualTo((ExpressionTokens)'.')
         from prop in Token.EqualTo(ExpressionTokens.STATUS_PROPERTY)
         select (IExpressionNode)new StatusPropertyNode(name, prop.ToStringValue()))
        .Or(Token.EqualTo(ExpressionTokens.VARIABLE_REF).Select(t => (IExpressionNode)new VariableNode(t.ToStringValue())));

    private static readonly TokenListParser<ExpressionTokens, IExpressionNode> LevelMapValueParser =
        from kw in Token.EqualTo(ExpressionTokens.LEVEL_MAP_VALUE)
        from open in Token.EqualTo((ExpressionTokens)'(')
        from name in NameParser
        from close in Token.EqualTo((ExpressionTokens)')')
        select (IExpressionNode)new FunctionNode("LevelMapValue", [new VariableNode(name)]);

    private static readonly TokenListParser<ExpressionTokens, IExpressionNode> ClassLevelParser =
        from kw in Token.EqualTo(ExpressionTokens.CLASS_LEVEL)
        from open in Token.EqualTo((ExpressionTokens)'(')
        from name in NameParser
        from close in Token.EqualTo((ExpressionTokens)')')
        select (IExpressionNode)new FunctionNode("ClassLevel", [new VariableNode(name)]);

    private static readonly TokenListParser<ExpressionTokens, IExpressionNode> ResourceRollParser =
        from kw in Token.EqualTo(ExpressionTokens.RESOURCE_ROLL)
        from open in Token.EqualTo((ExpressionTokens)'(')
        from name in NameParser
        from comma in Token.EqualTo((ExpressionTokens)',')
        from count in IntParser
        from close in Token.EqualTo((ExpressionTokens)')')
        select (IExpressionNode)new FunctionNode("ResourceRoll", [new VariableNode(name), new IntegerNode(count)]);

    private static readonly TokenListParser<ExpressionTokens, IExpressionNode> ForEachParser =
        from kw in Token.EqualTo(ExpressionTokens.FOR_EACH)
        from open in Token.EqualTo((ExpressionTokens)'(')
        from left in ExpressionParser
        from comma in Token.EqualTo((ExpressionTokens)',')
        from right in ExpressionParser
        from close in Token.EqualTo((ExpressionTokens)')')
        select (IExpressionNode)new FunctionNode("foreach", [left, right]);

    private static readonly TokenListParser<ExpressionTokens, IExpressionNode> MaxParser =
        from kw in Token.EqualTo(ExpressionTokens.MAX_EXPR)
        from open in Token.EqualTo((ExpressionTokens)'(')
        from args in ExpressionParser.ManyDelimitedBy(Token.EqualTo((ExpressionTokens)','))
        from close in Token.EqualTo((ExpressionTokens)')')
        select (IExpressionNode)new FunctionNode("max", [.. args]);

    private static readonly TokenListParser<ExpressionTokens, IExpressionNode> LVariableExpParser =
        LVarParser.Or(LevelMapValueParser).Or(ClassLevelParser).Or(ResourceRollParser);

    private static readonly TokenListParser<ExpressionTokens, IExpressionNode> LQualifiedExpParser =
        (from ctx in Token.EqualTo(ExpressionTokens.CONTEXT_TYPE)
         from dot in Token.EqualTo((ExpressionTokens)'.')
         from inner in LVariableExpParser
         select (IExpressionNode)new ContextPropertyNode(ctx.ToStringValue(), inner))
        .Or(LVariableExpParser);

    private static readonly TokenListParser<ExpressionTokens, IExpressionNode> LPrefixExpParser =
        LQualifiedExpParser.Or(ForEachParser).Or(MaxParser)
        .Or(from open in Token.EqualTo((ExpressionTokens)'(')
            from inner in ExpressionParser
            from close in Token.EqualTo((ExpressionTokens)')')
            select inner);

    private static readonly TokenListParser<ExpressionTokens, IExpressionNode> LExpNoUnOpParser =
        IntParser.Select(v => (IExpressionNode)new IntegerNode(v))
        .Or(DiceParser.Select(v => (IExpressionNode)new DiceRollNode(v)))
        .Or(LPrefixExpParser);

    private static readonly TokenListParser<ExpressionTokens, IExpressionNode> LExpParser =
        (from unOp in Token.EqualTo(ExpressionTokens.BIN_OR_UNOP)
         from inner in LExpNoUnOpParser
         select (IExpressionNode)new UnaryOpNode(unOp.ToStringValue(), inner))
        .Or(LExpNoUnOpParser).Chain(BinOpParser, LExpNoUnOpParser, (op, left, right) => new BinaryOpNode(op, left, right));

    private static TokenListParser<TToken, TResult> ParseRef<TToken, TResult>(Func<TokenListParser<TToken, TResult>> factory)
    {
        TokenListParser<TToken, TResult>? cache = null;
        return stream => (cache ??= factory())(stream);
    }

    public static bool TryParseExpression(string source, [NotNullWhen(true)] out IExpressionNode? rootNode, out string errorLog)
    {
        rootNode = null;
        errorLog = string.Empty;

        var tokenList = ExpressionTokenizer.Tokenize(source);
        if (tokenList.Any(t => t.Kind == ExpressionTokens.BAD))
        {
            errorLog = "Lexical validation constraint violation: Malformed characters detected during mathematical formula sweeps.";
            return false;
        }

        var cleanTokens = tokenList.Where(t => t.Kind != ExpressionTokens.BAD).ToArray();
        if (cleanTokens.Length == 0) return false;

        var stream = new TokenList<ExpressionTokens>(cleanTokens);
        var result = ExpressionParser.AtEnd().TryParse(stream);

        if (!result.HasValue)
        {
            errorLog = $"Formula syntax layout constraint violation: {result.FormatErrorMessageFragment()}";
            return false;
        }

        rootNode = result.Value;
        return true;
    }
}
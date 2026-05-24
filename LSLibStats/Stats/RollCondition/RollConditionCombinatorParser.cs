using LSLib.Stats.RollConditions;
using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using System.Diagnostics.CodeAnalysis;

namespace LSLibStats.Stats.RollCondition;

public interface IRollConditionNode { }
public sealed record RollConditionElement(string ConditionText) : IRollConditionNode;
public sealed record BracketConditionElement(string Name, string Expression) : IRollConditionNode;

public static class RollConditionCombinatorParser
{
    private static readonly TokenListParser<RollConditionTokens, string> NameParser =
        Token.EqualTo(RollConditionTokens.NAME).Select(t => t.ToStringValue() ?? string.Empty);

    private static readonly TokenListParser<RollConditionTokens, string> TextParser =
        Token.EqualTo(RollConditionTokens.TEXT).Select(t => t.ToStringValue() ?? string.Empty);

    private static readonly TokenListParser<RollConditionTokens, string> AtomParser =
        NameParser.Or(TextParser);

    private static readonly TokenListParser<RollConditionTokens, string> FullExpressionParser =
        AtomParser.AtLeastOnce().Select(words => string.Join(" ", words));

    private static readonly TokenListParser<RollConditionTokens, IRollConditionNode> IndividualConditionParser =
        (from name in NameParser
         from open in Token.EqualTo(RollConditionTokens.LEFT_BRACKET)
         from expr in FullExpressionParser
         from close in Token.EqualTo(RollConditionTokens.RIGHT_BRACKET)
         select (IRollConditionNode)new BracketConditionElement(name, expr))
        .Or(from baseWord in AtomParser
            from trailingExpr in FullExpressionParser.OptionalOrDefault(string.Empty)
            select (IRollConditionNode)new RollConditionElement(
                string.IsNullOrEmpty(trailingExpr) ? baseWord : $"{baseWord} {trailingExpr}"));

    private static readonly TokenListParser<RollConditionTokens, IRollConditionNode?> OptionalConditionParser =
        IndividualConditionParser.Select(c => (IRollConditionNode?)c)
        .Or(Parse.Return<RollConditionTokens, IRollConditionNode?>(null));

    private static readonly TokenListParser<RollConditionTokens, List<IRollConditionNode>> RollConditionsListParser =
        OptionalConditionParser.ManyDelimitedBy(Token.EqualTo(RollConditionTokens.SEMICOLON))
        .Select(items => items.Where(i => i is not null).Select(i => i!).ToList());

    public static bool TryParse(string source, [NotNullWhen(true)] out List<IRollConditionNode>? conditions, out string errorMsg)
    {
        conditions = null;
        errorMsg = string.Empty;

        var tokenList = RollConditionTokenizer.Tokenize(source);
        if (tokenList.Any(t => t.Kind == RollConditionTokens.BAD))
        {
            errorMsg = "Lexical analyzer validation constraint exception: Unexpected character matched inside roll condition blocks.";
            return false;
        }

        var cleanTokens = tokenList.Where(t => t.Kind != RollConditionTokens.BAD).ToArray();
        if (cleanTokens.Length == 0)
        {
            conditions = [];
            return true;
        }

        var tokenStream = new TokenList<RollConditionTokens>(cleanTokens);
        var result = RollConditionsListParser.AtEnd().TryParse(tokenStream);

        if (!result.HasValue)
        {
            errorMsg = $"Roll conditions grammar restriction break: {result.FormatErrorMessageFragment()}";
            return false;
        }

        conditions = result.Value;
        return true;
    }
}

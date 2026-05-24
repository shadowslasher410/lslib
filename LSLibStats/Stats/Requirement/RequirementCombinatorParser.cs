using LSLib.Stats.Requirements;
using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using System.Diagnostics.CodeAnalysis;

namespace LSLibStats.Stats.Requirement;

public static class RequirementCombinatorParser
{
    private static readonly TokenListParser<RequirementTokens, string> NameParser =
        Token.EqualTo(RequirementTokens.NAME).Select(t => t.ToStringValue() ?? string.Empty);

    private static readonly TokenListParser<RequirementTokens, string> IntParser =
        Token.EqualTo(RequirementTokens.INTEGER).Select(t => t.ToStringValue() ?? string.Empty);

    private static readonly TokenListParser<RequirementTokens, Requirement> BaseRequirementParser =
        (from name in NameParser
         from intArg in IntParser
         select new Requirement
         {
             Not = false,
             RequirementName = name,
             IntParam = int.Parse(intArg),
             TagParam = string.Empty
         })
        .Or(NameParser.Select(name => new Requirement
        {
            Not = false,
            RequirementName = name,
            IntParam = 0,
            TagParam = string.Empty
        }));

    private static readonly TokenListParser<RequirementTokens, Requirement> UnaryRequirementParser =
        (from exclamation in Token.EqualTo(RequirementTokens.EXCLAMATION)
         from req in BaseRequirementParser
         select new Requirement 
         {
             Not = true,
             RequirementName = req.RequirementName,
             IntParam = req.IntParam,
             TagParam = req.TagParam
         })
        .Or(BaseRequirementParser);

    private static readonly TokenListParser<RequirementTokens, List<Requirement>> RequirementsListParser =
        UnaryRequirementParser.ManyDelimitedBy(Token.EqualTo(RequirementTokens.SEMICOLON))
        .Select(items => items.ToList());

    public static bool TryParse(string source, [NotNullWhen(true)] out List<Requirement>? requirements, out string errorMessage)
    {
        requirements = null;
        errorMessage = string.Empty;

        var tokenList = RequirementTokenizer.Tokenize(source);
        if (tokenList.Any(t => t.Kind == RequirementTokens.BAD))
        {
            errorMessage = "Lexical error: Malformed characters detected inside requirements string parameter window.";
            return false;
        }

        var cleanTokens = tokenList.Where(t => t.Kind != RequirementTokens.BAD).ToArray();
        if (cleanTokens.Length == 0)
        {
            requirements = [];
            return true;
        }

        var tokenStream = new TokenList<RequirementTokens>(cleanTokens);
        var result = RequirementsListParser.AtEnd().TryParse(tokenStream);

        if (!result.HasValue)
        {
            errorMessage = $"Requirements grammar constraint violation: {result.FormatErrorMessageFragment()}";
            return false;
        }

        requirements = result.Value;
        return true;
    }
}
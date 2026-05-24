using Superpower.Model;
using System.Text.RegularExpressions;

namespace LSLib.Stats.Requirements;

public enum RequirementTokens
{
    BAD = 0,
    NAME,
    INTEGER,
    TEXT,
    SEMICOLON = ';',
    EXCLAMATION = '!'
}

public static partial class RequirementTokenizer
{
    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"^-?[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex IntegerRegex();

    [GeneratedRegex(@"^[^,;:()\[\]!\s]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonSeparatorRegex();

    public static TokenList<RequirementTokens> Tokenize(string source)
    {
        if (string.IsNullOrEmpty(source)) return TokenList<RequirementTokens>.Empty;

        List<Token<RequirementTokens>> tokens = [];
        var next = new TextSpan(source);

        while (!next.IsAtEnd)
        {
            var ch = (next.Source is not null && next.Position.Absolute < next.Source.Length)
                ? next.Source[next.Position.Absolute]
                : '\0';

            if (char.IsWhiteSpace(ch) || ch is '\t')
            {
                next = AdvanceTextSpan(next, 1);
                continue;
            }

            if (ch is ';' or '!')
            {
                var startPosition = next.Position;
                tokens.Add(new Token<RequirementTokens>((RequirementTokens)ch, new TextSpan(source, startPosition, 1)));
                next = AdvanceTextSpan(next, 1);
                continue;
            }

            var remainingText = next.Source is not null ? next.Source[next.Position.Absolute..] : string.Empty;

            if (NameRegex().Match(remainingText) is { Success: true } mName)
            {
                var startPosition = next.Position;
                tokens.Add(new Token<RequirementTokens>(RequirementTokens.NAME, new TextSpan(source, startPosition, mName.Length)));
                next = AdvanceTextSpan(next, mName.Length);
                continue;
            }

            if (IntegerRegex().Match(remainingText) is { Success: true } mInt)
            {
                var startPosition = next.Position;
                tokens.Add(new Token<RequirementTokens>(RequirementTokens.INTEGER, new TextSpan(source, startPosition, mInt.Length)));
                next = AdvanceTextSpan(next, mInt.Length);
                continue;
            }

            if (NonSeparatorRegex().Match(remainingText) is { Success: true } mNonSep && mNonSep.Length > 0)
            {
                var startPosition = next.Position;
                tokens.Add(new Token<RequirementTokens>(RequirementTokens.TEXT, new TextSpan(source, startPosition, mNonSep.Length)));
                next = AdvanceTextSpan(next, mNonSep.Length);
                continue;
            }

            var fallbackBadPos = next.Position;
            tokens.Add(new Token<RequirementTokens>(RequirementTokens.BAD, new TextSpan(source, fallbackBadPos, 1)));
            next = AdvanceTextSpan(next, 1);
        }

        return new TokenList<RequirementTokens>([.. tokens]);
    }

    private static TextSpan AdvanceTextSpan(TextSpan current, int offset)
    {
        if (offset <= 0) return current;
        var absoluteIndex = current.Position.Absolute + offset;

        int linesCount = current.Position.Line;
        int columnsCount = current.Position.Column;

        for (int i = current.Position.Absolute; i < absoluteIndex && i < current.Source?.Length; i++)
        {
            var charAt = current.Source[i];
            if (charAt == '\n')
            {
                linesCount++;
                columnsCount = 1;
            }
            else if (charAt != '\r')
            {
                columnsCount++;
            }
        }

        return new TextSpan(current.Source!, new Position(absoluteIndex, linesCount, columnsCount), current.Source!.Length - absoluteIndex);
    }
}

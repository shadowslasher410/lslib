using Superpower.Model;
using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace LSLibStats.Stats.Lua;

public enum StatLuaTokens
{
    BAD = 0,
    LUA_RESERVED_VAL,
    BINOP,
    UNOP,
    BIN_OR_UNOP,
    LITERAL_STRING,
    NAME,
    INTEGER,
    FLOAT,
    DICE_ROLL,
    COLON = ':',
    LEFT_PAREN = '(',
    RIGHT_PAREN = ')',
    LEFT_BRACKET = '[',
    RIGHT_BRACKET = ']',
    COMMA = ',',
    SEMICOLON = ';',
    DOT = '.',
    LEFT_BRACE = '{',
    RIGHT_BRACE = '}'
}

public static partial class LuaTokenizer
{
    private static readonly FrozenDictionary<string, StatLuaTokens> Keywords = new Dictionary<string, StatLuaTokens>(StringComparer.Ordinal)
    {
        ["nil"] = StatLuaTokens.LUA_RESERVED_VAL,
        ["false"] = StatLuaTokens.LUA_RESERVED_VAL,
        ["true"] = StatLuaTokens.LUA_RESERVED_VAL,
        ["..."] = StatLuaTokens.LUA_RESERVED_VAL,

        ["//"] = StatLuaTokens.BINOP,
        [">>"] = StatLuaTokens.BINOP,
        ["<<"] = StatLuaTokens.BINOP,
        [".."] = StatLuaTokens.BINOP,
        ["<="] = StatLuaTokens.BINOP,
        [">="] = StatLuaTokens.BINOP,
        ["=="] = StatLuaTokens.BINOP,
        ["~="] = StatLuaTokens.BINOP,
        ["and"] = StatLuaTokens.BINOP,
        ["or"] = StatLuaTokens.BINOP,
        ["+"] = StatLuaTokens.BINOP,
        ["*"] = StatLuaTokens.BINOP,
        ["/"] = StatLuaTokens.BINOP,
        ["^"] = StatLuaTokens.BINOP,
        ["%"] = StatLuaTokens.BINOP,
        ["&"] = StatLuaTokens.BINOP,
        ["|"] = StatLuaTokens.BINOP,
        ["<"] = StatLuaTokens.BINOP,
        [">"] = StatLuaTokens.BINOP,

        ["not"] = StatLuaTokens.UNOP,
        ["#"] = StatLuaTokens.UNOP,
        ["!"] = StatLuaTokens.UNOP,

        ["~"] = StatLuaTokens.BIN_OR_UNOP,
        ["-"] = StatLuaTokens.BIN_OR_UNOP
    }.ToFrozenDictionary(StringComparer.Ordinal);

    [GeneratedRegex(@"^""[^""]*""|^'[^']*'", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedStringRegex();

    [GeneratedRegex(@"^[0-9]+d[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex DiceRollRegex();

    [GeneratedRegex(@"^[0-9]+\.[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex FloatRegex();

    [GeneratedRegex(@"^[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex IntegerRegex();

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    public static TokenList<StatLuaTokens> Tokenize(string source)
    {
        if (string.IsNullOrEmpty(source)) return TokenList<StatLuaTokens>.Empty;

        List<Token<StatLuaTokens>> tokens = [];
        var next = new TextSpan(source);

        while (!next.IsAtEnd)
        {
            var ch = (next.Source is not null && next.Position.Absolute < next.Source.Length)
                ? next.Source[next.Position.Absolute]
                : '\0';

            if (char.IsWhiteSpace(ch) || ch is '\t' or '\r' or '\n')
            {
                next = AdvanceTextSpan(next, 1);
                continue;
            }

            var remainingText = next.Source is not null ? next.Source[next.Position.Absolute..] : string.Empty;

            int matchedLength = 0;
            StatLuaTokens matchedTokenKind = StatLuaTokens.BAD;

            foreach (var (keyword, token) in Keywords)
            {
                if (remainingText.StartsWith(keyword, StringComparison.Ordinal) && keyword.Length > matchedLength)
                {
                    // CRITICAL KEYWORD INSULATION FIX: If the keyword is purely text letters, verify the trailing boundary bounds
                    if (char.IsLetter(keyword[0]))
                    {
                        if (remainingText.Length > keyword.Length)
                        {
                            char nextChar = remainingText[keyword.Length];
                            if (char.IsLetterOrDigit(nextChar) || nextChar == '_')
                                continue;
                        }
                    }

                    matchedLength = keyword.Length;
                    matchedTokenKind = token;
                }
            }

            if (matchedLength > 0)
            {
                var startPosition = next.Position;
                tokens.Add(new Token<StatLuaTokens>(matchedTokenKind, new TextSpan(source, startPosition, matchedLength)));
                next = AdvanceTextSpan(next, matchedLength);
                continue;
            }

            if (ch is ':' or '(' or ')' or '[' or ']' or ',' or ';' or '.' or '{' or '}')
            {
                var startPosition = next.Position;
                tokens.Add(new Token<StatLuaTokens>((StatLuaTokens)ch, new TextSpan(source, startPosition, 1)));
                next = AdvanceTextSpan(next, 1);
                continue;
            }

            if (QuotedStringRegex().Match(remainingText) is { Success: true } mString)
            {
                var startPosition = next.Position;
                tokens.Add(new Token<StatLuaTokens>(StatLuaTokens.LITERAL_STRING, new TextSpan(source, startPosition, mString.Length)));
                next = AdvanceTextSpan(next, mString.Length);
                continue;
            }

            if (DiceRollRegex().Match(remainingText) is { Success: true } mDice)
            {
                var startPosition = next.Position;
                tokens.Add(new Token<StatLuaTokens>(StatLuaTokens.DICE_ROLL, new TextSpan(source, startPosition, mDice.Length)));
                next = AdvanceTextSpan(next, mDice.Length);
                continue;
            }

            if (FloatRegex().Match(remainingText) is { Success: true } mFloat)
            {
                var startPosition = next.Position;
                tokens.Add(new Token<StatLuaTokens>(StatLuaTokens.FLOAT, new TextSpan(source, startPosition, mFloat.Length)));
                next = AdvanceTextSpan(next, mFloat.Length);
                continue;
            }

            if (IntegerRegex().Match(remainingText) is { Success: true } mInt)
            {
                var startPosition = next.Position;
                tokens.Add(new Token<StatLuaTokens>(StatLuaTokens.INTEGER, new TextSpan(source, startPosition, mInt.Length)));
                next = AdvanceTextSpan(next, mInt.Length);
                continue;
            }

            if (IdentifierRegex().Match(remainingText) is { Success: true } annotationIdMatch)
            {
                var startPosition = next.Position;
                tokens.Add(new Token<StatLuaTokens>(StatLuaTokens.NAME, new TextSpan(source, startPosition, annotationIdMatch.Length)));
                next = AdvanceTextSpan(next, annotationIdMatch.Length);
                continue;
            }

            var fallbackBadPos = next.Position;
            tokens.Add(new Token<StatLuaTokens>(StatLuaTokens.BAD, new TextSpan(source, fallbackBadPos, 1)));
            next = AdvanceTextSpan(next, 1);
        }

        return new TokenList<StatLuaTokens>([.. tokens]);
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
using Superpower;
using Superpower.Parsers;
using Superpower.Tokenizers;

namespace LSLib.DebuggerFrontend.ExpressionParser;

public enum ExpressionTokens
{
    None,
    NOT,
    OpenParenthesis,
    CloseParenthesis,
    Comma,
    Dot,
    IDENTIFIER,
    GUIDSTRING,
    LOCAL_VAR,
    INTEGER,
    FLOAT,
    STRING,
    BAD
}

public static class ExpressionTokenizer
{
    private static readonly TextParser<char> Letter = Character.Letter;
    private static readonly TextParser<char> Digit = Character.Digit;
    private static readonly TextParser<char> Hex = Character.HexDigit;
    private static readonly TextParser<char> Underline = Character.EqualTo('_');
    private static readonly TextParser<char> Dash = Character.EqualTo('-');
    private static readonly TextParser<char> Sign = Character.EqualTo('+').Or(Character.EqualTo('-'));

    // 1. C# 14 Collection Expressions construct parser repetitions with minimal allocation overhead
    private static readonly TextParser<string> Hex4 = Hex.Repeat(4).Select(static c => new string(c));
    private static readonly TextParser<string> Hex8 = Hex.Repeat(8).Select(static c => new string(c));
    private static readonly TextParser<string> Hex12 = Hex.Repeat(12).Select(static c => new string(c));

    private static readonly TextParser<string> RawGuid =
        from h8 in Hex8
        from d1 in Dash
        from h4_1 in Hex4
        from d2 in Dash
        from h4_2 in Hex4
        from d3 in Dash
        from h4_3 in Hex4
        from d4 in Dash
        from h12 in Hex12
        select $"{h8}-{h4_1}-{h4_2}-{h4_3}-{h12}";

    public static readonly Tokenizer<ExpressionTokens> Instance = new TokenizerBuilder<ExpressionTokens>()
        .Ignore(Span.WhiteSpace)

        .Match(Character.EqualTo('('), ExpressionTokens.OpenParenthesis)
        .Match(Character.EqualTo(')'), ExpressionTokens.CloseParenthesis)
        .Match(Character.EqualTo(','), ExpressionTokens.Comma)
        .Match(Character.EqualTo('.'), ExpressionTokens.Dot)

        .Match(Span.EqualTo("NOT"), ExpressionTokens.NOT, requireDelimiters: true)

        .Match(
            from leading in Letter
            from middle in Letter.Or(Digit).Or(Underline).Or(Dash).Many()
            from guid in RawGuid
            select $"{leading}{new string(middle)}{guid}",
            ExpressionTokens.GUIDSTRING)

        .Match(RawGuid, ExpressionTokens.GUIDSTRING)

        .Match(
            from underline in Underline
            from trailing in Letter.Or(Digit).Or(Underline).Many()
            select $"_{new string(trailing)}",
            ExpressionTokens.LOCAL_VAR)

        .Match(
            from sign in Sign.Optional()
            // 2. Performance: Use static lambdas to prevent hidden delegate allocations on string selections
            from whole in Digit.AtLeastOnce().Select(static c => new string(c))
            from dot in Character.EqualTo('.')
            from fraction in Digit.AtLeastOnce().Select(static c => new string(c))
            select $"{(sign.HasValue ? sign.Value : "")}{whole}.{fraction}",
            ExpressionTokens.FLOAT)

        .Match(
            from sign in Sign.Optional()
            from digits in Digit.AtLeastOnce().Select(static c => new string(c))
            select $"{(sign.HasValue ? sign.Value : "")}{digits}",
            ExpressionTokens.INTEGER)

        .Match(
            from prefix in Character.EqualTo('L').Optional()
            from content in QuotedString.CStyle
            select $"{(prefix.HasValue ? prefix.Value : "")}\"{content}\"",
            ExpressionTokens.STRING)

        .Match(
            from leading in Letter
            from trailing in Letter.Or(Digit).Or(Underline).Many()
            select $"{leading}{new string(trailing)}",
            ExpressionTokens.IDENTIFIER)

        .Match(Character.AnyChar, ExpressionTokens.BAD)
        .Build();
}
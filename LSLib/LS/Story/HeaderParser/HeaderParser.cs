using LSLib.LS.Story.Compiler;
using Superpower;
using Superpower.Parsers;
using Superpower.Tokenizers;
using System.Globalization;

namespace LSLib.LS.Story.HeaderParser;

/// <summary>
/// Definitive, strongly-typed token representation enum structure for the story header combinator parser.
/// </summary>
public enum HeaderTokens
{
    None,
    Bad,
    Identifier,
    IntegerLiteral,
    KeywordType,
    KeywordAlias,
    OpenParenthesis,
    CloseParenthesis,
    Comma,
    DirectionIn,
    DirectionOut,
    Dot,
    OpenBrace,
    CloseBrace,
    OpenBracket,
    CloseBracket,
    Option,
    SysCall,
    SysQuery,
    Query,
    Call,
    Event
}

/// <summary>
/// High-performance lexical tokenizer tracking script header symbols allocation-free.
/// </summary>
public static class HeaderTokenizer
{
    public static readonly Tokenizer<HeaderTokens> Instance =
         new TokenizerBuilder<HeaderTokens>()
             .Ignore(Character.WhiteSpace)
             .Ignore(Comment.CStyle)
             .Ignore(Comment.CPlusPlusStyle)
             .Match(Span.EqualTo("option"), HeaderTokens.Option)
             .Match(Span.EqualTo("type"), HeaderTokens.KeywordType)
             .Match(Span.EqualTo("alias_type"), HeaderTokens.KeywordAlias)
             .Match(Span.EqualTo("syscall"), HeaderTokens.SysCall)
             .Match(Span.EqualTo("sysquery"), HeaderTokens.SysQuery)
             .Match(Span.EqualTo("query"), HeaderTokens.Query)
             .Match(Span.EqualTo("call"), HeaderTokens.Call)
             .Match(Span.EqualTo("event"), HeaderTokens.Event)
             .Match(Span.EqualTo("in"), HeaderTokens.DirectionIn)
             .Match(Span.EqualTo("out"), HeaderTokens.DirectionOut)
             .Match(Character.EqualTo('{'), HeaderTokens.OpenBrace)
             .Match(Character.EqualTo('}'), HeaderTokens.CloseBrace)
             .Match(Character.EqualTo('('), HeaderTokens.OpenParenthesis)
             .Match(Character.EqualTo(')'), HeaderTokens.CloseParenthesis)
             .Match(Character.EqualTo('['), HeaderTokens.OpenBracket)
             .Match(Character.EqualTo(']'), HeaderTokens.CloseBracket)
             .Match(Character.EqualTo(','), HeaderTokens.Comma)
             .Match(Span.Regex(@"[0-9]+"), HeaderTokens.IntegerLiteral)
             .Match(Span.Regex(@"[a-zA-Z_][a-zA-Z0-9_]*"), HeaderTokens.Identifier)
             .Match(Character.AnyChar, HeaderTokens.Bad)
             .Build();
}

/// <summary>
/// Monadic functional combinators mapping story header text layers straight down into a typed AST graph layout safely.
/// </summary>
public static class HeaderCombinatorParser
{
    private static readonly TokenListParser<HeaderTokens, ParamDirection> DirectionParser =
        Token.EqualTo(HeaderTokens.DirectionIn).Value(ParamDirection.In)
        .Or(Token.EqualTo(HeaderTokens.DirectionOut).Value(ParamDirection.Out))
        .Or(Token.Sequence(HeaderTokens.None).Value(ParamDirection.In).OptionalOrDefault(ParamDirection.In));

    private static readonly TokenListParser<HeaderTokens, ASTOption> OptionParser =
        from kw in Token.EqualTo(HeaderTokens.Option)
        from id in Token.EqualTo(HeaderTokens.Identifier)
        select new ASTOption
        {
            Name = id.ToStringValue() ?? string.Empty
        };

    private static readonly TokenListParser<HeaderTokens, ASTAlias> AliasParser =
        from kw in Token.EqualTo(HeaderTokens.KeywordAlias)
        from ob in Token.EqualTo(HeaderTokens.OpenBrace)
        from name in Token.EqualTo(HeaderTokens.Identifier)
        from c1 in Token.EqualTo(HeaderTokens.Comma)
        from typeId in Token.EqualTo(HeaderTokens.IntegerLiteral)
        from c2 in Token.EqualTo(HeaderTokens.Comma)
        from aliasId in Token.EqualTo(HeaderTokens.IntegerLiteral)
        from cb in Token.EqualTo(HeaderTokens.CloseBrace)
        select new ASTAlias
        {
            TypeName = name.ToStringValue() ?? string.Empty,
            TypeId = uint.Parse(typeId.ToStringValue() ?? "0", CultureInfo.InvariantCulture),
            AliasId = uint.Parse(aliasId.ToStringValue() ?? "0", CultureInfo.InvariantCulture)
        };

    private static readonly TokenListParser<HeaderTokens, ASTFunctionParam> InFunctionParamParser =
        from op in Token.EqualTo(HeaderTokens.OpenParenthesis)
        from typeNode in Token.EqualTo(HeaderTokens.Identifier)
        from cp in Token.EqualTo(HeaderTokens.CloseParenthesis)
        from nameNode in Token.EqualTo(HeaderTokens.Identifier)
        select new ASTFunctionParam
        {
            Name = nameNode.ToStringValue() ?? string.Empty,
            Type = typeNode.ToStringValue() ?? string.Empty,
            Direction = ParamDirection.In
        };

    private static readonly TokenListParser<HeaderTokens, ASTFunctionParam> InOutFunctionParamParser =
        (from ob in Token.EqualTo(HeaderTokens.OpenBracket)
         from inKw in Token.EqualTo(HeaderTokens.DirectionIn)
         from cb in Token.EqualTo(HeaderTokens.CloseBracket)
         from op in Token.EqualTo(HeaderTokens.OpenParenthesis)
         from typeNode in Token.EqualTo(HeaderTokens.Identifier)
         from cp in Token.EqualTo(HeaderTokens.CloseParenthesis)
         from nameNode in Token.EqualTo(HeaderTokens.Identifier)
         select new ASTFunctionParam
         {
             Name = nameNode.ToStringValue() ?? string.Empty,
             Type = typeNode.ToStringValue() ?? string.Empty,
             Direction = ParamDirection.In
         })
        .Or(from ob in Token.EqualTo(HeaderTokens.OpenBracket)
            from outKw in Token.EqualTo(HeaderTokens.DirectionOut)
            from cb in Token.EqualTo(HeaderTokens.CloseBracket)
            from op in Token.EqualTo(HeaderTokens.OpenParenthesis)
            from typeNode in Token.EqualTo(HeaderTokens.Identifier)
            from cp in Token.EqualTo(HeaderTokens.CloseParenthesis)
            from nameNode in Token.EqualTo(HeaderTokens.Identifier)
            select new ASTFunctionParam
            {
                Name = nameNode.ToStringValue() ?? string.Empty,
                Type = typeNode.ToStringValue() ?? string.Empty,
                Direction = ParamDirection.Out
            });

    private static readonly TokenListParser<HeaderTokens, List<ASTFunctionParam>> InFunctionParamsParser =
        InFunctionParamParser.ManyDelimitedBy(Token.EqualTo(HeaderTokens.Comma))
            .Select(arr => arr.ToList())
            .OptionalOrDefault([]);

    private static readonly TokenListParser<HeaderTokens, List<ASTFunctionParam>> InOutFunctionParamsParser =
        InOutFunctionParamParser.ManyDelimitedBy(Token.EqualTo(HeaderTokens.Comma))
            .Select(arr => arr.ToList())
            .OptionalOrDefault([]);

    private static readonly TokenListParser<HeaderTokens, (uint m1, uint m2, uint m3, uint m4)> MetadataParser =
        from op in Token.EqualTo(HeaderTokens.OpenParenthesis)
        from v1 in Token.EqualTo(HeaderTokens.IntegerLiteral)
        from c1 in Token.EqualTo(HeaderTokens.Comma)
        from v2 in Token.EqualTo(HeaderTokens.IntegerLiteral)
        from c2 in Token.EqualTo(HeaderTokens.Comma)
        from v3 in Token.EqualTo(HeaderTokens.IntegerLiteral)
        from c3 in Token.EqualTo(HeaderTokens.Comma)
        from v4 in Token.EqualTo(HeaderTokens.IntegerLiteral)
        from cp in Token.EqualTo(HeaderTokens.CloseParenthesis)
        select (
            uint.Parse(v1.ToStringValue() ?? "0", CultureInfo.InvariantCulture),
            uint.Parse(v2.ToStringValue() ?? "0", CultureInfo.InvariantCulture),
            uint.Parse(v3.ToStringValue() ?? "0", CultureInfo.InvariantCulture),
            uint.Parse(v4.ToStringValue() ?? "0", CultureInfo.InvariantCulture)
        );

    private static readonly TokenListParser<HeaderTokens, FunctionType> InOutFunctionTypeParser =
        Token.EqualTo(HeaderTokens.SysQuery).Value(FunctionType.SysQuery)
        .Or(Token.EqualTo(HeaderTokens.Query).Value(FunctionType.UserQuery));

    private static readonly TokenListParser<HeaderTokens, FunctionType> InFunctionTypeParser =
        Token.EqualTo(HeaderTokens.SysCall).Value(FunctionType.SysCall)
        .Or(Token.EqualTo(HeaderTokens.Call).Value(FunctionType.Call))
        .Or(Token.EqualTo(HeaderTokens.Event).Value(FunctionType.Event));

    private static readonly TokenListParser<HeaderTokens, ASTFunction> InOutFunctionParser =
        from type in InOutFunctionTypeParser
        from id in Token.EqualTo(HeaderTokens.Identifier)
        from open in Token.EqualTo(HeaderTokens.OpenParenthesis)
        from parameters in InOutFunctionParamsParser
        from close in Token.EqualTo(HeaderTokens.CloseParenthesis)
        from meta in MetadataParser
        select new ASTFunction
        {
            Type = (Compiler.FunctionType)type,
            Name = id.ToStringValue() ?? string.Empty,
            Params = parameters,
            Meta1 = meta.m1,
            Meta2 = meta.m2,
            Meta3 = meta.m3,
            Meta4 = meta.m4
        };

    private static readonly TokenListParser<HeaderTokens, ASTFunction> InFunctionParser =
        from type in InFunctionTypeParser
        from id in Token.EqualTo(HeaderTokens.Identifier)
        from open in Token.EqualTo(HeaderTokens.OpenParenthesis)
        from parameters in InFunctionParamsParser
        from close in Token.EqualTo(HeaderTokens.CloseParenthesis)
        from meta in MetadataParser
        select new ASTFunction
        {
            Type = (Compiler.FunctionType)type,
            Name = id.ToStringValue() ?? string.Empty,
            Params = parameters,
            Meta1 = meta.m1,
            Meta2 = meta.m2,
            Meta3 = meta.m3,
            Meta4 = meta.m4
        };

    private static readonly TokenListParser<HeaderTokens, ASTFunction> FunctionParser =
        InOutFunctionParser.Or(InFunctionParser);

    public static readonly TokenListParser<HeaderTokens, ASTDeclarations> HeaderFileParser =
        from options in OptionParser.Many()
        from aliases in AliasParser.Many()
        from functions in FunctionParser.Many()
        select new ASTDeclarations
        {
            Options = [.. options.Select(o => o.Name ?? string.Empty)],
            Aliases = aliases.ToList() ?? [],
            Functions = functions.ToList() ?? []
        };
}
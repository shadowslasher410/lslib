using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using System.Diagnostics.CodeAnalysis;

namespace LSLibStats.Stats.Functor;


public interface IFunctorNode { }

public sealed record FunctorListNode(List<IFunctorNode> Functors) : IFunctorNode;

public sealed record FunctorNode(List<string> Contexts, string? Condition, IFunctorNode Action) : IFunctorNode;

public sealed record TextKeyFunctorNode(string TextKey, List<IFunctorNode> Functors) : IFunctorNode;

public sealed record ActionNode(string Name, List<string> Arguments) : IFunctorNode
{
    public int StartPos { get; set; }
    public int EndPos { get; set; }
}


public static class FunctorCombinatorParser
{
    private static readonly TokenListParser<FunctorTokens, string> NameParser = Token.EqualTo(FunctorTokens.NAME).Select(t => t.ToStringValue());
    private static readonly TokenListParser<FunctorTokens, string> IntParser = Token.EqualTo(FunctorTokens.INTEGER).Select(t => t.ToStringValue());
    private static readonly TokenListParser<FunctorTokens, string> TextParser = Token.EqualTo(FunctorTokens.TEXT).Select(t => t.ToStringValue());
    private static readonly TokenListParser<FunctorTokens, string> DiceParser = Token.EqualTo(FunctorTokens.DICE_ROLL).Select(t => t.ToStringValue());
    private static readonly TokenListParser<FunctorTokens, string> ContextParser = Token.EqualTo(FunctorTokens.CONTEXT).Select(t => t.ToStringValue());

    private static readonly TokenListParser<FunctorTokens, string> LuaSymbolParser =
        NameParser.Or(IntParser).Or(TextParser).Or(ContextParser).Or(DiceParser)
        .Or(Token.EqualTo(FunctorTokens.COLON).Value(":"))
        .Or(Token.EqualTo(FunctorTokens.EXCLAMATION).Value("!"))
        .Or(Token.EqualTo(FunctorTokens.SEMICOLON).Value(";"))
        .Or(Token.EqualTo(FunctorTokens.MINUS).Value("-"))
        .Or(Token.EqualTo(FunctorTokens.DOT).Value("."))
        .Or(Token.EqualTo(FunctorTokens.COMMA).Value(","));

    private static readonly TokenListParser<FunctorTokens, string> BalancedParenthesesParser =
        from open in Token.EqualTo(FunctorTokens.LEFT_PAREN)
        from inner in ParseRef(() => LuaElementParser!).Many()
        from close in Token.EqualTo(FunctorTokens.RIGHT_PAREN)
        select $"({string.Join("", inner)})";

    private static readonly TokenListParser<FunctorTokens, string> LuaElementParser =
        BalancedParenthesesParser.Or(LuaSymbolParser);

    private static readonly TokenListParser<FunctorTokens, string> NonEmptyArgParser =
        LuaElementParser.AtLeastOnce().Select(items => string.Join("", items).Trim());

    private static readonly TokenListParser<FunctorTokens, string> ArgParser =
        NonEmptyArgParser.Or(Parse.Return<FunctorTokens, string>(string.Empty));

    private static readonly TokenListParser<FunctorTokens, List<string>> OptionalArgsParser =
        ArgParser.ManyDelimitedBy(Token.EqualTo(FunctorTokens.COMMA)).Select(items => items.ToList());

    private static readonly TokenListParser<FunctorTokens, List<string>> OptionalArgListParser =
        (from open in Token.EqualTo(FunctorTokens.LEFT_PAREN)
         from args in OptionalArgsParser
         from close in Token.EqualTo(FunctorTokens.RIGHT_PAREN)
         select args)
        .Or(Parse.Return<FunctorTokens, List<string>>([]));

    private static readonly TokenListParser<FunctorTokens, string> ContextAtomParser =
        from ctx in Token.EqualTo(FunctorTokens.CONTEXT)
        from colon in Token.EqualTo(FunctorTokens.COLON)
        select ctx.ToStringValue();

    private static readonly TokenListParser<FunctorTokens, string> ConditionParser =
        (from kw in Token.EqualTo(FunctorTokens.IF)
         from open in Token.EqualTo(FunctorTokens.LEFT_PAREN)
         from cond in NonEmptyArgParser
         from close in Token.EqualTo(FunctorTokens.RIGHT_PAREN)
         from colon in Token.EqualTo(FunctorTokens.COLON)
         select cond)
        .Or(Parse.Return<FunctorTokens, string>(null!));

    private static readonly TokenListParser<FunctorTokens, ActionNode> CallParser =
        from startToken in Token.EqualTo(FunctorTokens.NAME)
        from args in OptionalArgListParser
        select new ActionNode(startToken.ToStringValue(), args)
        {
            StartPos = startToken.Span.Position.Absolute,
            EndPos = startToken.Span.Position.Absolute + startToken.Span.Length
        };

    private static readonly TokenListParser<FunctorTokens, List<IFunctorNode>> FunctorsListParser =
        ParseRef(() => FunctorSequenceParser!);

    private static readonly TokenListParser<FunctorTokens, IFunctorNode> CallOrTextKeyFunctorParser =
        (from name in Token.EqualTo(FunctorTokens.NAME)
         from open in Token.EqualTo(FunctorTokens.LEFT_BRACKET)
         from inner in FunctorsListParser
         from close in Token.EqualTo(FunctorTokens.RIGHT_BRACKET)
         select (IFunctorNode)new TextKeyFunctorNode(name.ToStringValue(), inner))
        .Or(CallParser.Select(c => (IFunctorNode)c));

    private static readonly TokenListParser<FunctorTokens, IFunctorNode> TopLevelFunctorParser =
        from contexts in ContextAtomParser.Many()
        from condition in ConditionParser
        from call in CallOrTextKeyFunctorParser
        select (IFunctorNode)new FunctorNode([.. contexts], condition, call);

    private static readonly TokenListParser<FunctorTokens, IFunctorNode> InnerFunctorParser =
        from contexts in ContextAtomParser.Many()
        from condition in ConditionParser
        from call in CallParser
        select (IFunctorNode)new FunctorNode([.. contexts], condition, call);

    private static readonly TokenListParser<FunctorTokens, List<IFunctorNode>> FunctorSequenceParser =
        InnerFunctorParser.ManyDelimitedBy(Token.EqualTo(FunctorTokens.SEMICOLON)).Select(items => items.ToList());

    private static readonly TokenListParser<FunctorTokens, List<IFunctorNode>> TopLevelSequenceParser =
        TopLevelFunctorParser.ManyDelimitedBy(Token.EqualTo(FunctorTokens.SEMICOLON)).Select(items => items.ToList());

    private static readonly TokenListParser<FunctorTokens, IFunctorNode> RootParser =
        (from trigger in Token.EqualTo(FunctorTokens.EXPR_FUNCTORS)
         from list in TopLevelSequenceParser
         select (IFunctorNode)new FunctorListNode(list))
        .Or(from trigger in Token.EqualTo(FunctorTokens.EXPR_DESCRIPTION_PARAMS)
            from args in OptionalArgsParser
            select (IFunctorNode)new ActionNode("DescriptionParams", [.. args]));

    private static TokenListParser<TToken, TResult> ParseRef<TToken, TResult>(Func<TokenListParser<TToken, TResult>> parserFactory)
    {
        TokenListParser<TToken, TResult>? lazyCache = null;
        return tokenStream => (lazyCache ??= parserFactory())(tokenStream);
    }

    public static bool TryParse(string source, [NotNullWhen(true)] out IFunctorNode? rootNode, out string errorLog)
    {
        rootNode = null;
        errorLog = string.Empty;

        var tokenList = FunctorTokenizer.Tokenize(source);
        if (tokenList.Any(t => t.Kind == FunctorTokens.BAD))
        {
            errorLog = "Lexical parser constraints broken: Unmapped operator payload traces intercepted.";
            return false;
        }

        var cleanTokens = tokenList.Where(t => t.Kind != FunctorTokens.BAD).ToArray();
        var stream = new TokenList<FunctorTokens>(cleanTokens);
        var result = RootParser.AtEnd().TryParse(stream);

        if (!result.HasValue)
        {
            errorLog = $"Grammar validation error: {result.FormatErrorMessageFragment()}";
            return false;
        }

        rootNode = result.Value;
        return true;
    }
}
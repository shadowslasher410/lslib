using Superpower;
using Superpower.Parsers;
using Superpower.Tokenizers;

namespace LSLib.LS.Story.GoalParser;

public enum GoalTokens
{
    None,
    Bad,
    Version,
    SubGoalCombiner,
    SubGoalCombinerAnd,
    SubGoalCombinerOr,
    ParentTargetEdge,
    GoalCompleted,
    InitSection,
    KbSection,
    ExitSection,
    EndExitSection,
    If,
    Then,
    And,
    Not,
    Proc,
    Query,
    OpenParenthesis,    // (
    CloseParenthesis,   // )
    Comma,              // ,
    Semicolon,          // ;
    Dot,                // .
    OpEqual,            // ==
    OpNotEqual,         // !=
    OpGreater,          // >
    OpGreaterOrEqual,   // >=
    OpLess,             // <
    OpLessOrEqual,      // <=
    Identifier,
    LocalVariable,
    GuidString,
    TypeCast,
    IntegerLiteral,
    FloatLiteral,
    StringLiteral
}

public enum SubGoalCombinerType : uint
{
    None = 0,
    And = 1,
    Or = 2
}

public static class GoalTokenizer
{
    public static readonly Tokenizer<GoalTokens> Instance =
        new TokenizerBuilder<GoalTokens>()
            .Ignore(Character.WhiteSpace)
            .Ignore(Comment.CStyle)
            .Ignore(Comment.CPlusPlusStyle)

            .Match(Character.EqualTo('('), GoalTokens.OpenParenthesis)
            .Match(Character.EqualTo(')'), GoalTokens.CloseParenthesis)
            .Match(Character.EqualTo(';'), GoalTokens.Semicolon)
            .Match(Character.EqualTo(','), GoalTokens.Comma)
            .Match(Character.EqualTo('.'), GoalTokens.Dot)

            .Match(Span.EqualTo("=="), GoalTokens.OpEqual)
            .Match(Span.EqualTo("!="), GoalTokens.OpNotEqual)
            .Match(Span.EqualTo("<="), GoalTokens.OpLessOrEqual)
            .Match(Span.EqualTo(">="), GoalTokens.OpGreaterOrEqual)
            .Match(Character.EqualTo('<'), GoalTokens.OpLess)
            .Match(Character.EqualTo('>'), GoalTokens.OpGreater)

            .Match(Span.EqualTo("Version"), GoalTokens.Version)

            .Match(Span.EqualTo("SubGoalCombinerAnd"), GoalTokens.SubGoalCombinerAnd)
            .Match(Span.EqualTo("SubGoalCombinerOr"), GoalTokens.SubGoalCombinerOr)  
            .Match(Span.EqualTo("SubGoalCombiner"), GoalTokens.SubGoalCombiner)      

            .Match(Span.EqualTo("InitSection"), GoalTokens.InitSection)
            .Match(Span.EqualTo("KbSection"), GoalTokens.KbSection)
            .Match(Span.EqualTo("ExitSection"), GoalTokens.ExitSection)
            .Match(Span.EqualTo("EndExitSection"), GoalTokens.EndExitSection)
            .Match(Span.EqualTo("If"), GoalTokens.If)
            .Match(Span.EqualTo("Proc"), GoalTokens.Proc)
            .Match(Span.EqualTo("Query"), GoalTokens.Query)
            .Match(Span.EqualTo("Then"), GoalTokens.Then)
            .Match(Span.EqualTo("And"), GoalTokens.And)
            .Match(Span.EqualTo("Not"), GoalTokens.Not)
            .Match(Span.EqualTo("GoalCompleted"), GoalTokens.GoalCompleted)
            .Match(Span.EqualTo("ParentTargetEdge"), GoalTokens.ParentTargetEdge)

            .Match(Span.Regex(@"L?\""(\\.|[^\\""])*\"""), GoalTokens.StringLiteral)
            .Match(Span.Regex(@"[+\-]?[0-9]+\.[0-9]+"), GoalTokens.FloatLiteral)
            .Match(Span.Regex(@"[+\-]?[0-9]+"), GoalTokens.IntegerLiteral)
            .Match(Span.Regex(@"_[a-zA-Z0-9_]*"), GoalTokens.LocalVariable)

            .Match(Span.Regex(@"([0-9a-fA-F]{8})-([0-9a-fA-F]{4})-([0-9a-fA-F]{4})-([0-9a-fA-F]{4})-([0-9a-fA-F]{12})"), GoalTokens.GuidString)
            .Match(Span.Regex(@"[a-zA-Z][a-zA-Z0-9_\-]*([0-9a-fA-F]{8})-([0-9a-fA-F]{4})-([0-9a-fA-F]{4})-([0-9a-fA-F]{4})-([0-9a-fA-F]{12})"), GoalTokens.GuidString)

            .Match(Span.Regex(@"[a-zA-Z][a-zA-Z0-9_]*"), GoalTokens.Identifier)
            .Build();
}
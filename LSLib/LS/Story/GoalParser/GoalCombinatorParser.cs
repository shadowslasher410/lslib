using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using LSLib.Parser;
using LSLib.LS.Story.Compiler;

namespace LSLib.LS.Story.GoalParser;

public static class GoalCombinatorParser
{
    private static CodeLocation GetLocation(Token<GoalTokens> start, Token<GoalTokens> end) =>
        new(string.Empty, start.Position.Line, start.Position.Column, end.Position.Line, end.Position.Column);

    private static readonly TokenListParser<GoalTokens, RelOpType> OperatorParser =
        Token.EqualTo(GoalTokens.OpEqual).Value(RelOpType.Equal)
        .Or(Token.EqualTo(GoalTokens.OpNotEqual).Value(RelOpType.NotEqual))
        .Or(Token.EqualTo(GoalTokens.OpLess).Value(RelOpType.Less))
        .Or(Token.EqualTo(GoalTokens.OpLessOrEqual).Value(RelOpType.LessOrEqual))
        .Or(Token.EqualTo(GoalTokens.OpGreater).Value(RelOpType.Greater))
        .Or(Token.EqualTo(GoalTokens.OpGreaterOrEqual).Value(RelOpType.GreaterOrEqual));

    private static readonly TokenListParser<GoalTokens, ASTConstantValue> BaseConstantParser =
        Token.EqualTo(GoalTokens.GuidString).Select(t => new ASTConstantValue
        {
            Type = IRConstantType.Name,
            StringValue = t.ToStringValue(),
            Location = GetLocation(t, t)
        })
        .Or(Token.EqualTo(GoalTokens.StringLiteral).Select(t => new ASTConstantValue
        {
            Type = IRConstantType.String,
            StringValue = t.ToStringValue().Trim('"'),
            Location = GetLocation(t, t)
        }))
        .Or(Token.EqualTo(GoalTokens.IntegerLiteral).Select(t => new ASTConstantValue
        {
            Type = IRConstantType.Integer,
            IntegerValue = long.Parse(t.ToStringValue(), System.Globalization.CultureInfo.InvariantCulture),
            Location = GetLocation(t, t)
        }))
        .Or(Token.EqualTo(GoalTokens.FloatLiteral).Select(t => new ASTConstantValue
        {
            Type = IRConstantType.Float,
            FloatValue = float.Parse(t.ToStringValue(), System.Globalization.CultureInfo.InvariantCulture),
            Location = GetLocation(t, t)
        }));

    private static readonly TokenListParser<GoalTokens, ASTConstantValue> TypedConstantParser =
        (from open in Token.EqualTo(GoalTokens.OpenParenthesis)
         from typeId in Token.EqualTo(GoalTokens.Identifier)
         from close in Token.EqualTo(GoalTokens.CloseParenthesis)
         from constant in BaseConstantParser
         select new ASTConstantValue
         {
             Type = constant.Type,
             StringValue = constant.StringValue,
             IntegerValue = constant.IntegerValue,
             FloatValue = constant.FloatValue,
             TypeName = typeId.ToStringValue(),
             Location = GetLocation(open, close)
         })
        .Or(BaseConstantParser);

    private static readonly TokenListParser<GoalTokens, ASTLocalVar> TypedLocalVarParser =
        (from open in Token.EqualTo(GoalTokens.OpenParenthesis)
         from typeId in Token.EqualTo(GoalTokens.Identifier)
         from close in Token.EqualTo(GoalTokens.CloseParenthesis)
         from localVar in Token.EqualTo(GoalTokens.LocalVariable)
         select new ASTLocalVar
         {
             Type = typeId.ToStringValue(),
             Name = localVar.ToStringValue(),
             Location = GetLocation(open, localVar)
         })
        .Or(Token.EqualTo(GoalTokens.LocalVariable).Select(t => new ASTLocalVar
        {
            Name = t.ToStringValue(),
            Location = GetLocation(t, t)
        }));

    private static readonly TokenListParser<GoalTokens, ASTRValue> ConditionParamParser =
        TypedConstantParser.Select(c => (ASTRValue)c)
        .Or(TypedLocalVarParser.Select(v => (ASTRValue)v));

    private static readonly TokenListParser<GoalTokens, List<ASTRValue>> ConditionParamListParser =
        ConditionParamParser.ManyDelimitedBy(Token.EqualTo(GoalTokens.Comma))
        .Select(arr => arr.ToList())
        .OptionalOrDefault([]);

    private static readonly TokenListParser<GoalTokens, ASTFuncCondition> BaseFuncConditionParser =
        (from id in Token.EqualTo(GoalTokens.Identifier)
         from open in Token.EqualTo(GoalTokens.OpenParenthesis)
         from args in ConditionParamListParser
         from close in Token.EqualTo(GoalTokens.CloseParenthesis)
         select new ASTFuncCondition
         {
             Name = id.ToStringValue(),
             Params = args,
             Location = GetLocation(id, close)
         })
        .Or(from objVar in TypedLocalVarParser
            from dot in Token.EqualTo(GoalTokens.Dot)
            from id in Token.EqualTo(GoalTokens.Identifier)
            from open in Token.EqualTo(GoalTokens.OpenParenthesis)
            from args in ConditionParamListParser
            from close in Token.EqualTo(GoalTokens.CloseParenthesis)
            select new ASTFuncCondition
            {
                Name = $"{objVar.Name}.{id.ToStringValue()}",
                Params = args,
                Location = GetLocation(id, close)
            });

    private static readonly TokenListParser<GoalTokens, ASTCondition> ConditionParser =
        (from notKw in Token.EqualTo(GoalTokens.Not).Optional()
         from fc in BaseFuncConditionParser
         select (ASTCondition)new ASTFuncCondition
         {
             Name = fc.Name,
             Params = fc.Params,
             Not = notKw.HasValue,
             Location = notKw.HasValue ? GetLocation(notKw.Value, notKw.Value) : fc.Location
         })
        .Or(from notKw in Token.EqualTo(GoalTokens.Not).Optional()
            from left in ConditionParamParser
            from op in OperatorParser
            from right in ConditionParamParser
            select (ASTCondition)new ASTBinaryCondition
            {
                LValue = left,
                Op = op,
                RValue = right,
                Location = left.Location
            });

    private static readonly TokenListParser<GoalTokens, List<ASTCondition>> ConditionsParser =
        from initial in ConditionParser
        from remainder in (from andKw in Token.EqualTo(GoalTokens.And) from cond in ConditionParser select cond).Many()
        select new List<ASTCondition>(remainder.Length + 1) { initial }.Concat(remainder).ToList();

    private static readonly TokenListParser<GoalTokens, ASTRValue> ActionParamParser =
        TypedConstantParser.Select(c => (ASTRValue)c)
        .Or(TypedLocalVarParser.Select(v => (ASTRValue)v));

    private static readonly TokenListParser<GoalTokens, List<ASTRValue>> ActionParamListParser =
        ActionParamParser.ManyDelimitedBy(Token.EqualTo(GoalTokens.Comma))
        .Select(arr => arr.ToList())
        .OptionalOrDefault([]);

    private static readonly TokenListParser<GoalTokens, ASTAction> ActionStatementParser =
        (from notKw in Token.EqualTo(GoalTokens.Not).Optional()
         from id in Token.EqualTo(GoalTokens.Identifier)
         from open in Token.EqualTo(GoalTokens.OpenParenthesis)
         from args in ActionParamListParser
         from close in Token.EqualTo(GoalTokens.CloseParenthesis)
         from semi in Token.EqualTo(GoalTokens.Semicolon)
         select (ASTAction)new ASTStatement
         {
             Name = id.ToStringValue(),
             Not = notKw.HasValue,
             Params = args,
             Location = GetLocation(notKw ?? id, semi)
         })
        .Or(from notKw in Token.EqualTo(GoalTokens.Not).Optional()
            from objVar in TypedLocalVarParser
            from dot in Token.EqualTo(GoalTokens.Dot)
            from id in Token.EqualTo(GoalTokens.Identifier)
            from open in Token.EqualTo(GoalTokens.OpenParenthesis)
            from args in ActionParamListParser
            from close in Token.EqualTo(GoalTokens.CloseParenthesis)
            from semi in Token.EqualTo(GoalTokens.Semicolon)
            select (ASTAction)new ASTStatement
            {
                Name = $"{objVar.Name}.{id.ToStringValue()}",
                Not = notKw.HasValue,
                Params = args,
                Location = GetLocation(notKw ?? id, semi)
            })
        .Or(from kw in Token.EqualTo(GoalTokens.GoalCompleted)
            from semi in Token.EqualTo(GoalTokens.Semicolon)
            select (ASTAction)new ASTGoalCompletedAction { Location = GetLocation(kw, semi) });

    private static readonly TokenListParser<GoalTokens, List<ASTAction>> ActionListParser =
        ActionStatementParser.Many().Select(a => (List<ASTAction>)[.. a]);

    private static readonly TokenListParser<GoalTokens, RuleType> RuleTypeParser =
        Token.EqualTo(GoalTokens.If).Value(RuleType.Rule)
        .Or(Token.EqualTo(GoalTokens.Proc).Value(RuleType.Proc))
        .Or(Token.EqualTo(GoalTokens.Query).Value(RuleType.Query));

    private static readonly TokenListParser<GoalTokens, ASTRule> RuleParser =
        from type in RuleTypeParser
        from conditions in ConditionsParser
        from thenKw in Token.EqualTo(GoalTokens.Then)
        from actions in ActionListParser
        select new ASTRule
        {
            Type = type,
            Conditions = conditions,
            Actions = actions,
            Location = conditions.FirstOrDefault()?.Location ?? new CodeLocation(string.Empty, 0, 0, 0, 0)
        };

    private static readonly TokenListParser<GoalTokens, ASTBaseFact> FactStatementParser =
        (from notKw in Token.EqualTo(GoalTokens.Not).Optional()
         from id in Token.EqualTo(GoalTokens.Identifier)
         from open in Token.EqualTo(GoalTokens.OpenParenthesis)
         from elements in TypedConstantParser.ManyDelimitedBy(Token.EqualTo(GoalTokens.Comma)).Select(arr => arr.ToList())
         from close in Token.EqualTo(GoalTokens.CloseParenthesis)
         from semi in Token.EqualTo(GoalTokens.Semicolon)
         select (ASTBaseFact)new ASTFact
         {
             Database = id.ToStringValue(),
             Not = notKw.HasValue,
             Elements = elements,
             Location = GetLocation(notKw ?? id, semi)
         })
        .Or(from kw in Token.EqualTo(GoalTokens.GoalCompleted)
            from semi in Token.EqualTo(GoalTokens.Semicolon)
            select (ASTBaseFact)new ASTGoalCompletedFact { Location = GetLocation(kw, semi) });

    private static readonly TokenListParser<GoalTokens, ASTBaseFact> FactParser = FactStatementParser;

    private static readonly TokenListParser<GoalTokens, ASTParentTargetEdge> TargetEdgeParser =
        from kw in Token.EqualTo(GoalTokens.ParentTargetEdge)
        from str in Token.EqualTo(GoalTokens.StringLiteral)
        select new ASTParentTargetEdge
        {
            Goal = str.ToStringValue()[1..^1],
            Location = GetLocation(kw, str)
        };

    public static readonly TokenListParser<GoalTokens, ASTGoal> GoalFileParser =
        from verKw in Token.EqualTo(GoalTokens.Version)
        from verId in Token.EqualTo(GoalTokens.IntegerLiteral)
        from sgcKw in Token.EqualTo(GoalTokens.SubGoalCombiner)
        from sgcType in Token.EqualTo(GoalTokens.SubGoalCombinerAnd)
        from initKw in Token.EqualTo(GoalTokens.InitSection).Optional()
        from initFacts in FactParser.Many()
        from kbKw in Token.EqualTo(GoalTokens.KbSection).Optional()
        from rules in RuleParser.Many()
        from exitKw in Token.EqualTo(GoalTokens.ExitSection).Optional()
        from exitFacts in (from f in FactParser from end in Token.EqualTo(GoalTokens.EndExitSection).Optional() select f).Many()
        from targetEdges in TargetEdgeParser.Many()
        select new ASTGoal
        {
            InitSection = [.. initFacts],
            KBSection = [.. rules],
            ExitSection = [.. exitFacts],
            ParentTargetEdges = [.. targetEdges],
            Location = GetLocation(verKw, verId)
        };
}
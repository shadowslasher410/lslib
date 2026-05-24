using LSLib.LS.Story.Compiler;
using LSLib.Parser;
using Superpower;
using Superpower.Model;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace LSLib.LS.Story.GoalParser;

/// <summary>
/// List of actions in the THEN part of a rule
/// This is discarded during parsing and does not appear in the final AST.
/// </summary>
using ASTActionList = List<ASTAction>;
/// <summary>
/// List of conditions/predicates in a production rule
/// This is discarded during parsing and does not appear in the final AST.
/// </summary>
using ASTConditionList = List<ASTCondition>;
/// <summary>
/// Condition query parameter / database tuple column list
/// This is discarded during parsing and does not appear in the final AST.
/// </summary>
using ASTConditionParamList = List<ASTRValue>;
/// <summary>
/// List of scalar values in a fact tuple
/// This is discarded during parsing and does not appear in the final AST.
/// </summary>
using ASTFactElementList = List<ASTConstantValue>;
/// <summary>
/// List of facts in an INIT or EXIT section.
/// This is discarded during parsing and does not appear in the final AST.
/// </summary>
using ASTFactList = List<ASTBaseFact>;
/// <summary>
/// List of parent goals.
/// This is discarded during parsing and does not appear in the final AST.
/// </summary>
using ASTParentTargetEdgeList = List<ASTParentTargetEdge>;
/// <summary>
/// List of production rules in the KB section
/// This is discarded during parsing and does not appear in the final AST.
/// </summary>
using ASTRuleList = List<ASTRule>;
/// <summary>
/// Parameter list of a statement in the THEN part of a rule.
/// This is discarded during parsing and does not appear in the final AST.
/// </summary>
using ASTStatementParamList = List<ASTRValue>;

internal class ParserConstants
{
    public static readonly CultureInfo ParserCulture = CultureInfo.ReadOnly(new CultureInfo("en-US"));
}

public static class ASTGoalExtensions
{
    private static readonly ConditionalWeakTable<object, GoalMetadataStore> _metadataMap = [];

    private sealed class GoalMetadataStore
    {
        public uint Version { get; set; } = 1;
        public SubGoalCombinerType SubGoalCombiner { get; set; } = SubGoalCombinerType.And;
    }

    public static uint GetVersion(this object goal) =>
        _metadataMap.GetOrCreateValue(goal).Version;

    public static void SetVersion(this object goal, uint value) =>
        _metadataMap.GetOrCreateValue(goal).Version = value;

    public static SubGoalCombinerType GetSubGoalCombiner(this object goal) =>
        _metadataMap.GetOrCreateValue(goal).SubGoalCombiner;

    public static void SetSubGoalCombiner(this object goal, SubGoalCombinerType value) =>
        _metadataMap.GetOrCreateValue(goal).SubGoalCombiner = value;
}

public abstract class GoalScanBase
{
    protected string fileName = string.Empty;

    public virtual CodeLocation CodeLoc { get; set; } = new();

    protected virtual bool CodeWrap() => true;

    protected static string MakeLiteral(string lit) => lit;

    protected static string MakeString(string lit)
    {
        ArgumentNullException.ThrowIfNull(lit);
        if (lit.Length < 2) return lit;
        return MakeLiteral(lit[1..^1]);
    }
}

public sealed partial class GoalScanner : GoalScanBase
{
    private readonly TokenList<GoalTokens> _tokenStream;
    public GoalScanner(string fileName, string sourceText)
    {
        this.fileName = fileName ?? string.Empty;

        var tokenizeResult = GoalTokenizer.Instance.TryTokenize(sourceText);
        if (!tokenizeResult.HasValue)
        {
            throw new InvalidDataException($"Lexical error in '{fileName}': {tokenizeResult.ErrorMessage}");
        }

        _tokenStream = tokenizeResult.Value;
    }
    public TokenList<GoalTokens> TokenStream => _tokenStream;
    public CodeLocation LastLocation()
    {
        if (!TokenStream.Any())
        {
            return new CodeLocation(fileName, 0, 0, 0, 0);
        }
        var currentToken = TokenStream.First();

        int startRow = currentToken.Position.Line;
        int startCol = currentToken.Position.Column;

        int endRow = startRow;
        int endCol = startCol + currentToken.Span.Length;

        return new CodeLocation(fileName, startRow, startCol, endRow, endCol);
    }
}

public partial class GoalParser(GoalScanner scnr)
{
    private readonly GoalScanner _scanner = scnr ?? throw new ArgumentNullException(nameof(scnr));

    public ASTGoal Parse()
    {
        ArgumentNullException.ThrowIfNull(scnr);
        var result = GoalCombinatorParser.GoalFileParser.TryParse(scnr.TokenStream);
        if (!result.HasValue)
        {
            throw new InvalidDataException($"Grammar Parsing Failed: {result.ErrorMessage}");
        }

        return result.Value;
    }


    public ASTGoal GetGoal() => Parse();

    private static ASTGoal MakeGoal(
    CodeLocation location,
    object version,
    object subGoalCombiner,
    object initSection,
    object kbSection,
    object exitSection,
    object parentTargetEdges)
    {
        ASTGoal goal = new()
        {
            InitSection = (ASTFactList)initSection,
            KBSection = (ASTRuleList)kbSection,
            ExitSection = (ASTFactList)exitSection,
            ParentTargetEdges = (ASTParentTargetEdgeList)parentTargetEdges,
            Location = location
        };

        uint parsedVersion = version switch
        {
            uint u => u,
            int i => (uint)i,
            string s => uint.TryParse(s, out var v) ? v : 1,
            _ => 1
        };
        goal.SetVersion(parsedVersion);

        SubGoalCombinerType parsedCombiner = subGoalCombiner switch
        {
            SubGoalCombinerType sgc => sgc,
            int i => (SubGoalCombinerType)i,
            string s when s.Equals("SubGoalCombinerAnd", StringComparison.OrdinalIgnoreCase) => SubGoalCombinerType.And,
            string s when s.Equals("SubGoalCombinerOr", StringComparison.OrdinalIgnoreCase) => SubGoalCombinerType.Or,
            _ => SubGoalCombinerType.And
        };
        goal.SetSubGoalCombiner(parsedCombiner);

        return goal;
    }

    private static ASTParentTargetEdgeList MakeParentTargetEdgeList() => [];

    private static ASTParentTargetEdgeList MakeParentTargetEdgeList(object parentTargetEdgeList, object edge)
    {
        var edges = (ASTParentTargetEdgeList)parentTargetEdgeList;
        edges.Add((ASTParentTargetEdge)edge);
        return edges;
    }

    private static ASTParentTargetEdge MakeParentTargetEdge(CodeLocation location, object goal) => new()
    {
        Location = location,
        Goal = (string)goal
    };

    private static ASTFactList MakeFactList() => [];
    
    private static ASTFactList MakeFactList(object factList, object fact)
    {
        var facts = (ASTFactList)factList;
        facts.Add((ASTBaseFact)fact);
        return facts;
    }

    private static ASTFact MakeNotFact(CodeLocation location, object fact)
    {
        var factStmt = (ASTFact)fact;
        factStmt.Location = location;
        factStmt.Not = true;
        return factStmt;
    }

    private static ASTFact MakeFactStatement(CodeLocation location, object database, object elements) => new()
    {
        Location = location,
        Database = (string)database,
        Not = false,
        Elements = (ASTFactElementList)elements
    };

    private static ASTGoalCompletedFact MakeGoalCompletedFact(CodeLocation location) => new()
    {
        Location = location
    };

    private static ASTFactElementList MakeFactElementList() => [];

    private static ASTFactElementList MakeFactElementList(object element)
    {
        var elements = new ASTFactElementList
        {
            (ASTConstantValue)element
        };
        return elements;
    }

    private static ASTFactElementList MakeFactElementList(object elementList, object element)
    {
        var elements = (ASTFactElementList)elementList;
        elements.Add((ASTConstantValue)element);
        return elements;
    }

    private static ASTRuleList MakeRuleList() => [];

    private static ASTRuleList MakeRuleList(object ruleList, object rule)
    {
        var rules = (ASTRuleList)ruleList;
        rules.Add((ASTRule)rule);
        return rules;
    }

    private static ASTRule MakeRule(CodeLocation location, object ruleType, object conditions, object actions) => new()
    {
        Location = location,
        Type = (RuleType)ruleType,
        Conditions = (ASTConditionList)conditions,
        Actions = (ASTActionList)actions
    };

    private static RuleType MakeRuleType(RuleType type) => type;

    private static ASTConditionList MakeConditionList() => [];

    private static ASTConditionList MakeConditionList(object condition)
    {
        var conditions = new ASTConditionList
        {
            (ASTCondition)condition
        };
        return conditions;
    }

    private static ASTConditionList MakeConditionList(object conditionList, object condition)
    {
        var conditions = (ASTConditionList)conditionList;
        conditions.Add((ASTCondition)condition);
        return conditions;
    }

    private static ASTFuncCondition MakeFuncCondition(CodeLocation location, object name, object paramList, bool not) => new()
    {
        Location = location,
        Name = (string)name,
        Not = not,
        Params = (ASTConditionParamList)paramList
    };

    private static ASTFuncCondition MakeObjectFuncCondition(CodeLocation location, object thisValue, object name, object paramList, bool not)
    {
        var condParams = (ASTConditionParamList)paramList;
        condParams.Insert(0, (ASTRValue)thisValue);
        return new ASTFuncCondition()
        {
            Location = location,
            Name = (string)name,
            Not = not,
            Params = condParams
        };
    }

    private static ASTBinaryCondition MakeNegatedBinaryCondition(CodeLocation location, object lvalue, object op, object rvalue)
    {
        var cond = MakeBinaryCondition(location, lvalue, op, rvalue);
        cond.Op = cond.Op switch
        {
            RelOpType.Less => RelOpType.GreaterOrEqual,
            RelOpType.LessOrEqual => RelOpType.Greater,
            RelOpType.Greater => RelOpType.LessOrEqual,
            RelOpType.GreaterOrEqual => RelOpType.Less,
            RelOpType.Equal => RelOpType.NotEqual,
            RelOpType.NotEqual => RelOpType.Equal,
            _ => throw new InvalidOperationException("Cannot negate unknown binary operator"),
        };
        return cond;
    }

    private static ASTBinaryCondition MakeBinaryCondition(CodeLocation location, object lvalue, object op, object rvalue) => new()
    {
        Location = location,
        LValue = (ASTRValue)lvalue,
        Op = (RelOpType)op,
        RValue = (ASTRValue)rvalue
    };

    private static ASTConditionParamList MakeConditionParamList() => [];

    private static ASTConditionParamList MakeConditionParamList(object param)
    {
        var list = new ASTConditionParamList
        {
            (ASTRValue)param
        };
        return list;
    }

    private static ASTConditionParamList MakeConditionParamList(object list, object param)
    {
        var conditionParamList = (ASTConditionParamList)list;
        conditionParamList.Add((ASTRValue)param);
        return conditionParamList;
    }

    private static RelOpType MakeOperator(RelOpType op) => op;

    private static ASTActionList MakeActionList() => [];

    private static ASTActionList MakeActionList(object actionList, object action)
    {
        var actions = (ASTActionList)actionList;
        actions.Add((ASTAction)action);
        return actions;
    }

    private static ASTGoalCompletedAction MakeGoalCompletedAction(CodeLocation location) => new()
    {
        Location = location
    };

    private static ASTStatement MakeActionStatement(CodeLocation location, object name, object paramList, bool not) => new()
    {
        Location = location,
        Name = (string)name,
        Not = not,
        Params = (ASTStatementParamList)paramList
    };

    private static ASTStatement MakeActionStatement(CodeLocation location, object thisValue, object name, object paramList, bool not)
    {
        var stmt = new ASTStatement
        {
            Location = location,
            Name = (string)name,
            Not = not,
            Params = (ASTStatementParamList)paramList
        };
        stmt.Params.Insert(0, (ASTRValue)thisValue);
        return stmt;
    }

    private static ASTStatementParamList MakeActionParamList() => [];

    private static ASTStatementParamList MakeActionParamList(object param)
    {
        var list = new ASTStatementParamList
        {
            (ASTRValue)param
        };
        return list;
    }

    private static ASTStatementParamList MakeActionParamList(object list, object param)
    {
        var actionParamList = (ASTStatementParamList)list;
        actionParamList.Add((ASTRValue)param);
        return actionParamList;
    }

    private static ASTLocalVar MakeLocalVar(CodeLocation location, object varName) => new()
    {
        Location = location,
        Name = (string)varName
    };

    private static ASTLocalVar MakeLocalVar(CodeLocation location, object typeName, object varName) => new()
    {
        Location = location,
        Type = (string)typeName,
        Name = (string)varName
    };

    private static ASTConstantValue MakeTypedConstant(CodeLocation location, object typeName, object constant)
    {
        var c = (ASTConstantValue)constant;
        return new ASTConstantValue()
        {
            Location = location,
            TypeName = (string)typeName,
            Type = c.Type,
            StringValue = c.StringValue,
            FloatValue = c.FloatValue,
            IntegerValue = c.IntegerValue,
        };
    }

    private static ASTConstantValue MakeConstGuidString(CodeLocation location, object val) => new()
    {
        Location = location,
        Type = IRConstantType.Name,
        StringValue = (string)val
    };

    private static ASTConstantValue MakeConstString(CodeLocation location, object val) => new()
    {
        Location = location,
        Type = IRConstantType.String,
        StringValue = (string)val
    };

    private static ASTConstantValue MakeConstInteger(CodeLocation location, object val) => new()
    {
        Location = location,
        Type = IRConstantType.Integer,
        IntegerValue = Int64.Parse((string)val, ParserConstants.ParserCulture.NumberFormat)
    };

    private static ASTConstantValue MakeConstFloat(CodeLocation location, object val) => new()
    {
        Location = location,
        Type = IRConstantType.Float,
        FloatValue = Single.Parse((string)val, ParserConstants.ParserCulture.NumberFormat)
    };
}
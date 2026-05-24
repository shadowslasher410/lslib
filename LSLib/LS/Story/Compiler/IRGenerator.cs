using LSLib.LS.Story.GoalParser;
using LSLib.Parser;
using System.Globalization;

namespace LSLib.LS.Story.Compiler;

/// <summary>
/// Generates IR from story AST.
/// </summary>
public class IRGenerator(CompilationContext context)
{
    private readonly CompilationContext _context = context ?? throw new ArgumentNullException(nameof(context));
    public CodeLocation? LastLocation { get; set; }

    public IRGoal GenerateIR(ASTGoal astGoal)
    {
        return ASTGoalToIR(astGoal);
    }

    private IRGoal ASTGoalToIR(ASTGoal astGoal)
    {
        ArgumentNullException.ThrowIfNull(astGoal);

        var goal = new IRGoal
        {
            InitSection = new List<IRFact>(astGoal.InitSection.Count),
            KBSection = new List<IRRule>(astGoal.KBSection.Count),
            ExitSection = new List<IRFact>(astGoal.ExitSection.Count),
            ParentTargetEdges = new List<IRTargetEdge>(astGoal.ParentTargetEdges.Count),
            Location = astGoal.Location
        };

        foreach (var fact in astGoal.InitSection)
        {
            if (fact is not null) goal.InitSection.Add(ASTFactToIR(goal, fact));
        }

        foreach (var rule in astGoal.KBSection)
        {
            if (rule is not null) goal.KBSection.Add(ASTRuleToIR(goal, rule));
        }

        foreach (var fact in astGoal.ExitSection)
        {
            if (fact is not null) goal.ExitSection.Add(ASTFactToIR(goal, fact));
        }

        foreach (var refGoal in astGoal.ParentTargetEdges)
        {
            if (refGoal is null) continue;

            var edge = new IRTargetEdge
            {
                Goal = new IRGoalRef(refGoal.Goal ?? string.Empty),
                Location = refGoal.Location
            };
            goal.ParentTargetEdges.Add(edge);
        }

        return goal;
    }

    private IRRule ASTRuleToIR(IRGoal goal, ASTRule astRule)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(astRule);

        var rule = new IRRule
        {
            Goal = goal,
            Type = astRule.Type,
            Conditions = new List<IRCondition>(astRule.Conditions.Count),
            Actions = new List<IRStatement>(astRule.Actions.Count),
            Variables = [],
            VariablesByName = new Dictionary<string, IRRuleVariable>(StringComparer.OrdinalIgnoreCase),
            Location = astRule.Location
        };

        foreach (var condition in astRule.Conditions)
        {
            if (condition is not null) rule.Conditions.Add(ASTConditionToIR(rule, condition));
        }

        foreach (var action in astRule.Actions)
        {
            if (action is not null) rule.Actions.Add(ASTActionToIR(rule, action));
        }

        return rule;
    }

    private IRStatement ASTActionToIR(IRRule rule, ASTAction astAction)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(astAction);

        if (astAction is ASTGoalCompletedAction)
        {
            return new IRStatement
            {
                Func = null!,
                Goal = rule.Goal,
                Not = false,
                Params = [],
                Location = astAction.Location
            };
        }

        if (astAction is ASTStatement astStmt)
        {
            var stmt = new IRStatement
            {
                Func = new IRSymbolRef(new FunctionNameAndArity(astStmt.Name ?? string.Empty, astStmt.Params.Count)),
                Goal = null,
                Not = astStmt.Not,
                Params = new List<IRValue>(astStmt.Params.Count),
                Location = astAction.Location
            };

            foreach (var param in astStmt.Params)
            {
                if (param is not null) stmt.Params.Add(ASTValueToIR(rule, param));
            }

            return stmt;
        }

        throw new InvalidOperationException("Cannot convert unknown AST action type statement parameters to IR targets.");
    }

    private IRCondition ASTConditionToIR(IRRule rule, ASTCondition astCondition)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(astCondition);

        if (astCondition is ASTFuncCondition astFunc)
        {
            var func = new IRFuncCondition
            {
                Func = new IRSymbolRef(new FunctionNameAndArity(astFunc.Name ?? string.Empty, astFunc.Params.Count)),
                Not = astFunc.Not,
                Params = new List<IRValue>(astFunc.Params.Count),
                TupleSize = -1,
                Location = astCondition.Location
            };

            foreach (var param in astFunc.Params)
            {
                if (param is not null) func.Params.Add(ASTValueToIR(rule, param));
            }

            return func;
        }

        if (astCondition is ASTBinaryCondition astBin)
        {
            return new IRBinaryCondition
            {
                LValue = ASTValueToIR(rule, astBin.LValue),
                Op = (RelOpType)astBin.Op,
                RValue = ASTValueToIR(rule, astBin.RValue),
                TupleSize = -1,
                Location = astCondition.Location
            };
        }

        throw new InvalidOperationException("Cannot convert unknown AST condition type configuration mapping parameters to IR targets.");
    }

    private IRValue ASTValueToIR(IRRule rule, ASTRValue astValue)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(astValue);

        if (astValue is ASTConstantValue constantValue)
        {
            return ASTConstantToIR(constantValue);
        }

        if (astValue is ASTLocalVar astVar)
        {
            ValueType? type = null;
            if (astVar.Type is not null)
            {
                type = _context.LookupType(astVar.Type);
                if (type is null)
                {
                    _context.Log.Error(astVar.Location, DiagnosticCode.UnresolvedType,
                       string.Format(CultureInfo.InvariantCulture, "Type \"{0}\" does not exist across compilation tables definitions.", astVar.Type));
                }
            }

            var ruleVar = rule.FindOrAddVariable(astVar.Name ?? string.Empty, type ?? new ValueType { Name = "UNKNOWN" });

            return new IRVariable
            {
                Index = ruleVar.Index,
                Type = type!,
                Location = astValue.Location
            };
        }

        throw new InvalidOperationException("Cannot convert unknown AST value parameter configuration to IR targets.");
    }

    private IRFact ASTFactToIR(IRGoal goal, ASTBaseFact astFact)
    {
        ArgumentNullException.ThrowIfNull(astFact);

        if (astFact is ASTFact f)
        {
            var fact = new IRFact
            {
                Database = new IRSymbolRef(new FunctionNameAndArity(f.Database ?? string.Empty, f.Elements.Count)),
                Not = f.Not,
                Elements = new List<IRConstant>(f.Elements.Count),
                Goal = null,
                Location = f.Location
            };

            foreach (var element in f.Elements)
            {
                if (element is not null) fact.Elements.Add(ASTConstantToIR(element));
            }

            return fact;
        }

        if (astFact is ASTGoalCompletedFact fCompleted)
        {
            return new IRFact
            {
                Database = null!,
                Not = false,
                Elements = [],
                Goal = goal,
                Location = fCompleted.Location
            };
        }

        throw new InvalidOperationException("Cannot convert unknown AST fact node parameter mapping elements to IR targets.");
    }

    private ValueType? ConstantTypeToValueType(IRConstantType type)
    {
        return type switch
        {
            IRConstantType.Unknown => null,
            IRConstantType.Integer => _context.TypesById.TryGetValue((uint)Value.Type.Integer, out var t1) ? t1 : null,
            IRConstantType.Float => _context.TypesById.TryGetValue((uint)Value.Type.Float, out var t3) ? t3 : null,
            IRConstantType.String => _context.TypesById.TryGetValue((uint)Value.Type.String, out var t4) ? t4 : null,
            IRConstantType.Name => _context.TypesById.TryGetValue((uint)Value.Type.GuidString, out var t5) ? t5 : null,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Invalid or unsupported IR constant type parameter validation context criteria specified.")
        };
    }
    /// <summary>
    /// Converts an Abstract Syntax Tree (AST) constant value node into its Intermediate Representation (IR) equivalent.
    /// </summary>
    private IRConstant ASTConstantToIR(ASTConstantValue astConstant)
    {
        ArgumentNullException.ThrowIfNull(astConstant);

        ValueType? type;
        if (astConstant.TypeName is not null)
        {
            type = _context.LookupType(astConstant.TypeName);
            if (type is null)
            {
                _context.Log.Error(astConstant.Location, DiagnosticCode.UnresolvedType,
                    string.Format(CultureInfo.InvariantCulture, "Type \"{0}\" does not exist across compilation tables definitions.", astConstant.TypeName));
            }
        }
        else
        {
            type = ConstantTypeToValueType(astConstant.Type);
        }

        return new IRConstant
        {
            ValueType = astConstant.Type,
            Type = type ?? new ValueType { Name = "UNKNOWN" },
            InferredType = astConstant.TypeName is not null,
            IntegerValue = astConstant.IntegerValue,
            FloatValue = astConstant.FloatValue,
            StringValue = astConstant.StringValue ?? string.Empty,
            Location = astConstant.Location
        };
    }


    public ASTGoal? ParseGoal(string path, Stream stream)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            using var reader = new StreamReader(stream, leaveOpen: true);
            string sourceText = reader.ReadToEnd();

            var scanner = new GoalScanner(path, sourceText);

            GoalParser.GoalParser parser = new(scanner);

            return parser.Parse();
        }
        catch (InvalidDataException)
        {
            stream.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, leaveOpen: true);
            var scanner = new GoalScanner(path, reader.ReadToEnd());

            LastLocation = scanner.LastLocation();
            return null;
        }
    }

    public IRGoal GenerateGoalIR(ASTGoal goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        return ASTGoalToIR(goal);
    }
}

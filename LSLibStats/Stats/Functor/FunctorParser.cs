using LSLib.Parser;
using System.Collections.Frozen;

namespace LSLibStats.Stats.Functor;

public enum ExpressionType
{
    Boost = 0,
    Functor = 1,
    DescriptionParams = 2
}

public static partial class FunctorParserExtensions
{
    public static string MakeLiteral(string value) => value ?? string.Empty;

    public static string UnwrapNode(object node) => node?.ToString() ?? string.Empty;
}

public sealed class FunctorAction
{
    public string Action { get; set; } = string.Empty;
    public List<string> Arguments { get; init; } = [];
    public int StartPos { get; set; }
    public int EndPos { get; set; }
}

public sealed class Functor
{
    public string? TextKey { get; set; }
    public string? Context { get; set; }
    public object? Condition { get; set; }
    public FunctorAction? Action { get; set; }
}

public sealed class FunctorActionValidator(StatDefinitionRepository definitions, DiagnosticContext ctx, StatValueValidatorFactory validatorFactory, ExpressionType type)
{
    private readonly StatDefinitionRepository _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly DiagnosticContext _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
    private readonly StatValueValidatorFactory _validatorFactory = validatorFactory ?? throw new ArgumentNullException(nameof(validatorFactory));
    private readonly ExpressionType _exprType = type;

    private static readonly FrozenSet<string> StripContexts = FrozenSet.ToFrozenSet(
        ["SELF", "OWNER", "SWAP", "OBSERVER_OBSERVER", "OBSERVER_TARGET", "OBSERVER_SOURCE"],
        StringComparer.Ordinal
    );

    public void Validate(FunctorAction action, PropertyDiagnosticContainer errors, CodeLocation? baseLocation)
    {
        var functors = _exprType switch
        {
            ExpressionType.Boost => _definitions.Boosts,
            ExpressionType.Functor => _definitions.Functors,
            ExpressionType.DescriptionParams => _definitions.DescriptionParams,
            _ => throw new NotImplementedException("Cannot validate expressions of this type category.")
        };

        if (!functors.TryGetValue(action.Action, out var functor))
        {
            if (_exprType != ExpressionType.DescriptionParams)
            {
                errors.Add($"'{action.Action}' is not a valid {_exprType}");
            }
            return;
        }

        var firstArg = 0;
        while (firstArg < action.Arguments.Count && StripContexts.Contains(action.Arguments[firstArg]))
        {
            firstArg++;
        }

        var args = action.Arguments.GetRange(firstArg, action.Arguments.Count - firstArg);

        if (args.Count > functor.Args.Count)
        {
            errors.Add($"Too many arguments to '{action.Action}'; {args.Count} passed, expected at most {functor.Args.Count}");
        }

        if (args.Count < functor.RequiredArgs)
        {
            errors.Add($"Not enough arguments to '{action.Action}'; {args.Count} passed, expected at least {functor.RequiredArgs}");
        }

        var argErrors = new PropertyDiagnosticContainer();
        for (var i = 0; i < Math.Min(args.Count, functor.Args.Count); i++)
        {
            var arg = functor.Args[i];
            if (arg.Type.Length > 0)
            {
                var validator = _validatorFactory.CreateValidator(arg.Type, null, null, _definitions);
                validator.Validate(_ctx, baseLocation, args[i], argErrors);

                if (!argErrors.Empty)
                {
                    argErrors.AddContext(PropertyDiagnosticContextType.Argument, $"{i + 1} ({arg.Name})");
                    argErrors.MergeInto(errors);
                    argErrors.Clear();
                }
            }
        }
    }
}

public sealed class FunctorParserEngine(StatDefinitionRepository definitions, DiagnosticContext ctx,
    StatValueValidatorFactory validatorFactory, ExpressionType type, PropertyDiagnosticContainer errors,
    CodeLocation rootLocation, int tokenOffset)
{
    private readonly FunctorActionValidator _actionValidator = new(definitions, ctx, validatorFactory, type);
    private readonly PropertyDiagnosticContainer _errors = errors ?? throw new ArgumentNullException(nameof(errors));
    private readonly CodeLocation _rootLocation = rootLocation;
    private readonly int _tokenOffset = tokenOffset;
    private IFunctorNode? _parsedAstRoot;

    public bool Parse(string expressionText, out string errorMessage)
    {
        if (FunctorCombinatorParser.TryParse(expressionText, out var rootNode, out errorMessage))
        {
            _parsedAstRoot = rootNode;
            return true;
        }
        return false;
    }

    public object ParseFromAST()
    {
        if (_parsedAstRoot is null) return new List<Functor>();

        return _parsedAstRoot switch
        {
            FunctorListNode list => ProcessList(list.Functors),
            ActionNode action => new List<string>(action.Arguments),
            _ => throw new InvalidDataException("Parser architecture collision: Unsupported root syntax mapping block intercepted.")
        };
    }

    private List<Functor> ProcessList(List<IFunctorNode> nodes)
    {
        List<Functor> functors = [];
        foreach (var node in nodes)
        {
            switch (node)
            {
                case FunctorNode func:
                    var derivedFunctors = FlattenActionNode(func.Action, func.Contexts, func.Condition);
                    functors.AddRange(derivedFunctors);
                    break;

                case TextKeyFunctorNode tk:
                    var subList = ProcessList(tk.Functors);
                    foreach (var sub in subList)
                    {
                        sub.TextKey ??= tk.TextKey;
                        functors.Add(sub);
                    }
                    break;
            }
        }
        return functors;
    }

    private List<Functor> FlattenActionNode(IFunctorNode actionNode, List<string> contexts, string? condition)
    {
        List<Functor> result = [];

        switch (actionNode)
        {
            case ActionNode act:
                var validatedAction = MapAction(act);
                result.Add(new Functor
                {
                    Context = contexts.Count > 0 ? string.Join(",", contexts) : null,
                    Condition = condition,
                    Action = validatedAction
                });
                break;

            case TextKeyFunctorNode tk:
                var nestedFunctors = ProcessList(tk.Functors);
                foreach (var sub in nestedFunctors)
                {
                    sub.Context ??= contexts.Count > 0 ? string.Join(",", contexts) : null;
                    sub.Condition ??= condition;
                    sub.TextKey ??= tk.TextKey;
                    result.Add(sub);
                }
                break;

            case FunctorNode nestedFunc:
                var mergedContexts = contexts.Concat(nestedFunc.Contexts).ToList();
                result.AddRange(FlattenActionNode(nestedFunc.Action, mergedContexts, nestedFunc.Condition ?? condition));
                break;
        }

        return result;
    }

    private FunctorAction MapAction(ActionNode node)
    {
        var act = new FunctorAction
        {
            Action = node.Name,
            Arguments = [.. node.Arguments],
            StartPos = node.StartPos,
            EndPos = node.EndPos
        };

        var callErrors = new PropertyDiagnosticContainer();
        CodeLocation? location = null;

        if (_rootLocation is not null)
        {
            location = new CodeLocation(_rootLocation.FileName,
                _rootLocation.StartLine, _rootLocation.StartColumn + act.StartPos - _tokenOffset,
                _rootLocation.StartLine, _rootLocation.StartColumn + act.EndPos - _tokenOffset);
        }

        _actionValidator.Validate(act, callErrors, location);
        callErrors.AddContext(PropertyDiagnosticContextType.Call, act.Action, location);
        callErrors.MergeInto(_errors);

        return act;
    }
}
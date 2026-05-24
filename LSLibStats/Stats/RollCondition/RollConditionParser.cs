using LSLib.Parser;
using LSLib.Stats.RollConditions;
using Superpower.Model;
using System.Diagnostics.CodeAnalysis;

namespace LSLibStats.Stats.RollCondition;

public sealed class RollCondition
{
    public string TextKey { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
}

public abstract class RollConditionScanBase
{
    [SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Preserves naming parity with structural legacy pipeline loop boundaries")]
    protected virtual bool yywrap() => true;
}

public partial class RollConditionScanner(string? fileName) : RollConditionScanBase
{
    private readonly string _fileName = fileName ?? string.Empty;
    private int _currentTokenIndex;
    private Token<RollConditionTokens>[] _tokenCache = [];

    public void SetSource(string source)
    {
        _tokenCache = [.. RollConditionTokenizer.Tokenize(source)];
        _currentTokenIndex = 0;
    }
    public CodeLocation LastLocation()
    {
        if (_currentTokenIndex == 0 || _tokenCache.Length == 0)
            return new CodeLocation(_fileName, 1, 1, 1, 1);

        var activeToken = _tokenCache[Math.Min(_currentTokenIndex - 1, _tokenCache.Length - 1)];
        return new CodeLocation(
            _fileName,
            activeToken.Span.Position.Line,
            activeToken.Span.Position.Column,
            activeToken.Span.Position.Line,
            activeToken.Span.Position.Column + activeToken.Span.Length
        );
    }
}

public sealed class RollConditionParserEngine(IStatValueValidator expressionValidator, DiagnosticContext ctx, PropertyDiagnosticContainer errors)
{
    private readonly IStatValueValidator _expressionValidator = expressionValidator ?? throw new ArgumentNullException(nameof(expressionValidator));
    private readonly DiagnosticContext _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
    private readonly PropertyDiagnosticContainer _errors = errors ?? throw new ArgumentNullException(nameof(errors));
    private List<RollCondition>? _parsedObjects;

    public List<RollCondition>? ParseStream(string expressionText)
    {
        if (RollConditionCombinatorParser.TryParse(expressionText, out var nodes, out var errorMessage))
        {
            _parsedObjects = [];
            foreach (var node in nodes)
            {
                if (node is BracketConditionElement bracket)
                {
                    _parsedObjects.Add(MakeCondition(bracket.Name, bracket.Expression));
                }
                else if (node is RollConditionElement element)
                {
                    _parsedObjects.Add(MakeCondition(string.Empty, element.ConditionText));
                }
            }
            return _parsedObjects;
        }

        var location = _ctx.PropertyValueSpan ?? new CodeLocation(string.Empty, 0, 0, 0, 0);
        _errors.Add($"Roll condition processing failure: {errorMessage}", location);
        return null;
    }

    private RollCondition MakeCondition(string textKey, string expression)
    {
        _expressionValidator.Validate(_ctx, null, expression, _errors);

        return new RollCondition
        {
            TextKey = textKey,
            Expression = expression
        };
    }
}
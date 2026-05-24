using LSLib.Parser;
using Superpower.Model;
using System.Diagnostics.CodeAnalysis;

namespace LSLibStats.Stats.Expression;

public abstract class ExpressionScanBase
{
    [SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Preserves naming parity with structural historical pipeline loops")]
    protected virtual bool yywrap() => true;
}

public partial class ExpressionScanner(string? fileName) : ExpressionScanBase
{
    private readonly string _fileName = fileName ?? string.Empty;
    private int _currentTokenIndex;
    private Token<ExpressionTokens>[] _tokenCache = [];

    public void SetSource(string source)
    {
        _tokenCache = [.. ExpressionTokenizer.Tokenize(source)];
        _currentTokenIndex = 0;
    }

    public CodeLocation LastLocation()
    {
        if (_currentTokenIndex == 0 || _tokenCache.Length == 0)
            return new CodeLocation(_fileName, 1, 1, 1, 1);

        var activeToken = _tokenCache[Math.Min(_currentTokenIndex - 1, _tokenCache.Length - 1)];
        return ComputeLocation(activeToken.Span);
    }

    private CodeLocation ComputeLocation(TextSpan span)
    {
        return new CodeLocation(_fileName, span.Position.Line, span.Position.Column, span.Position.Line, span.Position.Column + span.Length);
    }
}

public partial class ExpressionParser(ExpressionScanner scnr)
{
    private readonly ExpressionScanner _scanner = scnr ?? throw new ArgumentNullException(nameof(scnr));
    private IExpressionNode? _parsedAstRoot;

    public bool Parse(string expressionText, out string errorMessage)
    {
        _scanner.SetSource(expressionText);

        if (ExpressionCombinatorParser.TryParseExpression(expressionText, out var rootNode, out errorMessage))
        {
            _parsedAstRoot = rootNode;
            return true;
        }

        return false;
    }

    public object? GetParsedObject()
    {
        return _parsedAstRoot;
    }
}

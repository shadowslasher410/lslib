using LSLib.Parser;
using Superpower.Model;
using System.Diagnostics.CodeAnalysis;

namespace LSLibStats.Stats.Lua;

public abstract class StatLuaScanBase
{
    [SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Preserves naming parity with structural legacy pipeline loop boundaries")]
    protected virtual bool yywrap() => true;
}

public partial class StatLuaScanner(string? fileName) : StatLuaScanBase
{
    private readonly string _fileName = fileName ?? string.Empty;
    private int _currentTokenIndex;
    private Token<StatLuaTokens>[] _tokenCache = [];

    public void SetSource(string source)
    {
        _tokenCache = [.. LuaTokenizer.Tokenize(source)];
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

public partial class LuaParser(StatLuaScanner scnr)
{
    private readonly StatLuaScanner _scanner = scnr ?? throw new ArgumentNullException(nameof(scnr));
    private ILuaNode? _parsedAstRoot;

    public bool Parse(string expressionText, out string errorMessage)
    {
        _scanner.SetSource(expressionText);

        if (LuaCombinatorParser.TryParse(expressionText, out var rootNode, out errorMessage))
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
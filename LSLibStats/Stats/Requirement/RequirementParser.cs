using LSLib.Parser;
using LSLib.Stats.Requirements;
using Superpower.Model;
using System.Diagnostics.CodeAnalysis;

namespace LSLibStats.Stats.Requirement;

public sealed class Requirement
{
    public bool Not { get; set; } = false;
    public string RequirementName { get; set; } = string.Empty;
    public int IntParam { get; set; } = 0;
    public string? TagParam { get; set; } = null;
}

public abstract class RequirementScanBase
{
    [SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Preserves naming parity with historical pipeline loop boundaries")]
    protected virtual bool yywrap() => true;
}

public partial class RequirementScanner(string? fileName) : RequirementScanBase
{
    private readonly string _fileName = fileName ?? string.Empty;
    private int _currentTokenIndex;
    private Token<RequirementTokens>[] _tokenCache = [];

    public void SetSource(string source)
    {
        _tokenCache = [.. RequirementTokenizer.Tokenize(source)];
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

public partial class RequirementParser(RequirementScanner scnr)
{
    private readonly RequirementScanner _scanner = scnr ?? throw new ArgumentNullException(nameof(scnr));
    private List<Requirement>? _parsedObjects;

    public bool Parse(string expressionText, out string errorMessage)
    {
        _scanner.SetSource(expressionText);

        if (RequirementCombinatorParser.TryParse(expressionText, out var requirements, out errorMessage))
        {
            _parsedObjects = requirements;
            return true;
        }

        return false;
    }

    public object? GetParsedObject()
    {
        return _parsedObjects;
    }

    private static List<Requirement> MakeRequirements() => [];

    private static List<Requirement> AddRequirement(object requirements, object requirement)
    {
        var req = (List<Requirement>)requirements;
        req.Add((Requirement)requirement);
        return req;
    }

    private static Requirement MakeNotRequirement(object requirement)
    {
        var req = (Requirement)requirement;
        req.Not = true;
        return req;
    }

    private static Requirement MakeRequirement(object name)
    {
        return new Requirement
        {
            Not = false,
            RequirementName = (string)name,
            IntParam = 0,
            TagParam = string.Empty
        };
    }

    private static Requirement MakeIntRequirement(object name, object intArg)
    {
        return new Requirement
        {
            Not = false,
            RequirementName = (string)name,
            IntParam = int.Parse((string)intArg),
            TagParam = string.Empty
        };
    }
}

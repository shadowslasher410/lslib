using LSLib.Parser;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LSLibStats.Stats;

public enum StatTokens
{
    BAD = 0,
    NEW,
    ADD,
    ENTRY,
    TYPE,
    DATA,
    PARAM,
    USING,
    KEY,
    ABILITY,
    ITEMCOLOR,
    NAMEGROUP,
    NAME,
    NAMECOOL,
    ITEMGROUP,
    LEVELGROUP,
    ROOTGROUP,
    REQUIREMENT,
    DELTAMOD,
    NEW_BOOST,
    EQUIPMENT,
    ADD_EQUIPMENTGROUP,
    ADD_EQUIPMENT_ENTRY,
    ITEMCOMBOPROPERTY,
    NEW_ITEMCOMBOPROPERTYENTRY,
    ITEM_COMBINATION,
    ITEM_COMBINATION_RESULT,
    CRAFTING_PREVIEW_DATA,
    SKILLSET,
    SKILL,
    CATEGORY_MAP,
    WEAPON_COUNTER,
    SKILLBOOK_COUNTER,
    ARMOR_COUNTER,
    TREASURE,
    ITEMTYPES,
    TREASURE_TABLE,
    NEW_SUBTABLE,
    OBJECT_CATEGORY,
    START_LEVEL,
    END_LEVEL,
    MIN_LEVEL,
    MAX_LEVEL,
    CAN_MERGE,
    IGNORE_LEVEL_DIFF,
    USE_TREASURE_GROUPS,
    INTEGER,
    STRING,
    DATA_ITEM
}

public sealed partial class StatDeclaration
{
    public CodeLocation? Location { get; set; }
    public Dictionary<string, StatProperty> Properties { get; init; } = new(StringComparer.Ordinal);
    public bool WasValidated { get; set; }

    public string Name => Properties.TryGetValue("Name", out var prop) ? prop.Value.ToString() ?? string.Empty : string.Empty;
}

public sealed partial class StatProperty(string key, object value, CodeLocation? location = null, CodeLocation? valueLocation = null)
{
    public string Key { get; set; } = key ?? throw new ArgumentNullException(nameof(key));
    public object Value { get; set; } = value ?? throw new ArgumentNullException(nameof(value));
    public CodeLocation? Location { get; set; } = location;
    public CodeLocation? ValueLocation { get; set; } = valueLocation;
}

public sealed partial class StatElement(string collection, object value, CodeLocation? location = null)
{
    public string Collection { get; set; } = collection ?? throw new ArgumentNullException(nameof(collection));
    public object Value { get; set; } = value ?? throw new ArgumentNullException(nameof(value));
    public CodeLocation? Location { get; set; } = location;
}

public static partial class StatParserExtensions
{
    [GeneratedRegex(@"^data([ ]+)""([^""]+)""([ ]+)""(.*)""", RegexOptions.CultureInvariant)]
    private static partial Regex InnerDataRegex();

    public static string MakeString(string lit)
    {
        if (string.IsNullOrEmpty(lit) || lit.Length < 2) return string.Empty;

        var inner = lit.StartsWith('L') ? lit[2..^1] : lit[1..^1];
        return Regex.Unescape(inner);
    }

    public static StatProperty MakeDataProperty(string fileName, int startLine, int startCol, int endLine, int endCol, string lit)
    {
        var match = InnerDataRegex().Match(lit);
        if (!match.Success)
        {
            throw new InvalidDataException("Malformed data line layout specification structure.");
        }

        var key = match.Groups[2].Value;
        var val = match.Groups[4].Value;

        return new StatProperty(
            key,
            val,
            new CodeLocation(fileName, startLine, startCol, endLine, endCol),
            new CodeLocation(fileName, startLine, startCol + match.Groups[4].Index, endLine, startCol + match.Groups[4].Index + val.Length)
        );
    }

    public static List<StatDeclaration> MakeDeclarationList() => [];

    public static List<StatDeclaration> AddDeclaration(List<StatDeclaration> list, StatDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(declaration);
        list.Add(declaration);
        return list;
    }

    public static StatDeclaration MakeDeclaration() => new();

    public static StatDeclaration MakeDeclaration(CodeLocation location) => new() { Location = location };

    public static StatDeclaration MakeDeclaration(IEnumerable<StatProperty> properties)
    {
        var decl = new StatDeclaration();
        foreach (var prop in properties) AddProperty(decl, prop);
        return decl;
    }

    public static StatDeclaration MergeItemCombo(StatDeclaration combo, StatDeclaration result)
    {
        ArgumentNullException.ThrowIfNull(combo);
        ArgumentNullException.ThrowIfNull(result);

        foreach (var (key, value) in result.Properties)
        {
            if (key is not ("EntityType" or "Name")) combo.Properties[key] = value;
        }
        return combo;
    }

    public static StatDeclaration AddProperty(StatDeclaration declaration, object property)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(property);

        switch (property)
        {
            case StatProperty prop:
                declaration.Properties[prop.Key] = prop;
                break;

            case StatElement ele:
                if (!declaration.Properties.TryGetValue(ele.Collection, out var propNode))
                {
                    propNode = new StatProperty(ele.Collection, new List<object>(), ele.Location, null);
                    declaration.Properties[ele.Collection] = propNode;
                }

                if (propNode.Value is List<object> list) list.Add(ele.Value);
                break;

            case StatDeclaration otherDecl:
                foreach (var (key, propertyNode) in otherDecl.Properties) declaration.Properties[key] = propertyNode;
                break;
        }

        return declaration;
    }

    public static StatProperty MakeProperty(object key, object value) => new((string)key, value);
    public static StatProperty MakeProperty(CodeLocation location, object key, object value) => new((string)key, value, location);
    public static StatElement MakeElement(string key, object value, CodeLocation? location = null) => new(key, value, location);
    public static List<object> MakeCollection() => [];

    public static List<object> AddElement(List<object> collection, object element)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(element);
        collection.Add(element);
        return collection;
    }

    public static string Unwrap(object node) => node?.ToString() ?? string.Empty;
}
public sealed partial class StatFileParserEngine(StatLoadingContext context)
{
    private readonly StatLoadingContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public List<StatDeclaration>? ParseStream(string path, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var fileContent = reader.ReadToEnd();

        var declarations = StatCombinatorParser.ParseFile(fileContent, path, _context);
        return declarations;
    }

    public void LoadStatsFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var normalizedPath = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

        if (!File.Exists(normalizedPath))
        {
            StatLoadingContext.LogError(
                DiagnosticCode.StatSyntaxError.ToString(),
                $"Target stats file could not be located on disk: '{normalizedPath}'",
                new CodeLocation(normalizedPath, 1, 1, 1, 1),
                null
            );
            return;
        }

        try
        {
            using var stream = new FileStream(normalizedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var stats = ParseStream(normalizedPath, stream);
            if (stats is not null)
            {
                AddDeclarationsByType(stats);
            }
        }
        catch (Exception ex)
        {
            StatLoadingContext.LogError(DiagnosticCode.StatSyntaxError.ToString(), $"I/O exception caught during stream initialization pass: {ex.Message}", new CodeLocation(normalizedPath, 1, 1, 1, 1), null);
        }
    }

    private void AddDeclarationsByType(List<StatDeclaration> declarations)
    {
        foreach (var declaration in declarations)
        {
            if (!declaration.Properties.TryGetValue("EntityType", out var entityTypeProp))
            {
                StatLoadingContext.LogError(DiagnosticCode.StatEntityTypeUnknown.ToString(), "Unable to determine type context parameters of statistics asset declaration line mapping.", declaration.Location, null);
                continue;
            }

            var statType = entityTypeProp.Value?.ToString();
            if (string.IsNullOrEmpty(statType)) continue;

            if (!_context.Definitions.Types.TryGetValue(statType, out var type))
            {
                StatLoadingContext.LogError(DiagnosticCode.StatEntityTypeUnknown.ToString(), $"No explicit definition structure format exists for stat profile type category: '{statType}'", declaration.Location, null);
                continue;
            }

            if (!declaration.Properties.ContainsKey(type.NameProperty))
            {
                StatLoadingContext.LogError(DiagnosticCode.StatNameMissing.ToString(), $"Stat structural layout record has no required indexing property identifier field named: '{type.NameProperty}'", declaration.Location, null);
                continue;
            }

            if (!_context.DeclarationsByType.TryGetValue(statType, out var declarationsByType))
            {
                declarationsByType = new Dictionary<string, StatDeclaration>(StringComparer.Ordinal);
                _context.DeclarationsByType[statType] = declarationsByType;
            }

            var name = declaration.Properties[type.NameProperty].Value?.ToString();
            if (string.IsNullOrEmpty(name)) continue;

            if (declarationsByType.TryGetValue(name, out var existingDeclaration))
            {
                StatLoadingContext.LogError(DiagnosticCode.StatNameDuplicate.ToString(), $"Duplicate statistics entries declaration key identifier detected inside active files context boundaries: '{name}'", declaration.Location, null);
                StatLoadingContext.LogError(DiagnosticCode.StatNameDuplicate.ToString(), $"  -> Prior conflicting entity reference record block layout traced back to this line sequence.", existingDeclaration.Location, null);

                continue;
            }

            declarationsByType[name] = declaration;
        }
    }
}

public static partial class StatParserExtensions
{
    public static List<StatDeclaration> MakeStatFile() => [];

    public static List<StatDeclaration> AddStatFileDeclaration(List<StatDeclaration> list, StatDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(declaration);
        list.Add(declaration);
        return list;
    }

    public static List<StatDeclaration> AddTreasureTypes(List<StatDeclaration> list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return list;
    }
}
public static class StatEnumerationParser
{
    public static void Parse(Stream stream, Dictionary<string, StatEnumeration> destination)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(destination);

        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        StatEnumeration? currentEnum = null;

        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            if (trimmed.StartsWith("valuelist", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var enumName = parts[1].Replace("\"", "");
                    currentEnum = new StatEnumeration(enumName);
                    destination[enumName] = currentEnum;
                }
            }
            else if (trimmed.StartsWith("value", StringComparison.OrdinalIgnoreCase) && currentEnum != null)
            {
                var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var valueLabel = parts[1].Replace("\"", "");
                    currentEnum.AddItem(valueLabel);
                }
            }
        }
    }
}

public static class StatEntryTypeParser
{
    public static void Parse(Stream stream, Dictionary<string, StatEntryType> destination)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(destination);

        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        StatEntryType? currentEntryType = null;

        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            if (parts[0].Equals("modifier", StringComparison.OrdinalIgnoreCase) && parts[1].Equals("type", StringComparison.OrdinalIgnoreCase) && parts.Length >= 3)
            {
                var typeName = parts[2].Replace("\"", "");
                currentEntryType = new StatEntryType(typeName, "Name", "using");
                destination[typeName] = currentEntryType;
            }
            else if (parts[0].Equals("modifier", StringComparison.OrdinalIgnoreCase) && currentEntryType != null && parts.Length >= 3)
            {
                var fieldName = parts[1].Replace("\"", "");
                var fieldType = parts[2].Replace("\"", "");

                currentEntryType.Fields[fieldName] = new StatField(fieldName, fieldType);
            }
        }
    }
}

public static class StatFunctorParser
{
    public static void Parse(Stream stream, Dictionary<string, StatFunctorType> functors, Dictionary<string, StatFunctorType> boosts, Dictionary<string, StatFunctorType> descriptionParams)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(functors);
        ArgumentNullException.ThrowIfNull(boosts);
        ArgumentNullException.ThrowIfNull(descriptionParams);

        var doc = XDocument.Load(stream);
        if (doc.Root == null) return;

        foreach (var definitionNode in doc.Root.Elements("Definition"))
        {
            var name = definitionNode.Attribute("Name")?.Value ?? string.Empty;
            var type = definitionNode.Attribute("Type")?.Value ?? string.Empty;

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type)) continue;

            int requiredArgs = 0;
            if (int.TryParse(definitionNode.Attribute("RequiredArgs")?.Value, out int parsedArgs))
            {
                requiredArgs = parsedArgs;
            }

            var argumentsList = new List<StatFunctorArgumentType>();
            foreach (var argNode in definitionNode.Elements("Argument"))
            {
                var argName = argNode.Attribute("Name")?.Value ?? "Param";
                var argType = argNode.Attribute("Type")?.Value ?? "String";
                argumentsList.Add(new StatFunctorArgumentType(argName, argType));
            }

            var functorTypeNode = new StatFunctorType(name, requiredArgs, argumentsList);

            if (type.Equals("Boost", StringComparison.OrdinalIgnoreCase))
            {
                boosts[name] = functorTypeNode;
            }
            else if (type.Equals("Functor", StringComparison.OrdinalIgnoreCase))
            {
                functors[name] = functorTypeNode;
            }
            else if (type.Equals("DescriptionParam", StringComparison.OrdinalIgnoreCase))
            {
                descriptionParams[name] = functorTypeNode;
            }
        }
    }
}
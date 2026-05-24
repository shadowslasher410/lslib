using LSLib.Parser;

namespace LSLibStats.Stats;

public partial class StatDefinitionRepository { }

public sealed class StatEnumeration(string name)
{
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));
    public List<string> Values { get; } = [];
    public Dictionary<string, int> ValueToIndexMap { get; } = new(StringComparer.Ordinal);

    public void AddItem(int index, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Values.Count != index)
            throw new InvalidOperationException("Enumeration item insertion indices must be perfectly sequential.");

        Values.Add(value);
        ValueToIndexMap.TryAdd(value, index);
    }

    public void AddItem(string label) => AddItem(Values.Count, label);
}

public sealed class StatReferenceConstraint
{
    public string StatType { get; set; } = string.Empty;
}

public sealed class StatField(string name, string type)
{
    public string Name { get; set; } = name ?? throw new ArgumentNullException(nameof(name));
    public string Type { get; set; } = type ?? throw new ArgumentNullException(nameof(type));
    public StatEnumeration? EnumType { get; set; }
    public List<StatReferenceConstraint>? ReferenceTypes { get; set; }
    private IStatValueValidator? _validator;

    public IStatValueValidator GetValidator(StatValueValidatorFactory factory, StatDefinitionRepository definitions)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(definitions);
        return _validator ??= factory.CreateValidator(this, definitions);
    }
}

public sealed class StatEntryType(string name, string nameProperty, string? basedOnProperty)
{
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));
    public string NameProperty { get; } = nameProperty ?? throw new ArgumentNullException(nameof(nameProperty));
    public string? BasedOnProperty { get; } = basedOnProperty;
    public Dictionary<string, StatField> Fields { get; } = new(StringComparer.Ordinal);
}

public sealed class StatFunctorArgumentType(string name, string type)
{
    public string Name { get; set; } = name ?? throw new ArgumentNullException(nameof(name));
    public string Type { get; set; } = type ?? throw new ArgumentNullException(nameof(type));
}

public sealed class StatFunctorType(string name, int requiredArgs, List<StatFunctorArgumentType> args)
{
    public string Name { get; set; } = name ?? throw new ArgumentNullException(nameof(name));
    public int RequiredArgs { get; set; } = requiredArgs;
    public List<StatFunctorArgumentType> Args { get; set; } = args ?? throw new ArgumentNullException(nameof(args));
}

public interface IStatValueValidator
{
    void Validate(DiagnosticContext ctx, CodeLocation? location, object value, PropertyDiagnosticContainer errors);
}

public interface IStatReferenceValidator
{
    bool IsValidReference(string reference, string statType);
    bool IsValidGuidResource(string name, string resourceType);
}

public sealed class StatLoadingContext
{
    public StatDefinitionRepository Definitions { get; init; } = new();
    public Dictionary<string, Dictionary<string, StatDeclaration>> DeclarationsByType { get; init; } = new(StringComparer.Ordinal);

    public static void LogError(string code, string message, CodeLocation? location, List<PropertyDiagnosticContext>? contexts = null)
    {
        var contextStr = contexts is not null ? $" [{string.Join(" -> ", contexts.Select(c => c.Context))}]" : string.Empty;
        Console.Error.WriteLine($"[{code}] {location?.FileName}:{location?.StartLine}:{location?.StartColumn} - {message}{contextStr}");
    }
}

public enum DiagnosticCode
{
    StatSyntaxError,
    StatPropertyValueInvalid,
    StatEntityTypeUnknown,
    StatNameMissing,
    StatNameDuplicate
}
public class DiagnosticContext
{
    public bool IgnoreMissingReferences { get; set; }
    public StatDeclaration? CurrentDeclaration { get; set; }
    public CodeLocation? PropertyValueSpan { get; set; }
}

public enum PropertyDiagnosticContextType
{
    Argument,
    Call,
    Property,
    Entry
}

public readonly struct PropertyDiagnosticContext
{
    public PropertyDiagnosticContextType Type { get; init; }
    public required string Context { get; init; }
    public CodeLocation? Location { get; init; }
}

public class PropertyDiagnostic(string message, CodeLocation? location = null, List<PropertyDiagnosticContext>? contexts = null)
{
    public string Message { get; set; } = message;
    public CodeLocation? Location { get; set; } = location;
    public List<PropertyDiagnosticContext>? Contexts { get; set; } = contexts;
}
public sealed class PropertyDiagnosticContainer
{
    public List<PropertyDiagnostic>? Messages { get; set; } = [];

    public bool Empty => Messages is null or { Count: 0 };

    public void AddContext(PropertyDiagnosticContextType type, string name, CodeLocation? location = null)
    {
        if (Empty) return;

        var context = new PropertyDiagnosticContext
        {
            Type = type,
            Context = name,
            Location = location
        };

        foreach (var msg in Messages!)
        {
            msg.Contexts ??= [];
            msg.Contexts.Add(context);
        }
    }

    public void Add(string message, CodeLocation? location = null)
    {
        Messages ??= [];
        Messages.Add(new PropertyDiagnostic(message, location));
    }

    public void MergeInto(PropertyDiagnosticContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        if (Empty) return;

        container.Messages ??= [];
        container.Messages.AddRange(Messages ?? []);
    }

    public void MergeInto(StatLoadingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Empty) return;

        foreach (var message in Messages ?? [])
        {
            var location = message.Location;
            if (message.Contexts is not null)
            {
                foreach (var ctx in message.Contexts)
                {
                    location ??= ctx.Location;
                }
            }

            StatLoadingContext.LogError(DiagnosticCode.StatPropertyValueInvalid.ToString(), message.Message, location, message.Contexts);
        }
    }

    public void Clear() => Messages?.Clear();
}
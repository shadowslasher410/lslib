using LSLib.Parser;
using LSLibStats.Stats.Functor;
using LSLibStats.Stats.Lua;
using LSLibStats.Stats.Requirement;
using LSLibStats.Stats.RollCondition;
using System.Collections.Frozen;
using System.Globalization;

namespace LSLibStats.Stats;

public interface IPropertyValidator
{
    void ValidateEntry(StatEntryType type, string declarationName, StatDeclaration declaration, PropertyDiagnosticContainer errors);
}

public enum StatExpressionType
{
    Boost,
    Functor,
    DescriptionParams
}

public partial class StatDefinitionRepository
{
    public Dictionary<string, StatEnumeration> Enumerations { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, StatEntryType> Types { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, StatFunctorType> Boosts { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, StatFunctorType> Functors { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, StatFunctorType> DescriptionParams { get; init; } = new(StringComparer.Ordinal);
}

public abstract class StatStringValidator : IStatValueValidator
{
    public abstract void Validate(DiagnosticContext ctx, string value, PropertyDiagnosticContainer errors);

    public void Validate(DiagnosticContext ctx, CodeLocation? location, object value, PropertyDiagnosticContainer errors)
    {
        ArgumentNullException.ThrowIfNull(value);
        Validate(ctx, value.ToString()!, errors);
    }
}

public class BooleanValidator : StatStringValidator
{
    public override void Validate(DiagnosticContext ctx, string value, PropertyDiagnosticContainer errors)
    {
        if (value is not ("true" or "false" or ""))
        {
            errors.Add("expected boolean value 'true' or 'false'");
        }
    }
}

public class Int32Validator : StatStringValidator
{
    public override void Validate(DiagnosticContext ctx, string value, PropertyDiagnosticContainer errors)
    {
        if (value != "" && !int.TryParse(value, out _))
        {
            errors.Add("expected an integer value");
        }
    }
}

public class FloatValidator : StatStringValidator
{
    public override void Validate(DiagnosticContext ctx, string value, PropertyDiagnosticContainer errors)
    {
        if (value != "" && !float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            errors.Add("expected a float value");
        }
    }
}

public class PercentageValidator : StatStringValidator
{
    public override void Validate(DiagnosticContext ctx, string value, PropertyDiagnosticContainer errors)
    {
        var trimmed = value.Trim();
        if (trimmed != "" && (trimmed[^1] != '%' || !float.TryParse(trimmed[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
        {
            errors.Add("expected a percentage value");
        }
    }
}

public class EnumValidator(StatEnumeration enumeration) : StatStringValidator
{
    private readonly StatEnumeration _enumeration = enumeration ?? throw new ArgumentNullException(nameof(enumeration));

    public override void Validate(DiagnosticContext ctx, string value, PropertyDiagnosticContainer errors)
    {
        if (value != "" && !_enumeration.ValueToIndexMap.ContainsKey(value))
        {
            errors.Add(_enumeration.Values.Count > 20
                ? $"expected one of: {string.Join(", ", _enumeration.Values.Take(20))}, ..."
                : $"expected one of: {string.Join(", ", _enumeration.Values)}");
        }
    }
}

public class MultiValueEnumValidator(StatEnumeration enumeration) : StatStringValidator
{
    private readonly EnumValidator _validator = new(enumeration);

    public override void Validate(DiagnosticContext ctx, string value, PropertyDiagnosticContainer errors)
    {
        if (value.Length == 0) return;

        string[] tokens = value.Split(';');
        foreach (var item in tokens)
        {
            var trimmed = item.Trim();
            if (trimmed.Length > 0)
            {
                _validator.Validate(ctx, trimmed, errors);
            }
        }
    }
}

public class StringValidator : StatStringValidator
{
    public override void Validate(DiagnosticContext ctx, string value, PropertyDiagnosticContainer errors)
    {
        if (value.Length > 2047)
        {
            errors.Add("Value cannot be longer than 2047 characters due to FixedString runtime pooling limitations.");
        }
    }
}

public class UUIDValidator : StatStringValidator
{
    public override void Validate(DiagnosticContext ctx, string value, PropertyDiagnosticContainer errors)
    {
        if (value != "" && !Guid.TryParseExact(value, "D", out _))
        {
            errors.Add($"'{value}' is not a valid UUID");
        }
    }
}

public class StatReferenceValidator(IStatReferenceValidator validator, List<StatReferenceConstraint> constraints) : StatStringValidator
{
    private readonly IStatReferenceValidator _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    private readonly List<StatReferenceConstraint> _constraints = constraints ?? throw new ArgumentNullException(nameof(constraints));

    public override void Validate(DiagnosticContext ctx, string value, PropertyDiagnosticContainer errors)
    {
        if (ctx.IgnoreMissingReferences || value == "") return;

        foreach (var constraint in _constraints)
        {
            if (_validator.IsValidReference(value, constraint.StatType)) return;
        }

        var refTypes = string.Join("/", _constraints.Select(c => c.StatType));
        errors.Add($"'{value}' is not a valid {refTypes} reference");
    }
}

public class MultiValueStatReferenceValidator(IStatReferenceValidator validator, List<StatReferenceConstraint> constraints) : StatStringValidator
{
    private readonly StatReferenceValidator _validator = new(validator, constraints);

    public override void Validate(DiagnosticContext ctx, string value, PropertyDiagnosticContainer errors)
    {
        string[] tokens = value.Split(';');
        foreach (var item in tokens)
        {
            var trimmed = item.Trim();
            if (trimmed.Length > 0)
            {
                _validator.Validate(ctx, trimmed, errors);
            }
        }
    }
}

public class MultiValueValidator(IStatValueValidator validator) : StatStringValidator
{
    private readonly IStatValueValidator _validator = validator ?? throw new ArgumentNullException(nameof(validator));

    public override void Validate(DiagnosticContext ctx, string value, PropertyDiagnosticContainer errors)
    {
        string[] tokens = value.Split(';');
        foreach (var item in tokens)
        {
            var trimmed = item.Trim();
            if (trimmed.Length > 0)
            {
                _validator.Validate(ctx, null, trimmed, errors);
            }
        }
    }
}

public class ExpressionValidator(string validatorType, StatDefinitionRepository definitions,
    StatValueValidatorFactory validatorFactory, StatExpressionType type) : StatStringValidator
{
    private readonly string _validatorType = validatorType ?? throw new ArgumentNullException(nameof(validatorType));
    private readonly StatDefinitionRepository _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly StatValueValidatorFactory _validatorFactory = validatorFactory ?? throw new ArgumentNullException(nameof(validatorFactory));
    private readonly StatExpressionType _type = type;

    public override void Validate(DiagnosticContext ctx, string value, PropertyDiagnosticContainer errors)
    {
        var typeLen = 10 + _validatorType.Length;
        var rootLocation = ctx.PropertyValueSpan ?? new CodeLocation(string.Empty, 0, 0, 0, 0);

        var bridgeEngine = new FunctorParserEngine(_definitions, ctx, _validatorFactory, (ExpressionType)(int)_type, errors, rootLocation, typeLen);

        if (!bridgeEngine.Parse(value, out var errorMessage))
        {
            errors.Add($"Functor syntax contract violation: {errorMessage}", rootLocation);
            return;
        }

        bridgeEngine.ParseFromAST();
    }
}

public class RequirementsValidator : StatStringValidator
{
    public override void Validate(DiagnosticContext ctx, string value, PropertyDiagnosticContainer errors)
    {
        var rootLocation = ctx.PropertyValueSpan ?? new CodeLocation(string.Empty, 0, 0, 0, 0);

        if (!RequirementCombinatorParser.TryParse(value, out var requirements, out var errorMessage))
        {
            errors.Add($"Requirements structural validation error: {errorMessage}", rootLocation);
            return;
        }

        foreach (var req in requirements)
        {
            if (req.RequirementName.Length > 2047)
            {
                errors.Add("Requirement name identifier exceeds FixedString pools limitation boundaries.", rootLocation);
            }
        }
    }
}

public class LuaExpressionValidator(bool allowEmpty = false) : StatStringValidator
{
    private readonly bool _allowEmpty = allowEmpty;

    public override void Validate(DiagnosticContext ctx, string value, PropertyDiagnosticContainer errors)
    {
        if (_allowEmpty && value.Trim().Length == 0) return;

        var targetValue = value;
        if (targetValue.Length > 0 && targetValue[^1] == ';')
        {
            targetValue = targetValue[..^1];
        }

        if (!LuaCombinatorParser.TryParse(targetValue, out _, out var errorMessage))
        {
            var location = ctx.PropertyValueSpan ?? new CodeLocation(string.Empty, 0, 0, 0, 0);
            errors.Add($"Lua validation check failure: {errorMessage}", location);
        }
    }
}

public class StatsExpressionValidator(bool allowEmpty = false) : StatStringValidator
{
    private readonly bool _allowEmpty = allowEmpty;

    public override void Validate(DiagnosticContext ctx, string value, PropertyDiagnosticContainer errors)
    {
        if (_allowEmpty && value.Trim().Length == 0) return;

        var scannerEngine = new Expression.ExpressionScanner(string.Empty);
        var parserEngine = new Expression.ExpressionParser(scannerEngine);

        if (!parserEngine.Parse(value, out var errorMessage))
        {
            var location = ctx.PropertyValueSpan ?? new CodeLocation(string.Empty, 0, 0, 0, 0);
            errors.Add($"Expression calculation syntax rule break: {errorMessage}", location);
        }
    }
}

public class DealDamageAmountValidator : StatsExpressionValidator
{
    public DealDamageAmountValidator() : base(false) { }

    private static readonly FrozenSet<string> DamageTypes = FrozenSet.ToFrozenSet(
    [
        "MainWeapon", "OffhandWeapon", "MainMeleeWeapon", "OffhandMeleeWeapon",
        "MainRangedWeapon", "OffhandRangedWeapon", "SourceWeapon", "UnarmedDamage",
        "ThrownWeapon", "ImprovisedWeapon"
    ], StringComparer.Ordinal);

    public override void Validate(DiagnosticContext ctx, string value, PropertyDiagnosticContainer errors)
    {
        var v = value;
        foreach (var damageType in DamageTypes)
        {
            v = v.Replace(damageType, "Placeholder0", StringComparison.Ordinal);
        }

        base.Validate(ctx, v, errors);
    }
}

public class ForceDistanceValidator : StatsExpressionValidator
{
    public ForceDistanceValidator() : base(false) { }

    public override void Validate(DiagnosticContext ctx, string value, PropertyDiagnosticContainer errors)
    {
        if (value == "ShoveDistance") return;

        base.Validate(ctx, value, errors);
    }
}

public class RollConditionsValidator : StatStringValidator
{
    private readonly LuaExpressionValidator _luaValidator = new();

    public override void Validate(DiagnosticContext ctx, string value, PropertyDiagnosticContainer errors)
    {
        if (value.Trim().Length == 0) return;

        var parserBridge = new RollConditionParserEngine(_luaValidator, ctx, errors);

        var parsedConditions = parserBridge.ParseStream(value);
        if (parsedConditions is null) return;
    }
}

public class UseCostsValidator(IStatReferenceValidator validator) : StatStringValidator
{
    private readonly IStatReferenceValidator _validator = validator ?? throw new ArgumentNullException(nameof(validator));

    public override void Validate(DiagnosticContext ctx, string value, PropertyDiagnosticContainer errors)
    {
        if (value.Length == 0) return;

        string[] resources = value.Split(';');
        foreach (var resource in resources)
        {
            var res = resource.Trim();
            if (res.Length == 0) continue;

            string[] parts = res.Split(':');
            if (parts.Length is < 2 or > 4)
            {
                errors.Add("Malformed use costs");
                return;
            }

            if (!ctx.IgnoreMissingReferences && !_validator.IsValidGuidResource(parts[0], "ActionResource") && !_validator.IsValidGuidResource(parts[0], "ActionResourceGroup"))
            {
                errors.Add($"Nonexistent action resource or action resource group: {parts[0]}");
            }

            string[] distanceExpr = parts[1].Split('*');
            if (distanceExpr[0] == "Distance")
            {
                if (distanceExpr.Length > 1 && !float.TryParse(distanceExpr[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    errors.Add($"Malformed distance multiplier: {distanceExpr[1]}");
                    continue;
                }
            }
            else if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                errors.Add($"Malformed resource amount: {parts[1]}");
                continue;
            }

            if (parts.Length > 2 && parts.Length == 3 && !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                errors.Add($"Malformed level: {parts[2]}");
                continue;
            }

            if (parts.Length > 3 && parts.Length == 4 && !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                errors.Add($"Malformed level: {parts[3]}");
                continue;
            }
        }
    }
}
public class DiceRollValidator : StatStringValidator
{
    public override void Validate(DiagnosticContext ctx, string value, PropertyDiagnosticContainer errors)
    {
        if (value.Length == 0) return;

        string[] parts = value.Split('d');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int dieSize))
        {
            errors.Add("Malformed dice roll");
            return;
        }

        if (dieSize is not (4 or 6 or 8 or 10 or 12 or 20 or 100))
        {
            errors.Add($"Invalid die size: {dieSize}");
        }
    }
}

public class TreasureDropValidator(IStatReferenceValidator validator) : StatStringValidator
{
    private readonly IStatReferenceValidator _validator = validator ?? throw new ArgumentNullException(nameof(validator));

    public override void Validate(DiagnosticContext ctx, string value, PropertyDiagnosticContainer errors)
    {
        if (value is ['I', '_', .. var item])
        {
            if (!_validator.IsValidReference(item, "Object")
                && !_validator.IsValidReference(item, "Armor")
                && !_validator.IsValidReference(item, "Weapon"))
            {
                errors.Add($"Nonexistent object, armor or weapon: {item}");
            }
        }
        else if (value is ['T', '_', .. var treasureTable])
        {
            if (!_validator.IsValidReference(treasureTable, "TreasureTable"))
            {
                errors.Add($"Nonexistent treasure table: {treasureTable}");
            }
        }
        else if (!_validator.IsValidReference(value, "ObjectCategory"))
        {
            errors.Add($"Nonexistent object category: {value}");
        }
    }
}

public class ObjectListValidator(IPropertyValidator propertyValidator, StatEntryType objectType) : IStatValueValidator
{
    private readonly IPropertyValidator _propertyValidator = propertyValidator ?? throw new ArgumentNullException(nameof(propertyValidator));
    private readonly StatEntryType _objectType = objectType;

    public void Validate(DiagnosticContext ctx, CodeLocation? location, object value, PropertyDiagnosticContainer errors)
    {
        if (value is not IEnumerable<object> objs || _objectType is null) return;

        foreach (var subobject in objs)
        {
            var declarationName = ctx.CurrentDeclaration?.Name ?? string.Empty;
            _propertyValidator.ValidateEntry(_objectType, declarationName, (StatDeclaration)subobject, errors);
        }
    }
}

public class AnyParser(IEnumerable<IStatValueValidator> validators, string? defaultMessage = null) : IStatValueValidator
{
    private readonly List<IStatValueValidator> _validators = [.. validators];
    private readonly string? _defaultMessage = defaultMessage;

    public void Validate(DiagnosticContext ctx, CodeLocation? location, object value, PropertyDiagnosticContainer errors)
    {
        var scratchpad = new PropertyDiagnosticContainer();

        foreach (var validator in _validators)
        {
            scratchpad.Clear();
            validator.Validate(ctx, location, value, scratchpad);

            if (scratchpad.Empty) return;
        }

        if (_defaultMessage != null)
        {
            errors.Add(_defaultMessage);
        }
        else if (!scratchpad.Empty)
        {
            scratchpad.MergeInto(errors);
        }
    }
}

public class StatValueValidatorFactory(IStatReferenceValidator referenceValidator, IPropertyValidator propertyValidator)
{
    private readonly IStatReferenceValidator _referenceValidator = referenceValidator ?? throw new ArgumentNullException(nameof(referenceValidator));
    private readonly IPropertyValidator _propertyValidator = propertyValidator ?? throw new ArgumentNullException(nameof(propertyValidator));

    public IStatValueValidator CreateReferenceValidator(List<StatReferenceConstraint> constraints)
    {
        return new StatReferenceValidator(_referenceValidator, constraints);
    }

    public IStatValueValidator CreateValidator(StatField field, StatDefinitionRepository definitions)
    {
        return field.Name switch
        {
            "Boosts" or "DefaultBoosts" or "BoostsOnEquipMainHand" or "BoostsOnEquipOffHand"
                => new ExpressionValidator("Functors", definitions, this, StatExpressionType.Boost),

            "TooltipDamage" or "TooltipDamageList" or "TooltipStatusApply" or "TooltipConditionalDamage"
                => new ExpressionValidator("Functors", definitions, this, StatExpressionType.DescriptionParams),

            "DescriptionParams" or "ExtraDescriptionParams" or "ShortDescriptionParams" or "TooltipUpcastDescriptionParams"
                => new ExpressionValidator("DescriptionParams", definitions, this, StatExpressionType.DescriptionParams),

            "ConcentrationSpellID" or "CombatAIOverrideSpell" or "SpellContainerID" or "FollowUpOriginalSpell" or "RootSpellID"
                => new StatReferenceValidator(_referenceValidator, [new StatReferenceConstraint { StatType = "SpellData" }]),

            "ContainerSpells"
                => new MultiValueStatReferenceValidator(_referenceValidator, [new StatReferenceConstraint { StatType = "SpellData" }]),

            "InterruptPrototype"
                => new StatReferenceValidator(_referenceValidator, [new StatReferenceConstraint { StatType = "InterruptData" }]),

            "Passives" or "PassivesOnEquip" or "PassivesMainHand" or "PassivesOffHand"
                => new MultiValueStatReferenceValidator(_referenceValidator, [new StatReferenceConstraint { StatType = "PassiveData" }]),

            "StatusOnEquip" or "StatusInInventory"
                => new MultiValueStatReferenceValidator(_referenceValidator, [new StatReferenceConstraint { StatType = "StatusData" }]),

            "Cost" or "UseCosts" or "DualWieldingUseCosts" or "ActionResources" or "TooltipUseCosts" or "RitualCosts" or "HitCosts"
                => new UseCostsValidator(_referenceValidator),

            "Damage" or "VersatileDamage" or "StableRoll"
                => new DiceRollValidator(),

            "Template" or "StatusEffectOverride" or "StatusEffectOnTurn" or "ManagedStatusEffectGroup" or "ApplyEffect" or
            "SpellEffect" or "StatusEffect" or "DisappearEffect" or "PreviewEffect" or "PositionEffect" or "HitEffect" or
            "TargetEffect" or "BeamEffect" or "CastEffect" or "PrepareEffect" or "TooltipOnSave"
                => new UUIDValidator(),

            "AmountOfTargets"
                => new LuaExpressionValidator(allowEmpty: true),

            _ => CreateValidator(field.Type, field.EnumType, field.ReferenceTypes, definitions)
        };
    }

    public IStatValueValidator CreateValidator(string type, StatEnumeration? enumType, List<StatReferenceConstraint>? constraints, StatDefinitionRepository definitions)
    {
        if (enumType is null && definitions.Enumerations.TryGetValue(type, out var enumInfo) && enumInfo.Values.Count > 0)
        {
            enumType = enumInfo;
        }

        if (enumType is not null)
        {
            return type is "SpellFlagList"
                or "SpellCategoryFlags"
                or "CinematicArenaFlags"
                or "RestErrorFlags"
                or "AuraFlags"
                or "StatusEvent"
                or "AIFlags"
                or "WeaponFlags"
                or "ProficiencyGroupFlags"
                or "InterruptContext"
                or "InterruptDefaultValue"
                or "AbilityFlags"
                or "AttributeFlags"
                or "PassiveFlags"
                or "ResistanceFlags"
                or "LineOfSightFlags"
                or "StatusPropertyFlags"
                or "StatusGroupFlags"
                or "StatsFunctorContext"
                or "ResurrectTypes"
                ? new MultiValueEnumValidator(enumType)
                : new EnumValidator(enumType);
        }

        return type switch
        {
            "Boolean" => new BooleanValidator(),
            "ConstantInt" or "Int" => new Int32Validator(),
            "ConstantFloat" or "Float" => new FloatValidator(),
            "String" or "FixedString" or "TranslatedString" => new StringValidator(),
            "Guid" => new UUIDValidator(),
            "Requirements" => new RequirementsValidator(),
            "StatsFunctors" => new ExpressionValidator("Functors", definitions, this, StatExpressionType.Functor),
            "Lua" => new LuaExpressionValidator(),
            "TargetConditions" or "Conditions" => new LuaExpressionValidator(allowEmpty: true),
            "RollConditions" => new RollConditionsValidator(),
            "UseCosts" => new UseCostsValidator(_referenceValidator),
            "StatReference" => new StatReferenceValidator(_referenceValidator, constraints!),

            "StatusId" => new AnyParser(
                [
                    new EnumValidator(definitions.Enumerations["EngineStatusType"]),
                    new StatReferenceValidator(_referenceValidator, [new StatReferenceConstraint { StatType = "StatusData" }])
                ],
                "Expected a status name"),

            "ResurrectTypes" => new MultiValueEnumValidator(definitions.Enumerations["ResurrectType"]),

            "StatusIdOrGroup" => new AnyParser(
                [
                    new EnumValidator(definitions.Enumerations["StatusGroupFlags"]),
                    new EnumValidator(definitions.Enumerations["EngineStatusType"]),
                    new StatReferenceValidator(_referenceValidator, [new StatReferenceConstraint { StatType = "StatusData" }])
                ],
                "Expected a status or StatusGroup name"),

            "SummonDurationOrInt" => new AnyParser(
                [
                    new EnumValidator(definitions.Enumerations["SummonDuration"]),
                    new Int32Validator()
                ]),

            "FloatOrPercentage" => new AnyParser(
                [
                    new FloatValidator(),
                    new PercentageValidator()
                ],
                "Expected a float or percentage value"),

            "StatsExpressionOrPercentage" => new AnyParser(
                [
                    new PercentageValidator(),
                    new StatsExpressionValidator(allowEmpty: false)
                ],
                "Expected a stats expression or a percentage value"),

            "StatsExpression" => new StatsExpressionValidator(allowEmpty: false),
            "DealDamageAmount" => new DealDamageAmountValidator(),
            "ForceDistance" => new ForceDistanceValidator(),

            "AllOrDamageType" => new AnyParser(
                [
                    new EnumValidator(definitions.Enumerations["AllEnum"]),
                    new EnumValidator(definitions.Enumerations["Damage Type"])
                ]),

            "RollAdjustmentTypeOrDamageType" => new AnyParser(
                [
                    new EnumValidator(definitions.Enumerations["RollAdjustmentType"]),
                    new EnumValidator(definitions.Enumerations["Damage Type"])
                ]),

            "AbilityOrAttackRollAbility" => new AnyParser(
                [
                    new EnumValidator(definitions.Enumerations["Ability"]),
                    new EnumValidator(definitions.Enumerations["AttackRollAbility"])
                ]),

            "AbilityOrSkill" => new AnyParser(
                [
                    new EnumValidator(definitions.Enumerations["Ability"]),
                    new EnumValidator(definitions.Enumerations["SkillType"])
                ]),

            "DamageTypeOrDealDamageWeaponDamageType" => new AnyParser(
                [
                    new EnumValidator(definitions.Enumerations["Damage Type"]),
                    new EnumValidator(definitions.Enumerations["DealDamageWeaponDamageType"])
                ]),

            "SpellId" => new StatReferenceValidator(_referenceValidator, [new StatReferenceConstraint { StatType = "SpellData" }]),
            "Interrupt" => new StatReferenceValidator(_referenceValidator, [new StatReferenceConstraint { StatType = "InterruptData" }]),
            "TreasureSubtables" => definitions.Types.TryGetValue("TreasureSubtable", out var subtableType) ? new ObjectListValidator(_propertyValidator, subtableType) : new StringValidator(),
            "TreasureSubtableObject" => definitions.Types.TryGetValue("TreasureSubtableObject", out var objectType) ? new ObjectListValidator(_propertyValidator, objectType) : new StringValidator(),
            "TreasureDrop" => new TreasureDropValidator(_referenceValidator),

            "StatusIDs" => new MultiValueValidator(
                new AnyParser(
                    [
                        new EnumValidator(definitions.Enumerations["StatusGroupFlags"]),
                        new EnumValidator(definitions.Enumerations["EngineStatusType"]),
                        new StatReferenceValidator(_referenceValidator, [new StatReferenceConstraint { StatType = "StatusData" }])
                    ])
                ),

            _ => throw new ArgumentException($"Could not create parser for type '{type}'")
        };
    }
}
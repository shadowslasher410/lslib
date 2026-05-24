using Superpower.Model;
using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace LSLibStats.Stats.Expression;

public enum ExpressionTokens
{
    BAD = 0,
    BINOP,
    BIN_OR_UNOP,
    VARIABLE_REF,
    LEVEL_MAP_VALUE,
    CLASS_LEVEL,
    RESOURCE_ROLL,
    FOR_EACH,
    MAX_EXPR,
    CONTEXT_TYPE,
    STATUS_PROPERTY,
    NAME,
    INTEGER,
    DICE_ROLL,
    LEFT_PAREN = '(',
    RIGHT_PAREN = ')',
    COMMA = ',',
    DOT = '.'
}

public static partial class ExpressionTokenizer
{
    private static readonly FrozenDictionary<string, ExpressionTokens> ExplicitKeywords = new Dictionary<string, ExpressionTokens>(StringComparer.Ordinal)
    {
        ["LevelMapValue"] = ExpressionTokens.LEVEL_MAP_VALUE,
        ["ClassLevel"] = ExpressionTokens.CLASS_LEVEL,
        ["ResourceRoll"] = ExpressionTokens.RESOURCE_ROLL,
        ["foreach"] = ExpressionTokens.FOR_EACH,
        ["max"] = ExpressionTokens.MAX_EXPR,
        ["Target"] = ExpressionTokens.CONTEXT_TYPE,
        ["Owner"] = ExpressionTokens.CONTEXT_TYPE,
        ["Cause"] = ExpressionTokens.CONTEXT_TYPE,
        ["Amount"] = ExpressionTokens.STATUS_PROPERTY,
        ["Duration"] = ExpressionTokens.STATUS_PROPERTY
    }.ToFrozenDictionary(StringComparer.Ordinal);

    [GeneratedRegex(@"^Placeholder[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"^(Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma|SpellCastingAbility|UnarmedMeleeAbility)(Modifier|Flat)?(SavingThrow)?(DialogueCheck)?(Advantage|Distadvantage)?", RegexOptions.CultureInvariant)]
    private static partial Regex AttributesRegex();

    [GeneratedRegex(@"^(Deception|Intimidation|Performance|Persuasion|Acrobatics|SleightOfHand|Stealth|Arcana|History|Investigation|Nature|Religion|Athletics|AnimalHandling|Insight|Medicine|Perception|Survival)(DialogueCheck)?(Advantage|Distadvantage)?", RegexOptions.CultureInvariant)]
    private static partial Regex SkillsRegex();

    [GeneratedRegex(@"^(ProficiencyBonus|Level|SpellDC|WeaponActionDC|CurrentHP|MaxHP|SpellPowerLevel|TadpolePowersCount|DamageDone)", RegexOptions.CultureInvariant)]
    private static partial Regex VariableDataRegex();

    [GeneratedRegex(@"^[0-9]+d[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex DiceRollRegex();

    [GeneratedRegex(@"^[0-9]+\.[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex FloatRegex();

    [GeneratedRegex(@"^[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex IntegerRegex();

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();

    public static TokenList<ExpressionTokens> Tokenize(string source)
    {
        if (string.IsNullOrEmpty(source)) return TokenList<ExpressionTokens>.Empty;

        List<Token<ExpressionTokens>> tokens = [];
        var next = new TextSpan(source);

        while (!next.IsAtEnd)
        {
            var ch = (next.Source is not null && next.Position.Absolute < next.Source.Length)
                ? next.Source[next.Position.Absolute]
                : '\0';

            if (char.IsWhiteSpace(ch) || ch is '\t' || ch is '\r' || ch is '\n')
            {
                next = AdvanceTextSpan(next, 1);
                continue;
            }

            if (ch is '(' or ')' or ',' or '.')
            {
                var startPosition = next.Position;
                tokens.Add(new Token<ExpressionTokens>((ExpressionTokens)ch, new TextSpan(source, startPosition, 1)));
                next = AdvanceTextSpan(next, 1);
                continue;
            }

            if (ch is '+' or '/' or '*')
            {
                var startPosition = next.Position;
                tokens.Add(new Token<ExpressionTokens>(ExpressionTokens.BINOP, new TextSpan(source, startPosition, 1)));
                next = AdvanceTextSpan(next, 1);
                continue;
            }

            if (ch == '-')
            {
                var startPosition = next.Position;
                tokens.Add(new Token<ExpressionTokens>(ExpressionTokens.BIN_OR_UNOP, new TextSpan(source, startPosition, 1)));
                next = AdvanceTextSpan(next, 1);
                continue;
            }

            var remainingText = next.Source is not null ? next.Source[next.Position.Absolute..] : string.Empty;

            if (PlaceholderRegex().Match(remainingText) is { Success: true } mPlaceholder)
            {
                var startPosition = next.Position;
                tokens.Add(new Token<ExpressionTokens>(ExpressionTokens.VARIABLE_REF, new TextSpan(source, startPosition, mPlaceholder.Length)));
                next = AdvanceTextSpan(next, mPlaceholder.Length);
                continue;
            }

            var baseWordMatch = NameRegex().Match(remainingText);
            if (baseWordMatch.Success && ExplicitKeywords.TryGetValue(baseWordMatch.Value, out var targetKeywordToken))
            {
                var startPosition = next.Position;
                tokens.Add(new Token<ExpressionTokens>(targetKeywordToken, new TextSpan(source, startPosition, baseWordMatch.Length)));
                next = AdvanceTextSpan(next, baseWordMatch.Length);
                continue;
            }

            if (AttributesRegex().Match(remainingText) is { Success: true } mAttr && mAttr.Length > 0)
            {
                var startPosition = next.Position;
                tokens.Add(new Token<ExpressionTokens>(ExpressionTokens.VARIABLE_REF, new TextSpan(source, startPosition, mAttr.Length)));
                next = AdvanceTextSpan(next, mAttr.Length);
                continue;
            }

            if (SkillsRegex().Match(remainingText) is { Success: true } mSkill && mSkill.Length > 0)
            {
                var startPosition = next.Position;
                tokens.Add(new Token<ExpressionTokens>(ExpressionTokens.VARIABLE_REF, new TextSpan(source, startPosition, mSkill.Length)));
                next = AdvanceTextSpan(next, mSkill.Length);
                continue;
            }

            if (VariableDataRegex().Match(remainingText) is { Success: true } mVarData && mVarData.Length > 0)
            {
                var startPosition = next.Position;
                tokens.Add(new Token<ExpressionTokens>(ExpressionTokens.VARIABLE_REF, new TextSpan(source, startPosition, mVarData.Length)));
                next = AdvanceTextSpan(next, mVarData.Length);
                continue;
            }

            // CRITICAL REORDERING FIX: Match DICE and FLOATS explicitly before evaluating flat integers
            if (DiceRollRegex().Match(remainingText) is { Success: true } mDice)
            {
                var startPosition = next.Position;
                tokens.Add(new Token<ExpressionTokens>(ExpressionTokens.DICE_ROLL, new TextSpan(source, startPosition, mDice.Length)));
                next = AdvanceTextSpan(next, mDice.Length);
                continue;
            }

            if (FloatRegex().Match(remainingText) is { Success: true } mFloat)
            {
                var startPosition = next.Position;
                tokens.Add(new Token<ExpressionTokens>(ExpressionTokens.INTEGER, new TextSpan(source, startPosition, mFloat.Length)));
                next = AdvanceTextSpan(next, mFloat.Length);
                continue;
            }

            if (IntegerRegex().Match(remainingText) is { Success: true } mInt)
            {
                var startPosition = next.Position;
                tokens.Add(new Token<ExpressionTokens>(ExpressionTokens.INTEGER, new TextSpan(source, startPosition, mInt.Length)));
                next = AdvanceTextSpan(next, mInt.Length);
                continue;
            }

            if (baseWordMatch.Success)
            {
                var startPosition = next.Position;
                tokens.Add(new Token<ExpressionTokens>(ExpressionTokens.NAME, new TextSpan(source, startPosition, baseWordMatch.Length)));
                next = AdvanceTextSpan(next, baseWordMatch.Length);
                continue;
            }

            var fallbackBadPos = next.Position;
            tokens.Add(new Token<ExpressionTokens>(ExpressionTokens.BAD, new TextSpan(source, fallbackBadPos, 1)));
            next = AdvanceTextSpan(next, 1);
        }

        return new TokenList<ExpressionTokens>([.. tokens]);
    }

    private static TextSpan AdvanceTextSpan(TextSpan current, int offset)
    {
        if (offset <= 0) return current;
        var absoluteIndex = current.Position.Absolute + offset;

        int linesCount = current.Position.Line;
        int columnsCount = current.Position.Column;

        for (int i = current.Position.Absolute; i < absoluteIndex && i < current.Source?.Length; i++)
        {
            var charAt = current.Source[i];
            if (charAt == '\n')
            {
                linesCount++;
                columnsCount = 1;
            }
            else if (charAt != '\r')
            {
                columnsCount++;
            }
        }

        return new TextSpan(current.Source!, new Position(absoluteIndex, linesCount, columnsCount), current.Source!.Length - absoluteIndex);
    }
}
using Superpower.Model;
using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace LSLibStats.Stats.Functor;

public enum FunctorTokens
{
    BAD = 0,
    EXPR_FUNCTORS,
    EXPR_DESCRIPTION_PARAMS,
    IF,
    CONTEXT,
    NAME,
    INTEGER,
    TEXT,
    DICE_ROLL,
    COLON = ':',
    LEFT_PAREN = '(',
    RIGHT_PAREN = ')',
    LEFT_BRACKET = '[',
    RIGHT_BRACKET = ']',
    COMMA = ',',
    SEMICOLON = ';',
    EXCLAMATION = '!',
    MINUS = '-',
    DOT = '.'
}

public static partial class FunctorTokenizer
{
    private static readonly FrozenDictionary<string, FunctorTokens> ExactKeywords = new Dictionary<string, FunctorTokens>(StringComparer.Ordinal)
    {
        ["__TYPE_Functors__"] = FunctorTokens.EXPR_FUNCTORS,
        ["__TYPE_DescriptionParams__"] = FunctorTokens.EXPR_DESCRIPTION_PARAMS,
        ["IF"] = FunctorTokens.IF,

        ["ABILITY_CHECK"] = FunctorTokens.CONTEXT,
        ["ACTION_RESOURCES_CHANGED"] = FunctorTokens.CONTEXT,
        ["AI_IGNORE"] = FunctorTokens.CONTEXT,
        ["AI_ONLY"] = FunctorTokens.CONTEXT,
        ["AOE"] = FunctorTokens.CONTEXT,
        ["ATTACK"] = FunctorTokens.CONTEXT,
        ["ATTACKED"] = FunctorTokens.CONTEXT,
        ["ATTACKED_IN_MELEE_RANGE"] = FunctorTokens.CONTEXT,
        ["ATTACKING_IN_MELEE_RANGE"] = FunctorTokens.CONTEXT,
        ["CAST"] = FunctorTokens.CONTEXT,
        ["CAST_RESOLVED"] = FunctorTokens.CONTEXT,
        ["COMBAT_ENDED"] = FunctorTokens.CONTEXT,
        ["CREATE_2"] = FunctorTokens.CONTEXT,
        ["DAMAGE"] = FunctorTokens.CONTEXT,
        ["DAMAGED"] = FunctorTokens.CONTEXT,
        ["DAMAGE_PREVENTED"] = FunctorTokens.CONTEXT,
        ["DAMAGED_PREVENTED"] = FunctorTokens.CONTEXT,
        ["ENTER_ATTACK_RANGE"] = FunctorTokens.CONTEXT,
        ["EQUIP"] = FunctorTokens.CONTEXT,
        ["LOCKPICKING_SUCCEEDED"] = FunctorTokens.CONTEXT,
        ["GROUND"] = FunctorTokens.CONTEXT,
        ["HEAL"] = FunctorTokens.CONTEXT,
        ["HEALED"] = FunctorTokens.CONTEXT,
        ["INTERRUPT_USED"] = FunctorTokens.CONTEXT,
        ["INVENTORY_CHANGED"] = FunctorTokens.CONTEXT,
        ["LEAVE_ATTACK_RANGE"] = FunctorTokens.CONTEXT,
        ["LONG_REST"] = FunctorTokens.CONTEXT,
        ["MOVED_DISTANCE"] = FunctorTokens.CONTEXT,
        ["OBSCURITY_CHANGED"] = FunctorTokens.CONTEXT,
        ["PROFICIENCY_CHANGED"] = FunctorTokens.CONTEXT,
        ["PROJECTILE"] = FunctorTokens.CONTEXT,
        ["PUSH"] = FunctorTokens.CONTEXT,
        ["PUSHED"] = FunctorTokens.CONTEXT,
        ["SELF"] = FunctorTokens.CONTEXT,
        ["SHORT_REST"] = FunctorTokens.CONTEXT,
        ["STATUS_APPLIED"] = FunctorTokens.CONTEXT,
        ["STATUS_APPLY"] = FunctorTokens.CONTEXT,
        ["STATUS_REMOVE"] = FunctorTokens.CONTEXT,
        ["STATUS_REMOVED"] = FunctorTokens.CONTEXT,
        ["SURFACE_ENTER"] = FunctorTokens.CONTEXT,
        ["TARGET"] = FunctorTokens.CONTEXT,
        ["TURN"] = FunctorTokens.CONTEXT
    }.ToFrozenDictionary(StringComparer.Ordinal);

    [GeneratedRegex(@"^[0-9]+d[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex DiceRollRegex();

    [GeneratedRegex(@"^-?[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex IntegerRegex();

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex(@"^[^,;:()\[\]!\s]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonSeparatorRegex();

    public static TokenList<FunctorTokens> Tokenize(string source)
    {
        if (string.IsNullOrEmpty(source)) return TokenList<FunctorTokens>.Empty;

        List<Token<FunctorTokens>> tokens = [];
        var next = new TextSpan(source);

        while (!next.IsAtEnd)
        {
            var ch = (next.Source is not null && next.Position.Absolute < next.Source.Length)
                ? next.Source[next.Position.Absolute]
                : '\0';

            if (char.IsWhiteSpace(ch) || ch is '\t' or '\r' or '\n')
            {
                next = AdvanceTextSpan(next, 1);
                continue;
            }

            if (ch is ':' or '(' or ')' or '[' or ']' or ',' or ';' or '!' or '-' or '.')
            {
                var startPosition = next.Position;
                tokens.Add(new Token<FunctorTokens>((FunctorTokens)ch, new TextSpan(source, startPosition, 1)));
                next = AdvanceTextSpan(next, 1);
                continue;
            }

            var remainingText = next.Source is not null ? next.Source[next.Position.Absolute..] : string.Empty;

            var baseIdentifierMatch = IdentifierRegex().Match(remainingText);
            if (baseIdentifierMatch.Success && ExactKeywords.TryGetValue(baseIdentifierMatch.Value, out var matchedTokenKind))
            {
                var startPosition = next.Position;
                tokens.Add(new Token<FunctorTokens>(matchedTokenKind, new TextSpan(source, startPosition, baseIdentifierMatch.Length)));
                next = AdvanceTextSpan(next, baseIdentifierMatch.Length);
                continue;
            }

            if (DiceRollRegex().Match(remainingText) is { Success: true } mDice)
            {
                var startPosition = next.Position;
                tokens.Add(new Token<FunctorTokens>(FunctorTokens.DICE_ROLL, new TextSpan(source, startPosition, mDice.Length)));
                next = AdvanceTextSpan(next, mDice.Length);
                continue;
            }

            if (IntegerRegex().Match(remainingText) is { Success: true } mInt)
            {
                var startPosition = next.Position;
                tokens.Add(new Token<FunctorTokens>(FunctorTokens.INTEGER, new TextSpan(source, startPosition, mInt.Length)));
                next = AdvanceTextSpan(next, mInt.Length);
                continue;
            }

            if (baseIdentifierMatch.Success)
            {
                var startPosition = next.Position;
                tokens.Add(new Token<FunctorTokens>(FunctorTokens.NAME, new TextSpan(source, startPosition, baseIdentifierMatch.Length)));
                next = AdvanceTextSpan(next, baseIdentifierMatch.Length);
                continue;
            }

            if (NonSeparatorRegex().Match(remainingText) is { Success: true } mNonSep && mNonSep.Length > 0)
            {
                var startPosition = next.Position;
                tokens.Add(new Token<FunctorTokens>(FunctorTokens.TEXT, new TextSpan(source, startPosition, mNonSep.Length)));
                next = AdvanceTextSpan(next, mNonSep.Length);
                continue;
            }

            var fallbackBadPos = next.Position;
            tokens.Add(new Token<FunctorTokens>(FunctorTokens.BAD, new TextSpan(source, fallbackBadPos, 1)));
            next = AdvanceTextSpan(next, 1);
        }

        return new TokenList<FunctorTokens>([.. tokens]);
    }

    private static TextSpan AdvanceTextSpan(TextSpan current, int count)
    {
        if (count <= 0) return current;
        var absoluteIndex = current.Position.Absolute + count;

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

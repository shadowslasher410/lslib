using LSLib.Parser;
using Superpower.Model;
using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LSLibStats.Stats;

public static partial class StatKeywordRegistry
{
    public static readonly FrozenDictionary<string, StatTokens> Keywords = new Dictionary<string, StatTokens>(StringComparer.Ordinal)
    {
        ["new"] = StatTokens.NEW,
        ["add"] = StatTokens.ADD,
        ["entry"] = StatTokens.ENTRY,
        ["type"] = StatTokens.TYPE,
        ["data"] = StatTokens.DATA,
        ["param"] = StatTokens.PARAM,
        ["using"] = StatTokens.USING,
        ["key"] = StatTokens.KEY,
        ["ability"] = StatTokens.ABILITY,
        ["itemcolor"] = StatTokens.ITEMCOLOR,
        ["namegroup"] = StatTokens.NAMEGROUP,
        ["namecool"] = StatTokens.NAMECOOL,
        ["name"] = StatTokens.NAME,
        ["itemgroup"] = StatTokens.ITEMGROUP,
        ["levelgroup"] = StatTokens.LEVELGROUP,
        ["rootgroup"] = StatTokens.ROOTGROUP,
        ["requirement"] = StatTokens.REQUIREMENT,
        ["deltamod"] = StatTokens.DELTAMOD,
        ["new boost"] = StatTokens.NEW_BOOST,
        ["equipment"] = StatTokens.EQUIPMENT,
        ["add equipmentgroup"] = StatTokens.ADD_EQUIPMENTGROUP,
        ["add equipment entry"] = StatTokens.ADD_EQUIPMENT_ENTRY,
        ["ItemCombination"] = StatTokens.ITEM_COMBINATION,
        ["ItemCombinationResult"] = StatTokens.ITEM_COMBINATION_RESULT,
        ["CraftingPreviewData"] = StatTokens.CRAFTING_PREVIEW_DATA,
        ["skillset"] = StatTokens.SKILLSET,
        ["skill"] = StatTokens.SKILL,
        ["CategoryMap"] = StatTokens.CATEGORY_MAP,
        ["WeaponCounter"] = StatTokens.WEAPON_COUNTER,
        ["SkillbookCounter"] = StatTokens.SKILLBOOK_COUNTER,
        ["ArmorCounter"] = StatTokens.ARMOR_COUNTER,
        ["treasure"] = StatTokens.TREASURE,
        ["itemtypes"] = StatTokens.ITEMTYPES,
        ["treasuretable"] = StatTokens.TREASURE_TABLE,
        ["new subtable"] = StatTokens.NEW_SUBTABLE,
        ["object category"] = StatTokens.OBJECT_CATEGORY,
        ["StartLevel"] = StatTokens.START_LEVEL,
        ["EndLevel"] = StatTokens.END_LEVEL,
        ["MinLevel"] = StatTokens.MIN_LEVEL,
        ["MaxLevel"] = StatTokens.MAX_LEVEL,
        ["CanMerge"] = StatTokens.CAN_MERGE,
        ["IgnoreLevelDiff"] = StatTokens.IGNORE_LEVEL_DIFF,
        ["UseTreasureGroupCounters"] = StatTokens.USE_TREASURE_GROUPS
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static bool IsPotentialKeywordSubstring(string text) =>
        text is "new" or "new " or "new b" or "new bo" or "new boo" or "new boos" or "new boost" or
                "add" or "add " or "add e" or "add eq" or "add equ" or "add equi" or "add equip" or "add equipm" or "add equipme" or "add equipmen" or "add equipment" or "add equipment " or "add equipmentg" or "add equipmentgr" or "add equipmentgro" or "add equipmentgrou" or "add equipmentgroup" or
                "add equipment e" or "add equipment en" or "add equipment ent" or "add equipment entr" or "add equipment entry" or
                "new s" or "new su" or "new sub" or "new subt" or "new subta" or "new subtab" or "new subtabl" or "new subtable" or
                "object" or "object " or "object c" or "object ca" or "object cat" or "object cate" or "object categ" or "object catego" or "object categor" or "object category";
}

public sealed partial class StatTokenizer(string fileName)
{
    [GeneratedRegex(@"^data\s+""([^""]+)""\s+""(.*)""", RegexOptions.CultureInvariant)]
    private static partial Regex DataPropertyRegex();

    private readonly string _fileName = fileName ?? string.Empty;
    private int _currentTokenIndex;
    private Token<StatTokens>[] _tokenCache = [];

    public Token<StatTokens>[] TokenCache => _tokenCache;

    public void SetSource(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var content = reader.ReadToEnd();
        _tokenCache = TokenizeContent(content);
        _currentTokenIndex = 0;
    }

    public CodeLocation LastLocation()
    {
        if (_currentTokenIndex == 0 || _tokenCache.Length == 0)
            return new CodeLocation(_fileName, 1, 1, 1, 1);

        var activeToken = _tokenCache[Math.Min(_currentTokenIndex - 1, _tokenCache.Length - 1)];
        return ComputeLocation(activeToken.Span);
    }

    [SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Preserves naming compatibility with original LSLib orchestration loops")]
    public int yylex(ref object yylval)
    {
        if (_currentTokenIndex >= _tokenCache.Length)
            return 0;

        var activeToken = _tokenCache[_currentTokenIndex++];
        var tokenStr = activeToken.Span.ToStringValue();

        yylval = activeToken.Kind switch
        {
            StatTokens.STRING => StatParserExtensions.MakeString(tokenStr),
            StatTokens.DATA_ITEM => ParseDataPropertyElement(activeToken.Span, tokenStr),
            _ => tokenStr
        };

        return (int)activeToken.Kind;
    }

    private StatProperty ParseDataPropertyElement(TextSpan span, string text)
    {
        var loc = ComputeLocation(span);
        return StatParserExtensions.MakeDataProperty(_fileName, loc.StartLine, loc.StartColumn, loc.EndLine, loc.EndColumn, text);
    }

    private static Token<StatTokens>[] TokenizeContent(string source)
    {
        List<Token<StatTokens>> tokens = [];
        var next = new TextSpan(source);

        while (!next.IsAtEnd)
        {
            var ch = (next.Source is not null && next.Position.Absolute < next.Source.Length)
                ? next.Source[next.Position.Absolute]
                : '\0';

            if (char.IsWhiteSpace(ch) || ch is '\t' or '\v' or '\r' or '\n' or '\f')
            {
                next = AdvanceTextSpanPosition(next, 1);
                continue;
            }

            if (ch == '/' && next.Position.Absolute + 1 < source.Length && source[next.Position.Absolute + 1] == '/')
            {
                while (!next.IsAtEnd && (next.Source is not null && next.Source[next.Position.Absolute] != '\n'))
                {
                    next = AdvanceTextSpanPosition(next, 1);
                }
                continue;
            }

            var remainingText = next.Source is not null ? next.Source[next.Position.Absolute..] : string.Empty;

            if (ch == 'd' && remainingText.StartsWith("data"))
            {
                var rowTextLine = FetchTextLineUntilEnd(next);
                var match = DataPropertyRegex().Match(rowTextLine);
                if (match.Success)
                {
                    var startPosition = next.Position;
                    var matchLength = match.Length;

                    tokens.Add(new Token<StatTokens>(StatTokens.DATA_ITEM, new TextSpan(source, startPosition, matchLength)));
                    next = AdvanceTextSpanPosition(next, matchLength);
                    continue;
                }
            }

            if (ch == '"' || (ch == 'L' && next.Position.Absolute + 1 < source.Length && source[next.Position.Absolute + 1] == '"'))
            {
                var startPosition = next.Position;
                var currentAbs = next.Position.Absolute;

                if (ch == 'L') currentAbs++;
                currentAbs++;

                while (currentAbs < source.Length && source[currentAbs] != '"')
                {
                    if (source[currentAbs] == '\\') currentAbs++;
                    currentAbs++;
                }

                if (currentAbs < source.Length) currentAbs++;

                var totalLen = currentAbs - startPosition.Absolute;
                tokens.Add(new Token<StatTokens>(StatTokens.STRING, new TextSpan(source, startPosition, totalLen)));
                next = AdvanceTextSpanPosition(next, totalLen);
                continue;
            }

            if (char.IsDigit(ch) || (ch == '-' && next.Position.Absolute + 1 < source.Length && char.IsDigit(source[next.Position.Absolute + 1])))
            {
                var startPosition = next.Position;
                var currentAbs = next.Position.Absolute;
                if (source[currentAbs] == '-') currentAbs++;
                while (currentAbs < source.Length && char.IsDigit(source[currentAbs]))
                {
                    currentAbs++;
                }
                var totalLen = currentAbs - startPosition.Absolute;
                tokens.Add(new Token<StatTokens>(StatTokens.INTEGER, new TextSpan(source, startPosition, totalLen)));
                next = AdvanceTextSpanPosition(next, totalLen);
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                var startPosition = next.Position;
                var currentAbs = next.Position.Absolute;
                while (currentAbs < source.Length && (char.IsLetterOrDigit(source[currentAbs]) || source[currentAbs] == '_' || source[currentAbs] == ' '))
                {
                    var innerSlice = source[startPosition.Absolute..(currentAbs + 1)];
                    if (!StatKeywordRegistry.IsPotentialKeywordSubstring(innerSlice) && source[currentAbs] == ' ')
                        break;

                    currentAbs++;
                }

                var totalScannedLength = currentAbs - startPosition.Absolute;
                var filteredText = source[startPosition.Absolute..currentAbs].TrimEnd();

                var resolvedTokenType = StatKeywordRegistry.Keywords.TryGetValue(filteredText, out var mappedToken) ? mappedToken : StatTokens.NAME;
                tokens.Add(new Token<StatTokens>(resolvedTokenType, new TextSpan(source, startPosition, filteredText.Length)));

                // CRITICAL FIX: Advance by full scanned size window to step cleanly over greedy trailing lookup spaces
                next = AdvanceTextSpanPosition(next, totalScannedLength);
                continue;
            }

            if (ch == ',')
            {
                var startPosition = next.Position;
                tokens.Add(new Token<StatTokens>((StatTokens)',', new TextSpan(source, startPosition, 1)));
                next = AdvanceTextSpanPosition(next, 1);
                continue;
            }

            var fallbackBadPos = next.Position;
            tokens.Add(new Token<StatTokens>(StatTokens.BAD, new TextSpan(source, fallbackBadPos, 1)));
            next = AdvanceTextSpanPosition(next, 1);
        }

        return [.. tokens];
    }

    private static string FetchTextLineUntilEnd(TextSpan span)
    {
        int totalLength = 0;
        var currentAbs = span.Position.Absolute;
        while (currentAbs < span.Source?.Length && span.Source[currentAbs] != '\n' && span.Source[currentAbs] != '\r')
        {
            totalLength++;
            currentAbs++;
        }
        return span.Source!.Substring(span.Position.Absolute, totalLength);
    }

    private static TextSpan AdvanceTextSpanPosition(TextSpan current, int count)
    {
        if (count <= 0) return current;

        var textSource = current.Source ?? string.Empty;
        var newAbs = current.Position.Absolute + count;

        int line = current.Position.Line;
        int col = current.Position.Column;

        for (int i = current.Position.Absolute; i < newAbs && i < textSource.Length; i++)
        {
            var charAt = textSource[i];
            if (charAt == '\n')
            {
                line++;
                col = 1;
            }
            else if (charAt != '\r')
            {
                col++;
            }
        }

        return new TextSpan(textSource, new Position(newAbs, line, col), textSource.Length - newAbs);
    }

    private CodeLocation ComputeLocation(TextSpan span)
    {
        if (span.Source is null)
            return new CodeLocation(_fileName, span.Position.Line, span.Position.Column, span.Position.Line, span.Position.Column + span.Length);

        int endLine = span.Position.Line;
        int endCol = span.Position.Column;
        string tokenText = span.ToStringValue();

        for (int i = 0; i < tokenText.Length; i++)
        {
            if (tokenText[i] == '\n')
            {
                endLine++;
                endCol = 1;
            }
            else if (tokenText[i] != '\r')
            {
                endCol++;
            }
        }

        return new CodeLocation(_fileName, span.Position.Line, span.Position.Column, endLine, endCol);
    }
}

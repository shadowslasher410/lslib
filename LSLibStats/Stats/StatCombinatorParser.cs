using LSLib.Parser;
using Superpower;
using Superpower.Parsers;

namespace LSLibStats.Stats;

public static class StatCombinatorParser
{
    private static readonly TokenListParser<StatTokens, string> NameParser =
        Token.EqualTo(StatTokens.NAME).Select(t => t.ToStringValue());

    private static readonly TokenListParser<StatTokens, string> StringParser =
        Token.EqualTo(StatTokens.STRING).Select(t => StatParserExtensions.MakeString(t.ToStringValue()));

    private static readonly TokenListParser<StatTokens, string> IntegerParser =
        Token.EqualTo(StatTokens.INTEGER).Select(t => t.ToStringValue());

    private static readonly TokenListParser<StatTokens, string> CommaParser =
        Token.EqualTo((StatTokens)',').Select(_ => ",");

    private static readonly TokenListParser<StatTokens, StatProperty> EntryTypeParser =
        from kw in Token.EqualTo(StatTokens.TYPE) from val in StringParser select new StatProperty("EntityType", val);

    private static readonly TokenListParser<StatTokens, StatProperty> EntryUsingParser =
        from kw in Token.EqualTo(StatTokens.USING) from val in StringParser select new StatProperty("Using", val);

    private static readonly TokenListParser<StatTokens, StatProperty> EntryDataParser =
        from kw in Token.EqualTo(StatTokens.DATA) from key in StringParser from val in StringParser select new StatProperty(key, val);

    private static readonly TokenListParser<StatTokens, StatProperty> EntryParamParser =
        from kw in Token.EqualTo(StatTokens.PARAM) from key in StringParser from val in StringParser select new StatProperty(key, val);

    private static readonly TokenListParser<StatTokens, StatProperty> DataItemParser =
        Token.EqualTo(StatTokens.DATA_ITEM).Select(token => StatParserExtensions.MakeDataProperty(string.Empty, 1, 1, 1, 1, token.Span.ToStringValue()));

    private static readonly TokenListParser<StatTokens, object> PropertyAtomParser =
        EntryTypeParser.Cast<StatTokens, StatProperty, object>()
        .Or(EntryUsingParser.Cast<StatTokens, StatProperty, object>())
        .Or(EntryDataParser.Cast<StatTokens, StatProperty, object>())
        .Or(EntryParamParser.Cast<StatTokens, StatProperty, object>())
        .Or(DataItemParser.Cast<StatTokens, StatProperty, object>())
        .Or(ParseRef(() => ExtendedPropertiesParser!));

    private static readonly TokenListParser<StatTokens, StatDeclaration> EntryStdHeader =
        from kwNew in Token.EqualTo(StatTokens.NEW)
        from kwUser in Token.EqualTo(StatTokens.ENTRY)
        from name in StringParser
        select StatParserExtensions.MakeDeclaration([new StatProperty("Name", name)]);

    private static readonly TokenListParser<StatTokens, StatDeclaration> DataKeyHeader =
        from kw in Token.EqualTo(StatTokens.KEY)
        from key in StringParser
        from c in CommaParser
        from val in StringParser
        select StatParserExtensions.MakeDeclaration([new StatProperty("Key", key), new StatProperty("EntityType", "Data"), new StatProperty("Value", val)]);

    private static readonly TokenListParser<StatTokens, StatDeclaration> AbilityHeader =
        from kw in Token.EqualTo(StatTokens.ABILITY)
        from name in NameParser
        from discard in Token.EqualTo((StatTokens)',').Then(_ => Token.EqualTo(StatTokens.INTEGER)).Many()
        select StatParserExtensions.MakeDeclaration([new StatProperty("Name", name), new StatProperty("EntityType", "Ability")]);

    private static readonly TokenListParser<StatTokens, StatDeclaration> RequirementsHeader =
        from kw in Token.EqualTo(StatTokens.REQUIREMENT)
        from name in StringParser
        from c in CommaParser
        from reqs in StringParser
        select StatParserExtensions.MakeDeclaration([new StatProperty("Name", name), new StatProperty("EntityType", "Requirement"), new StatProperty("Requirements", reqs)]);

    private static readonly TokenListParser<StatTokens, StatDeclaration> DeltaModHeader =
        from kwNew in Token.EqualTo(StatTokens.NEW)
        from kwDelta in Token.EqualTo(StatTokens.DELTAMOD)
        from name in StringParser
        select StatParserExtensions.MakeDeclaration([new StatProperty("Name", name), new StatProperty("EntityType", "DeltaModifier")]);

    private static readonly TokenListParser<StatTokens, StatDeclaration> ItemCombinationHeader =
        from kwNew in Token.EqualTo(StatTokens.NEW)
        from kwComb in Token.EqualTo(StatTokens.ITEM_COMBINATION)
        from name in StringParser
        select StatParserExtensions.MakeDeclaration([new StatProperty("Name", name), new StatProperty("EntityType", "ItemCombination")]);

    private static readonly TokenListParser<StatTokens, StatDeclaration> ItemCombinationResultHeader =
        from kwNew in Token.EqualTo(StatTokens.NEW)
        from kwRes in Token.EqualTo(StatTokens.ITEM_COMBINATION_RESULT)
        from name in StringParser
        select StatParserExtensions.MakeDeclaration([new StatProperty("Name", name), new StatProperty("EntityType", "ItemCombinationResult")]);

    private static readonly TokenListParser<StatTokens, StatDeclaration> ItemColorHeader =
        from kwNew in Token.EqualTo(StatTokens.NEW)
        from kwColor in Token.EqualTo(StatTokens.ITEMCOLOR)
        from name in StringParser
        from c1 in CommaParser
        from p in StringParser
        from c2 in CommaParser
        from s in StringParser
        from c3 in CommaParser
        from t in StringParser
        select StatParserExtensions.MakeDeclaration([new StatProperty("ItemColorName", name), new StatProperty("EntityType", "ItemColor"), new StatProperty("Primary Color", p), new StatProperty("Secondary Color", s), new StatProperty("Tertiary Color", t)]);

    private static readonly TokenListParser<StatTokens, StatDeclaration> ItemProgressionNamesHeader =
        from kwNew in Token.EqualTo(StatTokens.NEW)
        from kwGroup in Token.EqualTo(StatTokens.NAMEGROUP)
        from name in StringParser
        select StatParserExtensions.MakeDeclaration([new StatProperty("Name", name), new StatProperty("EntityType", "ItemProgressionNames")]);

    private static readonly TokenListParser<StatTokens, StatDeclaration> ItemProgressionVisualsHeader =
        from kwNew in Token.EqualTo(StatTokens.NEW)
        from kwGroup in Token.EqualTo(StatTokens.ITEMGROUP)
        from name in StringParser
        select StatParserExtensions.MakeDeclaration([new StatProperty("Name", name), new StatProperty("EntityType", "ItemProgressionVisuals")]);

    private static readonly TokenListParser<StatTokens, StatDeclaration> EquipmentHeader =
        from kwNew in Token.EqualTo(StatTokens.NEW)
        from kwEquip in Token.EqualTo(StatTokens.EQUIPMENT)
        from name in StringParser
        select StatParserExtensions.MakeDeclaration([new StatProperty("Name", name), new StatProperty("EntityType", "Equipment")]);

    private static readonly TokenListParser<StatTokens, StatDeclaration> ItemComboPropertyHeader =
        from kwNew in Token.EqualTo(StatTokens.NEW)
        from kwProp in Token.EqualTo(StatTokens.ITEMCOMBOPROPERTY)
        from name in StringParser
        select StatParserExtensions.MakeDeclaration([new StatProperty("Name", name), new StatProperty("EntityType", "ItemComboProperties")]);

    private static readonly TokenListParser<StatTokens, StatDeclaration> ObjectCategoryItemComboPreviewDataHeader =
        from kwNew in Token.EqualTo(StatTokens.NEW)
        from kwCraft in Token.EqualTo(StatTokens.CRAFTING_PREVIEW_DATA)
        from name in StringParser
        select StatParserExtensions.MakeDeclaration([new StatProperty("Name", name)]);

    private static readonly TokenListParser<StatTokens, StatDeclaration> SkillSetHeader =
        from kwNew in Token.EqualTo(StatTokens.NEW)
        from kwSet in Token.EqualTo(StatTokens.SKILLSET)
        from name in StringParser
        select StatParserExtensions.MakeDeclaration([new StatProperty("Name", name), new StatProperty("EntityType", "SkillSet")]);

    private static readonly TokenListParser<StatTokens, StatDeclaration> TreasureGroupHeader =
        from kwMap in Token.EqualTo(StatTokens.CATEGORY_MAP)
        from name in StringParser
        from c in CommaParser
        from groupName in StringParser
        select StatParserExtensions.MakeDeclaration([new StatProperty("Name", name), new StatProperty("TreasureGroup", groupName), new StatProperty("EntityType", "TreasureGroups")]);

    private static readonly TokenListParser<StatTokens, StatDeclaration> TreasureTableHeader =
        from kwNew in Token.EqualTo(StatTokens.NEW)
        from kwTable in Token.EqualTo(StatTokens.TREASURE_TABLE)
        from name in StringParser
        select StatParserExtensions.MakeDeclaration([new StatProperty("Name", name), new StatProperty("EntityType", "TreasureTable")]);

    private static readonly TokenListParser<StatTokens, StatDeclaration> EntryHeaderParser =
        DataKeyHeader.Or(AbilityHeader).Or(RequirementsHeader).Or(DeltaModHeader)
        .Or(ItemCombinationHeader).Or(ItemCombinationResultHeader).Or(ItemColorHeader)
        .Or(ItemProgressionNamesHeader).Or(ItemProgressionVisualsHeader).Or(EquipmentHeader)
        .Or(ItemComboPropertyHeader).Or(ObjectCategoryItemComboPreviewDataHeader)
        .Or(SkillSetHeader).Or(TreasureGroupHeader).Or(TreasureTableHeader).Or(EntryStdHeader);

    private static readonly TokenListParser<StatTokens, object> ItemProgressionNameParser =
        from add in Token.EqualTo(StatTokens.ADD)
        from kw in Token.EqualTo(StatTokens.NAME)
        from name in StringParser
        from c in CommaParser
        from desc in StringParser
        select (object)StatParserExtensions.MakeElement("Names", new Dictionary<string, object> { ["Name"] = name, ["Description"] = desc });

    private static readonly TokenListParser<StatTokens, object> ItemProgressionNameCoolParser =
        from add in Token.EqualTo(StatTokens.ADD)
        from kw in Token.EqualTo(StatTokens.NAMECOOL)
        from name in StringParser
        from c in CommaParser
        from desc in StringParser
        select (object)StatParserExtensions.MakeElement("NamesCool", new Dictionary<string, object> { ["Name"] = name, ["Description"] = desc });

    private static readonly TokenListParser<StatTokens, object> ItemProgressionVisualLevelParser =
        from add in Token.EqualTo(StatTokens.ADD)
        from kw in Token.EqualTo(StatTokens.LEVELGROUP)
        from min in IntegerParser
        from c1 in CommaParser
        from max in IntegerParser
        from c2 in CommaParser
        from rarity in StringParser
        select (object)StatParserExtensions.MakeElement("LevelGroups", new Dictionary<string, object> { ["MinLevel"] = min, ["MaxLevel"] = max, ["Rarity"] = rarity });

    private static readonly TokenListParser<StatTokens, object> ItemProgressionVisualNameParser =
        from add in Token.EqualTo(StatTokens.ADD)
        from kw in Token.EqualTo(StatTokens.ROOTGROUP)
        from root in StringParser
        from c in CommaParser
        from color in StringParser
        select (object)StatParserExtensions.MakeElement("NameGroups", new Dictionary<string, object> { ["RootTemplate"] = root, ["ItemColor"] = color });

    private static readonly TokenListParser<StatTokens, object> ItemProgressionVisualRootParser =
        from add in Token.EqualTo(StatTokens.ADD)
        from kw in Token.EqualTo(StatTokens.NAMEGROUP)
        from name in StringParser
        from c1 in CommaParser
        from affix in StringParser
        from c2 in CommaParser
        from icon in StringParser
        select (object)StatParserExtensions.MakeElement("RootGroups", new Dictionary<string, object> { ["NameGroup"] = name, ["AffixType"] = affix, ["Icon"] = icon });

    private static readonly TokenListParser<StatTokens, object> DeltaModifierBoostParser =
        from kw in Token.EqualTo(StatTokens.NEW_BOOST)
        from boost in StringParser
        from c in CommaParser
        from mult in IntegerParser
        select (object)StatParserExtensions.MakeElement("Boosts", new Dictionary<string, object> { ["Boost"] = boost, ["Multiplier"] = mult });

    private static readonly TokenListParser<StatTokens, object> EquipmentGroupParser =
        from kw in Token.EqualTo(StatTokens.ADD_EQUIPMENTGROUP)
        from entries in Token.EqualTo(StatTokens.ADD_EQUIPMENT_ENTRY).Then(_ => StringParser).Many()
        select (object)StatParserExtensions.MakeElement("EquipmentGroups", entries.ToList());

    private static readonly TokenListParser<StatTokens, object> ItemComboPropertyEntryParser =
        from kw in Token.EqualTo(StatTokens.NEW_ITEMCOMBOPROPERTYENTRY)
        from subProps in EntryDataParser.Many()
        select (object)StatParserExtensions.MakeElement("Entries", subProps.ToList());

    private static readonly TokenListParser<StatTokens, object> SkillSetSkillParser =
        from add in Token.EqualTo(StatTokens.ADD)
        from kw in Token.EqualTo(StatTokens.SKILL)
        from name in StringParser
        select (object)StatParserExtensions.MakeElement("NameGroups", name);

    private static readonly TokenListParser<StatTokens, object> TreasureGroupCounterParser =
        (from kw in Token.EqualTo(StatTokens.WEAPON_COUNTER)
         from k in StringParser
         from c in CommaParser
         from v in StringParser
         select (object)StatParserExtensions.MakeDeclaration([new StatProperty("WeaponTreasureGroup", k), new StatProperty("WeaponDefaultCounter", v)]))
        .Or(from kw in Token.EqualTo(StatTokens.SKILLBOOK_COUNTER)
            from k in StringParser
            from c in CommaParser
            from v in StringParser
            select (object)StatParserExtensions.MakeDeclaration([new StatProperty("SkillbookTreasureGroup", k), new StatProperty("SkillbookDefaultCounter", v)]))
        .Or(from kw in Token.EqualTo(StatTokens.ARMOR_COUNTER)
            from k in StringParser
            from c in CommaParser
            from v in StringParser
            select (object)StatParserExtensions.MakeDeclaration([new StatProperty("ArmorTreasureGroup", k), new StatProperty("ArmorDefaultCounter", v)]));

    private static readonly TokenListParser<StatTokens, StatProperty> TreasureTableObjectParser =
        from kw in Token.EqualTo(StatTokens.OBJECT_CATEGORY)
        from drop in StringParser
        from c1 in CommaParser
        from freq in IntegerParser
        from c2 in CommaParser
        from com in IntegerParser
        from c3 in CommaParser
        from unc in IntegerParser
        from c4 in CommaParser
        from rare in IntegerParser
        from c5 in CommaParser
        from epic in IntegerParser
        from c6 in CommaParser
        from leg in IntegerParser
        from c7 in CommaParser
        from div in IntegerParser
        from c8 in CommaParser
        from uni in IntegerParser
        select new StatProperty("Objects", StatParserExtensions.MakeElement("Objects", StatParserExtensions.MakeDeclaration([
            new StatProperty("Drop", drop), new StatProperty("Frequency", freq), new StatProperty("Common", com),
            new StatProperty("Uncommon", unc), new StatProperty("Rare", rare), new StatProperty("Epic", epic),
            new StatProperty("Legendary", leg), new StatProperty("Divine", div), new StatProperty("Unique", uni)
        ])));

    private static readonly TokenListParser<StatTokens, StatProperty> TreasureTableEntryParser =
        TreasureTableObjectParser
        .Or(from kw in Token.EqualTo(StatTokens.START_LEVEL) from v in StringParser select new StatProperty("StartLevel", v))
        .Or(from kw in Token.EqualTo(StatTokens.END_LEVEL) from v in StringParser select new StatProperty("EndLevel", v));

    private static readonly TokenListParser<StatTokens, object> TreasureSubtableParser =
        from kw in Token.EqualTo(StatTokens.NEW_SUBTABLE)
        from dropCount in StringParser
        from entries in TreasureTableEntryParser.Many()
        select (object)StatParserExtensions.MakeElement("Subtables", StatParserExtensions.AddProperty(
            StatParserExtensions.MakeDeclaration([new StatProperty("DropCount", dropCount)]), entries.ToList()));

    private static readonly TokenListParser<StatTokens, object> TreasureTableMetaParser =
        (from kw in Token.EqualTo(StatTokens.MIN_LEVEL) from v in StringParser select (object)new StatProperty("MinLevel", v))
        .Or(from kw in Token.EqualTo(StatTokens.MAX_LEVEL) from v in StringParser select (object)new StatProperty("MaxLevel", v))
        .Or(from kw in Token.EqualTo(StatTokens.CAN_MERGE) from v in StringParser.Or(IntegerParser) select (object)new StatProperty("CanMerge", v))
        .Or(from kw in Token.EqualTo(StatTokens.IGNORE_LEVEL_DIFF) from v in IntegerParser select (object)new StatProperty("IgnoreLevelDiff", v))
        .Or(from kw in Token.EqualTo(StatTokens.USE_TREASURE_GROUPS) from v in IntegerParser select (object)new StatProperty("UseTreasureGroupCounters", v));

    private static readonly TokenListParser<StatTokens, object> ExtendedPropertiesParser =
        ItemProgressionNameParser.Or(ItemProgressionNameCoolParser).Or(ItemProgressionVisualLevelParser)
        .Or(ItemProgressionVisualNameParser).Or(ItemProgressionVisualRootParser).Or(DeltaModifierBoostParser)
        .Or(EquipmentGroupParser).Or(ItemComboPropertyEntryParser).Or(SkillSetSkillParser)
        .Or(TreasureGroupCounterParser).Or(TreasureSubtableParser).Or(TreasureTableMetaParser);

    private static readonly TokenListParser<StatTokens, StatDeclaration> CompleteDeclarationParser =
        from header in EntryHeaderParser from properties in PropertyAtomParser.Many() select AssembleDeclaration(header, properties);

    private static readonly TokenListParser<StatTokens, object[]?> GlobalIgnoreParser =
        (from kw in Token.EqualTo(StatTokens.TREASURE)
         from items in Token.EqualTo(StatTokens.ITEMTYPES)
         from dropped in Token.Sequence(StatTokens.STRING, (StatTokens)',').Many()
         from last in Token.EqualTo(StatTokens.STRING)
         select Array.Empty<object>()).OptionalOrDefault([]);

    private static StatDeclaration AssembleDeclaration(StatDeclaration decl, object[] properties)
    {
        foreach (var property in properties) StatParserExtensions.AddProperty(decl, property);
        return decl;
    }

    private static TokenListParser<TToken, TResult> ParseRef<TToken, TResult>(Func<TokenListParser<TToken, TResult>> factory)
    {
        TokenListParser<TToken, TResult>? cache = null;
        return stream => (cache ??= factory())(stream);
    }

    public static List<StatDeclaration>? ParseFile(string source, string fileName, StatLoadingContext _)
    {
        var scanner = new StatTokenizer(fileName);

        using (var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(source)))
        {
            scanner.SetSource(ms);
        }

        var tokenCacheArray = scanner.TokenCache;

        if (tokenCacheArray.Any(t => t.Kind == StatTokens.BAD))
        {
            StatLoadingContext.LogError(
                DiagnosticCode.StatSyntaxError.ToString(),
                "Lexical syntax failure: Unmapped characters inside statistics text stream.",
                new CodeLocation(fileName, 1, 1, 1, 1),
                null
            );
            return null;
        }

        var cleanTokens = tokenCacheArray.Where(t => t.Kind != StatTokens.BAD).ToArray();
        var tokenStream = new Superpower.Model.TokenList<StatTokens>(cleanTokens);

        var ignoreCheck = GlobalIgnoreParser.TryParse(tokenStream);
        var remainderStream = ignoreCheck.HasValue ? ignoreCheck.Remainder : tokenStream;

        var result = CompleteDeclarationParser.Many().AtEnd().TryParse(remainderStream);
        if (!result.HasValue)
        {
            StatLoadingContext.LogError(
                DiagnosticCode.StatSyntaxError.ToString(),
                $"Combinator mapping exception details: {result.FormatErrorMessageFragment()}",
                new CodeLocation(fileName, 1, 1, 1, 1),
                null
            );
            return null;
        }

        return [.. result.Value];
    }
}

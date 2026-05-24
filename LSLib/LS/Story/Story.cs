namespace LSLib.LS.Story;

public sealed class Story
{
    public byte MinorVersion { get; set; }
    public byte MajorVersion { get; set; }
    public bool ShortTypeIds { get; set; }
    public SaveFileHeader Header { get; set; } = null!;
    public Dictionary<uint, OsirisEnum> Enums { get; set; } = [];
    public Dictionary<uint, OsirisType> Types { get; set; } = [];
    public List<OsirisDivObject> DivObjects { get; set; } = [];
    public List<Function> Functions { get; set; } = [];
    public Dictionary<uint, Node> Nodes { get; set; } = [];
    public Dictionary<uint, Adapter> Adapters { get; set; } = [];
    public Dictionary<uint, Database> Databases { get; set; } = [];
    public Dictionary<uint, Goal> Goals { get; set; } = [];
    public List<Call> GlobalActions { get; set; } = [];
    public List<string> ExternalStringTable { get; set; } = [];
    public Dictionary<string, Function> FunctionSignatureMap { get; set; } = new(StringComparer.Ordinal);

    public uint Version => ((uint)MajorVersion << 8) | MinorVersion;

    public void DebugDump(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteLine(" --- ENUMS ---");
        foreach (KeyValuePair<uint, OsirisEnum> e in Enums)
        {
            e.Value.DebugDump(writer);
        }

        writer.WriteLine(" --- TYPES ---");
        foreach (KeyValuePair<uint, OsirisType> type in Types)
        {
            type.Value.DebugDump(writer);
        }

        writer.WriteLine();
        writer.WriteLine(" --- DIV OBJECTS ---");
        foreach (OsirisDivObject obj in DivObjects)
        {
            obj.DebugDump(writer);
        }

        writer.WriteLine();
        writer.WriteLine(" --- FUNCTIONS ---");
        foreach (Function function in Functions)
        {
            function.DebugDump(writer, this);
        }

        writer.WriteLine();
        writer.WriteLine(" --- NODES ---");
        foreach (KeyValuePair<uint, Node> node in Nodes)
        {
            writer.Write("#{0} ", node.Key);
            node.Value.DebugDump(writer, this);
            writer.WriteLine();
        }

        writer.WriteLine();
        writer.WriteLine(" --- ADAPTERS ---");
        foreach (KeyValuePair<uint, Adapter> adapter in Adapters)
        {
            writer.Write("#{0} ", adapter.Key);
            adapter.Value.DebugDump(writer, this);
        }

        writer.WriteLine();
        writer.WriteLine(" --- DATABASES ---");
        foreach (KeyValuePair<uint, Database> database in Databases)
        {
            writer.Write("#{0} ", database.Key);
            database.Value.DebugDump(writer, this);
        }

        writer.WriteLine();
        writer.WriteLine(" --- GOALS ---");
        foreach (KeyValuePair<uint, Goal> goal in Goals)
        {
            writer.Write("#{0} ", goal.Key);
            goal.Value.DebugDump(writer, this);
            writer.WriteLine();
        }

        writer.WriteLine();
        writer.WriteLine(" --- GLOBAL ACTIONS ---");
        foreach (Call call in GlobalActions)
        {
            call.DebugDump(writer, this);
            writer.WriteLine();
        }
    }

    public uint FindBuiltinTypeId(uint typeId)
    {
        uint aliasId = typeId;

        while (typeId != 0 && Types.TryGetValue(aliasId, out OsirisType? type) && type.Alias != 0)
        {
            aliasId = type.Alias;
        }

        return aliasId;
    }
}

public sealed class StoryReader
{
    public StoryReader() { }

    private static List<string> ReadStrings(OsiReader reader)
    {
        uint count = reader.ReadUInt32();
        var stringTable = new List<string>((int)count);
        while (count-- > 0)
        {
            stringTable.Add(reader.ReadString() ?? string.Empty);
        }

        return stringTable;
    }

    private static Dictionary<uint, OsirisType> ReadTypes(OsiReader reader)
    {
        uint count = reader.ReadUInt32();
        var types = new Dictionary<uint, OsirisType>((int)count);
        while (count-- > 0)
        {
            var type = new OsirisType();
            type.Read(reader);
            types.Add(type.Index, type);
        }

        return types;
    }

    private static Dictionary<uint, OsirisEnum> ReadEnums(OsiReader reader)
    {
        uint count = reader.ReadUInt32();
        var enums = new Dictionary<uint, OsirisEnum>((int)count);
        while (count-- > 0)
        {
            var e = new OsirisEnum();
            e.Read(reader);
            enums.Add(e.UnderlyingType, e);
        }

        return enums;
    }

    private static Dictionary<uint, Node> ReadNodes(OsiReader reader)
    {
        uint count = reader.ReadUInt32();
        var nodes = new Dictionary<uint, Node>((int)count);
        while (count-- > 0)
        {
            byte type = reader.ReadByte();
            uint nodeId = reader.ReadUInt32();

            Node node = (Node.Type)type switch
            {
                Node.Type.Database => new DatabaseNode(),
                Node.Type.Proc => new ProcNode(),
                Node.Type.DivQuery => new DivQueryNode(),
                Node.Type.InternalQuery => new InternalQueryNode(),
                Node.Type.And => new AndNode(),
                Node.Type.NotAnd => new NotAndNode(),
                Node.Type.RelOp => new RelOpNode(),
                Node.Type.Rule => new RuleNode(),
                Node.Type.UserQuery => new UserQueryNode(),
                _ => throw new NotImplementedException($"No valid serializer architecture layout found for this specific node type index parameter: {type}")
            };

            node.Read(reader);
            nodes.Add(nodeId, node);
        }

        return nodes;
    }

    private static Dictionary<uint, Adapter> ReadAdapters(OsiReader reader)
    {
        uint count = reader.ReadUInt32();
        var adapters = new Dictionary<uint, Adapter>((int)count);
        while (count-- > 0)
        {
            var adapter = new Adapter();
            adapter.Read(reader);
            adapters.Add(adapter.Index, adapter);
        }

        return adapters;
    }

    private static Dictionary<uint, Database> ReadDatabases(OsiReader reader)
    {
        uint count = reader.ReadUInt32();
        var databases = new Dictionary<uint, Database>((int)count);
        while (count-- > 0)
        {
            var database = new Database();
            database.Read(reader);
            databases.Add(database.Index, database);
        }

        return databases;
    }

    private static Dictionary<uint, Goal> ReadGoals(OsiReader reader, Story story)
    {
        uint count = reader.ReadUInt32();
        var goals = new Dictionary<uint, Goal>((int)count);
        while (count-- > 0)
        {
            var goal = new Goal(story);
            goal.Read(reader);
            goals.Add(goal.Index, goal);
        }

        return goals;
    }

    private static Dictionary<uint, OsirisType> ReadTypes(OsiReader reader, Story _)
    {
        if (reader.Ver < OsiVersion.VerAddTypeMap)
        {
            return [];
        }

        Dictionary<uint, OsirisType> types = ReadTypes(reader);

        // Find outermost types
        foreach (KeyValuePair<uint, OsirisType> type in types)
        {
            if (type.Value.Alias != 0)
            {
                uint aliasId = type.Value.Alias;

                while (aliasId != 0 && types.TryGetValue(aliasId, out OsirisType? aliasType) && aliasType.Alias != 0)
                {
                    aliasId = aliasType.Alias;
                }

                reader.TypeAliases.Add(type.Key, aliasId);
            }
        }

        return types;
    }

    public static Story Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var story = new Story();
        using var reader = new OsiReader(stream, story);
        var header = new SaveFileHeader();
        header.Read(reader);

        reader.MinorVersion = header.MinorVersion;
        reader.MajorVersion = header.MajorVersion;
        story.MinorVersion = header.MinorVersion;
        story.MajorVersion = header.MajorVersion;

        if (reader.Ver > OsiVersion.VerLastSupported)
        {
            throw new InvalidDataException($"Osiris version v{reader.MajorVersion}.{reader.MinorVersion} unsupported; this tool supports loading up to version 1.15.");
        }

        if (reader.Ver < OsiVersion.VerRemoveExternalStringTable)
        {
            reader.ShortTypeIds = false;
        }
        else if (reader.Ver >= OsiVersion.VerEnums)
        {
            reader.ShortTypeIds = true;
        }

        if (reader.Ver >= OsiVersion.VerScramble)
        {
            reader.Scramble = 0xAD;
        }

        story.Types = ReadTypes(reader, story);

        if (reader.Ver >= OsiVersion.VerExternalStringTable && reader.Ver < OsiVersion.VerRemoveExternalStringTable)
        {
            story.ExternalStringTable = ReadStrings(reader);
        }
        else
        {
            story.ExternalStringTable = [];
        }

        story.Types[0] = OsirisType.MakeBuiltin(0, "UNKNOWN");
        story.Types[1] = OsirisType.MakeBuiltin(1, "INTEGER");

        if (reader.Ver >= OsiVersion.VerEnhancedTypes)
        {
            story.Types[2] = OsirisType.MakeBuiltin(2, "INTEGER64");
            story.Types[3] = OsirisType.MakeBuiltin(3, "REAL");
            story.Types[4] = OsirisType.MakeBuiltin(4, "STRING");

            if (!story.Types.ContainsKey(5))
            {
                story.Types[5] = OsirisType.MakeBuiltin(5, "GUIDSTRING");
            }
        }
        else
        {
            story.Types[2] = OsirisType.MakeBuiltin(2, "FLOAT");
            story.Types[3] = OsirisType.MakeBuiltin(3, "STRING");

            if (reader.Ver < OsiVersion.VerAddTypeMap)
            {
                for (byte typeId = 4; typeId <= 17; typeId++)
                {
                    var builtinType = OsirisType.MakeBuiltin(typeId, $"TYPE{typeId}");
                    builtinType.Alias = 3;
                    story.Types[typeId] = builtinType;
                    reader.TypeAliases.Add(typeId, 3);
                }
            }
        }

        if (reader.Ver >= OsiVersion.VerEnums)
        {
            story.Enums = ReadEnums(reader);
        }
        else
        {
            story.Enums = [];
        }

        story.DivObjects = reader.ReadList<OsirisDivObject>();
        story.Functions = reader.ReadList<Function>();
        story.Nodes = ReadNodes(reader);
        story.Adapters = ReadAdapters(reader);
        story.Databases = ReadDatabases(reader);
        story.Goals = ReadGoals(reader, story);
        story.GlobalActions = reader.ReadList<Call>();
        story.ShortTypeIds = reader.ShortTypeIds ?? false;

        story.FunctionSignatureMap = new Dictionary<string, Function>(story.Functions.Count, StringComparer.Ordinal);
        foreach (Function func in story.Functions)
        {
            if (func?.Name?.Parameters?.Types is not null)
            {
                string sigKey = $"{func.Name.Name}/{func.Name.Parameters.Types.Count}";
                story.FunctionSignatureMap.TryAdd(sigKey, func);
            }
        }

        foreach (KeyValuePair<uint, Node> node in story.Nodes)
        {
            node.Value?.PostLoad(story);
        }

        return story;
    }
}

public sealed class StoryWriter(OsiWriter writer)
{
    private readonly OsiWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));

    private static void WriteStrings(OsiWriter writer, List<string> stringTable)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(stringTable);

        writer.Write((uint)stringTable.Count);
        foreach (string s in stringTable)
        {
            writer.Write(s);
        }
    }

    private static void WriteTypes(OsiWriter writer, List<OsirisType> types, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(story);

        writer.Write((uint)types.Count);
        foreach (OsirisType type in types)
        {
            if (type is not null)
            {
                type.Write(writer);
                if (type.Alias != 0)
                {
                    writer.TypeAliases.TryAdd(type.Index, story.FindBuiltinTypeId(type.Index));
                }
            }
        }
    }

    private static void WriteNodes(OsiWriter writer, Dictionary<uint, Node> nodes)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(nodes);

        writer.Write((uint)nodes.Count);
        foreach (KeyValuePair<uint, Node> node in nodes)
        {
            if (node.Value is not null)
            {
                writer.Write((byte)node.Value.NodeType());
                writer.Write(node.Key);
                node.Value.Write(writer);
            }
        }
    }

    private static void WriteAdapters(OsiWriter writer, Dictionary<uint, Adapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(adapters);

        writer.Write((uint)adapters.Count);
        foreach (KeyValuePair<uint, Adapter> adapter in adapters)
        {
            if (adapter.Value is not null)
            {
                writer.Write(adapter.Key);
                adapter.Value.Write(writer);
            }
        }
    }

    private static void WriteDatabases(OsiWriter writer, Dictionary<uint, Database> databases)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(databases);

        writer.Write((uint)databases.Count);
        foreach (KeyValuePair<uint, Database> database in databases)
        {
            if (database.Value is not null)
            {
                writer.Write(database.Key);
                database.Value.Write(writer);
            }
        }
    }

    private static void WriteGoals(OsiWriter writer, Dictionary<uint, Goal> goals)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(goals);

        writer.Write((uint)goals.Count);
        foreach (KeyValuePair<uint, Goal> goal in goals)
        {
            goal.Value?.Write(writer);
        }
    }

    public static void Write(Stream stream, Story story, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(story);

        using var localWriter = new OsiWriter(stream, leaveOpen)
        {
            MajorVersion = story.MajorVersion,
            MinorVersion = story.MinorVersion,
            ShortTypeIds = story.ShortTypeIds,
            Enums = story.Enums
        };

        foreach (KeyValuePair<uint, Node> node in story.Nodes)
        {
            node.Value?.PreSave(story);
        }

        var header = new SaveFileHeader();
        if (localWriter.Ver >= OsiVersion.VerExternalStringTable)
        {
            header.Version = localWriter.ShortTypeIds
                ? "Osiris save file dd. 07/09/22 00:20:54. Version 1.8."
                : "Osiris save file dd. 03/30/17 07:28:20. Version 1.8.";
        }
        else
        {
            header.Version = "Osiris save file dd. 02/10/15 12:44:13. Version 1.5.";
        }

        header.MajorVersion = story.MajorVersion;
        header.MinorVersion = story.MinorVersion;
        header.BigEndian = false;
        header.Unused = 0;
        header.DebugFlags = 0x000C10A0;
        header.Write(localWriter);

        if (localWriter.Ver > OsiVersion.VerLastSupported)
        {
            throw new InvalidDataException($"Osiris version v{localWriter.MajorVersion}.{localWriter.MinorVersion} unsupported; this tool supports saving up to version 1.15.");
        }

        if (localWriter.Ver >= OsiVersion.VerScramble)
        {
            localWriter.Scramble = 0xAD;
        }

        if (localWriter.Ver >= OsiVersion.VerAddTypeMap)
        {
            List<OsirisType> types = localWriter.Ver >= OsiVersion.VerEnums
                ? [.. story.Types.Values.Where(t => t is not null && t.Name != "UNKNOWN")]
                : [.. story.Types.Values.Where(t => t is not null && !t.IsBuiltin)];

            WriteTypes(localWriter, types, story);
        }

        if (localWriter.Ver >= OsiVersion.VerEnums)
        {
            localWriter.WriteList([.. story.Enums.Values]);
        }

        if (localWriter.Ver >= OsiVersion.VerExternalStringTable && localWriter.Ver < OsiVersion.VerRemoveExternalStringTable)
        {
            WriteStrings(localWriter, story.ExternalStringTable);
        }

        localWriter.WriteList(story.DivObjects);
        localWriter.WriteList(story.Functions);
        WriteNodes(localWriter, story.Nodes);
        WriteAdapters(localWriter, story.Adapters);
        WriteDatabases(localWriter, story.Databases);
        WriteGoals(localWriter, story.Goals);
        localWriter.WriteList(story.GlobalActions);

        foreach (KeyValuePair<uint, Node> node in story.Nodes)
        {
            node.Value?.PostSave(story);
        }
    }
}
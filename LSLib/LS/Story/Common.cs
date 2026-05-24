namespace LSLib.LS.Story;

public interface IOsirisSerializable
{
    void Read(OsiReader reader);
    void Write(OsiWriter writer);
}

/// <summary>
/// Osiris file format version numbers
/// </summary>
public static class OsiVersion
{
    /// <summary>
    /// Initial version
    /// </summary>
    public const uint VerInitial = 0x0100;

    /// <summary>
    /// Added Init/Exit calls to goals
    /// </summary>
    public const uint VerAddInitExitCalls = 0x0101;

    /// <summary>
    /// Added version string at the beginning of the OSI file
    /// </summary>
    public const uint VerAddVersionString = 0x0102;

    /// <summary>
    /// Added debug flags in the header
    /// </summary>
    public const uint VerAddDebugFlags = 0x0103;

    /// <summary>
    /// Started scrambling strings by xor-ing with 0xAD
    /// </summary>
    public const uint VerScramble = 0x0104;

    /// <summary>
    /// Added custom (string) types
    /// </summary>
    public const uint VerAddTypeMap = 0x0105;

    /// <summary>
    /// Added Query nodes
    /// </summary>
    public const uint VerAddQuery = 0x0106;

    /// <summary>
    /// Types can be aliases of any builtin type, not just strings
    /// </summary>
    public const uint VerTypeAliases = 0x0109;

    /// <summary>
    /// Added INT64, GUIDSTRING types
    /// </summary>
    public const uint VerEnhancedTypes = 0x010a;

    /// <summary>
    /// Added external string table
    /// </summary>
    public const uint VerExternalStringTable = 0x010b;

    /// <summary>
    /// Removed external string table
    /// </summary>
    public const uint VerRemoveExternalStringTable = 0x010c;

    /// <summary>
    /// Added enumerations
    /// </summary>
    public const uint VerEnums = 0x010d;

    /// <summary>
    /// Changed values to store flags/indices in a more compact way
    /// </summary>
    public const uint VerValueFlags = 0x010e;

    /// <summary>
    /// Version bump in P8H2
    /// </summary>
    public const uint VerPatch8Hotfix2 = 0x010f;

    /// <summary>
    /// Last supported Osi version
    /// </summary>
    public const uint VerLastSupported = VerPatch8Hotfix2;
}

public class OsiReader(Stream stream, Story story) : BinaryReader(stream ?? throw new ArgumentNullException(nameof(stream)))
{
    public byte Scramble { get; set; } = 0x00;
    public uint MinorVersion { get; set; }
    public uint MajorVersion { get; set; }
    public bool? ShortTypeIds { get; set; } = null;
    public Dictionary<uint, uint> TypeAliases { get; init; } = [];
    public Story Story { get; init; } = story ?? throw new ArgumentNullException(nameof(story));

    public uint Ver => (MajorVersion << 8) | MinorVersion;

    public override string ReadString()
    {
        var bytes = new List<byte>();
        while (true)
        {
            var b = (byte)(ReadByte() ^ Scramble);
            if (b != 0)
            {
                bytes.Add(b);
            }
            else
            {
                break;
            }
        }

        return Encoding.UTF8.GetString([.. bytes]);
    }

    public override bool ReadBoolean()
    {
        byte b = ReadByte();
        if (b != 0 && b != 1)
        {
            throw new InvalidDataException("Invalid boolean value; expected 0 or 1.");
        }

        return b == 1;
    }

    public Guid ReadGuid()
    {
        byte[] guid = ReadBytes(16);
        if (guid.Length < 16)
        {
            throw new EndOfStreamException("Unable to read complete 16-byte Guid structure block data.");
        }
        return new Guid(guid);
    }

    public List<T> ReadList<T>() where T : IOsirisSerializable, new()
    {
        var items = new List<T>();
        ReadList(items);
        return items;
    }

    public void ReadList<T>(List<T> items) where T : IOsirisSerializable, new()
    {
        ArgumentNullException.ThrowIfNull(items);

        uint count = ReadUInt32();
        items.EnsureCapacity(items.Count + (int)count);
        
        while (count-- > 0)
        {
            var item = new T();
            item.Read(this);
            items.Add(item);
        }
    }

    public List<T> ReadRefList<T, TRef>() where T : OsiReference<TRef>, new() where TRef : class
    {
        var items = new List<T>();
        ReadRefList<T, TRef>(items);
        return items;
    }

    public void ReadRefList<T, TRef>(List<T> items) where T : OsiReference<TRef>, new() where TRef : class
    {
        ArgumentNullException.ThrowIfNull(items);

        uint count = ReadUInt32();
        items.EnsureCapacity(items.Count + (int)count);

        while (count-- > 0)
        {
            var item = new T();
            item.BindStory(Story);
            item.Read(this);
            items.Add(item);
        }
    }

    public NodeReference ReadNodeRef()
    {
        var nodeRef = new NodeReference();
        nodeRef.BindStory(Story);
        nodeRef.Read(this);
        return nodeRef;
    }

    public AdapterReference ReadAdapterRef()
    {
        var adapterRef = new AdapterReference();
        adapterRef.BindStory(Story);
        adapterRef.Read(this);
        return adapterRef;
    }

    public DatabaseReference ReadDatabaseRef()
    {
        var databaseRef = new DatabaseReference();
        databaseRef.BindStory(Story);
        databaseRef.Read(this);
        return databaseRef;
    }

    public GoalReference ReadGoalRef()
    {
        var goalRef = new GoalReference();
        goalRef.BindStory(Story);
        goalRef.Read(this);
        return goalRef;
    }
}

public class OsiWriter(Stream stream, bool leaveOpen) : BinaryWriter(stream ?? throw new ArgumentNullException(nameof(stream)), Encoding.UTF8, leaveOpen)
{
    public byte Scramble { get; set; }
    public uint MinorVersion { get; set; }
    public uint MajorVersion { get; set; }
    public bool ShortTypeIds { get; set; }
    public Dictionary<uint, uint> TypeAliases { get; init; } = [];
    public Dictionary<uint, OsirisEnum> Enums { get; init; } = [];

    public uint Ver => (MajorVersion << 8) | MinorVersion;

    public override void Write(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        byte[] bytes = Encoding.UTF8.GetBytes(value);
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(bytes[i] ^ Scramble);
        }
        Write(bytes, 0, bytes.Length);
        Write(Scramble);
    }

    public override void Write(bool value)
    {
        Write((byte)(value ? 1 : 0));
    }

    public void Write(Guid guid)
    {
        byte[] bytes = guid.ToByteArray();
        Write(bytes, 0, bytes.Length);
    }

    public void WriteList<T>(List<T> list) where T : IOsirisSerializable
    {
        ArgumentNullException.ThrowIfNull(list);

        Write((uint)list.Count);
        foreach (T item in list)
        {
            item.Write(this);
        }
    }
}

public class SaveFileHeader : IOsirisSerializable
{
    public string Version { get; set; } = string.Empty;
    public byte MajorVersion { get; set; }
    public byte MinorVersion { get; set; }
    public bool BigEndian { get; set; }
    public byte Unused { get; set; }
    public uint DebugFlags { get; set; }

    public uint Ver => ((uint)MajorVersion << 8) | MinorVersion;

    public void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        reader.ReadByte();
        Version = reader.ReadString() ?? string.Empty;
        MajorVersion = reader.ReadByte();
        MinorVersion = reader.ReadByte();
        BigEndian = reader.ReadBoolean();
        Unused = reader.ReadByte();

        if (Ver >= OsiVersion.VerAddVersionString)
        {
            reader.ReadBytes(0x80); // Version string buffer
        }

        if (Ver >= OsiVersion.VerAddDebugFlags)
        {
            DebugFlags = reader.ReadUInt32();
        }
        else
        {
            DebugFlags = 0;
        }
    }

    public void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write((byte)0);
        writer.Write(Version);
        writer.Write(MajorVersion);
        writer.Write(MinorVersion);
        writer.Write(BigEndian);
        writer.Write(Unused);

        if (Ver >= OsiVersion.VerAddVersionString)
        {
            string versionString = $"{MajorVersion}.{MinorVersion}";
            byte[] versionBytes = Encoding.UTF8.GetBytes(versionString);
            byte[] version = new byte[0x80];
            Array.Copy(versionBytes, version, Math.Min(versionBytes.Length, version.Length));
            writer.Write(version, 0, version.Length);
        }

        if (Ver >= OsiVersion.VerAddDebugFlags)
        {
            writer.Write(DebugFlags);
        }
    }
}

public class OsirisType : IOsirisSerializable
{
    public byte Index { get; set; }
    public byte Alias { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsBuiltin { get; set; }

    public static OsirisType MakeBuiltin(byte index, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return new OsirisType
        {
            Index = index,
            Alias = 0,
            Name = name,
            IsBuiltin = true
        };
    }

    public void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        Name = reader.ReadString() ?? string.Empty;
        Index = reader.ReadByte();
        IsBuiltin = false;

        if (reader.Ver >= OsiVersion.VerTypeAliases)
        {
            Alias = reader.ReadByte();
        }
        else
        {
            Alias = (byte)Value.Type_OS1.String;
        }
    }

    public void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write(Name);
        writer.Write(Index);

        if (writer.Ver >= OsiVersion.VerTypeAliases)
        {
            writer.Write(Alias);
        }
    }

    public void DebugDump(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (Alias == 0)
        {
            writer.WriteLine("{0}: {1}", Index, Name);
        }
        else
        {
            writer.WriteLine("{0}: {1} (Alias: {2})", Index, Name, Alias);
        }
    }
}

public class OsirisEnumElement : IOsirisSerializable
{
    public string Name { get; set; } = string.Empty;
    public ulong Value { get; set; }

    public void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        Name = reader.ReadString() ?? string.Empty;
        Value = reader.ReadUInt64();
    }

    public void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write(Name);
        writer.Write(Value);
    }

    public void DebugDump(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteLine("{0}: {1}", Name, Value);
    }
}

public class OsirisEnum : IOsirisSerializable
{
    public ushort UnderlyingType { get; set; }
    public List<OsirisEnumElement> Elements { get; set; } = [];

    public void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        UnderlyingType = reader.ReadUInt16();
        uint elementsCount = reader.ReadUInt32();
        Elements = new List<OsirisEnumElement>((int)elementsCount);
        while (elementsCount-- > 0)
        {
            var e = new OsirisEnumElement();
            e.Read(reader);
            Elements.Add(e);
        }
    }

    public void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write(UnderlyingType);
        writer.Write((uint)Elements.Count);

        foreach (OsirisEnumElement e in Elements)
        {
            e.Write(writer);
        }
    }

    public void DebugDump(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteLine("Type {0}", UnderlyingType);
        foreach (OsirisEnumElement e in Elements)
        {
            e.DebugDump(writer);
        }
    }
}

public class OsirisDivObject : IOsirisSerializable
{
    public string Name { get; set; } = string.Empty;
    public byte Type { get; set; }
    public uint Key1 { get; set; }
    public uint Key2 { get; set; }
    public uint Key3 { get; set; }
    public uint Key4 { get; set; }

    public void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        Name = reader.ReadString() ?? string.Empty;
        Type = reader.ReadByte();
        Key1 = reader.ReadUInt32();
        Key2 = reader.ReadUInt32();
        Key3 = reader.ReadUInt32();
        Key4 = reader.ReadUInt32();
    }

    public void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write(Name);
        writer.Write(Type);
        writer.Write(Key1);
        writer.Write(Key2);
        writer.Write(Key3);
        writer.Write(Key4);
    }

    public void DebugDump(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteLine("{0} {1} ({2}, {3}, {4}, {5})", Type, Name, Key1, Key2, Key3, Key4);
    }
}

public enum EntryPoint : uint
{
    // The next node is not an AND/NOT AND expression
    None = 0,
    // This node is on the left side of the next AND/NOT AND expression
    Left = 1,
    // This node is on the right side of the next AND/NOT AND expression
    Right = 2
};

public class NodeEntryItem : IOsirisSerializable
{
    public NodeReference NodeRef { get; set; } = null!;
    public EntryPoint EntryPoint { get; set; }
    public GoalReference GoalRef { get; set; } = null!;

    public void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        NodeRef = reader.ReadNodeRef();
        EntryPoint = (EntryPoint)reader.ReadUInt32();
        GoalRef = reader.ReadGoalRef();
    }

    public void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        NodeRef.Write(writer);
        writer.Write((uint)EntryPoint);
        GoalRef.Write(writer);
    }

    public void DebugDump(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);

        if (NodeRef is not null && NodeRef.IsValid)
        {
            writer.Write("(");
            NodeRef.DebugDump(writer, story);

            if (GoalRef is not null && GoalRef.IsValid && GoalRef.Resolve() is Goal goal)
            {
                writer.Write(", Entry Point {0}, Goal {1})", EntryPoint, goal.Name);
            }
            else
            {
                writer.Write(")");
            }
        }
        else
        {
            writer.Write("(none)");
        }
    }
}

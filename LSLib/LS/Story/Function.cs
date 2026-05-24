namespace LSLib.LS.Story;

public class FunctionSignature : IOsirisSerializable
{
    public string Name { get; set; } = string.Empty;
    public List<byte> OutParamMask { get; set; } = [];
    public ParameterList Parameters { get; set; } = new();

    public void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        Name = reader.ReadString() ?? string.Empty;
        uint outParamBytes = reader.ReadUInt32();

        OutParamMask = new List<byte>((int)outParamBytes);
        while (outParamBytes-- > 0)
        {
            OutParamMask.Add(reader.ReadByte());
        }

        Parameters = new ParameterList();
        Parameters.Read(reader);
    }

    public void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write(Name);
        writer.Write((uint)OutParamMask.Count);
        foreach (byte b in OutParamMask)
        {
            writer.Write(b);
        }

        Parameters.Write(writer);
    }

    public void DebugDump(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);

        writer.Write(Name);
        writer.Write("(");
        for (int i = 0; i < Parameters.Types.Count; i++)
        {
            if (story.Types.TryGetValue(Parameters.Types[i], out OsirisType? type))
            {
                bool isOutParam = ((OutParamMask[i >> 3] << (i & 7)) & 0x80) == 0x80;
                if (isOutParam) writer.Write("out ");
                writer.Write(type.Name);
            }
            else
            {
                writer.Write("UNKNOWN_TYPE");
            }

            if (i < Parameters.Types.Count - 1) writer.Write(", ");
        }
        writer.Write(")");
    }
}

public class ParameterList : IOsirisSerializable
{
    public List<uint> Types { get; set; } = [];

    public void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        byte count = reader.ReadByte();
        Types = new List<uint>(count);
        while (count-- > 0)
        {
            // BG3 heuristic: Patch 8 doesn't increment the version number but changes type ID format,
            // so we need to detect it by checking if a 32-bit type ID would be valid.
            if (reader.ShortTypeIds is null)
            {
                uint id = reader.ReadUInt32();
                reader.BaseStream.Position -= 4;
                reader.ShortTypeIds = id > 0xff;
            }

            if (reader.ShortTypeIds == true)
            {
                Types.Add(reader.ReadUInt16());
            }
            else
            {
                Types.Add(reader.ReadUInt32());
            }
        }
    }

    public void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write((byte)Types.Count);
        foreach (uint type in Types)
        {
            if (writer.ShortTypeIds)
            {
                writer.Write((ushort)type);
            }
            else
            {
                writer.Write(type);
            }
        }
    }

    public void DebugDump(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);

        for (int i = 0; i < Types.Count; i++)
        {
            if (story.Types.TryGetValue(Types[i], out OsirisType? type))
            {
                writer.Write(type.Name);
            }
            else
            {
                writer.Write("UNKNOWN_TYPE");
            }

            if (i < Types.Count - 1) writer.Write(", ");
        }
    }
}

public enum FunctionType
{
    Event = 1,
    Query = 2,
    Call = 3,
    Database = 4,
    Proc = 5,
    SysQuery = 6,
    SysCall = 7,
    UserQuery = 8
}

public class Function : IOsirisSerializable
{
    public uint Line { get; set; }
    public uint ConditionReferences { get; set; }
    public uint ActionReferences { get; set; }
    public NodeReference NodeRef { get; set; } = null!;
    public FunctionType Type { get; set; }
    public uint Meta1 { get; set; }
    public uint Meta2 { get; set; }
    public uint Meta3 { get; set; }
    public uint Meta4 { get; set; }
    public FunctionSignature Name { get; set; } = null!;

    public void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        Line = reader.ReadUInt32();
        ConditionReferences = reader.ReadUInt32();
        ActionReferences = reader.ReadUInt32();
        NodeRef = reader.ReadNodeRef();
        Type = (FunctionType)reader.ReadByte();
        Meta1 = reader.ReadUInt32();
        Meta2 = reader.ReadUInt32();
        Meta3 = reader.ReadUInt32();
        Meta4 = reader.ReadUInt32();
        Name = new FunctionSignature();
        Name.Read(reader);
    }

    public void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write(Line);
        writer.Write(ConditionReferences);
        writer.Write(ActionReferences);
        NodeRef.Write(writer);
        writer.Write((byte)Type);
        writer.Write(Meta1);
        writer.Write(Meta2);
        writer.Write(Meta3);
        writer.Write(Meta4);
        Name.Write(writer);
    }

    public void DebugDump(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);

        writer.Write($"{Type} ");
        Name.DebugDump(writer, story);

        if (NodeRef is { IsValid: true } && NodeRef.Resolve() is Node node)
        {
            writer.Write($" @ {node.Name ?? string.Empty}({node.NumParams})");
        }

        writer.Write($" CondRefs {ConditionReferences}, ActRefs {ActionReferences}");
        writer.WriteLine($" Meta ({Meta1}, {Meta2}, {Meta3}, {Meta4})");
    }
}
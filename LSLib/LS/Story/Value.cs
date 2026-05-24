using LSLib.LS.Story;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;

namespace LSLib.LS.Story;

public class Value : IOsirisSerializable
{
    // Original Sin 2 (v1.11) Type ID-s
    public enum Type : uint
    {
        None = 0,
        Integer = 1,
        Integer64 = 2,
        Float = 3,
        String = 4,
        GuidString = 5
    }

    // Original Sin 1 (v1.0 - v1.7) Type ID-s
    public enum Type_OS1 : uint
    {
        None = 0,
        Integer = 1,
        Float = 2,
        String = 3
    }

    // Format of flags after v1.14
    [Flags]
    protected enum ValueFlags : byte
    {
        NoneType = 0,
        SimpleValue = 0x01,
        TypedValue = 0x02,
        Variable = 0x03,
        IsValid = 0x08,
        OutParam = 0x10,
        IsAType = 0x20,
        Unused = 0x40,
        Adapted = 0x80,
    }

    public uint TypeId { get; set; }
    public int IntValue { get; set; }
    public long Int64Value { get; set; }
    public float FloatValue { get; set; }
    public string? StringValue { get; set; }

    // for TypedVal
    public bool IsValid { get; set; }
    public bool OutParam { get; set; }
    public bool IsAType { get; set; }

    // for Value
    public sbyte Index { get; set; }
    public bool Unused { get; set; }
    public bool Adapted { get; set; }

    public override string ToString() => (Type)TypeId switch
    {
        Type.None => string.Empty,
        Type.Integer => IntValue.ToString(CultureInfo.InvariantCulture),
        Type.Integer64 => Int64Value.ToString(CultureInfo.InvariantCulture),
        Type.Float => FloatValue.ToString(CultureInfo.InvariantCulture),
        Type.String or Type.GuidString => StringValue ?? string.Empty,
        _ => StringValue ?? string.Empty
    };

    public static uint ConvertOS1ToOS2Type(uint os1TypeId) => (Type_OS1)os1TypeId switch
    {
        Type_OS1.None => (uint)Type.None,
        Type_OS1.Integer => (uint)Type.Integer,
        Type_OS1.Float => (uint)Type.Float,
        Type_OS1.String => (uint)Type.String,
        _ => os1TypeId,
    };

    public static uint ConvertOS2ToOS1Type(uint os2TypeId) => (Type)os2TypeId switch
    {
        Type.None => (uint)Type_OS1.None,
        Type.Integer or Type.Integer64 => (uint)Type_OS1.Integer,
        Type.Float => (uint)Type_OS1.Float,
        Type.String or Type.GuidString => (uint)Type_OS1.String,
        _ => os2TypeId,
    };

    public Type GetBuiltinTypeId(Story story)
    {
        ArgumentNullException.ThrowIfNull(story);
        var aliasId = story.FindBuiltinTypeId(TypeId);

        return story.Version < OsiVersion.VerEnhancedTypes
            ? (Type)ConvertOS1ToOS2Type(aliasId)
            : (Type)aliasId;
    }

    public virtual void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader.Ver >= OsiVersion.VerValueFlags)
        {
            Index = reader.ReadSByte();

            var flags = (ValueFlags)reader.ReadByte();

            IsValid = flags.HasFlag(ValueFlags.IsValid);
            OutParam = flags.HasFlag(ValueFlags.OutParam);
            IsAType = flags.HasFlag(ValueFlags.IsAType);
            Unused = flags.HasFlag(ValueFlags.Unused);
            Adapted = flags.HasFlag(ValueFlags.Adapted);

            if (!IsValid) return;
        }

        switch (reader.ReadByte())
        {
            case (byte)'1':
                TypeId = (reader.ShortTypeIds ?? false) ? reader.ReadUInt16() : reader.ReadUInt32();
                IntValue = reader.ReadInt32();
                break;

            case (byte)'0':
                TypeId = (reader.ShortTypeIds ?? false) ? reader.ReadUInt16() : reader.ReadUInt32();
                uint writtenTypeId = TypeId;
                bool dos1alias = false;

                if (reader.TypeAliases.TryGetValue(writtenTypeId, out uint alias))
                {
                    writtenTypeId = alias;
                    dos1alias = reader.Ver < OsiVersion.VerEnhancedTypes;
                }

                if (reader.Ver < OsiVersion.VerEnhancedTypes)
                {
                    writtenTypeId = ConvertOS1ToOS2Type(writtenTypeId);
                }

                switch ((Type)writtenTypeId)
                {
                    case Type.None:
                        break;
                    case Type.Integer:
                        IntValue = reader.ReadInt32();
                        break;
                    case Type.Integer64:
                        Int64Value = reader.ReadInt64();
                        break;
                    case Type.Float:
                        FloatValue = reader.ReadSingle();
                        break;
                    case Type.GuidString or Type.String:
                        if (dos1alias || reader.ReadByte() > 0)
                        {
                            StringValue = reader.ReadString();
                        }
                        break;
                    default:
                        StringValue = reader.ReadString();
                        break;
                }
                break;

            case (byte)'e':
                TypeId = reader.ReadUInt16();

                if (reader.Story?.Enums is null || !reader.Story.Enums.TryGetValue(TypeId, out var osirisEnum))
                {
                    throw new InvalidDataException($"Enum label serialized for a non-enum type: {TypeId}");
                }

                StringValue = reader.ReadString();

                _ = osirisEnum.Elements.Find(v => v.Name == StringValue)
                    ?? throw new InvalidDataException($"Enumeration {TypeId} has no label named '{StringValue}'");
                break;

            default:
                throw new InvalidDataException("Unrecognized value type");
        }
    }

    protected virtual ValueFlags GetTypeFlags() => ValueFlags.SimpleValue;

    public virtual void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (writer.Ver >= OsiVersion.VerValueFlags)
        {
            writer.Write(Index);

            var flags = GetTypeFlags();
            if (IsValid) flags |= ValueFlags.IsValid;
            if (OutParam) flags |= ValueFlags.OutParam;
            if (IsAType) flags |= ValueFlags.IsAType;
            if (Unused) flags |= ValueFlags.Unused;
            if (Adapted) flags |= ValueFlags.Adapted;
            writer.Write((byte)flags);

            if (!IsValid) return;
        }

        if (writer.Enums.ContainsKey(TypeId))
        {
            writer.Write((byte)'e');
            writer.Write((ushort)TypeId);
            writer.Write(StringValue ?? string.Empty);
            return;
        }

        writer.Write((byte)'0');

        uint writtenTypeId = TypeId;
        bool aliased = false;
        if (writer.TypeAliases.TryGetValue(TypeId, out uint alias))
        {
            aliased = true;
            writtenTypeId = alias;
        }

        if (writer.ShortTypeIds)
        {
            writer.Write((ushort)TypeId);
        }
        else
        {
            writer.Write(TypeId);
        }

        if (writer.Ver < OsiVersion.VerEnhancedTypes)
        {
            writtenTypeId = ConvertOS1ToOS2Type(writtenTypeId);
        }

        switch ((Type)writtenTypeId)
        {
            case Type.None:
                break;

            case Type.Integer:
                writer.Write(IntValue);
                break;

            case Type.Integer64:
                if (writer.Ver >= OsiVersion.VerEnhancedTypes)
                {
                    writer.Write(Int64Value);
                }
                else
                {
                    writer.Write((int)Int64Value);
                }
                break;

            case Type.Float:
                writer.Write(FloatValue);
                break;

            case Type.String or Type.GuidString:
                if (!aliased || writer.Ver >= OsiVersion.VerEnhancedTypes)
                {
                    writer.Write(StringValue is not null);
                }

                if (StringValue is not null)
                {
                    writer.Write(StringValue);
                }
                break;

            default:
                writer.Write(StringValue ?? string.Empty);
                break;
        }
    }

    public virtual void DebugDump(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);

        var text = GetBuiltinTypeId(story) switch
        {
            Type.None => "<unknown>",
            Type.Integer => IntValue.ToString(CultureInfo.InvariantCulture),
            Type.Integer64 => Int64Value.ToString(CultureInfo.InvariantCulture),
            Type.Float => FloatValue.ToString(CultureInfo.InvariantCulture),
            Type.String => $"'{StringValue ?? string.Empty}'",
            Type.GuidString => StringValue ?? string.Empty,
            _ => throw new InvalidDataException("Unsupported builtin type ID encountered inside DebugDump execution path.")
        };
        writer.Write(text);
    }

    public virtual void MakeScript(TextWriter writer, Story story, Tuple tuple, bool printTypes = false)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);

        var text = GetBuiltinTypeId(story) switch
        {
            Type.None => throw new InvalidDataException("Script cannot contain unknown values"),
            Type.Integer or Type.Integer64 => IntValue.ToString(CultureInfo.InvariantCulture),
            Type.Float => ((decimal)FloatValue).ToString(CultureInfo.InvariantCulture),
            Type.String => $"\"{StringValue ?? string.Empty}\"",
            Type.GuidString => StringValue ?? string.Empty,
            _ => throw new InvalidDataException("Unsupported builtin type ID encountered inside MakeScript conversion loop.")
        };
        writer.Write(text);
    }
}

public class TypedValue : Value
{
    public override void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        base.Read(reader);
        if (reader.Ver < OsiVersion.VerValueFlags)
        {
            IsValid = reader.ReadBoolean();
            OutParam = reader.ReadBoolean();
            IsAType = reader.ReadBoolean();
        }
    }

    public override void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        base.Write(writer);
        if (writer.Ver < OsiVersion.VerValueFlags)
        {
            writer.Write(IsValid);
            writer.Write(OutParam);
            writer.Write(IsAType);
        }
    }

    protected override ValueFlags GetTypeFlags() => ValueFlags.TypedValue;

    public override void DebugDump(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);

        if (IsValid) writer.Write("valid ");
        if (OutParam) writer.Write("out ");
        if (IsAType) writer.Write("type ");

        if (IsValid)
        {
            base.DebugDump(writer, story);
        }
        else
        {
            if (story.Types is not null && story.Types.TryGetValue(TypeId, out var type) && type is not null)
            {
                writer.Write($"<{type.Name ?? string.Empty}>");
            }
            else
            {
                writer.Write($"<{TypeId}>");
            }
        }
    }
}

public class Variable : TypedValue
{
    public string VariableName { get; set; } = string.Empty;

    public override void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        base.Read(reader);
        if (reader.Ver < OsiVersion.VerValueFlags)
        {
            Index = reader.ReadSByte();
            Unused = reader.ReadBoolean();
            Adapted = reader.ReadBoolean();
        }
    }

    public override void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        base.Write(writer);
        if (writer.Ver < OsiVersion.VerValueFlags)
        {
            writer.Write(Index);
            writer.Write(Unused);
            writer.Write(Adapted);
        }
    }

    protected override ValueFlags GetTypeFlags() => ValueFlags.Variable;

    public override void DebugDump(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);

        writer.Write($"#{Index} ");

        if (!string.IsNullOrEmpty(VariableName))
        {
            writer.Write($"'{VariableName}' ");
        }

        if (Unused) writer.Write("unused ");
        if (Adapted) writer.Write("adapted ");

        base.DebugDump(writer, story);
    }
    public override void MakeScript(TextWriter writer, Story story, Tuple tuple, bool printTypes = false)
    {
        if (Unused)
        {
            if (printTypes && TypeId > 0)
            {
                writer.Write($"({story.Types[TypeId].Name})");
            }
            writer.Write("_");
        }
        else if (Adapted)
        {
            if (VariableName is { Length: > 0 })
            {
                if (printTypes && TypeId > 0)
                {
                    writer.Write($"({story.Types[TypeId].Name})");
                }
                writer.Write(VariableName);
            }
            else
            {
                tuple?.Logical[Index].MakeScript(writer, story, null!);
            }
        }
        else
        {
            base.MakeScript(writer, story, tuple);
        }
    }
}

public class Tuple : IOsirisSerializable
{
    public List<Value> Physical { get; set; } = [];
    public Dictionary<int, Value> Logical { get; set; } = [];

    public void Read(OsiReader reader)
    {
        Physical.Clear();
        Logical.Clear();

        byte count = reader.ReadByte();
        while (count-- > 0)
        {
            var value = new Value();

            if (reader.Ver >= OsiVersion.VerValueFlags)
            {
                value.Read(reader);
                Physical.Add(value);
                Logical.Add(value.Index, value);
            }
            else
            {
                byte index = reader.ReadByte();
                value.Read(reader);
                Physical.Add(value);
                Logical.Add(index, value);
            }
        }
    }

    public void Write(OsiWriter writer)
    {
        writer.Write((byte)Logical.Count);

        foreach (var (key, value) in Logical)
        {
            if (writer.Ver < OsiVersion.VerValueFlags)
            {
                writer.Write((byte)key);
            }
            value.Write(writer);
        }
    }

    public void DebugDump(TextWriter writer, Story story)
    {
        writer.Write("(");

        int i = 0;
        foreach (var (key, value) in Logical)
        {
            writer.Write($"{key}: ");
            value.DebugDump(writer, story);

            if (++i < Logical.Count)
            {
                writer.Write(", ");
            }
        }

        writer.Write(")");
    }

    public void MakeScript(TextWriter writer, Story story, bool printTypes = false)
    {
        int i = 0;
        foreach (var value in Physical)
        {
            value.MakeScript(writer, story, null!, printTypes);

            if (++i < Physical.Count)
            {
                writer.Write(", ");
            }
        }
    }
}
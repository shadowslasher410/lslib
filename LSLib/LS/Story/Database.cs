using System.ComponentModel;
using System.Globalization;

namespace LSLib.LS.Story;

public sealed class Fact : IOsirisSerializable
{
    public List<Value> Columns { get; set; } = [];

    public void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        byte count = reader.ReadByte();
        Columns = new List<Value>(count);
        while (count-- > 0)
        {
            var value = new Value();
            value.Read(reader);
            Columns.Add(value);
        }
    }

    public void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write((byte)Columns.Count);
        foreach (Value column in Columns)
        {
            column.Write(writer);
        }
    }

    public void DebugDump(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);

        writer.Write("(");
        for (int i = 0; i < Columns.Count; i++)
        {
            Columns[i].DebugDump(writer, story);
            if (i < Columns.Count - 1) writer.Write(", ");
        }
        writer.Write(")");
    }
}

internal sealed class FactPropertyDescriptor(int index, Value.Type baseType, byte type) : PropertyDescriptor(index.ToString(CultureInfo.InvariantCulture), [])
{
    public int Index { get; private set; } = index;
    public Value.Type BaseType { get; private set; } = baseType;
    public byte Type { get; private set; } = type;

    public override bool CanResetValue(object component) => false;

    public override Type ComponentType => typeof(Fact);

    public override object? GetValue(object? component)
    {
        ArgumentNullException.ThrowIfNull(component);
        Fact fact = (Fact)component;
        return fact.Columns[Index].ToString() ?? string.Empty;
    }

    public override bool IsReadOnly => false;

    public override Type PropertyType
    {
        get
        {
            return BaseType switch
            {
                Value.Type.Integer => typeof(int),
                Value.Type.Integer64 => typeof(long),
                Value.Type.Float => typeof(float),
                Value.Type.String or Value.Type.GuidString => typeof(string),
                _ => throw new InvalidOperationException("Cannot retrieve type of an unknown column parameter schema configuration.")
            };
        }
    }

    public override void ResetValue(object component) => throw new NotImplementedException();

    public override void SetValue(object? component, object? value)
    {
        ArgumentNullException.ThrowIfNull(component);
        Fact fact = (Fact)component;
        Value column = fact.Columns[Index];

        switch (BaseType)
        {
            case Value.Type.Integer:
                if (value is string s) column.IntValue = int.Parse(s, CultureInfo.InvariantCulture);
                else if (value is int i) column.IntValue = i;
                else throw new ArgumentException("Invalid int value payload conversion sequence.");
                break;

            case Value.Type.Integer64:
                if (value is string s64) column.Int64Value = long.Parse(s64, CultureInfo.InvariantCulture);
                else if (value is long l64) column.Int64Value = l64;
                else throw new ArgumentException("Invalid long value payload conversion sequence.");
                break;

            case Value.Type.Float:
                if (value is string sf) column.FloatValue = float.Parse(sf, CultureInfo.InvariantCulture);
                else if (value is float f) column.FloatValue = f;
                else throw new ArgumentException("Invalid float value payload conversion sequence.");
                break;

            case Value.Type.String:
            case Value.Type.GuidString:
                column.StringValue = value as string ?? string.Empty;
                break;

            default:
                throw new InvalidOperationException("Cannot resolve assignment target of an unknown column data paradigm schematic.");
        }
    }

    public override bool ShouldSerializeValue(object component) => false;
}

public sealed class FactCollection(Database database, Story story) : List<Fact>, ITypedList
{
    private readonly Story _story = story ?? throw new ArgumentNullException(nameof(story));
    private readonly Database _database = database ?? throw new ArgumentNullException(nameof(database));
    private PropertyDescriptorCollection? _properties;

    public PropertyDescriptorCollection GetItemProperties(PropertyDescriptor[]? listAccessors)
    {
        if (_properties is null)
        {
            var props = new List<PropertyDescriptor>();
            List<uint> types = _database.Parameters.Types;

            for (int i = 0; i < types.Count; i++)
            {
                if (_story.Types.TryGetValue(types[i], out OsirisType? type))
                {
                    Value.Type baseType = type.Alias != 0 ? (Value.Type)type.Alias : (Value.Type)type.Index;
                    props.Add(new FactPropertyDescriptor(i, baseType, type.Index));
                }
            }

            _properties = new PropertyDescriptorCollection([.. props], true);
        }

        return _properties;
    }

    public string GetListName(PropertyDescriptor[]? listAccessors) => string.Empty;
}

public sealed class Database : IOsirisSerializable
{
    public uint Index { get; set; }
    public ParameterList Parameters { get; set; } = new();
    public FactCollection Facts { get; set; } = null!;
    public Node? OwnerNode { get; set; }

    public void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        Index = reader.ReadUInt32();
        Parameters = new ParameterList();
        Parameters.Read(reader);

        Facts = new FactCollection(this, reader.Story);
        reader.ReadList(Facts);
    }

    public void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write(Index);
        Parameters.Write(writer);
        writer.WriteList(Facts);
    }

    public void DebugDump(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);

        if (OwnerNode is not null && !string.IsNullOrEmpty(OwnerNode.Name))
        {
            writer.Write("{0}({1})", OwnerNode.Name, OwnerNode.NumParams);
        }
        else if (OwnerNode is not null)
        {
            writer.Write("<{0}>", OwnerNode.TypeName());
        }
        else
        {
            writer.Write("(Not owned)");
        }

        Parameters.DebugDump(writer, story);

        writer.WriteLine();
        writer.WriteLine("    Facts: ");
        foreach (Fact fact in Facts)
        {
            writer.Write("        ");
            fact.DebugDump(writer, story);
            writer.WriteLine();
        }
    }
}
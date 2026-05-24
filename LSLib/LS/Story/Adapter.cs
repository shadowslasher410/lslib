using System;
using System.Collections.Generic;
using System.IO;

namespace LSLib.LS.Story;

public class Adapter : IOsirisSerializable
{
    /// <summary>
    /// Unique identifier of this adapter
    /// </summary>
    public uint Index { get; set; }

    /// <summary>
    /// Constant output values
    /// </summary>
    public Tuple Constants { get; set; } = new();

    /// <summary>
    /// Contains input logical column indices for each output physical column.
    /// A -1 means that the output column is a constant or null value; otherwise
    /// the output column maps to the specified logical index from the input tuple.
    /// </summary>
    public List<sbyte> LogicalIndices { get; set; } = [];

    /// <summary>
    /// Logical index => physical index map of the output tuple
    /// </summary>
    public Dictionary<byte, byte> LogicalToPhysicalMap { get; set; } = [];

    /// <summary>
    /// Node that we're attached to
    /// </summary>
    public Node? OwnerNode { get; set; }

    public void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        Index = reader.ReadUInt32();
        Constants = new Tuple();
        Constants.Read(reader);

        byte count = reader.ReadByte();
        LogicalIndices = new List<sbyte>(count);
        for (int i = 0; i < count; i++)
        {
            LogicalIndices.Add(reader.ReadSByte());
        }

        count = reader.ReadByte();
        LogicalToPhysicalMap = new Dictionary<byte, byte>(count);
        for (int i = 0; i < count; i++)
        {
            byte key = reader.ReadByte();
            byte value = reader.ReadByte();
            LogicalToPhysicalMap.Add(key, value);
        }
    }

    public void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        Constants.Write(writer);

        writer.Write((byte)LogicalIndices.Count);
        foreach (sbyte index in LogicalIndices)
        {
            writer.Write(index);
        }

        writer.Write((byte)LogicalToPhysicalMap.Count);
        foreach (KeyValuePair<byte, byte> pair in LogicalToPhysicalMap)
        {
            writer.Write(pair.Key);
            writer.Write(pair.Value);
        }
    }

    public Tuple Adapt(Tuple columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        var result = new Tuple();
        for (int i = 0; i < LogicalIndices.Count; i++)
        {
            sbyte index = LogicalIndices[i];

            if (index != -1)
            {
                if (columns.Logical.TryGetValue(index, out var value))
                {
                    result.Physical.Add(value);
                }
                else if (index == 0)
                {
                    var nullValue = new Variable
                    {
                        TypeId = (uint)Value.Type.None,
                        Unused = true
                    };
                    result.Physical.Add(nullValue);
                }
                else
                {
                    throw new InvalidDataException($"Logical column index {index} does not exist in tuple.");
                }
            }
            else if (Constants.Logical.TryGetValue(i, out var constValue))
            {
                result.Physical.Add(constValue);
            }
            else
            {
                var nullValue = new Variable
                {
                    TypeId = (uint)Value.Type.None,
                    Unused = true
                };
                result.Physical.Add(nullValue);
            }
        }

        foreach (KeyValuePair<byte, byte> map in LogicalToPhysicalMap)
        {
            if (map.Value < result.Physical.Count)
            {
                result.Logical.Add(map.Key, result.Physical[map.Value]);
            }
        }

        return result;
    }

    public void DebugDump(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);

        writer.Write("Adapter - ");
        if (OwnerNode is not null && !string.IsNullOrEmpty(OwnerNode.Name))
        {
            writer.WriteLine("Node {0}({1})", OwnerNode.Name, OwnerNode.NumParams);
        }
        else if (OwnerNode is not null)
        {
            writer.WriteLine("Node <{0}>", OwnerNode.TypeName());
        }
        else
        {
            writer.WriteLine("(Not owned)");
        }

        if (Constants.Logical.Count > 0)
        {
            writer.Write("    Constants: ");
            Constants.DebugDump(writer, story);
            writer.WriteLine();
        }

        if (LogicalIndices.Count > 0)
        {
            writer.Write("    Logical indices: ");
            foreach (sbyte index in LogicalIndices)
            {
                writer.Write("{0}, ", index);
            }
            writer.WriteLine();
        }

        if (LogicalToPhysicalMap.Count > 0)
        {
            writer.Write("    Logical to physical mappings: ");
            foreach (KeyValuePair<byte, byte> pair in LogicalToPhysicalMap)
            {
                writer.Write("{0} -> {1}, ", pair.Key, pair.Value);
            }
            writer.WriteLine();
        }
    }
}
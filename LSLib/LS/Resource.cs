namespace LSLib.LS;

public sealed class InvalidFormatException(string message) : Exception(message);

public struct PackedVersion
{
    public uint Major;
    public uint Minor;
    public uint Revision;
    public uint Build;

    public static PackedVersion FromInt64(long packed)
    {
        return new PackedVersion
        {
            Major = (uint)((packed >> 55) & 0x7f),
            Minor = (uint)((packed >> 47) & 0xff),
            Revision = (uint)((packed >> 31) & 0xffff),
            Build = (uint)(packed & 0x7fffffff),
        };
    }

    public static PackedVersion FromInt32(int packed)
    {
        return new PackedVersion
        {
            Major = (uint)((packed >> 28) & 0x0f),
            Minor = (uint)((packed >> 24) & 0x0f),
            Revision = (uint)((packed >> 16) & 0xff),
            Build = (uint)(packed & 0xffff),
        };
    }

    public readonly int ToVersion32()
    {
        return (int)((Major & 0x0f) << 28 |
            (Minor & 0x0f) << 24 |
            (Revision & 0xff) << 16 |
            (Build & 0xffff) << 0);
    }

    public readonly long ToVersion64()
    {
        return (long)(((long)Major & 0x7f) << 55 |
            ((long)Minor & 0xff) << 47 |
            ((long)Revision & 0xffff) << 31 |
            ((long)Build & 0x7fffffff) << 0);
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct LSMetadata
{
    public const uint CurrentMajorVersion = 33;

    public ulong Timestamp;
    public uint MajorVersion;
    public uint MinorVersion;
    public uint Revision;
    public uint BuildNumber;
}

[StructLayout(LayoutKind.Sequential)]
public struct LSBHeader
{
    /// <summary>
    /// LSB file signature since BG3 (Exposed as a zero-allocation read-only span)
    /// </summary>
    public static ReadOnlySpan<byte> SignatureBG3 => "LSFM"u8;

    /// <summary>
    /// LSB signature up to FW3 (DOS2 DE)
    /// </summary>
    public const uint SignatureFW3 = 0x40000000;

    public uint Signature;
    public uint TotalSize;
    public uint BigEndian;
    public uint Unknown;
    public LSMetadata Metadata;
}

public static class AttributeTypeMaps
{
    public static readonly Dictionary<string, AttributeType> TypeToId = new(StringComparer.Ordinal)
    {
        { "None", AttributeType.None },
        { "uint8", AttributeType.Byte },
        { "int16", AttributeType.Short },
        { "uint16", AttributeType.UShort },
        { "int32", AttributeType.Int },
        { "uint32", AttributeType.UInt },
        { "float", AttributeType.Float },
        { "double", AttributeType.Double },
        { "ivec2", AttributeType.IVec2 },
        { "ivec3", AttributeType.IVec3 },
        { "ivec4", AttributeType.IVec4 },
        { "fvec2", AttributeType.Vec2 },
        { "fvec3", AttributeType.Vec3 },
        { "fvec4", AttributeType.Vec4 },
        { "mat2x2", AttributeType.Mat2 },
        { "mat3x3", AttributeType.Mat3 },
        { "mat3x4", AttributeType.Mat3x4 },
        { "mat4x3", AttributeType.Mat4x3 },
        { "mat4x4", AttributeType.Mat4 },
        { "bool", AttributeType.Bool },
        { "string", AttributeType.String },
        { "path", AttributeType.Path },
        { "FixedString", AttributeType.FixedString },
        { "LSString", AttributeType.LSString },
        { "uint64", AttributeType.ULongLong },
        { "ScratchBuffer", AttributeType.ScratchBuffer },
        { "old_int64", AttributeType.Long },
        { "int8", AttributeType.Int8 },
        { "TranslatedString", AttributeType.TranslatedString },
        { "WString", AttributeType.WString },
        { "LSWString", AttributeType.LSWString },
        { "guid", AttributeType.UUID },
        { "int64", AttributeType.Int64 },
        { "TranslatedFSString", AttributeType.TranslatedFSString },
    };

    public static readonly Dictionary<AttributeType, string> IdToType = new()
    {
        { AttributeType.None, "None" },
        { AttributeType.Byte, "uint8" },
        { AttributeType.Short, "int16" },
        { AttributeType.UShort, "uint16" },
        { AttributeType.Int, "int32" },
        { AttributeType.UInt, "uint32" },
        { AttributeType.Float, "float" },
        { AttributeType.Double, "double" },
        { AttributeType.IVec2, "ivec2" },
        { AttributeType.IVec3, "ivec3" },
        { AttributeType.IVec4, "ivec4" },
        { AttributeType.Vec2, "fvec2" },
        { AttributeType.Vec3, "fvec3" },
        { AttributeType.Vec4, "fvec4" },
        { AttributeType.Mat2, "mat2x2" },
        { AttributeType.Mat3, "mat3x3" },
        { AttributeType.Mat3x4, "mat3x4" },
        { AttributeType.Mat4x3, "mat4x3" },
        { AttributeType.Mat4, "mat4x4" },
        { AttributeType.Bool, "bool" },
        { AttributeType.String, "string" },
        { AttributeType.Path, "path" },
        { AttributeType.FixedString, "FixedString" },
        { AttributeType.LSString, "LSString" },
        { AttributeType.ULongLong, "uint64" },
        { AttributeType.ScratchBuffer, "ScratchBuffer" },
        { AttributeType.Long, "old_int64" },
        { AttributeType.Int8, "int8" },
        { AttributeType.TranslatedString, "TranslatedString" },
        { AttributeType.WString, "WString" },
        { AttributeType.LSWString, "LSWString" },
        { AttributeType.UUID, "guid" },
        { AttributeType.Int64, "int64" },
        { AttributeType.TranslatedFSString, "TranslatedFSString" },
    };
}

public class Resource
{
    public LSMetadata Metadata;
    public LSFMetadataFormat? MetadataFormat { get; set; }
    public Dictionary<string, Region> Regions { get; set; } = new(StringComparer.Ordinal);

    public Resource()
    {
        Metadata.MajorVersion = 3;
    }
}

public class Region : Node
{
    public string RegionName { get; set; } = string.Empty;
}

public class Node
{
    public string Name { get; set; } = string.Empty;
    public Node? Parent { get; set; }
    public Dictionary<string, NodeAttribute> Attributes { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<Node>> Children { get; set; } = new(StringComparer.Ordinal);
    public int? Line { get; set; }
    public string? KeyAttribute { get; set; }

    /// <summary>
    /// Total direct single-level nested child nodes.
    /// </summary>
    public int ChildCount
    {
        get
        {
            int count = 0;
            foreach (KeyValuePair<string, List<Node>> pair in Children)
            {
                count += pair.Value.Count;
            }
            return count;
        }
    }

    public int TotalChildCount()
    {
        int count = 0;
        foreach (KeyValuePair<string, List<Node>> pair in Children)
        {
            foreach (Node child in pair.Value)
            {
                count += 1 + child.TotalChildCount();
            }
        }
        return count;
    }

    public void AppendChild(Node child)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (!Children.TryGetValue(child.Name, out List<Node>? children))
        {
            children = [];
            Children.Add(child.Name, children);
        }

        children.Add(child);
    }
}
namespace LSLib.LS;

public class TranslatedString
{
    public ushort Version { get; set; } = 0;
    public string Value { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;

    public override string ToString() => !string.IsNullOrEmpty(Value) ? Value : $"{Handle};{Version}";
}

public class TranslatedFSStringArgument
{
    public string Key { get; set; } = string.Empty;
    public TranslatedFSString String { get; set; } = null!;
    public string Value { get; set; } = string.Empty;
}

public class TranslatedFSString : TranslatedString
{
    public List<TranslatedFSStringArgument> Arguments { get; set; } = [];
}

public class NodeSerializationSettings
{
    public bool DefaultByteSwapGuids { get; set; } = true;
    public bool ByteSwapGuids { get; set; } = true;
    public LSFMetadataFormat LSFMetadata { get; set; } = LSFMetadataFormat.None;

    public void InitFromMeta(ReadOnlySpan<char> meta)
    {
        if (meta.IsEmpty)
        {
            ByteSwapGuids = DefaultByteSwapGuids;
            LSFMetadata = LSFMetadataFormat.None;
            return;
        }

        bool hasBswap = false;
        bool hasAdjacency = false;
        bool hasKeys = false;

        foreach (var range in meta.Split(','))
        {
            var tag = meta[range];
            if (tag.Equals("bswap_guids", StringComparison.Ordinal)) hasBswap = true;
            else if (tag.Equals("lsf_adjacency", StringComparison.Ordinal)) hasAdjacency = true;
            else if (tag.Equals("lsf_keys_adjacency", StringComparison.Ordinal)) hasKeys = true;
        }

        ByteSwapGuids = hasBswap;
        LSFMetadata = LSFMetadataFormat.None;

        if (hasAdjacency) LSFMetadata = LSFMetadataFormat.None2;
        else if (hasKeys) LSFMetadata = LSFMetadataFormat.KeysAndAdjacency;
    }

    public string BuildMeta()
    {
        List<string> tags = ["v1"];
        if (ByteSwapGuids) tags.Add("bswap_guids");

        if (LSFMetadata == LSFMetadataFormat.None2) tags.Add("lsf_adjacency");
        else if (LSFMetadata == LSFMetadataFormat.KeysAndAdjacency) tags.Add("lsf_keys_adjacency");

        return string.Join(",", tags);
    }
}

public enum AttributeType
{
    None = 0, Byte = 1, Short = 2, UShort = 3, Int = 4, UInt = 5, Float = 6, Double = 7,
    IVec2 = 8, IVec3 = 9, IVec4 = 10, Vec2 = 11, Vec3 = 12, Vec4 = 13,
    Mat2 = 14, Mat3 = 15, Mat3x4 = 16, Mat4x3 = 17, Mat4 = 18,
    Bool = 19, String = 20, Path = 21, FixedString = 22, LSString = 23, ULongLong = 24, ScratchBuffer = 25,
    Long = 26, Int8 = 27, TranslatedString = 28, WString = 29, LSWString = 30, UUID = 31, Int64 = 32,
    TranslatedFSString = 33, Max = TranslatedFSString
}

public static class AttributeTypeExtensions
{
    public static int GetRows(this AttributeType type) => type switch
    {
        AttributeType.IVec2 or AttributeType.IVec3 or AttributeType.IVec4 or
        AttributeType.Vec2 or AttributeType.Vec3 or AttributeType.Vec4 => 1,
        AttributeType.Mat2 => 2,
        AttributeType.Mat3 or AttributeType.Mat3x4 => 3,
        AttributeType.Mat4x3 or AttributeType.Mat4 => 4,
        _ => throw new NotSupportedException("Data type does not have rows")
    };

    public static int GetColumns(this AttributeType type) => type switch
    {
        AttributeType.IVec2 or AttributeType.Vec2 or AttributeType.Mat2 => 2,
        AttributeType.IVec3 or AttributeType.Vec3 or AttributeType.Mat3 or AttributeType.Mat4x3 => 3,
        AttributeType.IVec4 or AttributeType.Vec4 or AttributeType.Mat3x4 or AttributeType.Mat4 => 4,
        _ => throw new NotSupportedException("Data type does not have columns")
    };

    public static bool IsNumeric(this AttributeType type) => type switch
    {
        AttributeType.Byte or AttributeType.Short or AttributeType.Int or AttributeType.UInt or
        AttributeType.Float or AttributeType.Double or AttributeType.ULongLong or
        AttributeType.Long or AttributeType.Int8 or AttributeType.Int64 => true,
        _ => false
    };
}

public class NodeAttribute(AttributeType type)
{
    public AttributeType Type { get; } = type;
    public object? Value { get; set; }
    public int? Line { get; set; }

    public override string ToString() => throw new NotImplementedException("ToString() is not safe to use anymore, AsString(settings) instead");

    public static Guid ByteSwapGuid(Guid g)
    {
        Span<byte> bytes = stackalloc byte[16];
        g.TryWriteBytes(bytes);
        for (int i = 8; i < 16; i += 2)
        {
            (bytes[i + 1], bytes[i]) = (bytes[i], bytes[i + 1]);
        }
        return new Guid(bytes);
    }

    public string AsString(NodeSerializationSettings settings)
    {
        if (Value is null) return string.Empty;

        return Type switch
        {
            AttributeType.ScratchBuffer => Convert.ToBase64String((byte[])Value),
            AttributeType.IVec2 or AttributeType.IVec3 or AttributeType.IVec4 => string.Join(" ", (int[])Value),
            AttributeType.Vec2 or AttributeType.Vec3 or AttributeType.Vec4 => string.Join(" ", (float[])Value),
            AttributeType.UUID => settings.ByteSwapGuids ? ByteSwapGuid((Guid)Value).ToString() : Value.ToString()!,
            _ => Value.ToString()!
        };
    }

    public Guid AsGuid(NodeSerializationSettings settings) => AsGuid(settings.ByteSwapGuids);
    public Guid AsGuid() => AsGuid(true);

    public Guid AsGuid(bool byteSwapGuids)
    {
        if (Value is null) throw new InvalidOperationException("Value is null.");

        return Type switch
        {
            AttributeType.UUID => (Guid)Value,
            AttributeType.String or AttributeType.FixedString or AttributeType.LSString =>
                byteSwapGuids ? ByteSwapGuid(Guid.Parse((string)Value)) : Guid.Parse((string)Value),
            _ => throw new NotSupportedException("Type not convertible to GUID")
        };
    }

    public void FromString(string str, NodeSerializationSettings settings)
    {
        Value = ParseFromString(str, Type, settings.ByteSwapGuids);
    }

    public static object? ParseFromString(ReadOnlySpan<char> str, AttributeType type, bool byteSwapGuids)
    {
        if (type.IsNumeric())
        {
            if (str.IsEmpty)
            {
                str = "0";
            }
            else if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                ulong hexVal = ulong.Parse(str[2..], System.Globalization.NumberStyles.HexNumber);
                return ParseNumericType(hexVal, type);
            }
        }

        return type switch
        {
            AttributeType.None => null,
            AttributeType.Byte => byte.Parse(str),
            AttributeType.Short => short.Parse(str),
            AttributeType.UShort => ushort.Parse(str),
            AttributeType.Int => int.Parse(str),
            AttributeType.UInt => uint.Parse(str),
            AttributeType.Float => float.Parse(str),
            AttributeType.Double => double.Parse(str),
            AttributeType.Long or AttributeType.Int64 => long.Parse(str),
            AttributeType.ULongLong => ulong.Parse(str),
            AttributeType.Int8 => sbyte.Parse(str),

            AttributeType.Bool => str switch
            {
                "0" => false,
                "1" => true,
                _ => bool.Parse(str)
            },

            AttributeType.IVec2 or AttributeType.IVec3 or AttributeType.IVec4 => ParseIntVector(str, type.GetColumns()),
            AttributeType.Vec2 or AttributeType.Vec3 or AttributeType.Vec4 => ParseFloatVector(str, type.GetColumns()),

            AttributeType.Mat2 or AttributeType.Mat3 or AttributeType.Mat3x4 or AttributeType.Mat4x3 or AttributeType.Mat4 =>
                Matrix.Parse(str.ToString()) switch
                {
                    var mat when mat.Cols != type.GetColumns() || mat.Rows != type.GetRows() =>
                        throw new FormatException("Invalid column/row count for matrix"),
                    var mat => mat
                },

            AttributeType.String or AttributeType.Path or AttributeType.FixedString or
            AttributeType.LSString or AttributeType.WString or AttributeType.LSWString => str.ToString(),

            AttributeType.TranslatedString => new TranslatedString { Value = str.ToString() },
            AttributeType.TranslatedFSString => new TranslatedFSString { Value = str.ToString() },
            AttributeType.ScratchBuffer => Convert.FromBase64String(str.ToString()),
            AttributeType.UUID => byteSwapGuids ? ByteSwapGuid(Guid.Parse(str)) : Guid.Parse(str),

            _ => throw new NotImplementedException($"FromString() not implemented for type {type}")
        };
    }

    private static double ParseNumericType(ulong value, AttributeType type) => type switch
    {
        AttributeType.Byte => (byte)value,
        AttributeType.Short => (short)value,
        AttributeType.UShort => (ushort)value,
        AttributeType.Int => (int)value,
        AttributeType.UInt => (uint)value,
        AttributeType.Long or AttributeType.Int64 => (long)value,
        AttributeType.ULongLong => value,
        AttributeType.Int8 => (sbyte)value,
        AttributeType.Float => (float)value,
        AttributeType.Double => (double)value,
        _ => throw new InvalidOperationException("Unsupported numeric type for hex conversion.")
    };

    private static int[] ParseIntVector(ReadOnlySpan<char> str, int expectedLength)
    {
        int[] vec = new int[expectedLength];
        int index = 0;

        foreach (var range in str.Split(' '))
        {
            if (index >= expectedLength) throw new FormatException($"A vector of length {expectedLength} was expected.");
            vec[index++] = int.Parse(str[range]);
        }

        return index != expectedLength ? throw new FormatException($"A vector of length {expectedLength} was expected, got {index}") : vec;
    }

    private static float[] ParseFloatVector(ReadOnlySpan<char> str, int expectedLength)
    {
        float[] vec = new float[expectedLength];
        int index = 0;

        foreach (var range in str.Split(' '))
        {
            if (index >= expectedLength) throw new FormatException($"A vector of length {expectedLength} was expected.");
            vec[index++] = float.Parse(str[range]);
        }

        return index != expectedLength ? throw new FormatException($"A vector of length {expectedLength} was expected, got {index}") : vec;
    }

    public static object? ParseFromString(string str, AttributeType type, bool byteSwapGuids)
    {
        ReadOnlySpan<char> span = str.AsSpan();

        if (type.IsNumeric())
        {
            if (span.IsEmpty)
            {
                span = "0";
            }
            else if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                ulong hexVal = ulong.Parse(span[2..], System.Globalization.NumberStyles.HexNumber);
                return ParseNumericType(hexVal, type);
            }
        }

        return type switch
        {
            AttributeType.None => null,
            AttributeType.Byte => byte.Parse(span),
            AttributeType.Short => short.Parse(span),
            AttributeType.UShort => ushort.Parse(span),
            AttributeType.Int => int.Parse(span),
            AttributeType.UInt => uint.Parse(span),
            AttributeType.Float => float.Parse(span),
            AttributeType.Double => double.Parse(span),
            AttributeType.Long or AttributeType.Int64 => long.Parse(span),
            AttributeType.ULongLong => ulong.Parse(span),
            AttributeType.Int8 => sbyte.Parse(span),

            AttributeType.Bool => span switch
            {
                "0" => false,
                "1" => true,
                _ => bool.Parse(span)
            },

            AttributeType.IVec2 or AttributeType.IVec3 or AttributeType.IVec4 => ParseIntVector(span, type.GetColumns()),
            AttributeType.Vec2 or AttributeType.Vec3 or AttributeType.Vec4 => ParseFloatVector(span, type.GetColumns()),

            AttributeType.Mat2 or AttributeType.Mat3 or AttributeType.Mat3x4 or AttributeType.Mat4x3 or AttributeType.Mat4 =>
                Matrix.Parse(str) switch
                {
                    var mat when mat.Cols != type.GetColumns() || mat.Rows   != type.GetRows() =>
                        throw new FormatException("Invalid column/row count for matrix"),
                    var mat => mat
                },

            AttributeType.String or AttributeType.Path or AttributeType.FixedString or
            AttributeType.LSString or AttributeType.WString or AttributeType.LSWString => str,

            AttributeType.TranslatedString => new TranslatedString { Value = str },
            AttributeType.TranslatedFSString => new TranslatedFSString { Value = str },

            AttributeType.ScratchBuffer => Convert.FromBase64String(str),
            AttributeType.UUID => byteSwapGuids ? ByteSwapGuid(Guid.Parse(span)) : Guid.Parse(span),

            _ => throw new NotImplementedException($"FromString() not implemented for type {type}")
        };
    }

}
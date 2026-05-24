using System.Text.Json;
using System.Text.RegularExpressions;
using System.Numerics;
using System.Text.Json.Serialization;
using System.Buffers;
using System.Globalization;

namespace LSLib.LS;

public partial class LSJResourceConverter(NodeSerializationSettings settings) : JsonConverter<object>
{
    private LSMetadata _metadata = default;
    private readonly NodeSerializationSettings _serializationSettings = settings ?? throw new ArgumentNullException(nameof(settings));

    [GeneratedRegex(@"^([0-9]+)\.([0-9]+)\.([0-9]+)\.([0-9]+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();

    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert == typeof(Node) || typeToConvert == typeof(Resource);
    }

    private static TranslatedFSStringArgument ReadFSStringArgument(ref Utf8JsonReader reader)
    {
        var fs = new TranslatedFSStringArgument();
        string key = string.Empty;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }
            else if (reader.TokenType == JsonTokenType.PropertyName)
            {
                key = reader.GetString() ?? string.Empty;
            }
            else if (reader.TokenType == JsonTokenType.String)
            {
                if (key == "key")
                {
                    fs.Key = reader.GetString() ?? string.Empty;
                }
                else if (key == "value")
                {
                    fs.Value = reader.GetString() ?? string.Empty;
                }
                else
                {
                    throw new InvalidDataException($"Unknown property encountered during TranslatedFSString argument parsing: {key}");
                }
            }
            else if (reader.TokenType == JsonTokenType.StartObject && string.Equals(key, "string", StringComparison.OrdinalIgnoreCase))
            {
                fs.String = ReadTranslatedFSString(ref reader);
            }
            else
            {
                throw new InvalidDataException($"Unexpected JSON token during parsing of TranslatedFSString argument: {reader.TokenType}");
            }
        }

        return fs;
    }

    private static TranslatedFSString ReadTranslatedFSString(ref Utf8JsonReader reader)
    {
        var fs = new TranslatedFSString();
        string key = string.Empty;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                key = reader.GetString() ?? string.Empty;
            }
            else if (reader.TokenType == JsonTokenType.String)
            {
                if (string.Equals(key, "value", StringComparison.OrdinalIgnoreCase))
                {
                    fs.Value = reader.GetString() ?? string.Empty;
                }
                else if (string.Equals(key, "handle", StringComparison.OrdinalIgnoreCase))
                {
                    fs.Handle = reader.GetString() ?? string.Empty;
                }
                else
                {
                    throw new InvalidDataException($"Unknown TranslatedFSString property: {key}");
                }
            }
            else if (reader.TokenType == JsonTokenType.StartArray && string.Equals(key, "arguments", StringComparison.OrdinalIgnoreCase))
            {
                fs.Arguments = ReadFSStringArguments(ref reader);
            }
            else if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }
            else
            {
                throw new InvalidDataException($"Unexpected JSON token during parsing of TranslatedFSString: {reader.TokenType}");
            }
        }

        return fs;
    }

    private static List<TranslatedFSStringArgument> ReadFSStringArguments(ref Utf8JsonReader reader)
    {
        var args = new List<TranslatedFSStringArgument>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                args.Add(ReadFSStringArgument(ref reader));
            }
            else if (reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }
            else
            {
                throw new InvalidDataException($"Unexpected JSON token during parsing of TranslatedFSString argument list: {reader.TokenType}");
            }
        }

        return args;
    }

    private NodeAttribute ReadAttribute(ref Utf8JsonReader reader)
    {
        string key = string.Empty;
        string? handle = null;
        List<TranslatedFSStringArgument>? fsStringArguments = null;
        NodeAttribute? attribute = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                key = reader.GetString() ?? string.Empty;
            }
            else if (reader.TokenType is JsonTokenType.String or JsonTokenType.Number or JsonTokenType.True or JsonTokenType.False or JsonTokenType.Null)
            {
                if (string.Equals(key, "type", StringComparison.OrdinalIgnoreCase))
                {
                    string? typeStr = reader.GetString();
                    uint typeId;

                    if (uint.TryParse(typeStr, out uint parsedId))
                    {
                        typeId = parsedId;
                    }
                    else if (typeStr is not null && AttributeTypeMaps.TypeToId.TryGetValue(typeStr, out var mappedType))
                    {
                        typeId = (uint)mappedType;
                    }
                    else
                    {
                        throw new InvalidDataException($"Invalid or unrecognized AttributeType token identifier: {typeStr}");
                    }

                    attribute = new NodeAttribute((AttributeType)typeId);
                    if (typeId == (uint)AttributeType.TranslatedString)
                    {
                        attribute.Value = new TranslatedString { Handle = handle ?? string.Empty };
                    }
                    else if (typeId == (uint)AttributeType.TranslatedFSString)
                    {
                        attribute.Value = new TranslatedFSString { Handle = handle ?? string.Empty, Arguments = fsStringArguments ?? [] };
                    }
                }
                else if (string.Equals(key, "value", StringComparison.OrdinalIgnoreCase) && attribute is not null)
                {
                    switch (attribute.Type)
                    {
                        case AttributeType.Byte:
                            attribute.Value = reader.GetByte();
                            break;

                        case AttributeType.Short:
                            attribute.Value = reader.GetInt16();
                            break;

                        case AttributeType.UShort:
                            attribute.Value = reader.GetUInt16();
                            break;

                        case AttributeType.Int:
                            attribute.Value = reader.GetInt32();
                            break;

                        case AttributeType.UInt:
                            attribute.Value = reader.GetUInt32();
                            break;

                        case AttributeType.Float:
                            attribute.Value = reader.GetSingle();
                            break;

                        case AttributeType.Double:
                            attribute.Value = reader.GetDouble();
                            break;

                        case AttributeType.Bool:
                            attribute.Value = reader.GetBoolean();
                            break;

                        case AttributeType.String:
                        case AttributeType.Path:
                        case AttributeType.FixedString:
                        case AttributeType.LSString:
                        case AttributeType.WString:
                        case AttributeType.LSWString:
                            attribute.Value = reader.GetString() ?? string.Empty;
                            break;

                        case AttributeType.ULongLong:
                            if (reader.TryGetUInt64(out ulong uLongVal))
                            {
                                attribute.Value = uLongVal;
                            }
                            else if (reader.TokenType == JsonTokenType.Number)
                            {
                                ReadOnlySpan<byte> rawSpan;
                                byte[]? rented = null;

                                if (reader.HasValueSequence)
                                {
                                    int seqLen = (int)reader.ValueSequence.Length;
                                    rented = ArrayPool<byte>.Shared.Rent(seqLen);
                                    reader.ValueSequence.CopyTo(rented);
                                    rawSpan = rented.AsSpan(0, seqLen);
                                }
                                else
                                {
                                    rawSpan = reader.ValueSpan;
                                }

                                string rawText = Encoding.UTF8.GetString(rawSpan);
                                if (rented is not null)
                                {
                                    ArrayPool<byte>.Shared.Return(rented);
                                }

                                if (BigInteger.TryParse(rawText, CultureInfo.InvariantCulture, out BigInteger bigIntVal))
                                {
                                    attribute.Value = (ulong)bigIntVal;
                                }
                            }
                            break;

                        case AttributeType.ScratchBuffer:
                            attribute.Value = reader.GetBytesFromBase64();
                            break;

                        case AttributeType.Long:
                        case AttributeType.Int64:
                            attribute.Value = reader.GetInt64();
                            break;

                        case AttributeType.Int8:
                            attribute.Value = reader.GetSByte();
                            break;

                        case AttributeType.TranslatedString:
                            {
                                attribute.Value ??= new TranslatedString();
                                var ts = (TranslatedString)attribute.Value;
                                ts.Value = reader.GetString() ?? string.Empty;
                                ts.Handle = handle ?? string.Empty;
                                break;
                            }

                        case AttributeType.TranslatedFSString:
                            {
                                attribute.Value ??= new TranslatedFSString();
                                var fsString = (TranslatedFSString)attribute.Value;
                                fsString.Value = reader.GetString() ?? string.Empty;
                                fsString.Handle = handle ?? string.Empty;
                                fsString.Arguments = fsStringArguments ?? [];
                                attribute.Value = fsString;
                                break;
                            }

                        case AttributeType.UUID:
                            {
                                var parsedGuid = new Guid(reader.GetString() ?? Guid.Empty.ToString());
                                attribute.Value = _serializationSettings.ByteSwapGuids
                                    ? NodeAttribute.ByteSwapGuid(parsedGuid)
                                    : parsedGuid;
                                break;
                            }

                        case AttributeType.IVec2:
                        case AttributeType.IVec3:
                        case AttributeType.IVec4:
                            {
                                string valStr = reader.GetString() ?? string.Empty;
                                string[] nums = valStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                int expectedLength = (int)attribute.Type - (int)AttributeType.IVec2 + 2;
                                if (expectedLength != nums.Length)
                                    throw new FormatException($"A vector of length {expectedLength} was expected, got {nums.Length}");

                                int[] vec = new int[expectedLength];
                                for (int i = 0; i < expectedLength; i++)
                                {
                                    vec[i] = int.Parse(nums[i], CultureInfo.InvariantCulture);
                                }

                                attribute.Value = vec;
                                break;
                            }

                        case AttributeType.Vec2:
                        case AttributeType.Vec3:
                        case AttributeType.Vec4:
                            {
                                string valStr = reader.GetString() ?? string.Empty;
                                string[] nums = valStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                int expectedLength = (int)attribute.Type - (int)AttributeType.Vec2 + 2;
                                if (expectedLength != nums.Length)
                                    throw new FormatException($"A vector of length {expectedLength} was expected, got {nums.Length}");

                                float[] vec = new float[expectedLength];
                                for (int i = 0; i < expectedLength; i++)
                                {
                                    vec[i] = float.Parse(nums[i], CultureInfo.InvariantCulture);
                                }

                                attribute.Value = vec;
                                break;
                            }

                        case AttributeType.Mat2:
                        case AttributeType.Mat3:
                        case AttributeType.Mat3x4:
                        case AttributeType.Mat4x3:
                        case AttributeType.Mat4:
                            {
                                string valStr = reader.GetString() ?? string.Empty;
                                var mat = Matrix.Parse(valStr);
                                attribute.Value = mat;
                                break;
                            }

                        case AttributeType.None:
                        default:
                            throw new NotImplementedException($"Unable to unserialize unhandled type {attribute.Type}");
                    }
                }
                else if (string.Equals(key, "handle", StringComparison.OrdinalIgnoreCase))
                {
                    string currentHandle = reader.GetString() ?? string.Empty;
                    if (attribute is not null)
                    {
                        if (attribute.Type == AttributeType.TranslatedString)
                        {
                            attribute.Value ??= new TranslatedString();
                            ((TranslatedString)attribute.Value).Handle = currentHandle;
                        }
                        else if (attribute.Type == AttributeType.TranslatedFSString)
                        {
                            attribute.Value ??= new TranslatedFSString();
                            ((TranslatedFSString)attribute.Value).Handle = currentHandle;
                        }
                    }
                    else
                    {
                        handle = currentHandle;
                    }
                }
                else if (string.Equals(key, "version", StringComparison.OrdinalIgnoreCase) && attribute is not null)
                {
                    attribute.Value ??= new TranslatedString();
                    var ts = (TranslatedString)attribute.Value;
                    ts.Version = ushort.Parse(reader.GetString() ?? "0", CultureInfo.InvariantCulture);
                }
                else
                {
                    throw new InvalidDataException($"Unknown property encountered during attribute parsing: {key}");
                }
            }
            else if (reader.TokenType == JsonTokenType.StartArray && string.Equals(key, "arguments", StringComparison.OrdinalIgnoreCase))
            {
                var args = ReadFSStringArguments(ref reader);
                if (attribute?.Value is not null)
                {
                    var fs = (TranslatedFSString)attribute.Value;
                    fs.Arguments = args;
                }
                else
                {
                    fsStringArguments = args;
                }
            }
            else
            {
                throw new InvalidDataException($"Unexpected JSON token during parsing of attribute: {reader.TokenType}");
            }
        }

        return attribute ?? throw new InvalidDataException("Attribute schema structure parsing completely failed to yield a valid node definition context mapping.");
    }

    private Node ReadNode(ref Utf8JsonReader reader, Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        string key = string.Empty;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                key = reader.GetString() ?? string.Empty;
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                var attribute = ReadAttribute(ref reader);
                node.Attributes.Add(key, attribute);
            }
            else if (reader.TokenType == JsonTokenType.StartArray)
            {
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        break;
                    }
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        var childNode = new Node
                        {
                            Name = key
                        };
                        ReadNode(ref reader, childNode);
                        node.AppendChild(childNode);
                        childNode.Parent = node;
                    }
                    else
                    {
                        throw new InvalidDataException($"Unexpected JSON token during parsing of child node list: {reader.TokenType}");
                    }
                }
            }
            else
            {
                throw new InvalidDataException($"Unexpected JSON token during parsing of node: {reader.TokenType}");
            }
        }

        return node;
    }

    private Resource ReadResource(ref Utf8JsonReader reader, Resource? resource)
    {
        resource ??= new Resource();

        if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !string.Equals(reader.GetString(), "save", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Expected JSON property 'save'");
        }

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            throw new InvalidDataException($"Expected JSON object start token for 'save': {reader.TokenType}");
        }

        if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !string.Equals(reader.GetString(), "header", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Expected JSON property 'header'");
        }

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            throw new InvalidDataException($"Expected JSON object start token for 'header': {reader.TokenType}");
        }

        string key = string.Empty;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                key = reader.GetString() ?? string.Empty;
            }
            else if (reader.TokenType is JsonTokenType.String or JsonTokenType.Number)
            {
                if (string.Equals(key, "time", StringComparison.OrdinalIgnoreCase))
                {
                    resource.Metadata.Timestamp = reader.GetUInt32();
                }
                else if (string.Equals(key, "version", StringComparison.OrdinalIgnoreCase))
                {
                    string verStr = reader.GetString() ?? string.Empty;
                    var match = VersionRegex().Match(verStr);
                    if (match.Success)
                    {
                        resource.Metadata.MajorVersion = uint.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                        resource.Metadata.MinorVersion = uint.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                        resource.Metadata.Revision = uint.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                        resource.Metadata.BuildNumber = uint.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
                    }
                    else
                    {   
                        throw new InvalidDataException($"Malformed version string: {verStr}");
                    }
                }
                else
                {
                    throw new InvalidDataException($"Unknown property encountered during header parsing: {key}");
                }
            }
            else
            {
                throw new InvalidDataException($"Unexpected JSON token during parsing of header: {reader.TokenType}");
            }
        }

        if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !string.Equals(reader.GetString(), "regions", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Expected JSON property 'regions'");
        }

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            throw new InvalidDataException($"Expected JSON object start token for 'regions': {reader.TokenType}");
        }

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                key = reader.GetString() ?? string.Empty;
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                var region = new Region();
                ReadNode(ref reader, region);
                region.Name = key;
                region.RegionName = key;
                resource.Regions.Add(key, region);
            }
            else
            {
                throw new InvalidDataException($"Unexpected JSON token during parsing of region list: {reader.TokenType}");
            }
        }

        return resource;
    }
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        _ = options;
        if (typeToConvert == typeof(Node))
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("Expected StartObject token to initiate standard Node structure parsing rules.");
            var node = new Node();
            return ReadNode(ref reader, node);
        }
        else if (typeToConvert == typeof(Resource))
        {
            var resource = new Resource();
            return ReadResource(ref reader, resource);
        }
        else
        {
            throw new InvalidOperationException("Cannot unserialize unknown structure layout mapping configurations targets.");
        }
    }

    private void WriteResource(Utf8JsonWriter writer, Resource resource)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(resource);

        _metadata = resource.Metadata;
        writer.WriteStartObject();

        writer.WritePropertyName("save");
        writer.WriteStartObject();

        writer.WritePropertyName("header");
        writer.WriteStartObject();
        writer.WriteNumber("time", resource.Metadata.Timestamp);

        string versionString = $"{resource.Metadata.MajorVersion}.{resource.Metadata.MinorVersion}.{resource.Metadata.Revision}.{resource.Metadata.BuildNumber}";
        writer.WriteString("version", versionString);
        writer.WriteEndObject();

        writer.WritePropertyName("regions");
        writer.WriteStartObject();
        foreach (var region in resource.Regions)
        {
            writer.WritePropertyName(region.Key);
            WriteNode(writer, region.Value);
        }
        writer.WriteEndObject();

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteTranslatedFSString(Utf8JsonWriter writer, TranslatedFSString fs)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(fs);

        writer.WriteStartObject();
        writer.WritePropertyName("value");
        WriteTranslatedFSStringInner(writer, fs);
        writer.WriteEndObject();
    }

    private static void WriteTranslatedFSStringInner(Utf8JsonWriter writer, TranslatedFSString fs)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(fs);

        writer.WriteStringValue(fs.Value ?? string.Empty);
        writer.WriteString("handle", fs.Handle);
        writer.WritePropertyName("arguments");
        writer.WriteStartArray();

        foreach (var arg in fs.Arguments)
        {
            if (arg is null) continue;
            writer.WriteStartObject();
            writer.WriteString("key", arg.Key);
            writer.WritePropertyName("string");
            WriteTranslatedFSString(writer, arg.String);
            writer.WriteString("value", arg.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private void WriteNode(Utf8JsonWriter writer, Node node)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(node);

        writer.WriteStartObject();

        foreach (var attribute in node.Attributes)
        {
            if (attribute.Value is null) continue;

            writer.WritePropertyName(attribute.Key);
            writer.WriteStartObject();

            writer.WritePropertyName("type");
            if (_metadata.MajorVersion >= 4)
            {
                writer.WriteStringValue(attribute.Value.Type.ToString());
            }
            else
            {
                writer.WriteNumberValue((int)attribute.Value.Type);
            }

            if (attribute.Value.Type != AttributeType.TranslatedString)
            {
                writer.WritePropertyName("value");
            }

            switch (attribute.Value.Type)
            {
                case AttributeType.Byte:
                    writer.WriteNumberValue(Convert.ToByte(attribute.Value.Value, CultureInfo.InvariantCulture));
                    break;

                case AttributeType.Short:
                    writer.WriteNumberValue(Convert.ToInt16(attribute.Value.Value, CultureInfo.InvariantCulture));
                    break;

                case AttributeType.UShort:
                    writer.WriteNumberValue(Convert.ToUInt16(attribute.Value.Value, CultureInfo.InvariantCulture));
                    break;

                case AttributeType.Int:
                    writer.WriteNumberValue(Convert.ToInt32(attribute.Value.Value, CultureInfo.InvariantCulture));
                    break;

                case AttributeType.UInt:
                    writer.WriteNumberValue(Convert.ToUInt32(attribute.Value.Value, CultureInfo.InvariantCulture));
                    break;

                case AttributeType.Float:
                    writer.WriteNumberValue(Convert.ToSingle(attribute.Value.Value, CultureInfo.InvariantCulture));
                    break;

                case AttributeType.Double:
                    writer.WriteNumberValue(Convert.ToDouble(attribute.Value.Value, CultureInfo.InvariantCulture));
                    break;

                case AttributeType.Bool:
                    writer.WriteBooleanValue(Convert.ToBoolean(attribute.Value.Value, CultureInfo.InvariantCulture));
                    break;

                case AttributeType.String:
                case AttributeType.Path:
                case AttributeType.FixedString:
                case AttributeType.LSString:
                case AttributeType.WString:
                case AttributeType.LSWString:
                    writer.WriteStringValue(attribute.Value.Value?.ToString() ?? string.Empty);
                    break;

                case AttributeType.ULongLong:
                    writer.WriteNumberValue(Convert.ToUInt64(attribute.Value.Value, CultureInfo.InvariantCulture));
                    break;

                case AttributeType.ScratchBuffer:
                    writer.WriteStringValue(Convert.ToBase64String((byte[])(attribute.Value.Value ?? Array.Empty<byte>())));
                    break;

                case AttributeType.Long:
                case AttributeType.Int64:
                    writer.WriteNumberValue(Convert.ToInt64(attribute.Value.Value, CultureInfo.InvariantCulture));
                    break;

                case AttributeType.Int8:
                    writer.WriteNumberValue(Convert.ToSByte(attribute.Value.Value, CultureInfo.InvariantCulture));
                    break;

                case AttributeType.TranslatedString:
                    {
                        var ts = (TranslatedString)(attribute.Value.Value ?? new TranslatedString());

                        if (ts.Value is not null)
                        {
                            writer.WritePropertyName("value");
                            writer.WriteStringValue(ts.Value);
                        }

                        if (ts.Version > 0)
                        {
                            writer.WritePropertyName("version");
                            writer.WriteNumberValue(ts.Version);
                        }

                        writer.WritePropertyName("handle");
                        writer.WriteStringValue(ts.Handle);
                        break;
                    }

                case AttributeType.TranslatedFSString:
                    {
                        var fs = (TranslatedFSString)(attribute.Value.Value ?? new TranslatedFSString());
                        WriteTranslatedFSStringInner(writer, fs);
                        break;
                    }

                case AttributeType.UUID:
                    {
                        var guidVal = (Guid)(attribute.Value.Value ?? Guid.Empty);
                        writer.WriteStringValue(_serializationSettings.ByteSwapGuids
                            ? NodeAttribute.ByteSwapGuid(guidVal).ToString()
                            : guidVal.ToString());
                        break;
                    }

                case AttributeType.Vec2:
                case AttributeType.Vec3:
                case AttributeType.Vec4:
                    {
                        var vec = (float[])(attribute.Value.Value ?? Array.Empty<float>());
                        writer.WriteStringValue(string.Join(" ", vec));
                        break;
                    }

                case AttributeType.IVec2:
                case AttributeType.IVec3:
                case AttributeType.IVec4:
                    {
                        var ivec = (int[])(attribute.Value.Value ?? Array.Empty<int>());
                        writer.WriteStringValue(string.Join(" ", ivec));
                        break;
                    }

                case AttributeType.Mat2:
                case AttributeType.Mat3:
                case AttributeType.Mat3x4:
                case AttributeType.Mat4x3:
                case AttributeType.Mat4:
                    {
                        int cols = attribute.Value.Type switch
                        {
                            AttributeType.Mat2 => 2,
                            AttributeType.Mat3 or AttributeType.Mat3x4 => 3,
                            AttributeType.Mat4 or AttributeType.Mat4x3 => 4,
                            _ => 1
                        };
                        int rows = attribute.Value.Type switch
                        {
                            AttributeType.Mat2 => 2,
                            AttributeType.Mat3 => 3,
                            AttributeType.Mat4 or AttributeType.Mat3x4 => 4,
                            AttributeType.Mat4x3 => 3,
                            _ => 1
                        };
                        var mat = (Matrix)(attribute.Value.Value ?? new Matrix(rows, cols));
                        var sb = new StringBuilder();
                        for (int r = 0; r < mat.Rows; r++)
                        {
                            for (int c = 0; c < mat.Cols; c++)
                            {
                                sb.Append(mat[r, c].ToString(CultureInfo.InvariantCulture)).Append(' ');
                            }
                            sb.Append(' ');
                        }
                        writer.WriteStringValue(sb.ToString().TrimEnd());
                        break;
                    }

                case AttributeType.None:
                default:
                    throw new NotImplementedException($"Don't know how to serialize structural attribute layout parameter type {attribute.Value.Type}");
            }

            writer.WriteEndObject();
        }

        foreach (var children in node.Children)
        {
            writer.WritePropertyName(children.Key);
            writer.WriteStartArray();
            foreach (var child in children.Value)
            {
                if (child is not null) WriteNode(writer, child);
            }
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        _ = options;

        if (value is Node node)
        {
            WriteNode(writer, node);
        }
        else if (value is Resource resource)
        {
            WriteResource(writer, resource);
        }
        else
        {
            throw new InvalidOperationException("Cannot serialize unknown target configuration model data payload structure.");
        }
    }
}
using LSLib.LS.Enums;

namespace LSLib.LS;

public partial class LSFWriter(Stream stream)
{
    private readonly static int StringHashMapSize = 0x200;

    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    private BinaryWriter? _writer;
    private LSMetadata _meta = default;

    private MemoryStream? _nodeStream;
    private BinaryWriter? _nodeWriter;
    private int _nextNodeIndex;
    private Dictionary<Node, int> _nodeIndices = [];

    private MemoryStream? _attributeStream;
    private BinaryWriter? _attributeWriter;
    private int _nextAttributeIndex;

    private MemoryStream? _valueStream;
    private BinaryWriter? _valueWriter;

    private MemoryStream? _keyStream;
    private BinaryWriter? _keyWriter;

    private List<List<string>> _stringHashMap = [];
    private List<int>? _nextSiblingIndices;

    public LSFVersion Version { get; set; } = LSFVersion.MaxWriteVersion;
    public LSFMetadataFormat MetadataFormat { get; set; } = LSFMetadataFormat.None;
    public CompressionMethod Compression { get; set; } = CompressionMethod.None;
    public LSCompressionLevel CompressionLevel { get; set; } = LSCompressionLevel.Default;

    public void Write(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (Version > LSFVersion.MaxWriteVersion)
        {
            throw new InvalidDataException($"Writing LSF version {Version} is not supported (highest is {LSFVersion.MaxWriteVersion})");
        }

        _meta = resource.Metadata;

        using var rootWriter = new BinaryWriter(_stream, Encoding.Default, leaveOpen: true);
        _writer = rootWriter;

        using var nodeMs = new MemoryStream();
        _nodeStream = nodeMs;
        using var nodeBinWriter = new BinaryWriter(_nodeStream, Encoding.Default, leaveOpen: true);
        _nodeWriter = nodeBinWriter;

        using var attrMs = new MemoryStream();
        _attributeStream = attrMs;
        using var attrBinWriter = new BinaryWriter(_attributeStream, Encoding.Default, leaveOpen: true);
        _attributeWriter = attrBinWriter;

        using var valMs = new MemoryStream();
        _valueStream = valMs;
        using var valBinWriter = new BinaryWriter(_valueStream, Encoding.Default, leaveOpen: true);
        _valueWriter = valBinWriter;

        using var keyMs = new MemoryStream();
        _keyStream = keyMs;
        using var keyBinWriter = new BinaryWriter(_keyStream, Encoding.Default, leaveOpen: true);
        _keyWriter = keyBinWriter;

        _nextNodeIndex = 0;
        _nextAttributeIndex = 0;
        _nodeIndices = [];
        _nextSiblingIndices = null;

        _stringHashMap = new List<List<string>>(StringHashMapSize);
        while (_stringHashMap.Count < StringHashMapSize)
        {
            _stringHashMap.Add([]);
        }

        if (MetadataFormat != LSFMetadataFormat.None)
        {
            ComputeSiblingIndices(resource);
        }

        WriteRegions(resource);

        byte[] stringBuffer;
        using (var stringStream = new MemoryStream())
        using (var stringWriter = new BinaryWriter(stringStream, Encoding.Default, leaveOpen: true))
        {
            WriteStaticStrings(stringWriter);
            stringBuffer = stringStream.ToArray();
        }

        var nodeBuffer = _nodeStream.ToArray();
        var attributeBuffer = _attributeStream.ToArray();
        var valueBuffer = _valueStream.ToArray();
        var keyBuffer = _keyStream.ToArray();

        var magic = new LSFMagic
        {
            Magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(LSFMagic.Signature),
            Version = (uint)Version
        };
        BinUtils.WriteStruct(_writer, ref magic);

        var gameVersion = new PackedVersion
        {
            Major = resource.Metadata.MajorVersion,
            Minor = resource.Metadata.MinorVersion,
            Revision = resource.Metadata.Revision,
            Build = resource.Metadata.BuildNumber
        };

        if (Version < LSFVersion.VerBG3ExtendedHeader)
        {
            var header = new LSFHeader
            {
                EngineVersion = gameVersion.ToVersion32()
            };
            BinUtils.WriteStruct(_writer, ref header);
        }
        else
        {
            var header = new LSFHeaderV5
            {
                EngineVersion = gameVersion.ToVersion64()
            };
            BinUtils.WriteStruct(_writer, ref header);
        }

        bool chunked = Version >= LSFVersion.VerChunkedCompress;
        byte[] stringsCompressed = CompressionHelpers.Compress(stringBuffer, Compression, CompressionLevel);
        byte[] nodesCompressed = CompressionHelpers.Compress(nodeBuffer, Compression, CompressionLevel, chunked);
        byte[] attributesCompressed = CompressionHelpers.Compress(attributeBuffer, Compression, CompressionLevel, chunked);
        byte[] valuesCompressed = CompressionHelpers.Compress(valueBuffer, Compression, CompressionLevel, chunked);
        byte[] keysCompressed;

        if (MetadataFormat == LSFMetadataFormat.KeysAndAdjacency)
        {
            keysCompressed = CompressionHelpers.Compress(keyBuffer, Compression, CompressionLevel, chunked);
        }
        else
        {
            keysCompressed = [];
        }

        if (Version < LSFVersion.VerBG3NodeKeys)
        {
            var meta = new LSFMetadataV5
            {
                StringsUncompressedSize = (uint)stringBuffer.Length,
                NodesUncompressedSize = (uint)nodeBuffer.Length,
                AttributesUncompressedSize = (uint)attributeBuffer.Length,
                ValuesUncompressedSize = (uint)valueBuffer.Length
            };

            if (Compression == CompressionMethod.None)
            {
                meta.StringsSizeOnDisk = 0;
                meta.NodesSizeOnDisk = 0;
                meta.AttributesSizeOnDisk = 0;
                meta.ValuesSizeOnDisk = 0;
            }
            else
            {
                meta.StringsSizeOnDisk = (uint)stringsCompressed.Length;
                meta.NodesSizeOnDisk = (uint)nodesCompressed.Length;
                meta.AttributesSizeOnDisk = (uint)attributesCompressed.Length;
                meta.ValuesSizeOnDisk = (uint)valuesCompressed.Length;
            }

            meta.CompressionFlags = CompressionHelpers.MakeCompressionFlags(Compression, CompressionLevel);
            meta.Unknown2 = 0;
            meta.Unknown3 = 0;
            meta.MetadataFormat = MetadataFormat;

            BinUtils.WriteStruct(_writer, ref meta);
        }
        else
        {
            var meta = new LSFMetadataV6
            {
                StringsUncompressedSize = (uint)stringBuffer.Length,
                KeysUncompressedSize = (uint)keyBuffer.Length,
                NodesUncompressedSize = (uint)nodeBuffer.Length,
                AttributesUncompressedSize = (uint)attributeBuffer.Length,
                ValuesUncompressedSize = (uint)valueBuffer.Length
            };

            if (Compression == CompressionMethod.None)
            {
                meta.StringsSizeOnDisk = 0;
                meta.KeysSizeOnDisk = 0;
                meta.NodesSizeOnDisk = 0;
                meta.AttributesSizeOnDisk = 0;
                meta.ValuesSizeOnDisk = 0;
            }
            else
            {
                meta.StringsSizeOnDisk = (uint)stringsCompressed.Length;
                meta.KeysSizeOnDisk = (uint)keysCompressed.Length;
                meta.NodesSizeOnDisk = (uint)nodesCompressed.Length;
                meta.AttributesSizeOnDisk = (uint)attributesCompressed.Length;
                meta.ValuesSizeOnDisk = (uint)valuesCompressed.Length;
            }

            meta.CompressionFlags = CompressionHelpers.MakeCompressionFlags(Compression, CompressionLevel);
            meta.Unknown2 = 0;
            meta.Unknown3 = 0;
            meta.MetadataFormat = MetadataFormat;

            BinUtils.WriteStruct(_writer, ref meta);
        }

        _writer.Write(stringsCompressed, 0, stringsCompressed.Length);
    }

    private int ComputeSiblingIndices(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        int index = _nextNodeIndex;
        _nextNodeIndex++;
        _nextSiblingIndices?.Add(-1);

        int lastSiblingIndex = -1;
        foreach (var childList in node.Children.Values)
        {
            if (childList is null) continue;
            foreach (var child in childList)
            {
                int childIndex = ComputeSiblingIndices(child);
                if (lastSiblingIndex != -1 && _nextSiblingIndices is not null)
                {
                    _nextSiblingIndices[lastSiblingIndex] = childIndex;
                }

                lastSiblingIndex = childIndex;
            }
        }

        return index;
    }

    private void ComputeSiblingIndices(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        _nextNodeIndex = 0;
        _nextSiblingIndices = [];

        int lastRegionIndex = -1;
        foreach (var region in resource.Regions)
        {
            int regionIndex = ComputeSiblingIndices(region.Value);
            if (lastRegionIndex != -1)
            {
                _nextSiblingIndices[lastRegionIndex] = regionIndex;
            }

            lastRegionIndex = regionIndex;
        }
    }

    private void WriteRegions(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        _nextNodeIndex = 0;
        foreach (var region in resource.Regions)
        {
            if (Version >= LSFVersion.VerExtendedNodes
                && MetadataFormat == LSFMetadataFormat.KeysAndAdjacency)
            {
                WriteNodeV3(region.Value);
            }
            else
            {
                WriteNodeV2(region.Value);
            }
        }
    }

    private void WriteNodeAttributesV2(Node node)
    {
        if (_valueStream is null || _valueWriter is null || _attributeWriter is null) return;

        uint lastOffset = (uint)_valueStream.Position;
        foreach (KeyValuePair<string, NodeAttribute> entry in node.Attributes)
        {
            if (entry.Value is null) continue;
            WriteAttributeValue(_valueWriter, entry.Value);

            uint length = (uint)_valueStream.Position - lastOffset;
            uint staticStringId = AddStaticString(entry.Key);
            var attributeInfo = new LSFAttributeEntryV2
            {
                NameHashTableIndex = staticStringId,
                TypeAndLength = (uint)entry.Value.Type | (length << 6),
                NodeIndex = _nextNodeIndex
            };

            BinUtils.WriteStruct(_attributeWriter, ref attributeInfo);
            _nextAttributeIndex++;

            lastOffset = (uint)_valueStream.Position;
        }
    }

    private void WriteNodeAttributesV3(Node node)
    {
        if (_valueStream is null || _valueWriter is null || _attributeWriter is null) return;

        uint lastOffset = (uint)_valueStream.Position;
        int numWritten = 0;
        foreach (KeyValuePair<string, NodeAttribute> entry in node.Attributes)
        {
            if (entry.Value is null) continue;
            WriteAttributeValue(_valueWriter, entry.Value);
            numWritten++;

            uint length = (uint)_valueStream.Position - lastOffset;
            uint staticStringId = AddStaticString(entry.Key);
            int nextAttrIndex = (numWritten == node.Attributes.Count) ? -1 : _nextAttributeIndex + 1;
            var attributeInfo = new LSFAttributeEntryV3
            {
                NameHashTableIndex = staticStringId,
                TypeAndLength = (uint)entry.Value.Type | (length << 6),
                Offset = lastOffset,
                NextAttributeIndex = nextAttrIndex
            };

            BinUtils.WriteStruct(_attributeWriter, ref attributeInfo);
            _nextAttributeIndex++;

            lastOffset = (uint)_valueStream.Position;
        }
    }


    private void WriteNodeChildren(Node node)
    {
        foreach (var childList in node.Children.Values)
        {
            if (childList is null) continue;
            foreach (var child in childList)
            {
                if (Version >= LSFVersion.VerExtendedNodes
                    && MetadataFormat == LSFMetadataFormat.KeysAndAdjacency)
                {
                    WriteNodeV3(child);
                }
                else
                {
                    WriteNodeV2(child);
                }
            }
        }
    }

    private void WriteNodeV2(Node node)
    {
        if (_nodeWriter is null) return;

        var nodeInfo = new LSFNodeEntryV2();
        if (node.Parent is null)
        {
            nodeInfo.ParentIndex = -1;
        }
        else
        {
            nodeInfo.ParentIndex = _nodeIndices[node.Parent];
        }

        nodeInfo.NameHashTableIndex = AddStaticString(node.Name);

        if (node.Attributes.Count > 0)
        {
            nodeInfo.FirstAttributeIndex = _nextAttributeIndex;
            WriteNodeAttributesV2(node);
        }
        else
        {
            nodeInfo.FirstAttributeIndex = -1;
        }

        BinUtils.WriteStruct(_nodeWriter, ref nodeInfo);
        _nodeIndices[node] = _nextNodeIndex;
        _nextNodeIndex++;

        WriteNodeChildren(node);
    }

    private void WriteNodeV3(Node node)
    {
        if (_nodeWriter is null || _keyWriter is null || _nextSiblingIndices is null) return;

        var nodeInfo = new LSFNodeEntryV3();
        if (node.Parent is null)
        {
            nodeInfo.ParentIndex = -1;
        }
        else
        {
            nodeInfo.ParentIndex = _nodeIndices[node.Parent];
        }

        nodeInfo.NameHashTableIndex = AddStaticString(node.Name);
        nodeInfo.NextSiblingIndex = _nextSiblingIndices[_nextNodeIndex];

        if (node.Attributes.Count > 0)
        {
            nodeInfo.FirstAttributeIndex = _nextAttributeIndex;
            WriteNodeAttributesV3(node);
        }
        else
        {
            nodeInfo.FirstAttributeIndex = -1;
        }

        BinUtils.WriteStruct(_nodeWriter, ref nodeInfo);

        if (node.KeyAttribute is not null && MetadataFormat == LSFMetadataFormat.KeysAndAdjacency)
        {
            var keyInfo = new LSFKeyEntry
            {
                NodeIndex = (uint)_nextNodeIndex,
                KeyName = AddStaticString(node.KeyAttribute)
            };
            BinUtils.WriteStruct(_keyWriter, ref keyInfo);
        }

        _nodeIndices[node] = _nextNodeIndex;
        _nextNodeIndex++;

        WriteNodeChildren(node);
    }

    private void WriteTranslatedFSString(BinaryWriter writer, TranslatedFSString fs)
    {
        if (Version >= LSFVersion.VerBG3 ||
            (_meta.MajorVersion > 4 ||
            (_meta.MajorVersion == 4 && _meta.Revision > 0) ||
            (_meta.MajorVersion == 4 && _meta.Revision == 0 && _meta.BuildNumber >= 0x1a)))
        {
            writer.Write(fs.Version);
        }
        else
        {
            WriteStringWithLength(writer, fs.Value ?? string.Empty);
        }

        WriteStringWithLength(writer, fs.Handle);

        writer.Write((uint)fs.Arguments.Count);
        foreach (var arg in fs.Arguments)
        {
            WriteStringWithLength(writer, arg.Key);
            WriteTranslatedFSString(writer, arg.String);
            WriteStringWithLength(writer, arg.Value);
        }
    }

    private void WriteAttributeValue(BinaryWriter writer, NodeAttribute attr)
    {
        switch (attr.Type)
        {
            case AttributeType.String:
            case AttributeType.Path:
            case AttributeType.FixedString:
            case AttributeType.LSString:
            case AttributeType.WString:
            case AttributeType.LSWString:
                WriteString(writer, (string)(attr.Value ?? string.Empty));
                break;

            case AttributeType.TranslatedString:
                {
                    var ts = (TranslatedString)(attr.Value ?? new TranslatedString());
                    if (Version >= LSFVersion.VerBG3)
                    {
                        writer.Write(ts.Version);
                    }
                    else
                    {
                        WriteStringWithLength(writer, ts.Value ?? string.Empty);
                    }

                    WriteStringWithLength(writer, ts.Handle);
                    break;
                }

            case (AttributeType)9: // Evaluates mapping for AttributeType.TranslatedFSString
                {
                    var fs = (TranslatedFSString)(attr.Value ?? new TranslatedFSString());
                    WriteTranslatedFSString(writer, fs);
                    break;
                }

            case AttributeType.ScratchBuffer:
                {
                    var buffer = (byte[])(attr.Value ?? Array.Empty<byte>());
                    writer.Write(buffer);
                    break;
                }

            default:
                BinUtils.WriteAttribute(writer, attr);
                break;
        }
    }

    private uint AddStaticString(string s)
    {
        uint hashCode = (uint)s.GetHashCode(StringComparison.Ordinal);
        int bucket = (int)((hashCode & 0x1ff) ^ ((hashCode >> 9) & 0x1ff) ^ ((hashCode >> 18) & 0x1ff) ^ ((hashCode >> 27) & 0x1ff));
        for (int i = 0; i < _stringHashMap[bucket].Count; i++)
        {
            if (_stringHashMap[bucket][i].Equals(s, StringComparison.Ordinal))
            {
                return (uint)((bucket << 16) | i);
            }
        }

        _stringHashMap[bucket].Add(s);
        return (uint)((bucket << 16) | (_stringHashMap[bucket].Count - 1));
    }

    private void WriteStaticStrings(BinaryWriter writer)
    {
        writer.Write((uint)_stringHashMap.Count);
        for (int i = 0; i < _stringHashMap.Count; i++)
        {
            var entry = _stringHashMap[i];
            writer.Write((ushort)entry.Count);
            for (int j = 0; j < entry.Count; j++)
            {
                WriteStaticString(writer, entry[j]);
            }
        }
    }

    private static void WriteStaticString(BinaryWriter writer, string s)
    {
        byte[] utf = Encoding.UTF8.GetBytes(s);
        writer.Write((ushort)utf.Length);
        writer.Write(utf);
    }

    private static void WriteStringWithLength(BinaryWriter writer, string s)
    {
        byte[] utf = Encoding.UTF8.GetBytes(s);
        writer.Write(utf.Length + 1);
        writer.Write(utf);
        writer.Write((byte)0);
    }

    private static void WriteString(BinaryWriter writer, string s)
    {
        byte[] utf = Encoding.UTF8.GetBytes(s);
        writer.Write(utf);
        writer.Write((byte)0);
    }
}
// #define DEBUG_LSF_SERIALIZATION
// #define DUMP_LSF_SERIALIZATION

using LSLib.LS.Enums;
using System.Reflection;

namespace LSLib.LS.Resources.LSF;

public partial class LSFReader(Stream stream, bool keepOpen = false) : IDisposable
{
    /// <summary>
    /// Input stream
    /// </summary>
    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    private readonly bool _keepOpen = keepOpen;

    /// <summary>
    /// Static string hash map
    /// </summary>
    private List<List<string>> _names = [];
    /// <summary>
    /// Preprocessed list of nodes (structures)
    /// </summary>
    private List<LSFNodeInfo> _nodes = [];
    /// <summary>
    /// Preprocessed list of node attributes
    /// </summary>
    private List<LSFAttributeInfo> _attributes = [];
    /// <summary>
    /// Node instances
    /// </summary>
    private List<Node> _nodeInstances = [];
    /// <summary>
    /// Raw value data stream
    /// </summary>
    private Stream? _values;
    /// <summary>
    /// Version of the file we're serializing
    /// </summary>
    private LSFVersion _version;
    /// <summary>
    /// Game version that generated the LSF file
    /// </summary>
    private PackedVersion _gameVersion;
    private LSFMetadataV6 _metadata;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (!_keepOpen)
            {
                _stream.Dispose();
            }
            _values?.Dispose();
        }
    }

    /// <summary>
    /// Reads the static string hash table from the specified stream.
    /// </summary>
    /// <param name="s">Stream to read the hash table from</param>
    /// 
    /// Format:
    /// 32-bit hash entry count (N)
    /// N x 16-bit chain length (L)
    /// L x 16-bit string length (S)
    /// [S bytes of UTF-8 string data]
    private void ReadNames(Stream s)
    {
        ArgumentNullException.ThrowIfNull(s);

#if DEBUG_LSF_SERIALIZATION
        Debug.WriteLine(" ----- DUMP OF NAME TABLE -----");
#endif

        using var reader = new BinaryReader(s, Encoding.UTF8, leaveOpen: true);
        uint numHashEntries = reader.ReadUInt32();
        while (numHashEntries-- > 0)
        {
            var hash = new List<string>();
            _names.Add(hash);

            ushort numStrings = reader.ReadUInt16();
            while (numStrings-- > 0)
            {
                ushort nameLen = reader.ReadUInt16();
                byte[] bytes = reader.ReadBytes(nameLen);
                string name = Encoding.UTF8.GetString(bytes);
                hash.Add(name);
#if DEBUG_LSF_SERIALIZATION
                Debug.WriteLine(string.Format("{0,3:X}/{1}: {2}", _names.Count - 1, hash.Count - 1, name));
#endif
            }
        }
    }

    /// <summary>
    /// Reads the structure headers for the LSOF resource
    /// </summary>
    /// <param name="s">Stream to read the node headers from</param>
    /// <param name="longNodes">Use the long (V3) on-disk node format</param>
    private void ReadNodes(Stream s, bool longNodes)
    {
        ArgumentNullException.ThrowIfNull(s);

#if DEBUG_LSF_SERIALIZATION
        Debug.WriteLine(" ----- DUMP OF NODE TABLE -----");
#endif

        using var reader = new BinaryReader(s, Encoding.UTF8, leaveOpen: true);
        int index = 0;
        while (s.Position < s.Length)
        {
            var resolved = new LSFNodeInfo();
#if DEBUG_LSF_SERIALIZATION
            long pos = s.Position;
#endif

            if (longNodes)
            {
                var item = BinUtils.ReadStruct<LSFNodeEntryV3>(reader);
                resolved.ParentIndex = item.ParentIndex;
                resolved.NameIndex = item.NameIndex;
                resolved.NameOffset = item.NameOffset;
                resolved.FirstAttributeIndex = item.FirstAttributeIndex;
            }
            else
            {
                var item = BinUtils.ReadStruct<LSFNodeEntryV2>(reader);
                resolved.ParentIndex = item.ParentIndex;
                resolved.NameIndex = item.NameIndex;
                resolved.NameOffset = item.NameOffset;
                resolved.FirstAttributeIndex = item.FirstAttributeIndex;
            }

#if DEBUG_LSF_SERIALIZATION
            Debug.WriteLine(string.Format(
                "{0}: {1} @ {2:X} (parent {3}, firstAttribute {4})",
                index, _names[resolved.NameIndex][resolved.NameOffset], pos, resolved.ParentIndex,
                resolved.FirstAttributeIndex
            ));
#endif

            _nodes.Add(resolved);
            index++;
        }
    }

    /// <summary>
    /// Reads the V2 attribute headers for the LSOF resource
    /// </summary>
    /// <param name="s">Stream to read the node headers from</param>
    private void ReadAttributesV2(Stream s)
    {
        ArgumentNullException.ThrowIfNull(s);

        using var reader = new BinaryReader(s, Encoding.UTF8, leaveOpen: true);
#if DEBUG_LSF_SERIALIZATION
        var rawAttributes = new List<LSFAttributeEntryV2>();
#endif

        var prevAttributeRefs = new List<int>();
        uint dataOffset = 0;
        int index = 0;
        while (s.Position < s.Length)
        {
            var attribute = BinUtils.ReadStruct<LSFAttributeEntryV2>(reader);

            var resolved = new LSFAttributeInfo
            {
                NameIndex = attribute.NameIndex,
                NameOffset = attribute.NameOffset,
                TypeId = attribute.TypeId,
                Length = attribute.Length,
                DataOffset = dataOffset,
                NextAttributeIndex = -1
            };

            int nodeIndex = attribute.NodeIndex + 1;
            if (prevAttributeRefs.Count > nodeIndex)
            {
                if (prevAttributeRefs[nodeIndex] != -1)
                {
                    _attributes[prevAttributeRefs[nodeIndex]].NextAttributeIndex = index;
                }

                prevAttributeRefs[nodeIndex] = index;
            }
            else
            {
                while (prevAttributeRefs.Count < nodeIndex)
                {
                    prevAttributeRefs.Add(-1);
                }

                prevAttributeRefs.Add(index);
            }

#if DEBUG_LSF_SERIALIZATION
            rawAttributes.Add(attribute);
#endif

            dataOffset += resolved.Length;
            _attributes.Add(resolved);
            index++;
        }

#if DEBUG_LSF_SERIALIZATION
        Debug.WriteLine(" ----- DUMP OF ATTRIBUTE REFERENCES -----");
        for (int i = 0; i < prevAttributeRefs.Count; i++)
        {
            Debug.WriteLine(string.Format("Node {0}: last attribute {1}", i, prevAttributeRefs[i]));
        }

        Debug.WriteLine(" ----- DUMP OF V2 ATTRIBUTE TABLE -----");
        for (int i = 0; i < _attributes.Count; i++)
        {
            var resolved = _attributes[i];
            var attribute = rawAttributes[i];

            var debug = string.Format(
                "{0}: {1} (offset {2:X}, typeId {3}, nextAttribute {4}, node {5})",
                i, _names[resolved.NameIndex][resolved.NameOffset], resolved.DataOffset,
                resolved.TypeId, resolved.NextAttributeIndex, attribute.NodeIndex
            );
            Debug.WriteLine(debug);
        }
#endif
    }

    /// <summary>
    /// Reads the V3 attribute headers for the LSOF resource
    /// </summary>
    /// <param name="s">Stream to read the attribute headers from</param>
    private void ReadAttributesV3(Stream s)
    {
        ArgumentNullException.ThrowIfNull(s);
        using var reader = new BinaryReader(s, Encoding.UTF8, leaveOpen: true);

        while (s.Position < s.Length)
        {
            var attribute = BinUtils.ReadStruct<LSFAttributeEntryV3>(reader);

            var resolved = new LSFAttributeInfo
            {
                NameIndex = attribute.NameIndex,
                NameOffset = attribute.NameOffset,
                TypeId = attribute.TypeId,
                Length = attribute.Length,
                DataOffset = attribute.Offset,
                NextAttributeIndex = attribute.NextAttributeIndex
            };

            _attributes.Add(resolved);
        }

#if DEBUG_LSF_SERIALIZATION
        Debug.WriteLine(" ----- DUMP OF V3 ATTRIBUTE TABLE -----");
        for (int i = 0; i < _attributes.Count; i++)
        {
            var resolved = _attributes[i];

            var debug = String.Format(
                "{0}: {1} (offset {2:X}, typeId {3}, length {4}, nextAttribute {5})",
                i, _names[resolved.NameIndex][resolved.NameOffset], resolved.DataOffset,
                resolved.TypeId, resolved.Length, resolved.NextAttributeIndex
            );
            Debug.WriteLine(debug);
        }
#endif
    }

    /// <summary>
    /// Reads the V3 attribute headers for the LSOF resource
    /// </summary>
    /// <param name="s">Stream to read the attribute headers from</param>
    private void ReadKeys(Stream s)
    {
        ArgumentNullException.ThrowIfNull(s);
        using var reader = new BinaryReader(s, Encoding.UTF8, leaveOpen: true);

#if DEBUG_LSF_SERIALIZATION
        Debug.WriteLine(" ----- DUMP OF KEY TABLE -----");
#endif

        while (s.Position < s.Length)
        {
            var key = BinUtils.ReadStruct<LSFKeyEntry>(reader);
            var KeyAttribute = _names[key.KeyNameIndex][key.KeyNameOffset];
            var node = _nodes[(int)key.NodeIndex];
            node.KeyAttribute = KeyAttribute;

#if DEBUG_LSF_SERIALIZATION
            var debug = String.Format(
                "{0} ({1}): {2}",
                key.NodeIndex, _names[node.NameIndex][node.NameOffset], KeyAttribute
            );
            Debug.WriteLine(debug);
#endif
        }
    }

    private MemoryStream Decompress(BinaryReader reader, uint sizeOnDisk, uint uncompressedSize, string debugDumpTo, bool allowChunked)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _ = debugDumpTo;

        if (sizeOnDisk == 0 && uncompressedSize != 0)
        {
            var buf = reader.ReadBytes((int)uncompressedSize);

#if DUMP_LSF_SERIALIZATION
        using (var nodesFile = new FileStream(debugDumpTo, FileMode.Create, FileAccess.Write))
        {
            nodesFile.Write(buf, 0, buf.Length);
        }
#endif

            return new MemoryStream(buf);
        }

        if (sizeOnDisk == 0 && uncompressedSize == 0)
        {
            return new MemoryStream();
        }

        bool chunked = (_version >= LSFVersion.VerChunkedCompress && allowChunked);

        bool isCompressed = _metadata.CompressionFlags.Method() != CompressionMethod.None;

        uint compressedSize = isCompressed ? sizeOnDisk : uncompressedSize;
        byte[] compressed = reader.ReadBytes((int)compressedSize);
        var uncompressed = CompressionHelpers.Decompress(compressed, (int)uncompressedSize, _metadata.CompressionFlags, chunked);

#if DUMP_LSF_SERIALIZATION
    using (var nodesFile = new FileStream(debugDumpTo, FileMode.Create, FileAccess.Write))
    {
        nodesFile.Write(uncompressed, 0, uncompressed.Length);
    }
#endif

        return new MemoryStream(uncompressed);
    }

    private void ReadHeaders(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var magic = BinUtils.ReadStruct<LSFMagic>(reader);
        if (magic.Magic != BitConverter.ToUInt32(LSFMagic.Signature))
        {
            var msg = string.Format(
                "Invalid LSF signature; expected {0,8:X}, got {1,8:X}",
                BitConverter.ToUInt32(LSFMagic.Signature), magic.Magic
            );
            throw new InvalidDataException(msg);
        }

        if (magic.Version < (ulong)LSFVersion.VerInitial || magic.Version > (ulong)LSFVersion.MaxReadVersion)
        {
            var msg = string.Format("LSF version {0} is not supported", magic.Version);
            throw new InvalidDataException(msg);
        }

        _version = (LSFVersion)magic.Version;

        if (_version >= LSFVersion.VerBG3ExtendedHeader)
        {
            var hdr = BinUtils.ReadStruct<LSFHeaderV5>(reader);
            _gameVersion = PackedVersion.FromInt64(hdr.EngineVersion);

            // Workaround for merged LSF files with missing engine version number
            if (_gameVersion.Major == 0)
            {
                _gameVersion.Major = 4;
                _gameVersion.Minor = 0;
                _gameVersion.Revision = 9;
                _gameVersion.Build = 0;
            }
        }
        else
        {
            var hdr = BinUtils.ReadStruct<LSFHeader>(reader);
            _gameVersion = PackedVersion.FromInt32(hdr.EngineVersion);
        }

        if (_version < LSFVersion.VerBG3NodeKeys)
        {
            var meta = BinUtils.ReadStruct<LSFMetadataV5>(reader);
            _metadata = new LSFMetadataV6
            {
                StringsUncompressedSize = meta.StringsUncompressedSize,
                StringsSizeOnDisk = meta.StringsSizeOnDisk,
                NodesUncompressedSize = meta.NodesUncompressedSize,
                NodesSizeOnDisk = meta.NodesSizeOnDisk,
                AttributesUncompressedSize = meta.AttributesUncompressedSize,
                AttributesSizeOnDisk = meta.AttributesSizeOnDisk,
                ValuesUncompressedSize = meta.ValuesUncompressedSize,
                ValuesSizeOnDisk = meta.ValuesSizeOnDisk,
                CompressionFlags = meta.CompressionFlags,
                MetadataFormat = meta.MetadataFormat
            };
        }
        else
        {
            _metadata = BinUtils.ReadStruct<LSFMetadataV6>(reader);
        }
    }

    public Resource Read()
    {
        // Fix: Use explicitly referenced _stream field to sidestep primary constructor capture parameters bugs
        using var reader = new BinaryReader(_stream, Encoding.UTF8, leaveOpen: true);
        ReadHeaders(reader);

        _names = [];
        var namesStream = Decompress(reader, _metadata.StringsSizeOnDisk, _metadata.StringsUncompressedSize, "strings.bin", false);
        using (namesStream)
        {
            ReadNames(namesStream);
        }

        _nodes = [];
        var nodesStream = Decompress(reader, _metadata.NodesSizeOnDisk, _metadata.NodesUncompressedSize, "nodes.bin", true);
        using (nodesStream)
        {
            bool hasAdjacencyData = _version >= LSFVersion.VerExtendedNodes
                && _metadata.MetadataFormat == LSFMetadataFormat.KeysAndAdjacency;
            ReadNodes(nodesStream, hasAdjacencyData);
        }

        _attributes = [];
        var attributesStream = Decompress(reader, _metadata.AttributesSizeOnDisk, _metadata.AttributesUncompressedSize, "attributes.bin", true);
        using (attributesStream)
        {
            bool hasAdjacencyData = _version >= LSFVersion.VerExtendedNodes
                && _metadata.MetadataFormat == LSFMetadataFormat.KeysAndAdjacency;
            if (hasAdjacencyData)
            {
                ReadAttributesV3(attributesStream);
            }
            else
            {
                ReadAttributesV2(attributesStream);
            }
        }

        _values = Decompress(reader, _metadata.ValuesSizeOnDisk, _metadata.ValuesUncompressedSize, "values.bin", true);

        if (_metadata.MetadataFormat == LSFMetadataFormat.KeysAndAdjacency)
        {
            var keysStream = Decompress(reader, _metadata.KeysSizeOnDisk, _metadata.KeysUncompressedSize, "keys.bin", true);
            using (keysStream)
            {
                ReadKeys(keysStream);
            }
        }

        var resource = new Resource();

        var metadataFormatProp = resource.GetType().GetProperty("MetadataFormat", BindingFlags.Public | BindingFlags.Instance)
                                 ?? resource.GetType().GetProperty("_metadataFormat", BindingFlags.NonPublic | BindingFlags.Instance);
        metadataFormatProp?.SetValue(resource, _metadata.MetadataFormat);

        ReadRegions(resource);

        var resourceMetadataProp = resource.GetType().GetProperty("Metadata", BindingFlags.Public | BindingFlags.Instance)
                                    ?? resource.GetType().GetProperty("_metadata", BindingFlags.NonPublic | BindingFlags.Instance);
        var metaObj = resourceMetadataProp?.GetValue(resource);

        if (metaObj is not null)
        {
            var mType = metaObj.GetType();
            mType.GetProperty("MajorVersion")?.SetValue(metaObj, _gameVersion.Major);
            mType.GetProperty("MinorVersion")?.SetValue(metaObj, _gameVersion.Minor);
            mType.GetProperty("Revision")?.SetValue(metaObj, _gameVersion.Revision);
            mType.GetProperty("BuildNumber")?.SetValue(metaObj, _gameVersion.Build);
        }

        return resource;
    }

    private void ReadRegions(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (_values is null)
        {
            throw new InvalidOperationException("Cannot deserialize LSF regions block before initializing underlying value tracking streams.");
        }

        var attrReader = new BinaryReader(_values, Encoding.UTF8, leaveOpen: true);
        _nodeInstances = [];
        for (int i = 0; i < _nodes.Count; i++)
        {
            var defn = _nodes[i];
            if (defn.ParentIndex == -1)
            {
                var region = new Region();
                ReadNode(defn, region, attrReader);
                region.KeyAttribute = defn.KeyAttribute;
                _nodeInstances.Add(region);
                region.RegionName = region.Name;
                resource.Regions[region.Name] = region;
            }
            else
            {
                var node = new Node();
                ReadNode(defn, node, attrReader);
                node.KeyAttribute = defn.KeyAttribute;
                node.Parent = _nodeInstances[defn.ParentIndex];
                _nodeInstances.Add(node);
                _nodeInstances[defn.ParentIndex].AppendChild(node);
            }
        }
    }

    private void ReadNode(LSFNodeInfo defn, Node node, BinaryReader attributeReader)
    {
        node.Name = _names[defn.NameIndex][defn.NameOffset];

#if DEBUG_LSF_SERIALIZATION
        Debug.WriteLine(string.Format("Begin node {0}", node.Name));
        var debugSerializationSettings = new NodeSerializationSettings();
#endif

        if (defn.FirstAttributeIndex != -1)
        {
            var attribute = _attributes[defn.FirstAttributeIndex];
            while (true)
            {
                _values?.Position = attribute.DataOffset;

                var value = ReadAttribute((AttributeType)attribute.TypeId, attributeReader, attribute.Length);
                node.Attributes[_names[attribute.NameIndex][attribute.NameOffset]] = value;

#if DEBUG_LSF_SERIALIZATION
                Debug.WriteLine(string.Format("    {0:X}: {1} ({2})", attribute.DataOffset, _names[attribute.NameIndex][attribute.NameOffset], value.AsString(debugSerializationSettings)));
#endif

                if (attribute.NextAttributeIndex == -1)
                {
                    break;
                }

                attribute = _attributes[attribute.NextAttributeIndex];
            }
        }
    }

    // LSF and LSB serialize the buffer types differently, so specialized
    // code is added to the LSB and LSf serializers, and the common code is
    // available in BinUtils.ReadAttribute()
    private NodeAttribute ReadAttribute(AttributeType type, BinaryReader reader, uint length)
    {
        switch (type)
        {
            case AttributeType.String:
            case AttributeType.Path:
            case AttributeType.FixedString:
            case AttributeType.LSString:
            case AttributeType.WString:
            case AttributeType.LSWString:
                {
                    return new NodeAttribute(type)
                    {
                        Value = ReadString(reader, (int)length)
                    };
                }

            case AttributeType.TranslatedString:
                {
                    var attr = new NodeAttribute(type);
                    var str = new TranslatedString();

                    if (_version >= LSFVersion.VerBG3 ||
                        _gameVersion.Major > 4 ||
                        (_gameVersion.Major == 4 && _gameVersion.Revision > 0) ||
                        (_gameVersion.Major == 4 && _gameVersion.Revision == 0 && _gameVersion.Build >= 0x1a))
                    {
                        str.Version = reader.ReadUInt16();
                    }
                    else
                    {
                        str.Version = 0;
                        int valueLength = reader.ReadInt32();
                        str.Value = ReadString(reader, valueLength);
                    }

                    int handleLength = reader.ReadInt32();
                    str.Handle = ReadString(reader, handleLength);

                    attr.Value = str;
                    return attr;
                }

            case AttributeType.TranslatedFSString:
                {
                    return new NodeAttribute(type)
                    {
                        Value = ReadTranslatedFSString(reader)
                    };
                }

            case AttributeType.ScratchBuffer:
                {
                    return new NodeAttribute(type)
                    {
                        Value = reader.ReadBytes((int)length)
                    };
                }

            default:
                return BinUtils.ReadAttribute(type, reader);
        }
    }

    private TranslatedFSString ReadTranslatedFSString(BinaryReader reader)
    {
        var str = new TranslatedFSString();

        if (_version >= LSFVersion.VerBG3)
        {
            str.Version = reader.ReadUInt16();
        }
        else
        {
            str.Version = 0;
            int valueLength = reader.ReadInt32();
            str.Value = ReadString(reader, valueLength);
        }

        int handleLength = reader.ReadInt32();
        str.Handle = ReadString(reader, handleLength);

        int arguments = reader.ReadInt32();
        str.Arguments = new List<TranslatedFSStringArgument>(arguments);
        for (int i = 0; i < arguments; i++)
        {
            var arg = new TranslatedFSStringArgument();
            int argKeyLength = reader.ReadInt32();
            arg.Key = ReadString(reader, argKeyLength);

            arg.String = ReadTranslatedFSString(reader);

            int argValueLength = reader.ReadInt32();
            arg.Value = ReadString(reader, argValueLength);

            str.Arguments.Add(arg);
        }

        return str;
    }

    private static string ReadString(BinaryReader reader, int length)
    {
        var bytes = reader.ReadBytes(length - 1);

        int lastNull = bytes.Length;
        while (lastNull > 0 && bytes[lastNull - 1] == 0)
            lastNull--;

        byte nullTerminator = reader.ReadByte();
        if (nullTerminator != 0)
        {
            throw new InvalidDataException("String segment extraction break error: Target stream slice is not null-terminated.");
        }

        return Encoding.UTF8.GetString(bytes, 0, lastNull);
    }

    private static string ReadString(BinaryReader reader)
    {
        var bytes = new List<byte>();
        while (true)
        {
            byte b = reader.ReadByte();
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
}
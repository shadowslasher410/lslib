using LSLib.LS.Enums;

namespace LSLib.LS;

public enum LSFMetadataFormat : uint
{
    None = 0,
    KeysAndAdjacency = 1,
    None2 = 2 // Behaves same way as None
};

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct LSFMagic
{
    /// <summary>
    /// LSOF file signature
    /// </summary>
    public static ReadOnlySpan<byte> Signature => [0x4C, 0x53, 0x4F, 0x46];

    /// <summary>
    /// LSOF file signature; should be the same as LSFHeader.Signature
    /// </summary>
    public uint Magic;

    /// <summary>
    /// Version of the LSOF file; D:OS EE is version 1/2, D:OS 2 is version 3
    /// </summary>
    public uint Version;
};

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct LSFHeader
{
    /// <summary>
    /// Possibly version number? (major, minor, rev, build)
    /// </summary>
    public int EngineVersion;
};

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct LSFHeaderV5
{
    /// <summary>
    /// Possibly version number? (major, minor, rev, build)
    /// </summary>
    public long EngineVersion;
};

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct LSFMetadataV5
{
    /// <summary>
    /// Total uncompressed size of the string hash table
    /// </summary>
    public uint StringsUncompressedSize;
    /// <summary>
    /// Compressed size of the string hash table
    /// </summary>
    public uint StringsSizeOnDisk;
    /// <summary>
    /// Total uncompressed size of the node list
    /// </summary>
    public uint NodesUncompressedSize;
    /// <summary>
    /// Compressed size of the node list
    /// </summary>
    public uint NodesSizeOnDisk;
    /// <summary>
    /// Total uncompressed size of the attribute list
    /// </summary>
    public uint AttributesUncompressedSize;
    /// <summary>
    /// Compressed size of the attribute list
    /// </summary>
    public uint AttributesSizeOnDisk;
    /// <summary>
    /// Total uncompressed size of the raw value buffer
    /// </summary>
    public uint ValuesUncompressedSize;
    /// <summary>
    /// Compressed size of the raw value buffer
    /// </summary>
    public uint ValuesSizeOnDisk;
    /// <summary>
    /// Compression method and level used for the string, node, attribute and value buffers.
    /// Uses the same format as packages (see BinUtils.MakeCompressionFlags)
    /// </summary>
    public CompressionFlags CompressionFlags;
    /// <summary>
    /// Possibly unused, always 0
    /// </summary>
    public byte Unknown2;
    public ushort Unknown3;
    /// <summary>
    /// Extended node/attribute format indicator, 0 for V2, 0/1 for V3
    /// </summary>
    public LSFMetadataFormat MetadataFormat;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct LSFMetadataV6
{
    /// <summary>
    /// Total uncompressed size of the string hash table
    /// </summary>
    public uint StringsUncompressedSize;
    /// <summary>
    /// Compressed size of the string hash table
    /// </summary>
    public uint StringsSizeOnDisk;
    /// <summary>
    /// Total uncompressed size of the node key attribute table
    /// </summary>
    public uint KeysUncompressedSize;
    /// <summary>
    /// Compressed size of the node key attribute table
    /// </summary>
    public uint KeysSizeOnDisk;
    /// <summary>
    /// Total uncompressed size of the node list
    /// </summary>
    public uint NodesUncompressedSize;
    /// <summary>
    /// Compressed size of the node list
    /// </summary>
    public uint NodesSizeOnDisk;
    /// <summary>
    /// Total uncompressed size of the attribute list
    /// </summary>
    public uint AttributesUncompressedSize;
    /// <summary>
    /// Compressed size of the attribute list
    /// </summary>
    public uint AttributesSizeOnDisk;
    /// <summary>
    /// Total uncompressed size of the raw value buffer
    /// </summary>
    public uint ValuesUncompressedSize;
    /// <summary>
    /// Compressed size of the raw value buffer
    /// </summary>
    public uint ValuesSizeOnDisk;
    /// <summary>
    /// Compression method and level used for the string, node, attribute and value buffers.
    /// Uses the same format as packages (see BinUtils.MakeCompressionFlags)
    /// </summary>
    public CompressionFlags CompressionFlags;
    /// <summary>
    /// Possibly unused, always 0
    /// </summary>
    public byte Unknown2;
    public ushort Unknown3;
    /// <summary>
    /// Extended node/attribute format indicator
    /// </summary>
    public LSFMetadataFormat MetadataFormat;
}

/// <summary>
/// Node (structure) entry in the LSF file
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct LSFNodeEntryV2
{
    /// <summary>
    /// Name of this node
    /// (16-bit MSB: index into name hash table, 16-bit LSB: offset in hash chain)
    /// </summary>
    public uint NameHashTableIndex;
    /// <summary>
    /// Index of the first attribute of this node
    /// (-1: node has no attributes)
    /// </summary>
    public int FirstAttributeIndex;
    /// <summary>
    /// Index of the parent node
    /// (-1: this node is a root region)
    /// </summary>
    public int ParentIndex;

    /// <summary>
    /// Index into name hash table
    /// </summary>
    public readonly int NameIndex => (int)(NameHashTableIndex >> 16);

    /// <summary>
    /// Offset in hash chain
    /// </summary>
    public readonly int NameOffset => (int)(NameHashTableIndex & 0xffff);
};

/// <summary>
/// Node (structure) entry in the LSF file
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct LSFNodeEntryV3
{
    /// <summary>
    /// Name of this node
    /// (16-bit MSB: index into name hash table, 16-bit LSB: offset in hash chain)
    /// </summary>
    public uint NameHashTableIndex;
    /// <summary>
    /// Index of the parent node
    /// (-1: this node is a root region)
    /// </summary>
    public int ParentIndex;
    /// <summary>
    /// Index of the next sibling of this node
    /// (-1: this is the last node)
    /// </summary>
    public int NextSiblingIndex;
    /// <summary>
    /// Index of the first attribute of this node
    /// (-1: node has no attributes)
    /// </summary>
    public int FirstAttributeIndex;

    /// <summary>
    /// Index into name hash table
    /// </summary>
    public readonly int NameIndex => (int)(NameHashTableIndex >> 16);

    /// <summary>
    /// Offset in hash chain
    /// </summary>
    public readonly int NameOffset => (int)(NameHashTableIndex & 0xffff);
};

/// <summary>
/// Key attribute name definition for a specific node in the LSF file
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct LSFKeyEntry
{
    /// <summary>
    /// Index of the node
    /// </summary>
    public uint NodeIndex;

    /// <summary>
    /// Name of key attribute
    /// (16-bit MSB: index into name hash table, 16-bit LSB: offset in hash chain)
    /// </summary>
    public uint KeyName;

    /// <summary>
    /// Index into name hash table
    /// </summary>
    public readonly int KeyNameIndex => (int)(KeyName >> 16);

    /// <summary>
    /// Offset in hash chain
    /// </summary>
    public readonly int KeyNameOffset => (int)(KeyName & 0xffff);
};

/// <summary>
/// Processed node information for a node in the LSF file
/// </summary>
public partial class LSFNodeInfo
{
    /// <summary>
    /// Index of the parent node
    /// (-1: this node is a root region)
    /// </summary>
    public int ParentIndex { get; set; }
    /// <summary>
    /// Index into name hash table
    /// </summary>
    public int NameIndex { get; set; }
    /// <summary>
    /// Offset in hash chain
    /// </summary>
    public int NameOffset { get; set; }
    /// <summary>
    /// Index of the first attribute of this node
    /// (-1: node has no attributes)
    /// </summary>
    public int FirstAttributeIndex { get; set; }
    public string KeyAttribute { get; set; } = string.Empty;
};

/// <summary>
/// V2 attribute extension in the LSF file
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct LSFAttributeEntryV2
{
    /// <summary>
    /// Name of this attribute
    /// (16-bit MSB: index into name hash table, 16-bit LSB: offset in hash chain)
    /// </summary>
    public uint NameHashTableIndex;

    /// <summary>
    /// 6-bit LSB: Type of this attribute (see NodeAttribute.DataType)
    /// 26-bit MSB: Length of this attribute
    /// </summary>
    public uint TypeAndLength;

    /// <summary>
    /// Index of the node that this attribute belongs to
    /// Note: These indexes are assigned seemingly arbitrarily, and are not neccessarily indices into the node list
    /// </summary>
    public int NodeIndex;

    /// <summary>
    /// Index into name hash table
    /// </summary>
    public readonly int NameIndex => (int)(NameHashTableIndex >> 16);

    /// <summary>
    /// Offset in hash chain
    /// </summary>
    public readonly int NameOffset => (int)(NameHashTableIndex & 0xffff);

    /// <summary>
    /// Type of this attribute (see NodeAttribute.DataType)
    /// </summary>
    public readonly uint TypeId => TypeAndLength & 0x3f;

    /// <summary>
    /// Length of this attribute
    /// </summary>
    public readonly uint Length => TypeAndLength >> 6;
};

/// <summary>
/// V3 attribute extension in the LSF file
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct LSFAttributeEntryV3
{
    /// <summary>
    /// Name of this attribute
    /// (16-bit MSB: index into name hash table, 16-bit LSB: offset in hash chain)
    /// </summary>
    public uint NameHashTableIndex;

    /// <summary>
    /// 6-bit LSB: Type of this attribute (see NodeAttribute.DataType)
    /// 26-bit MSB: Length of this attribute
    /// </summary>
    public uint TypeAndLength;

    /// <summary>
    /// Index of the node that this attribute belongs to
    /// Note: These indexes are assigned seemingly arbitrarily, and are not neccessarily indices into the node list
    /// </summary>
    public int NextAttributeIndex;

    /// <summary>
    /// Absolute position of attribute value in the value stream
    /// </summary>
    public uint Offset;

    /// <summary>
    /// Index into name hash table
    /// </summary>
    public readonly int NameIndex => (int)(NameHashTableIndex >> 16);

    /// <summary>
    /// Offset in hash chain
    /// </summary>
    public readonly int NameOffset => (int)(NameHashTableIndex & 0xffff);

    /// <summary>
    /// Type of this attribute (see NodeAttribute.DataType)
    /// </summary>
    public readonly uint TypeId => TypeAndLength & 0x3f;

    /// <summary>
    /// Length of this attribute
    /// </summary>
    public readonly uint Length => TypeAndLength >> 6;
};

internal class LSFAttributeInfo
{
    /// <summary>
    /// Index into name hash table
    /// </summary>
    public int NameIndex;
    /// <summary>
    /// Offset in hash chain
    /// </summary>
    public int NameOffset;
    /// <summary>
    /// Type of this attribute (see NodeAttribute.DataType)
    /// </summary>
    public uint TypeId;
    /// <summary>
    /// Length of this attribute
    /// </summary>
    public uint Length;
    /// <summary>
    /// Absolute position of attribute data in the values section
    /// </summary>
    public uint DataOffset;
    /// <summary>
    /// Index of the next attribute in this node
    /// (-1: this is the last attribute)
    /// </summary>
    public int NextAttributeIndex;
}
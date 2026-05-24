using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace LSLib.VirtualTextures;

[InlineArray(11)]
public struct Reserved11Buffer
{
    private uint _element0;
}

[InlineArray(16)]
public struct Compression16Buffer
{
    private byte _element0;
}

[InlineArray(512)]
public struct FileName512Buffer
{
    private byte _element0;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct DDSHeader
{
    public const uint DDSMagic = 0x20534444;
    public const uint HeaderSize = 0x7c;
    public const uint FourCC_DXT5 = 0x35545844;

    public uint dwMagic;
    public uint dwSize;
    public uint dwFlags;
    public uint dwHeight;
    public uint dwWidth;
    public uint dwPitchOrLinearSize;
    public uint dwDepth;
    public uint dwMipMapCount;
    public Reserved11Buffer dwReserved1;

    public uint dwPFSize;
    public uint dwPFFlags;
    public uint dwFourCC;
    public uint dwRGBBitCount;
    public uint dwRBitMask;
    public uint dwGBitMask;
    public uint dwBBitMask;
    public uint dwABitMask;

    public uint dwCaps;
    public uint dwCaps2;
    public uint dwCaps3;
    public uint dwCaps4;
    public uint dwReserved2;

    public string FourCCName
    {
        readonly get
        {
            uint localFourCC = dwFourCC;
            if (!BitConverter.IsLittleEndian)
            {
                localFourCC = BinaryPrimitives.ReverseEndianness(localFourCC);
            }
            ReadOnlySpan<byte> bytes = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<uint, byte>(ref localFourCC), 4);
            return Encoding.UTF8.GetString(bytes);
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Length < 4) throw new ArgumentException("FourCC string parameters require 4 characters length.", nameof(value));

            uint localFourCC = 0;
            Span<byte> bytes = MemoryMarshal.CreateSpan(ref Unsafe.As<uint, byte>(ref localFourCC), 4);
            Encoding.UTF8.GetBytes(value.AsSpan(0, 4), bytes);

            dwFourCC = BitConverter.IsLittleEndian ? localFourCC : BinaryPrimitives.ReverseEndianness(localFourCC);
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct DDSHeaderDX10
{
    public uint dxgiFormat;
    public uint resourceDimension;
    public uint miscFlag;
    public uint arraySize;
    public uint miscFlags2;
}

public enum GTSDataType : uint
{
    R8G8B8_SRGB = 0,
    R8G8B8A8_SRGB = 1,
    X8Y8Z0_TANGENT = 2,
    R8G8B8_LINEAR = 3,
    R8G8B8A8_LINEAR = 4,
    X8 = 5,
    X8Y8 = 6,
    X8Y8Z8 = 7,
    X8Y8Z8W8 = 8,
    X16 = 9,
    X16Y16 = 10,
    X16Y16Z16 = 11,
    X16Y16Z16W16 = 12,
    X32 = 13,
    X32_FLOAT = 14,
    X32Y32 = 15,
    X32Y32_FLOAT = 16,
    X32Y32Z32 = 17,
    X32Y32Z32_FLOAT = 18,
    R32G32B32 = 19,
    R32G32B32_FLOAT = 20,
    X32Y32Z32W32 = 21,
    X32Y32Z32W32_FLOAT = 22,
    R32G32B32A32 = 23,
    R32G32B32A32_FLOAT = 24,
    R16G16B16_FLOAT = 25,
    R16G16B16A16_FLOAT = 26
}

public enum GTSCodec : uint
{
    Uniform = 0,
    Color420 = 1,
    Normal = 2,
    RawColor = 3,
    Binary = 4,
    Codec15Color420 = 5,
    Codec15Normal = 6,
    RawNormal = 7,
    Half = 8,
    BC3 = 9,
    MultiChannel = 10,
    ASTC = 11
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GTSHeader
{
    public const uint GRPGMagic = 0x47505247;
    public const uint CurrentVersion = 5;

    public uint Magic;
    public uint Version;
    public uint Unused;
    public Guid GUID;
    public uint NumLayers;
    public ulong LayersOffset;
    public uint NumLevels;
    public ulong LevelsOffset;
    public uint TileWidth;
    public uint TileHeight;
    public uint TileBorder;

    public uint I2;
    public uint NumFlatTileInfos;
    public ulong FlatTileInfoOffset;
    public uint I6;
    public uint I7;

    public uint NumPackedTileIDs;
    public ulong PackedTileIDsOffset;

    public uint M;
    public uint N;
    public uint O;
    public uint P;
    public uint Q;
    public uint R;
    public uint S;

    public uint PageSize;
    public uint NumPageFiles;
    public ulong PageFileMetadataOffset;

    public uint FourCCListSize;
    public ulong FourCCListOffset;

    public uint ParameterBlockHeadersCount;
    public ulong ParameterBlockHeadersOffset;

    public ulong ThumbnailsOffset;
    public uint XJJ;
    public uint XKK;
    public uint XLL;
    public uint XMM;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GTSTileSetLayer
{
    public GTSDataType DataType;
    public uint DefaultColor;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GTSTileSetLevel
{
    public uint Width;
    public uint Height;
    public ulong FlatTileIndicesOffset;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GTSParameterBlockHeader
{
    public uint ParameterBlockID;
    public GTSCodec Codec;
    public uint ParameterBlockSize;
    public ulong FileInfoOffset;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GTSBCParameterBlock
{
    public ushort Version;
    public Compression16Buffer Compression1;
    public Compression16Buffer Compression2;

    public string CompressionName1
    {
        readonly get => ExtractStringFromBuffer(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<Compression16Buffer, byte>(ref Unsafe.AsRef(in Compression1)), 16), Encoding.UTF8);
        set => InjectStringToBuffer(value, MemoryMarshal.CreateSpan(ref Unsafe.As<Compression16Buffer, byte>(ref Compression1), 16), Encoding.UTF8);
    }

    public string CompressionName2
    {
        readonly get => ExtractStringFromBuffer(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<Compression16Buffer, byte>(ref Unsafe.AsRef(in Compression2)), 16), Encoding.UTF8);
        set => InjectStringToBuffer(value, MemoryMarshal.CreateSpan(ref Unsafe.As<Compression16Buffer, byte>(ref Compression2), 16), Encoding.UTF8);
    }

    public uint B;
    public byte C1;
    public byte C2;
    public byte BCField3;
    public byte DataType;
    public ushort D;
    public uint FourCC;
    public byte E1;
    public byte SaveMip;
    public byte E3;
    public byte E4;
    public uint F;

    private static string ExtractStringFromBuffer(ReadOnlySpan<byte> buffer, Encoding encoding)
    {
        int length = buffer.IndexOf((byte)0);
        if (length < 0) length = buffer.Length;
        return encoding.GetString(buffer[..length]);
    }

    private static void InjectStringToBuffer(string source, Span<byte> buffer, Encoding encoding)
    {
        buffer.Clear();
        if (string.IsNullOrEmpty(source)) return;

        int encodedBytes = encoding.GetBytes(source, buffer);
        if (encodedBytes < buffer.Length) buffer[encodedBytes] = 0;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GTSUniformParameterBlock
{
    public ushort Version;
    public ushort A_Unused;
    public uint Width;
    public uint Height;
    public GTSDataType DataType;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GTSPageFileInfo
{
    public FileName512Buffer FileNameBuf;

    public uint NumPages;
    public Guid Checksum;
    public uint F;

    public readonly string FileName
    {
        get
        {
            ReadOnlySpan<byte> buffer = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<FileName512Buffer, byte>(ref Unsafe.AsRef(in FileNameBuf)), 512);

            ReadOnlySpan<ushort> unicodeBuffer = MemoryMarshal.Cast<byte, ushort>(buffer);
            int nameLen = unicodeBuffer.IndexOf((ushort)0);
            if (nameLen < 0) nameLen = unicodeBuffer.Length;

            var rawPath = Encoding.Unicode.GetString(buffer[..(nameLen << 1)]);
            return rawPath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        }
        set
        {
            ref var mutableBuf = ref Unsafe.AsRef(in FileNameBuf);
            Span<byte> buffer = MemoryMarshal.CreateSpan(ref Unsafe.As<FileName512Buffer, byte>(ref mutableBuf), 512);

            buffer.Clear();
            if (string.IsNullOrEmpty(value)) return;

            var cleanValue = value.Replace(Path.DirectorySeparatorChar, '\\');
            int encodedBytes = Encoding.Unicode.GetBytes(cleanValue, buffer);

            if (encodedBytes + 1 < buffer.Length)
            {
                buffer[encodedBytes] = 0;
                buffer[encodedBytes + 1] = 0;
            }
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GTSFourCCMetadata
{
    public uint FourCC;
    public byte Format;
    public byte ExtendedLength;
    public ushort Length;

    public string FourCCName
    {
        readonly get
        {
            uint localFourCC = FourCC;
            if (!BitConverter.IsLittleEndian)
            {
                localFourCC = BinaryPrimitives.ReverseEndianness(localFourCC);
            }
            ReadOnlySpan<byte> bytes = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<uint, byte>(ref localFourCC), 4);
            return Encoding.UTF8.GetString(bytes);
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Length < 4) throw new ArgumentException("FourCC metadata demands a minimal 4 character field matrix.", nameof(value));

            uint localFourCC = 0;
            Span<byte> bytes = MemoryMarshal.CreateSpan(ref Unsafe.As<uint, byte>(ref localFourCC), 4);
            Encoding.UTF8.GetBytes(value.AsSpan(0, 4), bytes);

            FourCC = BitConverter.IsLittleEndian ? localFourCC : BinaryPrimitives.ReverseEndianness(localFourCC);
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GTSThumbnailInfoHeader
{
    public uint NumThumbnails;
    public uint A;
    public uint B;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GTSThumbnailInfo
{
    public Guid GUID;
    public ulong OffsetInFile;
    public uint CompressedSize;
    public uint Unknown1;
    public ushort Unknown2;
    public ushort Unknown3;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GTSPackedTileID(uint layer, uint level, uint x, uint y)
{
    public uint Val = (layer & 0xF)

            | ((level & 0xF) << 4)
            | ((y & 0xFFF) << 8)
            | ((x & 0xFFF) << 20);

    public GTSPackedTileID() : this(0, 0, 0, 0) { }

    public readonly uint Layer => Val & 0x0F;
    public readonly uint Level => (Val >> 4) & 0x0F;
    public readonly uint Y => (Val >> 8) & 0x0FFF;
    public readonly uint X => Val >> 20;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GTSFlatTileInfo
{
    public ushort PageFileIndex;
    public ushort PageIndex;
    public ushort ChunkIndex;
    public ushort D;
    public uint PackedTileIndex;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GTPHeader
{
    public const uint HeaderMagic = 0x50415247;
    public const uint DefaultVersion = 4;

    public uint Magic;
    public uint Version;
    public Guid GUID;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GTPChunkHeader
{
    public GTSCodec Codec;
    public uint ParameterBlockID;
    public uint Size;
}
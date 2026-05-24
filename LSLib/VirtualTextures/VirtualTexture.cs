using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace LSLib.VirtualTextures;

public sealed class PageFileInfo
{
    public required GTSPageFileInfo Meta { get; init; }
    public required uint FirstPageIndex { get; init; }
    public string FileName { get; set; } = string.Empty;
}

public enum FourCCElementType : uint
{
    Node,
    Int,
    String,
    BinaryInt,
    BinaryGuid
}

public sealed class FourCCElement
{
    public FourCCElementType Type { get; set; }
    public string FourCC { get; set; } = string.Empty;
    public string Str { get; set; } = string.Empty;
    public uint UInt { get; set; }
    public byte[] Blob { get; set; } = [];
    public List<FourCCElement> Children { get; set; } = [];

    public static FourCCElement Make(string fourCC) => new()
    {
        Type = FourCCElementType.Node,
        FourCC = fourCC
    };

    public static FourCCElement Make(string fourCC, uint value) => new()
    {
        Type = FourCCElementType.Int,
        FourCC = fourCC,
        UInt = value
    };

    public static FourCCElement Make(string fourCC, string value) => new()
    {
        Type = FourCCElementType.String,
        FourCC = fourCC,
        Str = value
    };

    public static FourCCElement Make(string fourCC, FourCCElementType type, byte[] value) => new()
    {
        Type = type,
        FourCC = fourCC,
        Blob = value ?? []
    };

    public FourCCElement? GetChild(string fourCC)
    {
        foreach (var child in CollectionsMarshal.AsSpan(Children))
        {
            if (string.Equals(child.FourCC, fourCC, StringComparison.Ordinal)) return child;
        }
        return null;
    }
}

public sealed class FourCCTextureMeta
{
    public string Name { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class BC3Image
{
    public byte[] Data { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public BC3Image(byte[] data, int width, int height)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        Width = width;
        Height = height;
    }

    public BC3Image(int width, int height)
    {
        Data = new byte[width * height];
        Width = width;
        Height = height;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CalculateOffset(int x, int y)
    {
        if (((x | y) & 3) != 0)
            throw new ArgumentException("BC block coordinates must be exact multiples of 4.");

        return ((x >> 2) + (y >> 2) * (Width >> 2)) << 4;
    }

    public void CopyTo(BC3Image destination, int srcX, int srcY, int dstX, int dstY, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (((srcX | srcY | dstX | dstY | width | height) & 3) != 0)
            throw new ArgumentException("BC coordinates must be multiples of 4");

        if (srcX < 0 || dstX < 0 || srcY < 0 || dstY < 0 || srcX + width > Width || srcY + height > Height || dstX + width > destination.Width || dstY + height > destination.Height)
            throw new ArgumentException("Texture block coordinates out of bounds mapping thresholds.");

        ReadOnlySpan<byte> sourceSpan = Data;
        Span<byte> destSpan = destination.Data;

        var wrY = dstY;
        for (var y = srcY; y < srcY + height; y += 4)
        {
            var wrX = dstX;
            for (var x = srcX; x < srcX + width; x += 4)
            {
                var srcoff = CalculateOffset(x, y);
                var dstoff = destination.CalculateOffset(wrX, wrY);

                sourceSpan.Slice(srcoff, 16).CopyTo(destSpan[dstoff..]);
                wrX += 4;
            }
            wrY += 4;
        }
    }
}

public sealed class BC3Mips
{
    public List<BC3Image> Mips { get; set; } = [];

    public void LoadDDS(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var normalizedPath = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

        using var f = File.OpenRead(normalizedPath);

        Span<byte> structBuffer = stackalloc byte[Unsafe.SizeOf<DDSHeader>()];
        if (f.Read(structBuffer) != structBuffer.Length)
            throw new EndOfStreamException("Truncated stream while reading DDS main header.");

        var header = MemoryMarshal.Read<DDSHeader>(structBuffer);
        Mips = [];

        if (header.dwMagic != DDSHeader.DDSMagic)
            throw new InvalidDataException($"{normalizedPath}: Incorrect DDS signature.");

        if (header.dwSize != DDSHeader.HeaderSize)
            throw new InvalidDataException($"{normalizedPath}: Incorrect DDS header size.");

        if (header.FourCCName == "DX10")
        {
            Span<byte> dx10Buffer = stackalloc byte[Unsafe.SizeOf<DDSHeaderDX10>()];
            if (f.Read(dx10Buffer) != dx10Buffer.Length)
                throw new EndOfStreamException("Truncated stream while reading DDS DX10 extension header.");
        }

        int mipsCount = ((header.dwFlags & 0x20000) == 0x20000) ? (int)header.dwMipMapCount : 1;
        Mips = new List<BC3Image>(mipsCount);

        for (var i = 0; i < mipsCount; i++)
        {
            var width = Math.Max((int)header.dwWidth >> i, 1);
            var height = Math.Max((int)header.dwHeight >> i, 1);
            var bytes = Math.Max(width / 4, 1) * Math.Max(height / 4, 1) * 16;

            var blob = new byte[bytes];
            if (f.Read(blob) != bytes)
                throw new EndOfStreamException("Unexpected end of stream while reading DDS mip level data.");

            Mips.Add(new BC3Image(blob, width, height));
        }
    }
}

public sealed class PageFile(VirtualTileSet tileSet, string path) : IDisposable
{
    private readonly VirtualTileSet _tileSet = tileSet ?? throw new ArgumentNullException(nameof(tileSet));
    private readonly FileStream _stream = File.OpenRead(path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));

    public GTPHeader Header;
    public List<uint[]> ChunkOffsets { get; } = [];

    private void LoadMetadata()
    {
        _stream.Position = 0;
        Span<byte> headerBuffer = stackalloc byte[Unsafe.SizeOf<GTPHeader>()];
        if (_stream.Read(headerBuffer) != headerBuffer.Length)
            throw new EndOfStreamException("Failed to read the GTP file container allocation signature header.");

        Header = MemoryMarshal.Read<GTPHeader>(headerBuffer);

        Span<byte> uintBuffer = stackalloc byte[4];
        if (_stream.Read(uintBuffer) != 4)
            throw new EndOfStreamException("Truncated data reading chunk count.");

        uint numChunks = BinaryPrimitives.ReadUInt32LittleEndian(uintBuffer);
        var offsets = new uint[numChunks];

        Span<byte> offsetsBuffer = MemoryMarshal.AsBytes(offsets.AsSpan());
        if (_stream.Read(offsetsBuffer) != offsetsBuffer.Length)
            throw new EndOfStreamException("Truncated chunk data block parsing sequence thresholds.");

        if (!BitConverter.IsLittleEndian)
        {
            for (int i = 0; i < offsets.Length; i++)
            {
                offsets[i] = BinaryPrimitives.ReverseEndianness(offsets[i]);
            }
        }

        ChunkOffsets.Add(offsets);
    }

    public void Initialize() => LoadMetadata();

    public void Dispose()
    {
        _stream.Dispose();
    }

    public BC3Image UnpackTileBC3(uint pageIndex, uint chunkIndex, TileCompressor compressor)
    {
        ArgumentNullException.ThrowIfNull(compressor);

        if (pageIndex >= ChunkOffsets.Count || chunkIndex >= ChunkOffsets[(int)pageIndex].Length)
            throw new ArgumentOutOfRangeException(nameof(chunkIndex), "Texture unpack coordinates fall outside valid block boundaries.");

        uint offset = ChunkOffsets[(int)pageIndex][(int)chunkIndex];
        _stream.Position = offset;

        Span<byte> chunkHdrBuffer = stackalloc byte[Unsafe.SizeOf<GTPChunkHeader>()];
        if (_stream.Read(chunkHdrBuffer) != chunkHdrBuffer.Length)
            throw new EndOfStreamException("Failed to read tile chunk descriptor mapping headers.");

        var chunkHeader = MemoryMarshal.Read<GTPChunkHeader>(chunkHdrBuffer);

        var compressedData = new byte[chunkHeader.Size];
        if (_stream.Read(compressedData) != compressedData.Length)
            throw new EndOfStreamException("Truncated stream payload reading compressed chunk data.");

        var tileWidth = _tileSet.Header.TileWidth;
        var tileHeight = _tileSet.Header.TileHeight;

        var unpackedImg = new BC3Image((int)tileWidth, (int)tileHeight);

        ReadOnlySpan<byte> sourceSpan = compressedData;
        sourceSpan[..Math.Min(compressedData.Length, unpackedImg.Data.Length)].CopyTo(unpackedImg.Data);

        return unpackedImg;
    }
}

public sealed class TileSetFourCC
{
    public FourCCElement? Root { get; set; }

    public void Read(Stream fs, BinaryReader reader, long length)
    {
        List<FourCCElement> fourCCs = [];
        Read(fs, reader, length, fourCCs);
        Root = fourCCs.Count > 0 ? fourCCs[0] : null;
    }

    public void Read(Stream fs, BinaryReader reader, long length, List<FourCCElement> elements)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(elements);

        var end = fs.Position + length;
        Span<byte> structBuffer = stackalloc byte[Unsafe.SizeOf<GTSFourCCMetadata>()];
        Span<byte> uintBuffer = stackalloc byte[4];

        while (fs.Position < end)
        {
            if (fs.Read(structBuffer) != structBuffer.Length)
                throw new EndOfStreamException("Failed to extract complete FourCC header metadata.");

            var header = MemoryMarshal.Read<GTSFourCCMetadata>(structBuffer);
            var cc = new FourCCElement { FourCC = header.FourCCName };
            int valueSize = header.Length;

            if (header.ExtendedLength == 1)
            {
                if (fs.Read(uintBuffer) != 4)
                    throw new EndOfStreamException("Truncated extended size descriptor header.");
                valueSize |= ((int)BinaryPrimitives.ReadUInt32LittleEndian(uintBuffer) << 16);
            }

            switch (header.Format)
            {
                case 1:
                    cc.Type = FourCCElementType.Node;
                    cc.Children = [];
                    Read(fs, reader, valueSize, cc.Children);
                    break;
                case 2:
                    cc.Type = FourCCElementType.String;
                    var strBytes = reader.ReadBytes(valueSize - 2);
                    cc.Str = Encoding.Unicode.GetString(strBytes).Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                    fs.Position += 2;
                    break;
                case 3:
                    cc.Type = FourCCElementType.Int;
                    if (fs.Read(uintBuffer) != 4)
                        throw new EndOfStreamException("Truncated value payload reading integer entry.");
                    cc.UInt = BinaryPrimitives.ReadUInt32LittleEndian(uintBuffer);
                    break;
                case 8 or 0x0D:
                    cc.Type = header.Format == 8 ? FourCCElementType.BinaryInt : FourCCElementType.BinaryGuid;
                    cc.Blob = new byte[valueSize];
                    if (fs.Read(cc.Blob) != valueSize)
                        throw new EndOfStreamException("Truncated value payload reading binary blob entry.");
                    break;
                default:
                    throw new InvalidDataException($"Unrecognized FourCC tag: {header.Format}");
            }

            fs.Position = (fs.Position + 3) & ~3L;
            elements.Add(cc);
        }
    }

    public void Write(Stream fs, BinaryWriter writer)
    {
        if (Root is not null) Write(fs, writer, Root);
    }

    public void Write(Stream fs, BinaryWriter writer, FourCCElement element)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(element);

        var header = new GTSFourCCMetadata { FourCCName = element.FourCC };
        var cleanString = element.Type == FourCCElementType.String ? element.Str.Replace(Path.DirectorySeparatorChar, '\\') : string.Empty;

        uint length = element.Type switch
        {
            FourCCElementType.Node => 0,
            FourCCElementType.Int => 4,
            FourCCElementType.String => (uint)Encoding.Unicode.GetByteCount(cleanString) + 2,
            FourCCElementType.BinaryInt or FourCCElementType.BinaryGuid => (uint)element.Blob.Length,
            _ => throw new InvalidDataException()
        };

        header.Format = element.Type switch
        {
            FourCCElementType.Node => 1,
            FourCCElementType.String => 2,
            FourCCElementType.Int => 3,
            FourCCElementType.BinaryInt => 8,
            FourCCElementType.BinaryGuid => 0xD,
            _ => throw new InvalidDataException()
        };

        header.Length = (ushort)(length & 0xffff);
        if (length > 0xffff) header.ExtendedLength = 1;

        Span<byte> structBuffer = stackalloc byte[Unsafe.SizeOf<GTSFourCCMetadata>()];
        MemoryMarshal.Write(structBuffer, in header);
        fs.Write(structBuffer);

        if (length > 0xffff)
        {
            Span<byte> extLenBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(extLenBytes, length >> 16);
            fs.Write(extLenBytes);
        }

        switch (element.Type)
        {
            case FourCCElementType.Node:
                var lengthOffset = fs.Position - 6;
                var childrenOffset = fs.Position;

                foreach (var child in CollectionsMarshal.AsSpan(element.Children)) Write(fs, writer, child);

                var endOffset = fs.Position;
                var childrenSize = (uint)(endOffset - childrenOffset);

                fs.Position = lengthOffset;
                Span<byte> sizeBytes = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(sizeBytes, childrenSize);
                fs.Write(sizeBytes);
                fs.Position = endOffset;
                break;

            case FourCCElementType.Int:
                Span<byte> intBytes = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(intBytes, element.UInt);
                fs.Write(intBytes);
                break;

            case FourCCElementType.String:
                var stringBytes = Encoding.Unicode.GetBytes(cleanString);
                fs.Write(stringBytes);

                Span<byte> terminatorBytes = stackalloc byte[2]; 
                fs.Write(terminatorBytes);
                break;

            case FourCCElementType.BinaryInt:
            case FourCCElementType.BinaryGuid:
                fs.Write(element.Blob);
                break;
        }

        var alignedPosition = (fs.Position + 3) & ~3L;
        var paddingCount = (int)(alignedPosition - fs.Position);
        if (paddingCount > 0)
        {
            Span<byte> paddingZeroes = stackalloc byte[3];
            fs.Write(paddingZeroes[..paddingCount]);
        }
    }
}

public sealed class VirtualTileSet : IDisposable
{
    public string PagePath { get; set; } = string.Empty;
    public GTSHeader Header;
    public GTSTileSetLayer[] TileSetLayers { get; set; } = [];
    public GTSTileSetLevel[] TileSetLevels { get; set; } = [];
    public List<uint[]> PerLevelFlatTileIndices { get; set; } = [];
    public GTSParameterBlockHeader[] ParameterBlockHeaders { get; set; } = [];
    public Dictionary<uint, object> ParameterBlocks { get; set; } = [];
    public List<PageFileInfo> PageFileInfos { get; set; } = [];
    public TileSetFourCC FourCCMetadata { get; set; } = new();
    public GTSThumbnailInfo[] ThumbnailInfos { get; set; } = [];
    public GTSPackedTileID[] PackedTileIDs { get; set; } = [];
    public GTSFlatTileInfo[] FlatTileInfos { get; set; } = [];

    private readonly Dictionary<int, PageFile> _pageFiles = [];
    private readonly TileCompressor _compressor = new()
    {
        ParameterBlocks = new ParameterBlockContainer()
    };
    public VirtualTileSet(string path, string pagePath)
    {
        PagePath = pagePath ?? string.Empty;
        var normalizedPath = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

        using var fs = File.OpenRead(normalizedPath);
        using var reader = new BinaryReader(fs);
        LoadFromStream(fs, reader, false);
    }

    public VirtualTileSet(string path) : this(path, Path.GetDirectoryName(path) ?? string.Empty) { }
    public VirtualTileSet() { }

    public void Save(string path)
    {
        var normalizedPath = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        using var fs = File.OpenWrite(normalizedPath);
        using var writer = new BinaryWriter(fs);
        SaveToStream(fs, writer);
    }

    public void Dispose()
    {
        foreach (var pageFile in _pageFiles.Values) pageFile.Dispose();
        _pageFiles.Clear();
    }

    public PageFile GetOrLoadPageFile(int pageFileIdx)
    {
        ref var file = ref CollectionsMarshal.GetValueRefOrAddDefault(_pageFiles, pageFileIdx, out var exists);
        if (!exists)
        {
            var meta = PageFileInfos[pageFileIdx];
            file = new PageFile(this, Path.Join(PagePath, meta.FileName));
            file.Initialize();
        }
        return file!;
    }

    private void LoadThumbnails(Stream fs, BinaryReader reader)
    {
        fs.Position = (long)Header.ThumbnailsOffset;
        Span<byte> infoHdrBuffer = stackalloc byte[Unsafe.SizeOf<GTSThumbnailInfoHeader>()];
        if (fs.Read(infoHdrBuffer) != infoHdrBuffer.Length) throw new EndOfStreamException();

        var thumbHdr = MemoryMarshal.Read<GTSThumbnailInfoHeader>(infoHdrBuffer);
        ThumbnailInfos = new GTSThumbnailInfo[thumbHdr.NumThumbnails];

        Span<byte> infoArrayBuffer = MemoryMarshal.AsBytes(ThumbnailInfos.AsSpan());
        if (fs.Read(infoArrayBuffer) != infoArrayBuffer.Length) throw new EndOfStreamException();

        Span<byte> bcParamBuffer = stackalloc byte[Unsafe.SizeOf<GTSBCParameterBlock>()];
        foreach (var thumb in ThumbnailInfos)
        {
            fs.Position = (long)thumb.OffsetInFile;
            fs.Position += thumb.CompressedSize + 12;

            if (fs.Read(bcParamBuffer) != bcParamBuffer.Length) throw new EndOfStreamException();
            _ = MemoryMarshal.Read<GTSBCParameterBlock>(bcParamBuffer);
        }
    }


    public void LoadFromStream(Stream fs, BinaryReader reader, bool loadThumbnails)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(reader);

        Span<byte> headerBuffer = stackalloc byte[Unsafe.SizeOf<GTSHeader>()];
        if (fs.Read(headerBuffer) != headerBuffer.Length) throw new EndOfStreamException();
        Header = MemoryMarshal.Read<GTSHeader>(headerBuffer);

        fs.Position = (long)Header.LayersOffset;
        TileSetLayers = new GTSTileSetLayer[Header.NumLayers];
        Span<byte> layersArrayBuffer = MemoryMarshal.AsBytes(TileSetLayers.AsSpan());
        if (fs.Read(layersArrayBuffer) != layersArrayBuffer.Length) throw new EndOfStreamException();

        fs.Position = (long)Header.LevelsOffset;
        TileSetLevels = new GTSTileSetLevel[Header.NumLevels];
        Span<byte> levelsArrayBuffer = MemoryMarshal.AsBytes(TileSetLevels.AsSpan());
        if (fs.Read(levelsArrayBuffer) != levelsArrayBuffer.Length) throw new EndOfStreamException();

        PerLevelFlatTileIndices = [];
        foreach (var level in TileSetLevels)
        {
            fs.Position = (long)level.FlatTileIndicesOffset;
            var elementCount = level.Height * level.Width * (int)Header.NumLayers;
            var tileIndices = new uint[elementCount];

            Span<byte> indicesArrayBuffer = MemoryMarshal.AsBytes(tileIndices.AsSpan());
            if (fs.Read(indicesArrayBuffer) != indicesArrayBuffer.Length) throw new EndOfStreamException();

            if (!BitConverter.IsLittleEndian)
            {
                for (int i = 0; i < tileIndices.Length; i++)
                    tileIndices[i] = BinaryPrimitives.ReverseEndianness(tileIndices[i]);
            }

            PerLevelFlatTileIndices.Add(tileIndices);
        }

        fs.Position = (long)Header.ParameterBlockHeadersOffset;
        ParameterBlockHeaders = new GTSParameterBlockHeader[Header.ParameterBlockHeadersCount];
        Span<byte> paramHeadersArrayBuffer = MemoryMarshal.AsBytes(ParameterBlockHeaders.AsSpan());
        if (fs.Read(paramHeadersArrayBuffer) != paramHeadersArrayBuffer.Length) throw new EndOfStreamException();

        ParameterBlocks = [];
        Span<byte> bcBlockBuffer = stackalloc byte[Unsafe.SizeOf<GTSBCParameterBlock>()];
        Span<byte> uniformBlockBuffer = stackalloc byte[Unsafe.SizeOf<GTSUniformParameterBlock>()];

        foreach (var hdr in ParameterBlockHeaders)
        {
            fs.Position = (long)hdr.FileInfoOffset;
            if (hdr.Codec == GTSCodec.BC3)
            {
                if (fs.Read(bcBlockBuffer) != bcBlockBuffer.Length) throw new EndOfStreamException();
                ParameterBlocks.Add(hdr.ParameterBlockID, MemoryMarshal.Read<GTSBCParameterBlock>(bcBlockBuffer));
            }
            else
            {
                if (fs.Read(uniformBlockBuffer) != uniformBlockBuffer.Length) throw new EndOfStreamException();
                ParameterBlocks.Add(hdr.ParameterBlockID, MemoryMarshal.Read<GTSUniformParameterBlock>(uniformBlockBuffer));
            }
        }

        fs.Position = (long)Header.PageFileMetadataOffset;
        var pageFileInfos = new GTSPageFileInfo[Header.NumPageFiles];
        Span<byte> pageFilesArrayBuffer = MemoryMarshal.AsBytes(pageFileInfos.AsSpan());
        if (fs.Read(pageFilesArrayBuffer) != pageFilesArrayBuffer.Length) throw new EndOfStreamException();

        PageFileInfos = [];
        uint nextPageIndex = 0;
        foreach (var info in pageFileInfos)
        {
            PageFileInfos.Add(new PageFileInfo { Meta = info, FirstPageIndex = nextPageIndex, FileName = info.FileName });
            nextPageIndex += info.NumPages;
        }

        fs.Position = (long)Header.FourCCListOffset;
        FourCCMetadata = new TileSetFourCC();
        FourCCMetadata.Read(fs, reader, Header.FourCCListSize);

        if (loadThumbnails) LoadThumbnails(fs, reader);

        fs.Position = (long)Header.PackedTileIDsOffset;
        PackedTileIDs = new GTSPackedTileID[Header.NumPackedTileIDs];
        Span<byte> packedTilesArrayBuffer = MemoryMarshal.AsBytes(PackedTileIDs.AsSpan());
        if (fs.Read(packedTilesArrayBuffer) != packedTilesArrayBuffer.Length) throw new EndOfStreamException();

        fs.Position = (long)Header.FlatTileInfoOffset;
        FlatTileInfos = new GTSFlatTileInfo[Header.NumFlatTileInfos];
        Span<byte> flatTilesArrayBuffer = MemoryMarshal.AsBytes(FlatTileInfos.AsSpan());
        if (fs.Read(flatTilesArrayBuffer) != flatTilesArrayBuffer.Length) throw new EndOfStreamException();
    }
    public void SaveToStream(Stream fs, BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(writer);

        Span<byte> headerBuffer = stackalloc byte[Unsafe.SizeOf<GTSHeader>()];
        MemoryMarshal.Write(headerBuffer, in Header);
        fs.Write(headerBuffer);

        Header.LayersOffset = (ulong)fs.Position;
        Header.NumLayers = (uint)TileSetLayers.Length;

        fs.Write(MemoryMarshal.AsBytes(TileSetLayers.AsSpan()));

        var levelsSpan = TileSetLevels.AsSpan();
        for (var i = 0; i < levelsSpan.Length; i++)
        {
            ref var level = ref levelsSpan[i];
            level.FlatTileIndicesOffset = (ulong)fs.Position;

            var tileIndices = PerLevelFlatTileIndices[i];
            fs.Write(MemoryMarshal.AsBytes(tileIndices.AsSpan()));
        }

        Header.LevelsOffset = (ulong)fs.Position;
        Header.NumLevels = (uint)TileSetLevels.Length;
        fs.Write(MemoryMarshal.AsBytes(levelsSpan));

        Header.ParameterBlockHeadersOffset = (ulong)fs.Position;
        Header.ParameterBlockHeadersCount = (uint)ParameterBlockHeaders.Length;

        var paramHeadersSpan = ParameterBlockHeaders.AsSpan();
        fs.Write(MemoryMarshal.AsBytes(paramHeadersSpan));

        Span<byte> uniformBuffer = stackalloc byte[Unsafe.SizeOf<GTSUniformParameterBlock>()];
        Span<byte> bcBuffer = stackalloc byte[Unsafe.SizeOf<GTSBCParameterBlock>()];

        for (var i = 0; i < paramHeadersSpan.Length; i++)
        {
            ref var hdr = ref paramHeadersSpan[i];
            hdr.FileInfoOffset = (ulong)fs.Position;

            if (ParameterBlocks.TryGetValue(hdr.ParameterBlockID, out var block))
            {
                if (hdr.Codec == GTSCodec.BC3 && block is GTSBCParameterBlock bcBlock)
                {
                    MemoryMarshal.Write(bcBuffer, in bcBlock);
                    fs.Write(bcBuffer);
                }
                else if (hdr.Codec == GTSCodec.Uniform && block is GTSUniformParameterBlock uniformBlock)
                {
                    hdr.ParameterBlockSize = 0x10;
                    MemoryMarshal.Write(uniformBuffer, in uniformBlock);
                    fs.Write(uniformBuffer);
                }
            }
        }

        Header.PageFileMetadataOffset = (ulong)fs.Position;
        Header.NumPageFiles = (uint)PageFileInfos.Count;
        Span<byte> pageFileBuffer = stackalloc byte[Unsafe.SizeOf<GTSPageFileInfo>()];
        foreach (var fileInfo in CollectionsMarshal.AsSpan(PageFileInfos))
        {
            var metaCopy = fileInfo.Meta;
            MemoryMarshal.Write(pageFileBuffer, in metaCopy);

            fs.Write(pageFileBuffer);
        }

        Header.FourCCListOffset = (ulong)fs.Position;
        FourCCMetadata.Write(fs, writer);
        Header.FourCCListSize = (uint)((ulong)fs.Position - Header.FourCCListOffset);

        Header.ThumbnailsOffset = (ulong)fs.Position;
        var thumbHdr = new GTSThumbnailInfoHeader { NumThumbnails = 0 };
        Span<byte> thumbHdrBuffer = stackalloc byte[Unsafe.SizeOf<GTSThumbnailInfoHeader>()];
        MemoryMarshal.Write(thumbHdrBuffer, in thumbHdr);
        fs.Write(thumbHdrBuffer);

        Header.PackedTileIDsOffset = (ulong)fs.Position;
        Header.NumPackedTileIDs = (uint)PackedTileIDs.Length;
        fs.Write(MemoryMarshal.AsBytes(PackedTileIDs.AsSpan()));

        Header.FlatTileInfoOffset = (ulong)fs.Position;
        Header.NumFlatTileInfos = (uint)FlatTileInfos.Length;
        fs.Write(MemoryMarshal.AsBytes(FlatTileInfos.AsSpan()));

        var currentPosition = fs.Position;
        fs.Position = 0;
        MemoryMarshal.Write(headerBuffer, in Header);
        fs.Write(headerBuffer);

        fs.Position = (long)Header.ParameterBlockHeadersOffset;
        fs.Write(MemoryMarshal.AsBytes(paramHeadersSpan));
        fs.Position = currentPosition;
    }

    public bool GetTileInfo(int level, int layer, int x, int y, ref GTSFlatTileInfo tile)
    {
        var tileIndices = PerLevelFlatTileIndices[level];
        var tileIndex = tileIndices[layer + (int)Header.NumLayers * (x + (y * TileSetLevels[level].Width))];

        if ((tileIndex & 0x80000000) == 0)
        {
            tile = FlatTileInfos[tileIndex];
            return true;
        }
        return false;
    }

    public void Validate()
    {
        foreach (var tileId in PackedTileIDs.AsSpan())
        {
            if (tileId.Level >= TileSetLevels.Length)
                throw new InvalidDataException($"Tile references nonexistent level {tileId.Level}");
            if (tileId.Layer >= TileSetLayers.Length)
                throw new InvalidDataException($"Tile references nonexistent layer {tileId.Layer}");

            var level = TileSetLevels[tileId.Level];
            if (tileId.X >= level.Width || tileId.Y >= level.Height)
                throw new InvalidDataException($"Tile references out of bounds map coordinates: {tileId.X},{tileId.Y}");
        }

        var pageFileInfosSpan = CollectionsMarshal.AsSpan(PageFileInfos);
        for (var i = 0; i < pageFileInfosSpan.Length; i++)
        {
            GetOrLoadPageFile(i);
        }

        foreach (var (i, file) in _pageFiles)
        {
            var info = pageFileInfosSpan[i];

            if (info.Meta.NumPages != file.ChunkOffsets.Count)
                throw new InvalidDataException($"Page count mismatch in metadata: {info.FileName}");

            if (info.Meta.Checksum != file.Header.GUID)
                throw new InvalidDataException($"Checksum mismatch in metadata: {info.FileName}");
        }

        foreach (var tileInfo in FlatTileInfos.AsSpan())
        {
            if (tileInfo.PageFileIndex >= (uint)pageFileInfosSpan.Length)
                throw new InvalidDataException($"Flat tile maps to a nonexistent page file index: {tileInfo.PageFileIndex}");

            var file = _pageFiles[(int)tileInfo.PageFileIndex];
            if (tileInfo.PageIndex >= file.ChunkOffsets.Count)
                throw new InvalidDataException($"Flat tile references nonexistent page index {tileInfo.PageFileIndex}:{tileInfo.PageIndex}");

            if (tileInfo.ChunkIndex >= (uint)file.ChunkOffsets[(int)tileInfo.PageIndex].Length)
                throw new InvalidDataException($"Flat tile references nonexistent chunk index {tileInfo.PageFileIndex}:{tileInfo.PageIndex}:{tileInfo.ChunkIndex}");

            if (tileInfo.PackedTileIndex >= PackedTileIDs.Length)
                throw new InvalidDataException($"Flat tile maps to a nonexistent packed tile index: {tileInfo.PackedTileIndex}");
        }

        foreach (var levelInds in CollectionsMarshal.AsSpan(PerLevelFlatTileIndices))
        {
            if (levelInds is null) continue;

            foreach (var tileIndex in levelInds.AsSpan())
            {
                if ((tileIndex & 0x80000000) == 0)
                {
                    if (tileIndex >= (uint)FlatTileInfos.Length)
                        throw new InvalidDataException($"Level indices link to an unmapped flat tile target: {tileIndex}");
                }
                else
                {
                    var downsampleIndex = tileIndex & ~0x80000000u;
                    if (downsampleIndex >= (uint)FlatTileInfos.Length)
                        throw new InvalidDataException($"Level indices link to an unmapped downsampled tile target: {downsampleIndex}");
                }
            }
        }
    }

    public void StitchTexture(int level, int layer, int minX, int minY, int maxX, int maxY, BC3Image output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var borderOffset = (int)Header.TileBorder << 1;
        var tileWidth = (int)Header.TileWidth - borderOffset;
        var tileHeight = (int)Header.TileHeight - borderOffset;
        GTSFlatTileInfo tileInfo = default;

        for (var y = minY; y <= maxY; y++)
        {
            var dstY = (y - minY) * tileHeight;
            for (var x = minX; x <= maxX; x++)
            {
                if (GetTileInfo(level, layer, x, y, ref tileInfo))
                {
                    var pageFile = GetOrLoadPageFile((int)tileInfo.PageFileIndex);
                    var tile = pageFile.UnpackTileBC3(tileInfo.PageIndex, tileInfo.ChunkIndex, _compressor);

                    tile.CopyTo(output, (int)Header.TileBorder, (int)Header.TileBorder, (x - minX) * tileWidth, dstY, tileWidth, tileHeight);
                }
            }
        }
    }

    public BC3Image ExtractTexture(int level, int layer, int minX, int minY, int maxX, int maxY)
    {
        var borderOffset = (int)Header.TileBorder << 1;
        var tileWidth = (int)Header.TileWidth - borderOffset;
        var tileHeight = (int)Header.TileHeight - borderOffset;

        var width = (maxX - minX + 1) * tileWidth;
        var height = (maxY - minY + 1) * tileHeight;

        var stitched = new BC3Image(width, height);
        StitchTexture(level, layer, minX, minY, maxX, maxY, stitched);
        return stitched;
    }

    public BC3Image? ExtractTexture(int level, int layer, VirtualTextureInfo tex)
    {
        ArgumentNullException.ThrowIfNull(tex);

        var borderOffset = (int)Header.TileBorder << 1;
        var tlW = (int)Header.TileWidth - borderOffset;
        var tlH = (int)Header.TileHeight - borderOffset;

        var tX = tex.X / tlW;
        var tY = tex.Y / tlH;
        var tW = tex.Width / tlW;
        var tH = tex.Height / tlH;

        var lv = 1 << level;

        var minX = (tX / lv) + ((tX % lv > 0) ? 1 : 0);
        var minY = (tY / lv) + ((tY % lv > 0) ? 1 : 0);
        var maxX = ((tX + tW) / lv) + (((tX + tW) % lv > 0) ? 1 : 0) - 1;
        var maxY = ((tY + tH) / lv) + (((tY + tH) % lv > 0) ? 1 : 0) - 1;

        return ExtractTextureIfExists(level, layer, minX, minY, maxX, maxY);
    }

    public int FindPageFile(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var fileInfosSpan = CollectionsMarshal.AsSpan(PageFileInfos);
        for (var i = 0; i < fileInfosSpan.Length; i++)
        {
            if (fileInfosSpan[i].FileName.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    public void ReleasePageFiles() => _pageFiles.Clear();

    public BC3Image? ExtractTexture(int level, int layer, FourCCTextureMeta tex)
    {
        ArgumentNullException.ThrowIfNull(tex);

        var borderOffset = (int)Header.TileBorder << 1;
        var tlW = (int)Header.TileWidth - borderOffset;
        var tlH = (int)Header.TileHeight - borderOffset;

        var tX = tex.X / tlW;
        var tY = tex.Y / tlH;
        var tW = tex.Width / tlW;
        var tH = tex.Height / tlH;

        var lv = 1 << level;

        var minX = (tX / lv) + ((tX % lv > 0) ? 1 : 0);
        var minY = (tY / lv) + ((tY % lv > 0) ? 1 : 0);
        var maxX = ((tX + tW) / lv) + (((tX + tW) % lv > 0) ? 1 : 0) - 1;
        var maxY = ((tY + tH) / lv) + (((tY + tH) % lv > 0) ? 1 : 0) - 1;

        return ExtractTextureIfExists(level, layer, minX, minY, maxX, maxY);
    }

    public BC3Image? ExtractTextureIfExists(int levelIndex, int layer, int minX, int minY, int maxX, int maxY)
    {
        GTSFlatTileInfo tile = default;
        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                if (!GetTileInfo(levelIndex, layer, x, y, ref tile))
                {
                    return null;
                }
            }
        }
        return ExtractTexture(levelIndex, layer, minX, minY, maxX, maxY);
    }
}
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace LSLib.VirtualTextures;

public sealed class BuiltChunk
{
    public GTSCodec Codec { get; set; }
    public uint ParameterBlockID { get; set; }
    public byte[] EncodedBlob { get; set; } = [];
    public int ChunkIndex { get; set; }
    public uint OffsetInPage { get; set; }
}

public sealed class PageBuilder
{
    public PageFileBuilder? PageFile { get; set; }
    public List<BuiltChunk> Chunks { get; init; } = [];
    public int PageFileIndex { get; set; }
    public int PageIndex { get; set; }
    public int Budget { get; set; }

    public bool TryAdd(BuildTile tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        var pageConfig = PageFile?.Config ?? throw new InvalidOperationException("PageBuilder missing reference context registry loop parameters.");

        if (tile.AddedToPageFile)
        {
            throw new InvalidOperationException("Tried to add tile to page file multiple times");
        }

        var chunkSize = 4 + Unsafe.SizeOf<GTPChunkHeader>() + tile.Compressed.Data.Length;
        if (Budget + chunkSize > pageConfig.PageSize)
        {
            return false;
        }

        var chunk = new BuiltChunk
        {
            Codec = GTSCodec.BC3,
            ParameterBlockID = tile.Compressed.ParameterBlockID,
            EncodedBlob = tile.Compressed.Data,
            ChunkIndex = Chunks.Count
        };

        tile.AddedToPageFile = true;
        tile.PageFileIndex = PageFileIndex;
        tile.PageIndex = PageIndex;
        tile.ChunkIndex = chunk.ChunkIndex;
        Chunks.Add(chunk);
        Budget += chunkSize;
        return true;
    }
}

public sealed class PageFileBuilder(TileSetConfiguration config)
{
    public TileSetConfiguration Config { get; } = config ?? throw new ArgumentNullException(nameof(config));
    public List<PageBuilder> Pages { get; init; } = [];
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public Guid Checksum { get; set; }
    public int PageFileIndex { get; set; }

    public List<BuildTile> PendingTiles { get; set; } = [];
    public List<(BuildTile Tile, BuildTile Original)> Duplicates { get; init; } = [];

    public void AddTile(BuildTile tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        PendingTiles.Add(tile);
    }

    public void DeduplicateTiles()
    {
        var digests = new Dictionary<Guid, BuildTile>();
        Span<byte> hashBuffer = stackalloc byte[MD5.HashSizeInBytes];

        foreach (var tile in CollectionsMarshal.AsSpan(PendingTiles))
        {
            if (tile?.Image?.Data is null) continue;

            MD5.HashData(tile.Image.Data, hashBuffer);
            var digest = new Guid(hashBuffer);

            if (!digests.TryAdd(digest, tile))
            {
                var original = digests[digest];
                tile.DuplicateOf = original;
                Duplicates.Add((tile, original));
            }
        }

        PendingTiles = [.. digests.Values];
    }

    public void CommitTiles()
    {
        foreach (var tile in CollectionsMarshal.AsSpan(PendingTiles))
        {
            CommitTile(tile);
        }

        PendingTiles.Clear();

        foreach (var (tile, original) in CollectionsMarshal.AsSpan(Duplicates))
        {
            if (tile is null || original is null) continue;
            tile.AddedToPageFile = true;
            tile.PageFileIndex = original.PageFileIndex;
            tile.PageIndex = original.PageIndex;
            tile.ChunkIndex = original.ChunkIndex;
            tile.DuplicateOf = original;
        }

        Duplicates.Clear();
    }

    private void CommitTile(BuildTile tile)
    {
        if (Config.BackfillPages)
        {
            foreach (var page in CollectionsMarshal.AsSpan(Pages))
            {
                if (page.TryAdd(tile))
                {
                    return;
                }
            }
        }

        if (Pages.Count == 0 || !Pages[^1].TryAdd(tile))
        {
            var newPage = new PageBuilder
            {
                PageFile = this,
                PageFileIndex = PageFileIndex,
                PageIndex = Pages.Count,
                Budget = 4
            };

            if (newPage.PageIndex == 0)
            {
                newPage.Budget += Unsafe.SizeOf<GTPHeader>();
            }

            Pages.Add(newPage);
            newPage.TryAdd(tile);
        }
    }

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var normalizedPath = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

        using var stream = File.Open(normalizedPath, FileMode.Create, FileAccess.ReadWrite);
        using var writer = new BinaryWriter(stream);
        Save(stream, writer);
    }

    public static void SaveChunk(Stream s, BuiltChunk chunk)
    {
        var header = new GTPChunkHeader
        {
            Codec = chunk.Codec,
            ParameterBlockID = chunk.ParameterBlockID,
            Size = (uint)chunk.EncodedBlob.Length
        };

        Span<byte> structBuffer = stackalloc byte[Unsafe.SizeOf<GTPChunkHeader>()];
        MemoryMarshal.Write(structBuffer, in header);
        s.Write(structBuffer);
        s.Write(chunk.EncodedBlob);
    }

    public void Save(Stream s, BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(writer);

        var header = new GTPHeader
        {
            Magic = GTPHeader.HeaderMagic,
            Version = GTPHeader.DefaultVersion,
            GUID = Checksum
        };

        Span<byte> headerBuffer = stackalloc byte[Unsafe.SizeOf<GTPHeader>()];
        MemoryMarshal.Write(headerBuffer, in header);
        s.Write(headerBuffer);

        var pagesSpan = CollectionsMarshal.AsSpan(Pages);
        Span<byte> paddingFallback = stackalloc byte[1024];

        for (var i = 0; i < pagesSpan.Length; i++)
        {
            var page = pagesSpan[i];

            writer.Write((uint)page.Chunks.Count);
            foreach (var chunk in CollectionsMarshal.AsSpan(page.Chunks))
            {
                writer.Write(chunk.OffsetInPage);
            }

            foreach (var chunk in CollectionsMarshal.AsSpan(page.Chunks))
            {
                chunk.OffsetInPage = (uint)(s.Position % Config.PageSize);
                SaveChunk(s, chunk);
            }

            if (s.Position > (long)Config.PageSize * (i + 1))
            {
                throw new InvalidDataException($"Overrun while writing page {i} of page file {Name}");
            }

            var padSize = (Config.PageSize - (uint)(s.Position % Config.PageSize)) % Config.PageSize;
            if (padSize > 0)
            {
                if (padSize <= 1024)
                {
                    s.Write(paddingFallback[..(int)padSize]);
                }
                else
                {
                    s.Write(new byte[padSize]);
                }
            }
        }

        for (var i = 0; i < pagesSpan.Length; i++)
        {
            var page = pagesSpan[i];
            s.Position = (long)(i * Config.PageSize);
            if (i == 0)
            {
                s.Position += Unsafe.SizeOf<GTPHeader>();
            }

            writer.Write((uint)page.Chunks.Count);
            foreach (var chunk in CollectionsMarshal.AsSpan(page.Chunks))
            {
                writer.Write(chunk.OffsetInPage);
            }
        }
    }
}

public sealed class PageFileSetBuilder(TileSetBuildData buildData, TileSetConfiguration config)
{
    private readonly TileSetBuildData _buildData = buildData ?? throw new ArgumentNullException(nameof(buildData));
    private readonly TileSetConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));

    public List<PageFileBuilder> PageFiles { get; init; } = [];

    private void BuildPageFile(PageFileBuilder file, int level, int minTileX, int minTileY, int maxTileX, int maxTileY)
    {
        if (_buildData.Layers is null) return;

        var layersSpan = CollectionsMarshal.AsSpan(_buildData.Layers);

        for (var y = minTileY; y <= maxTileY; y++)
        {
            for (var x = minTileX; x <= maxTileX; x++)
            {
                for (var layer = 0; layer < layersSpan.Length; layer++)
                {
                    var currentLayer = layersSpan[layer];
                    if (currentLayer?.Levels is null || level >= currentLayer.Levels.Count) continue;

                    var levelsSpan = CollectionsMarshal.AsSpan(currentLayer.Levels);
                    var tile = levelsSpan[level]?.Get(x, y);

                    if (tile is not null)
                    {
                        file.AddTile(tile);
                    }
                }
            }
        }
    }

    private void BuildPageFile(PageFileBuilder file, BuildTexture texture)
    {
        for (var level = 0; level < _buildData.MipFileStartLevel; level++)
        {
            var x = texture.X >> level;
            var y = texture.Y >> level;
            var width = texture.Width >> level;
            var height = texture.Height >> level;

            var minTileX = x / _buildData.RawTileWidth;
            var minTileY = y / _buildData.RawTileHeight;
            var maxTileX = (x + width - 1) / _buildData.RawTileWidth;
            var maxTileY = (y + height - 1) / _buildData.RawTileHeight;

            BuildPageFile(file, level, minTileX, minTileY, maxTileX, maxTileY);
        }
    }

    private void BuildMipPageFile(PageFileBuilder file)
    {
        if (_buildData.Layers is null || _buildData.Layers.Count == 0) return;

        var firstLayer = _buildData.Layers[0];
        if (firstLayer?.Levels is null) return;

        for (var level = _buildData.MipFileStartLevel; level < _buildData.PageFileLevels; level++)
        {
            if (level >= firstLayer.Levels.Count) continue;

            var levelsSpan = CollectionsMarshal.AsSpan(firstLayer.Levels);
            var lvl = levelsSpan[level];
            if (lvl is null) continue;

            BuildPageFile(file, level, 0, 0, lvl.TilesX - 1, lvl.TilesY - 1);
        }
    }

    private void BuildFullPageFile(PageFileBuilder file)
    {
        if (_buildData.Layers is null || _buildData.Layers.Count == 0) return;

        var firstLayer = _buildData.Layers[0];
        if (firstLayer?.Levels is null) return;

        for (var level = 0; level < _buildData.PageFileLevels; level++)
        {
            if (level >= firstLayer.Levels.Count) continue;

            var levelsSpan = CollectionsMarshal.AsSpan(firstLayer.Levels);
            var lvl = levelsSpan[level];
            if (lvl is null) continue;

            BuildPageFile(file, level, 0, 0, lvl.TilesX - 1, lvl.TilesY - 1);
        }
    }

    public List<PageFileBuilder> BuildFilePerGTex(List<BuildTexture> textures)
    {
        ArgumentNullException.ThrowIfNull(textures);
        uint firstPageIndex = 0;

        foreach (var texture in CollectionsMarshal.AsSpan(textures))
        {
            if (texture is null) continue;

            var file = new PageFileBuilder(_config)
            {
                Name = texture.Name ?? string.Empty,
                FileName = $"{_buildData.GTSName}_{texture.Name}.gtp",
                Checksum = Guid.NewGuid(),
                PageFileIndex = PageFiles.Count
            };

            PageFiles.Add(file);
            BuildPageFile(file, texture);

            firstPageIndex += (uint)file.Pages.Count;
        }

        if (_buildData.MipFileStartLevel < _buildData.PageFileLevels)
        {
            var file = new PageFileBuilder(_config)
            {
                Name = "Mips",
                FileName = $"{_buildData.GTSName}_Mips.gtp",
                Checksum = Guid.NewGuid(),
                PageFileIndex = PageFiles.Count
            };

            PageFiles.Add(file);
            BuildMipPageFile(file);
        }

        return PageFiles;
    }

    public List<PageFileBuilder> BuildSingleFile()
    {
        var file = new PageFileBuilder(_config)
        {
            Name = "Global",
            FileName = $"{_buildData.GTSName}.gtp",
            Checksum = Guid.NewGuid(),
            PageFileIndex = PageFiles.Count
        };

        PageFiles.Add(file);
        BuildFullPageFile(file);

        return PageFiles;
    }

    public void DeduplicateTiles()
    {
        if (_config.DeduplicateTiles)
        {
            foreach (PageFileBuilder file in CollectionsMarshal.AsSpan(PageFiles))
            {
                file.DeduplicateTiles();
            }
        }
    }

    public List<PageFileBuilder> CommitPageFiles()
    {
        foreach (PageFileBuilder file in CollectionsMarshal.AsSpan(PageFiles))
        {
            file.CommitTiles();
        }

        return PageFiles;
    }
}
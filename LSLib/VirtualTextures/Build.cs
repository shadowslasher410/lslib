using System.Diagnostics.CodeAnalysis;
using System.Xml;

namespace LSLib.VirtualTextures;

public sealed class TextureDescriptor
{
    public string Name { get; set; } = string.Empty;
    public List<string> Layers { get; set; } = [];
}

public sealed class TileSetDescriptor
{
    public string Name { get; set; } = string.Empty;
    public List<TextureDescriptor> Textures { get; set; } = [];

    public TileSetConfiguration Config { get; set; } = new();

    public string RootPath { get; set; } = string.Empty;
    public string SourceTexturePath { get; set; } = string.Empty;
    public string VirtualTexturePath { get; set; } = string.Empty;

    public void Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        using var f = File.OpenRead(path);
        var doc = new XmlDocument();
        doc.Load(f);
        Load(doc);
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "Safe unboxed string parsing loops.")]
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safe parsing lookup endpoints.")]
    public void Load(XmlDocument? doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var root = doc.DocumentElement ?? throw new ArgumentNullException(nameof(doc), "XML text configuration initialization stream model cannot be null.");

        var version = root.GetAttribute("Version");
        if (version is not "2")
        {
            throw new InvalidDataException("Expected TileSet XML descriptor version 2");
        }

        Name = root.GetAttribute("Name") ?? string.Empty;
        Config.GTSName = Name;
        Config.Layers = [];

        if (root.GetElementsByTagName("TileSetConfig").Cast<XmlElement>().FirstOrDefault() is { } tileSetConfig)
        {
            foreach (var node in tileSetConfig.ChildNodes)
            {
                if (node is not XmlElement childElement) continue;

                var key = childElement.Name;
                var value = childElement.InnerText;

                switch (key)
                {
                    case "TileWidth": Config.TileWidth = int.Parse(value); break;
                    case "TileHeight": Config.TileHeight = int.Parse(value); break;
                    case "TileBorder": Config.TileBorder = int.Parse(value); break;
                    case "Compression":
                        Config.Compression = value.ToLowerInvariant() switch
                        {
                            "none" => TileCompressionPreference.Uncompressed,
                            "fast" => TileCompressionPreference.LZ4,
                            _ => TileCompressionPreference.Best
                        };
                        break;
                    case "PageSize": Config.PageSize = int.Parse(value); break;
                    case "OneFilePerGTex": Config.OneFilePerGTex = bool.Parse(value); break;
                    case "BackfillPages": Config.BackfillPages = bool.Parse(value); break;
                    case "DeduplicateTiles": Config.DeduplicateTiles = bool.Parse(value); break;
                    case "EmbedMips": Config.EmbedMips = bool.Parse(value); break;
                    case "EmbedTopLevelMips": Config.EmbedTopLevelMips = bool.Parse(value); break;
                    case "ZeroBorders": Config.ZeroBorders = bool.Parse(value); break;
                    case "FastBuild": Config.FastBuild = bool.Parse(value); break;
                    case "Validate": Config.Validate = bool.Parse(value); break;
                    default: throw new InvalidDataException($"Unsupported configuration key: {key}");
                }
            }
        }

        if (root.GetElementsByTagName("Paths").Cast<XmlElement>().FirstOrDefault() is { } pathsElement)
        {
            foreach (var node in pathsElement.ChildNodes)
            {
                if (node is not XmlElement pathNode) continue;

                var key = pathNode.Name;
                var normalizedValue = pathNode.InnerText.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

                _ = key switch
                {
                    "SourceTextures" => SourceTexturePath = Path.Combine(RootPath ?? string.Empty, normalizedValue),
                    "VirtualTextures" => VirtualTexturePath = Path.Combine(RootPath ?? string.Empty, normalizedValue),
                    _ => throw new InvalidDataException($"Unsupported path type: {key}")
                };
            }
        }

        if (root.GetElementsByTagName("Layers").Cast<XmlElement>().FirstOrDefault() is { } layersElement)
        {
            foreach (var node in layersElement.GetElementsByTagName("Layer").Cast<XmlElement>())
            {
                var typeAttr = node.GetAttribute("Type");
                var resolvedDataType = typeAttr.ToLowerInvariant() switch
                {
                    "normal" => GTSDataType.X8Y8Z0_TANGENT, // Replaces GTSDataType.Normal
                    "height" => GTSDataType.X16,            // Replaces GTSDataType.Height
                    "clear" => GTSDataType.R8G8B8A8_SRGB,   // Replaces GTSDataType.Clear
                    _ => GTSDataType.R8G8B8_SRGB            // Replaces GTSDataType.None
                };

                Config.Layers.Add(new BuildLayer
                {
                    DataType = resolvedDataType,
                    Name = node.GetAttribute("Name") ?? string.Empty
                });
            }
        }

        if (Config.Layers.Count == 0)
        {
            throw new InvalidDataException("No tile set layers specified");
        }

        foreach (var textureElement in root.GetElementsByTagName("Texture").Cast<XmlElement>())
        {
            var tex = new TextureDescriptor
            {
                Name = textureElement.GetAttribute("Name") ?? string.Empty,
                Layers = [.. Enumerable.Repeat(string.Empty, Config.Layers.Count)]
            };
            Textures.Add(tex);

            foreach (var layerElement in textureElement.GetElementsByTagName("Layer").Cast<XmlElement>())
            {
                var name = layerElement.GetAttribute("Name");
                var index = Config.Layers.FindIndex(ly => ly.Name == name);
                if (index == -1)
                {
                    throw new InvalidDataException($"Layer does not exist inside active configuration properties map: '{name}'");
                }

                tex.Layers[index] = layerElement.GetAttribute("Source") ?? string.Empty;
            }
        }
    }
}

public sealed class BuildTile(int width = 0, int height = 0)
{
    public BC3Image Image { get; set; } = new(width, height);
    public BC3Image EmbeddedMip { get; set; } = new(width, height);
    public CompressedTile Compressed { get; set; } = new();

    public int Layer { get; set; }
    public GTSCodec Codec { get; set; }
    public GTSDataType DataType { get; set; }

    public int Level { get; set; }
    public int X { get; set; }
    public int Y { get; set; }

    public bool AddedToPageFile { get; set; }
    public int PageFileIndex { get; set; }
    public int PageIndex { get; set; }
    public int ChunkIndex { get; set; }
    public required BuildTile DuplicateOf { get; set; }
}

public sealed class BuildLayer
{
    public GTSDataType DataType { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<BuildLevel> Levels { get; set; } = [];
}

public sealed class TileSetConfiguration
{
    public string GTSName { get; set; } = string.Empty;
    public int TileWidth { get; set; } = 0x80;
    public int TileHeight { get; set; } = 0x80;
    public int TileBorder { get; set; } = 8;
    public List<BuildLayer> Layers { get; set; } = [];
    public TileCompressionPreference Compression { get; set; } = TileCompressionPreference.Best;
    public int PageSize { get; set; } = 0x100000;
    public bool OneFilePerGTex { get; set; }
    public bool BackfillPages { get; set; } = true;
    public bool DeduplicateTiles { get; set; } = true;
    public bool EmbedMips { get; set; } = true;
    public bool EmbedTopLevelMips { get; set; } = true;
    public bool ZeroBorders { get; set; }
    public bool FastBuild { get; set; }
    public bool Validate { get; set; }
}

public sealed class BuildLayerTexture
{
    public string Path { get; set; } = string.Empty;
    public int FirstMip { get; set; }
    public BC3Mips Mips { get; init; } = new();
}

public sealed class BuildTexture
{
    public string Name { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public List<BuildLayerTexture> Layers { get; init; } = [];
}

public sealed class TileSetBuildData
{
    public List<BuildLayer> Layers { get; init; } = [];
    public string GTSName { get; set; } = string.Empty;
    public int PaddedTileWidth { get; set; }
    public int PaddedTileHeight { get; set; }
    public int RawTileWidth { get; set; }
    public int RawTileHeight { get; set; }
    public int TileBorder { get; set; }
    public int TotalWidth { get; set; }
    public int TotalHeight { get; set; }
    public int PageFileLevels { get; set; }
    public int BuildLevels { get; set; }
    public int MipFileStartLevel { get; set; }
}

public sealed class ParameterBlock
{
    public required GTSCodec Codec { get; init; }
    public required GTSDataType DataType { get; init; }
    public required TileCompressionMethod Compression { get; init; }
    public required uint ParameterBlockID { get; init; }
}

public sealed class ParameterBlockContainer
{
    public List<ParameterBlock> ParameterBlocks { get; init; } = [];

    public uint NextParameterBlockID { get; private set; } = 1;

    public ParameterBlock GetOrAdd(GTSCodec codec, GTSDataType dataType, TileCompressionMethod compression)
    {
        if (ParameterBlocks.FirstOrDefault(b => b is not null && b.Codec == codec && b.DataType == dataType && b.Compression == compression) is { } block)
        {
            return block;
        }

        var newBlock = new ParameterBlock
        {
            Codec = codec,
            DataType = dataType,
            Compression = compression,
            ParameterBlockID = NextParameterBlockID++
        };
        ParameterBlocks.Add(newBlock);

        return newBlock;
    }
}

public sealed class BuildLevel
{
    public int Level { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int TilesX { get; set; }
    public int TilesY { get; set; }
    public int PaddedTileWidth { get; set; }
    public int PaddedTileHeight { get; set; }
    public BuildTile[] Tiles { get; set; } = [];

    private int GetTileOffset(int x, int y) =>
        (x >= TilesX || y >= TilesY) ? throw new ArgumentException("Invalid tile index") : (x + TilesX * y);

    public BuildTile Get(int x, int y) => Tiles[GetTileOffset(x, y)];

    public BuildTile GetOrCreateTile(int x, int y, int layer, GTSCodec codec, GTSDataType dataType)
    {
        int off = GetTileOffset(x, y);

        return Tiles[off] ??= new BuildTile(PaddedTileWidth, PaddedTileHeight)
        {
            Layer = layer,
            Codec = codec,
            DataType = dataType,
            Level = Level,
            X = x,
            Y = y,
            DuplicateOf = null!
        };
    }
}
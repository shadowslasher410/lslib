using System.Numerics;

namespace LSLib.VirtualTextures;

public sealed class TileSetBuilder
{
    private readonly TileSetBuildData _buildData;
    private readonly TileSetConfiguration _config;
    private readonly TileCompressor _compressor;
    private readonly ParameterBlockContainer _parameterBlocks;

    public PageFileSetBuilder? SetBuilder { get; set; }
    public VirtualTileSet? TileSet { get; set; }
    public List<BuildTexture> Textures { get; init; } = [];
    public List<PageFileBuilder>? PageFiles { get; set; }
    public Action<string> OnStepStarted { get; set; } = delegate { };
    public Action<int, int> OnStepProgress { get; set; } = delegate { };

    public TileSetBuilder(TileSetConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _config = config;
        _buildData = new TileSetBuildData
        {
            Layers = config.Layers,
            GTSName = config.GTSName,
            PaddedTileWidth = config.TileWidth + (config.TileBorder << 1),
            PaddedTileHeight = config.TileHeight + (config.TileBorder << 1),
            RawTileWidth = config.TileWidth,
            RawTileHeight = config.TileHeight,
            TileBorder = config.TileBorder
        };

        _parameterBlocks = new ParameterBlockContainer();
        _compressor = new TileCompressor
        {
            Preference = _config.Compression,
            ParameterBlocks = _parameterBlocks
        };
    }

    public void AddTexture(string name, List<string> texturePaths)
    {
        ArgumentNullException.ThrowIfNull(texturePaths);

        var tex = new BuildTexture
        {
            Name = name,
            Width = 0,
            Height = 0,
            X = 0,
            Y = 0,
            Layers = new List<BuildLayerTexture>(texturePaths.Count)
        };

        foreach (var path in CollectionsMarshal.AsSpan(texturePaths))
        {
            if (path is null)
            {
                tex.Layers.Add(null!);
                continue;
            }

            var mips = new BC3Mips();
            mips.LoadDDS(path);

            if (mips.Mips.Count <= 1)
            {
                throw new InvalidDataException($"Texture must include mipmaps: {path}");
            }

            var mip = mips.Mips[0];
            if ((mip.Width % _buildData.RawTileWidth) != 0 || (mip.Height % _buildData.RawTileHeight) != 0)
            {
                throw new InvalidDataException($"Texture {path} size ({mip.Width}x{mip.Height}) must be a multiple of the virtual tile size ({_buildData.RawTileWidth}x{_buildData.RawTileHeight})");
            }

            if (!BitOperations.IsPow2((uint)mip.Width) || !BitOperations.IsPow2((uint)mip.Height))
            {
                throw new InvalidDataException($"Texture {path} size ({mip.Width}x{mip.Height}) must be a power of two");
            }

            tex.Layers.Add(new BuildLayerTexture
            {
                Path = path,
                FirstMip = 0,
                Mips = mips
            });
        }

        var layersSpan = CollectionsMarshal.AsSpan(tex.Layers);

        foreach (var layer in layersSpan)
        {
            if (layer is null) continue;

            var topMip = layer.Mips.Mips[0];

            if (topMip.Width > tex.Width) tex.Width = topMip.Width;
            if (topMip.Height > tex.Height) tex.Height = topMip.Height;
        }

        foreach (var layer in layersSpan)
        {
            if (layer is null) continue;

            var mip = layer.Mips.Mips[0];
            if (mip.Width > tex.Width || mip.Height > tex.Height)
            {
                throw new InvalidDataException($"Top-level texture size mismatch; texture {layer.Path} is {mip.Width}x{mip.Height}, size across all layers is {tex.Width}x{tex.Height}");
            }

            var mulW = tex.Width / mip.Width;
            var mulH = tex.Height / mip.Height;

            if ((tex.Width % mip.Width) != 0 || (tex.Height % mip.Height) != 0 || mulW != mulH || !BitOperations.IsPow2((uint)mulW))
            {
                throw new InvalidDataException($"Texture sizes within all layers should be multiples of each other; texture {layer.Path} is {mip.Width}x{mip.Height}, size across all layers is {tex.Width}x{tex.Height}");
            }

            if (mulW > 1)
            {
                layer.FirstMip += BitOperations.Log2((uint)mulW);
            }
        }

        Console.WriteLine($"Added GTex {tex.Name} ({tex.Width}x{tex.Height})");
        Textures.Add(tex);
    }
}
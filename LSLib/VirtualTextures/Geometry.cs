namespace LSLib.VirtualTextures;

public sealed class TileSetGeometryCalculator
{
    public List<BuildTexture> Textures { get; init; } = [];
    public TileSetBuildData BuildData { get; init; } = new();

    private int _placementTileWidth = 0x1000;
    private int _placementTileHeight = 0x1000;
    private int _placementGridWidth;
    private int _placementGridHeight;
    private BuildTexture[] _placementGrid = [];

    private void ResizePlacementGrid(int w, int h)
    {
        _placementGridWidth = w;
        _placementGridHeight = h;

        int size = w * h;
        if (_placementGrid.Length < size)
        {
            _placementGrid = new BuildTexture[size];
        }
        else
        {
            _placementGrid.AsSpan(0, size).Clear();
        }
    }

    private void GrowPlacementGrid()
    {
        if (_placementGridWidth * _placementTileWidth <= _placementGridHeight * _placementTileHeight)
        {
            ResizePlacementGrid(_placementGridWidth << 1, _placementGridHeight);
        }
        else
        {
            ResizePlacementGrid(_placementGridWidth, _placementGridHeight << 1);
        }
    }

    private bool TryToPlaceTexture(BuildTexture texture, int texX, int texY)
    {
        var width = texture.Width / BuildData.RawTileWidth / _placementTileWidth;
        var height = texture.Height / BuildData.RawTileHeight / _placementTileHeight;

        if (texX + width > _placementGridWidth || texY + height > _placementGridHeight)
        {
            return false;
        }

        ReadOnlySpan<BuildTexture> gridSpan = _placementGrid;
        for (var y = texY; y < texY + height; y++)
        {
            var rowOffset = y * _placementGridWidth;
            var rowSlice = gridSpan.Slice(rowOffset + texX, width);

            if (rowSlice.IndexOfAnyExcept((BuildTexture)null!) >= 0)
            {
                return false;
            }
        }

        texture.X = texX * _placementTileWidth * BuildData.RawTileWidth;
        texture.Y = texY * _placementTileHeight * BuildData.RawTileHeight;

        Span<BuildTexture> writableGrid = _placementGrid;
        for (var y = texY; y < texY + height; y++)
        {
            var rowOffset = y * _placementGridWidth;
            writableGrid.Slice(rowOffset + texX, width).Fill(texture);
        }

        return true;
    }

    private bool TryToPlaceTexture(BuildTexture texture)
    {
        var width = texture.Width / BuildData.RawTileWidth / _placementTileWidth;
        var height = texture.Height / BuildData.RawTileHeight / _placementTileHeight;

        for (var y = 0; y < _placementGridHeight - height + 1; y++)
        {
            for (var x = 0; x < _placementGridWidth - width + 1; x++)
            {
                if (TryToPlaceTexture(texture, x, y))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool PlaceAllTextures()
    {
        foreach (var tex in CollectionsMarshal.AsSpan(Textures))
        {
            if (!TryToPlaceTexture(tex))
            {
                return false;
            }
        }
        return true;
    }

    private void DoAutoPlacement()
    {
        var startingX = 0;
        var startingY = 0;

        foreach (var tex in CollectionsMarshal.AsSpan(Textures))
        {
            _placementTileWidth = Math.Min(_placementTileWidth, tex.Width / BuildData.RawTileWidth);
            _placementTileHeight = Math.Min(_placementTileHeight, tex.Height / BuildData.RawTileHeight);
            startingX = Math.Max(startingX, tex.Width / BuildData.RawTileWidth);
            startingY = Math.Max(startingY, tex.Height / BuildData.RawTileHeight);
        }

        if (_placementTileWidth == 0 || _placementTileHeight == 0)
        {
            throw new InvalidOperationException("Malformed image resolution parameters encountered during packer alignment sweeps.");
        }

        ResizePlacementGrid(startingX / _placementTileWidth, startingY / _placementTileHeight);

        while (!PlaceAllTextures())
        {
            GrowPlacementGrid();
        }

        BuildData.TotalWidth = _placementTileWidth * _placementGridWidth * BuildData.RawTileWidth;
        BuildData.TotalHeight = _placementTileHeight * _placementGridHeight * BuildData.RawTileWidth;
    }

    private void UpdateGeometry()
    {
        var minTexSize = 0x10000;
        foreach (var tex in CollectionsMarshal.AsSpan(Textures))
        {
            minTexSize = Math.Min(minTexSize, Math.Min(tex.Height / BuildData.RawTileHeight, tex.Width / BuildData.RawTileHeight));
        }

        BuildData.MipFileStartLevel = 0;
        while (minTexSize > 0)
        {
            BuildData.MipFileStartLevel++;
            minTexSize >>= 1;
        }

        var minSize = Math.Min(BuildData.TotalWidth / BuildData.RawTileHeight, BuildData.TotalHeight / BuildData.RawTileHeight);
        BuildData.PageFileLevels = 0;
        while (minSize > 0)
        {
            BuildData.PageFileLevels++;
            minSize >>= 1;
        }

        BuildData.BuildLevels = BuildData.PageFileLevels + 1;

        foreach (var layer in CollectionsMarshal.AsSpan(BuildData.Layers))
        {
            var levelWidth = BuildData.TotalWidth;
            var levelHeight = BuildData.TotalHeight;

            layer.Levels = new List<BuildLevel>(BuildData.BuildLevels);
            for (var i = 0; i < BuildData.BuildLevels; i++)
            {
                var tilesX = (levelWidth / BuildData.RawTileWidth) + ((levelWidth % BuildData.RawTileWidth > 0) ? 1 : 0);
                var tilesY = (levelHeight / BuildData.RawTileHeight) + ((levelHeight % BuildData.RawTileHeight > 0) ? 1 : 0);

                var level = new BuildLevel
                {
                    Level = i,
                    Width = tilesX * BuildData.RawTileWidth,
                    Height = tilesY * BuildData.RawTileHeight,
                    TilesX = tilesX,
                    TilesY = tilesY,
                    PaddedTileWidth = BuildData.PaddedTileWidth,
                    PaddedTileHeight = BuildData.PaddedTileHeight,
                    Tiles = new BuildTile[tilesX * tilesY]
                };
                layer.Levels.Add(level);

                levelWidth = Math.Max(1, levelWidth >>= 1);
                levelHeight = Math.Max(1, levelHeight >>= 1);
            }
        }
    }

    public void Update()
    {
        DoAutoPlacement();
        UpdateGeometry();
    }
}
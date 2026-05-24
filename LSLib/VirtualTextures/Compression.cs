using LSLib.LS;
using LSLib.LS.Enums;

namespace LSLib.VirtualTextures;

public enum TileCompressionMethod { Raw, LZ4, LZ77 }
public enum TileCompressionPreference { Uncompressed, Best, LZ4, LZ77 }

public sealed class CompressedTile
{
    public TileCompressionMethod Method { get; set; }
    public uint ParameterBlockID { get; set; }
    public byte[] Data { get; set; } = [];
}

public sealed class TileCompressor
{
    public required ParameterBlockContainer ParameterBlocks { get; init; }
    public TileCompressionPreference Preference { get; set; } = TileCompressionPreference.Best;

    private static readonly Func<byte[], LSCompressionLevel, byte[]>? Lz4CompressDelegate =
        Type.GetType("LSLib.LS.CompressionHelpers")?.GetMethod("CompressLZ4", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?.CreateDelegate<Func<byte[], LSCompressionLevel, byte[]>>();

    private static readonly Func<byte[], int, byte[]>? Lz77CompressDelegate =
        Type.GetType("LSLib.Native.FastLZCompressor")?.GetMethod("Compress", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?.CreateDelegate<Func<byte[], int, byte[]>>();

    private static readonly Func<byte[], int, CompressionFlags, byte[]>? Lz4DecompressDelegate =
        Type.GetType("LSLib.LS.CompressionHelpers")?.GetMethod("Decompress", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?.CreateDelegate<Func<byte[], int, CompressionFlags, byte[]>>();

    private static readonly Func<byte[], int, byte[]>? Lz77DecompressDelegate =
        Type.GetType("LSLib.Native.FastLZCompressor")?.GetMethod("Decompress", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?.CreateDelegate<Func<byte[], int, byte[]>>();

    private static byte[] GetRawBytes(BuildTile tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        if (tile.EmbeddedMip is null)
        {
            return tile.Image.Data;
        }

        var data = new byte[tile.Image.Data.Length + tile.EmbeddedMip.Data.Length];
        Span<byte> destination = data;

        tile.Image.Data.CopyTo(destination);
        tile.EmbeddedMip.Data.CopyTo(destination[tile.Image.Data.Length..]);

        return data;
    }

    public static byte[] CompressLZ4(byte[] raw, bool fast)
    {
        ArgumentNullException.ThrowIfNull(raw);
        return Lz4CompressDelegate?.Invoke(raw, fast ? LSCompressionLevel.Fast : LSCompressionLevel.Max) ?? [];
    }

    public static byte[] CompressLZ77(byte[] raw, bool fast)
    {
        ArgumentNullException.ThrowIfNull(raw);
        return Lz77CompressDelegate?.Invoke(raw, fast ? 0 : 2) ?? [];
    }

    public byte[] Compress(byte[] uncompressed, bool fast, out TileCompressionMethod method)
    {
        ArgumentNullException.ThrowIfNull(uncompressed);

        return Preference switch
        {
            TileCompressionPreference.Uncompressed => ExecuteCopy(uncompressed, TileCompressionMethod.Raw, out method),
            TileCompressionPreference.Best => ExecuteBestCompression(uncompressed, fast, out method),
            TileCompressionPreference.LZ4 => ExecuteLZ4(uncompressed, fast, out method),
            TileCompressionPreference.LZ77 => ExecuteLZ77(uncompressed, fast, out method),
            _ => throw new ArgumentException("Invalid compression preference framework parameter context mapping configuration.")
        };

        static byte[] ExecuteCopy(byte[] input, TileCompressionMethod selection, out TileCompressionMethod m)
        { m = selection; return input; }

        byte[] ExecuteBestCompression(byte[] input, bool speed, out TileCompressionMethod m)
        {
            var lz4 = CompressLZ4(input, speed);
            var lz77 = CompressLZ77(input, speed);
            m = lz4.Length <= lz77.Length ? TileCompressionMethod.LZ4 : TileCompressionMethod.LZ77;
            return m is TileCompressionMethod.LZ4 ? lz4 : lz77;
        }

        byte[] ExecuteLZ4(byte[] input, bool speed, out TileCompressionMethod m)
        { m = TileCompressionMethod.LZ4; return CompressLZ4(input, speed); }

        byte[] ExecuteLZ77(byte[] input, bool speed, out TileCompressionMethod m)
        { m = TileCompressionMethod.LZ77; return CompressLZ77(input, speed); }
    }

    public CompressedTile Compress(BuildTile tile, bool fast)
    {
        ArgumentNullException.ThrowIfNull(tile);

        if (tile.Compressed is CompressedTile cachedTile)
        {
            return cachedTile;
        }

        var uncompressed = GetRawBytes(tile);
        var compressedData = Compress(uncompressed, fast, out var outMethod);

        var paramBlock = ParameterBlocks.GetOrAdd(
            (GTSCodec)tile.Codec,
            (GTSDataType)tile.DataType,
            (TileCompressionMethod)outMethod
        );

        var compressed = new CompressedTile
        {
            Data = compressedData,
            Method = outMethod,
            ParameterBlockID = paramBlock.ParameterBlockID
        };

        tile.Compressed = compressed;
        return compressed;
    }

    public static TileCompressionMethod GetMethod(string method1, string method2)
    {
        return (method1.ToLowerInvariant(), method2.ToLowerInvariant()) switch
        {
            ("lz77", "fastlz0.1.0") => TileCompressionMethod.LZ77,
            ("lz4", "lz40.1.0") => TileCompressionMethod.LZ4,
            ("raw", _) => TileCompressionMethod.Raw,
            _ => throw new InvalidDataException($"Unsupported compression configuration format criteria: '{method1}', '{method2}'")
        };
    }

    public static byte[] Decompress(byte[] compressed, int outputSize, string method1, string method2) =>
        Decompress(compressed, outputSize, GetMethod(method1, method2));

    public static byte[] Decompress(byte[] compressed, int outputSize, TileCompressionMethod method)
    {
        ArgumentNullException.ThrowIfNull(compressed);

        return method switch
        {
            TileCompressionMethod.Raw => compressed,
            TileCompressionMethod.LZ4 => Lz4DecompressDelegate?.Invoke(compressed, outputSize, CompressionFlags.MethodLZ4) ?? [],
            TileCompressionMethod.LZ77 => Lz77DecompressDelegate?.Invoke(compressed, outputSize) ?? [],
            _ => throw new ArgumentException("Unsupported unmanaged virtual textures compression parsing format specification method scenario.")
        };
    }
}
#pragma warning disable IDE0060 // Remove unused parameter
using System.Buffers;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;

namespace LSLib.LS;

public enum CompressionMethod
{
    None,
    Zlib,
    LZ4,
    Zstd
}

public enum LSCompressionLevel
{
    Fast,
    Default,
    Max
}

public enum CompressionFlags : byte
{
    MethodNone = 0,
    MethodZlib = 1,
    MethodLZ4 = 2,
    MethodZstd = 3,
    FastCompress = 0x10,
    DefaultCompress = 0x20,
    MaxCompress = 0x40
}

public static class CompressionFlagExtensions
{
    public static CompressionMethod Method(this CompressionFlags f)
    {
        int methodBits = (byte)f & 0x0F;
        return methodBits switch
        {
            (int)CompressionFlags.MethodNone => CompressionMethod.None,
            (int)CompressionFlags.MethodZlib => CompressionMethod.Zlib,
            (int)CompressionFlags.MethodLZ4 => CompressionMethod.LZ4,
            (int)CompressionFlags.MethodZstd => CompressionMethod.Zstd,
            _ => throw new NotSupportedException($"Unsupported compression method bitmask: {methodBits}")
        };
    }

    public static LSCompressionLevel Level(this CompressionFlags f)
    {
        int levelBits = (byte)f & 0xF0;
        return levelBits switch
        {
            (int)CompressionFlags.FastCompress => LSCompressionLevel.Fast,
            (int)CompressionFlags.DefaultCompress => LSCompressionLevel.Default,
            (int)CompressionFlags.MaxCompress => LSCompressionLevel.Max,
            _ => LSCompressionLevel.Default
        };
    }

    public static CompressionFlags ToFlags(this CompressionMethod method)
    {
        return method switch
        {
            CompressionMethod.None => CompressionFlags.MethodNone,
            CompressionMethod.Zlib => CompressionFlags.MethodZlib,
            CompressionMethod.LZ4 => CompressionFlags.MethodLZ4,
            CompressionMethod.Zstd => CompressionFlags.MethodZstd,
            _ => throw new NotSupportedException($"Unsupported compression method identifier: {method}")
        };
    }

    public static CompressionFlags ToFlags(this LSCompressionLevel level)
    {
        return level switch
        {
            LSCompressionLevel.Fast => CompressionFlags.FastCompress,
            LSCompressionLevel.Default => CompressionFlags.DefaultCompress,
            LSCompressionLevel.Max => CompressionFlags.MaxCompress,
            _ => throw new NotSupportedException($"Unsupported compression level modifier: {level}")
        };
    }
}

public sealed class LZ4DecompressionStream(MemoryMappedViewAccessor view, long offset, int size, int decompressedSize) : Stream
{
    private readonly MemoryMappedViewAccessor _view = view ?? throw new ArgumentNullException(nameof(view));
    private readonly long _offset = offset;
    private readonly int _size = size;
    private readonly int _decompressedSize = decompressedSize;

    private MemoryStream _decompressed = null!;
    private bool _isDecompressed;

    private unsafe void DoDecompression()
    {
        byte* basePtr = null;
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);
        try
        {
            ReadOnlySpan<byte> compressedSpan = new(basePtr + _view.PointerOffset + _offset, _size);
            byte[] decompressedBytes = new byte[_decompressedSize];

            int length = LZ4Codec.Decode(
                compressedSpan,
                decompressedBytes
            );

            if (length != _decompressedSize)
            {
                throw new InvalidDataException($"Failed to decompress LZ4 stream; expected {_decompressedSize}, got {length}");
            }

            _decompressed = new MemoryStream(decompressedBytes);
            _isDecompressed = true;
        }
        finally
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
        }
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _decompressedSize;

    public override long Position
    {
        get => _isDecompressed ? _decompressed.Position : 0;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (!_isDecompressed)
        {
            DoDecompression();
        }
        return _decompressed.Read(buffer, offset, count);
    }

    public override int Read(Span<byte> buffer)
    {
        if (!_isDecompressed)
        {
            DoDecompression();
        }
        return _decompressed.Read(buffer);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (!_isDecompressed)
        {
            DoDecompression();
        }
        return _decompressed.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!_isDecompressed)
        {
            DoDecompression();
        }
        return _decompressed.ReadAsync(buffer, cancellationToken);
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() { }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _isDecompressed)
        {
            _decompressed.Dispose();
        }
        base.Dispose(disposing);
    }
}

public static class CompressionHelpers
{
    public static CompressionFlags MakeCompressionFlags(CompressionMethod method, LSCompressionLevel level)
    {
        if(method == CompressionMethod.None) return CompressionFlags.MethodNone;

        CompressionFlags methodFlags = method.ToFlags();
        CompressionFlags levelFlags = level.ToFlags();
        return methodFlags | levelFlags;
    }

    public static byte[] Decompress(byte[] compressed, int decompressedSize, CompressionFlags compression, bool chunked = false)
    {
        ArgumentNullException.ThrowIfNull(compressed);
        CompressionMethod targetMethod = compression.Method();

        return targetMethod switch
        {
            CompressionMethod.None => compressed,
            CompressionMethod.Zlib => DecompressZlib(compressed),
            CompressionMethod.LZ4 => DecompressLZ4(compressed, decompressedSize, chunked),
            CompressionMethod.Zstd => DecompressZstd(compressed),
            _ => throw new InvalidDataException($"No decompressor implementation available for format: {compression}")
        };
    }

    private static byte[] DecompressZlib(byte[] compressed)
    {
        using var compressedStream = new MemoryStream(compressed);
        using var decompressedStream = new MemoryStream();
        using var stream = new ZLibStream(compressedStream, CompressionMode.Decompress);
        stream.CopyTo(decompressedStream);
        return decompressedStream.ToArray();
    }

    private static byte[] DecompressLZ4(byte[] compressed, int decompressedSize, bool chunked)
    {
        if (chunked)
        {
            using var input = new MemoryStream(compressed);
            using var output = new MemoryStream();

            using var decompressor = LZ4Stream.Decode(input);
            byte[] temp = ArrayPool<byte>.Shared.Rent(0x10000);

            try
            {
                while (decompressedSize > 0)
                {
                    int count = decompressor.Read(temp, 0, Math.Min(decompressedSize, temp.Length));
                    if (count == 0) break;
                    output.Write(temp, 0, count);
                    decompressedSize -= count;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(temp);
            }
            return output.ToArray();
        }
        else
        {
            byte[] decompressed = new byte[decompressedSize];

            int resultSize = LZ4Codec.Decode(
                compressed.AsSpan(),
                decompressed.AsSpan()
            );

            if (resultSize != decompressedSize)
            {
                throw new InvalidDataException($"LZ4 compressor disagrees about the size of compressed buffer; expected {decompressedSize}, got {resultSize}");
            }

            return decompressed;
        }
    }

    private static byte[] DecompressZstd(byte[] compressed)
    {
        using var compressedStream = new MemoryStream(compressed);
        using var decompressedStream = new MemoryStream();
        using var stream = new ZstdSharp.DecompressionStream(compressedStream);
        stream.CopyTo(decompressedStream);
        return decompressedStream.ToArray();
    }

    public static Stream Decompress(MemoryMappedFile file, MemoryMappedViewAccessor view, long sourceOffset,
        int sourceSize, int decompressedSize, CompressionFlags compression)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(view);

        if (sourceSize == 0)
        {
            return new MemoryStream();
        }
        CompressionMethod targetMethod = compression.Method();

        return targetMethod switch
        {
            CompressionMethod.None => file.CreateViewStream(sourceOffset, sourceSize, MemoryMappedFileAccess.Read),
            CompressionMethod.Zlib => new ZLibStream(file.CreateViewStream(sourceOffset, sourceSize, MemoryMappedFileAccess.Read), CompressionMode.Decompress),
            CompressionMethod.LZ4 => new LZ4DecompressionStream(view, sourceOffset, sourceSize, decompressedSize),
            CompressionMethod.Zstd => new ZstdSharp.DecompressionStream(file.CreateViewStream(sourceOffset, sourceSize, MemoryMappedFileAccess.Read)),
            _ => throw new InvalidDataException($"No streaming decompressor found for format: {compression}")
        };
    }

    public static byte[] Compress(byte[] uncompressed, CompressionFlags compression)
    {
        CompressionMethod method = compression.Method();
        LSCompressionLevel level = compression.Level();

        return Compress(uncompressed, method, level);
    }


    public static byte[] Compress(byte[] uncompressed, CompressionMethod method, LSCompressionLevel level, bool chunked = false)
    {

        return method switch
        {
            CompressionMethod.None => uncompressed,
            CompressionMethod.Zlib => CompressZlib(uncompressed, level),
            CompressionMethod.LZ4 => throw new NotImplementedException("LZ4 compression pipeline requires independent chunk configuration."),
            CompressionMethod.Zstd => throw new NotImplementedException("Zstd native compression binding context omitted for runtime parsing constraints."),
            _ => throw new ArgumentException("Invalid compression method specified")
        };
    }

    public static byte[] CompressZlib(byte[] uncompressed, LSCompressionLevel level)
    {
        CompressionLevel zLevel = level switch
        {
            LSCompressionLevel.Fast => CompressionLevel.Fastest,
            LSCompressionLevel.Default => CompressionLevel.Optimal,
            LSCompressionLevel.Max => CompressionLevel.SmallestSize,
            _ => throw new ArgumentException("Unsupported compression level map.")
        };

        using var outputStream = new MemoryStream();
        using (var compressor = new ZLibStream(outputStream, zLevel, true))
        {
            compressor.Write(uncompressed, 0, uncompressed.Length);
        }

        return outputStream.ToArray();
    }

    public static byte[] CompressLZ4(byte[] uncompressed, LSCompressionLevel compressionLevel, bool chunked = false)
    {
        ArgumentNullException.ThrowIfNull(uncompressed);

        LZ4Level level = compressionLevel switch
        {
            LSCompressionLevel.Fast => LZ4Level.L00_FAST,
            LSCompressionLevel.Default => LZ4Level.L10_OPT,
            LSCompressionLevel.Max => LZ4Level.L12_MAX,
            _ => throw new ArgumentException("Unsupported LZ4 compression level specification parameter mapping.", nameof(compressionLevel))
        };

        if (chunked)
        {
            var settings = new LZ4EncoderSettings
            {
                CompressionLevel = level
            };

            using var input = new MemoryStream(uncompressed);
            using var output = new MemoryStream();
            using (var compressor = LZ4Stream.Encode(output, settings))
            {
                input.CopyTo(compressor);
            }
            return output.ToArray();
        }
        else
        {
            byte[] compressed = new byte[LZ4Codec.MaximumOutputSize(uncompressed.Length)];

            int length = LZ4Codec.Encode(
                uncompressed,
                0,
                uncompressed.Length,
                compressed,
                0,
                compressed.Length,
                level
            );

            if (length < 0)
            {
                throw new InvalidDataException($"LZ4 data frame layout generation process failed with exit error signal: {length}");
            }

            return compressed.AsSpan(0, length).ToArray();
        }
    }

    public static byte[] CompressZstd(byte[] uncompressed, LSCompressionLevel level)
    {
        ArgumentNullException.ThrowIfNull(uncompressed);

        int zLevel = level switch
        {
            LSCompressionLevel.Fast => 3,
            LSCompressionLevel.Default => 9,
            LSCompressionLevel.Max => 22,
            _ => throw new ArgumentException("Unsupported Zstd compression level specification parameter mapping.", nameof(level))
        };

        using var outputStream = new MemoryStream();
        using (var compressor = new ZstdSharp.CompressionStream(outputStream, zLevel, 0, leaveOpen: true))
        {
            compressor.Write(uncompressed, 0, uncompressed.Length);
        }

        return outputStream.ToArray();
    }
}
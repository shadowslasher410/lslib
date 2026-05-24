using System.Runtime.CompilerServices;

namespace LSLib.Granny.GR2;

public static partial class Granny2Compressor
{
    private const string LibraryName = "granny2";

    [LibraryImport(LibraryName, EntryPoint = "GrannyDecompressData")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool NativeGrannyDecompressData(
        int format,
        [MarshalAs(UnmanagedType.Bool)] bool fileIsByteReversed,
        int compressedBytesSize,
        byte* compressedBytes,
        int stop0,
        int stop1,
        int stop2,
        byte* decompressedBytes
    );

    [LibraryImport(LibraryName, EntryPoint = "GrannyBeginFileDecompression")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe partial void* NativeGrannyBeginFileDecompression(
        int format,
        [MarshalAs(UnmanagedType.Bool)] bool fileIsByteReversed,
        int decompressedBytesSize,
        byte* decompressedBytes,
        int workMemSize,
        void* workMemBuffer
    );

    [LibraryImport(LibraryName, EntryPoint = "GrannyDecompressIncremental")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool NativeGrannyDecompressIncremental(
        void* state,
        int compressedBytesSize,
        byte* compressedBytes
    );

    [LibraryImport(LibraryName, EntryPoint = "GrannyEndFileDecompression")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool NativeGrannyEndFileDecompression(void* state);

    static Granny2Compressor()
    {
        NativeLibrary.SetDllImportResolver(typeof(Granny2Compressor).Assembly, ResolveGrannyLibrary);
    }

    public static void LoadGranny()
    {
        try
        {
            IntPtr handle = NativeLibrary.Load(LibraryName, typeof(Granny2Compressor).Assembly, null);
            if (handle == IntPtr.Zero) throw new DllNotFoundException();
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("The Granny2 runtime library asset is required to process compressed GR2 game files.", ex);
        }
    }

    public static unsafe byte[] Decompress(int format, byte[] compressed, int decompressedSize, int stop0, int stop1, int stop2)
    {
        ArgumentNullException.ThrowIfNull(compressed);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(decompressedSize);

        byte[] decompressed = new byte[decompressedSize];

        fixed (byte* pCompressed = compressed)
        fixed (byte* pDecompressed = decompressed)
        {
            bool ok = NativeGrannyDecompressData(format, false, compressed.Length, pCompressed, stop0, stop1, stop2, pDecompressed);
            if (!ok)
            {
                throw new InvalidDataException("Failed to decompress Oodle compressed GR2 section via Granny2 SDK execution branch.");
            }
        }

        return decompressed;
    }

    public static unsafe byte[] Decompress4(byte[] compressed, int decompressedSize)
    {
        ArgumentNullException.ThrowIfNull(compressed);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(decompressedSize);

        byte[] decompressed = new byte[decompressedSize];

        const int workMemSize = 0x4000;
        byte* workMem = stackalloc byte[workMemSize];

        fixed (byte* pCompressed = compressed)
        fixed (byte* pDecompressed = decompressed)
        {
            void* state = NativeGrannyBeginFileDecompression(4, false, decompressedSize, pDecompressed, workMemSize, workMem);
            if (state == null)
            {
                throw new InvalidDataException("Failed to initialize incremental GR2 section decompression state machine.");
            }

            int pos = 0;
            const int maxChunkSize = 0x2000;

            while (pos < compressed.Length)
            {
                int chunkSize = Math.Min(compressed.Length - pos, maxChunkSize);

                bool incrementOk = NativeGrannyDecompressIncremental(state, chunkSize, pCompressed + pos);
                if (!incrementOk)
                {
                    throw new InvalidDataException($"Incremental block decompilation aborted at offset sequence bounds: 0x{pos:X8}.");
                }

                pos += chunkSize;
            }

            bool ok = NativeGrannyEndFileDecompression(state);
            if (!ok)
            {
                throw new InvalidDataException("Failed to finish trailing GR2 file section decompression cycles cleanly.");
            }
        }

        return decompressed;
    }

    private static IntPtr ResolveGrannyLibrary(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName.Equals(LibraryName, StringComparison.OrdinalIgnoreCase))
        {
            string platformFileName = libraryName;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) platformFileName = "granny2.dll";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) platformFileName = "libgranny2.so";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) platformFileName = "libgranny2.dylib";

            if (NativeLibrary.TryLoad(platformFileName, assembly, searchPath, out IntPtr handle))
            {
                return handle;
            }
        }
        return IntPtr.Zero;
    }
}

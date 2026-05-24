using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace LSLib.LS;
public static class PackageVersionExtensionsBridge
{
    public static bool IsAtLeast(this PackageVersion version, PackageVersion other) => (int)version >= (int)other;
    public static bool IsGreaterThan(this PackageVersion version, PackageVersion other) => (int)version > (int)other;
    public static bool IsLessThan(this PackageVersion version, PackageVersion other) => (int)version < (int)other;
    public static bool IsEqualTo(this PackageVersion version, PackageVersion other) => (int)version == (int)other;
}

public class PackageBuildTransientFile : PackagedFileInfoCommon;

public abstract class PackageWriter(PackageBuildData build, string packagePath) : IDisposable
{
    public delegate void WriteProgressDelegate(PackageBuildInputFile file, long numerator, long denominator);

    protected readonly PackageHeaderCommon Metadata = new()
    {
        Version = (uint)build.Version,
        Flags = build.Flags,
        Priority = build.Priority,
        Md5 = new byte[16]
    };

    protected readonly List<Stream> Streams = [File.Open(packagePath, FileMode.Create, FileAccess.Write)];
    protected readonly PackageBuildData Build = build;
    protected readonly string PackagePath = packagePath;
    protected Stream MainStream => Streams[0];
    public WriteProgressDelegate WriteProgress { get; set; } = delegate { };

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (Stream stream in Streams)
        {
            stream.Dispose();
        }
    }

    protected static bool CanCompressFile(PackageBuildInputFile file, Stream inputStream)
    {
        ReadOnlySpan<char> extension = Path.GetExtension(file.Path.AsSpan());

        return !extension.Equals(".gts", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".gtp", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".wem", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".bnk", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".bk2", StringComparison.OrdinalIgnoreCase)
            && inputStream.Length > 0;
    }

    protected void WritePadding(Stream stream)
    {
        int padLength = Build.Version.PaddingSize();
        long alignTo = Build.Version.IsAtLeast(PackageVersion.V16)
            ? stream.Position - Unsafe.SizeOf<LSPKHeader16>() - 4
            : stream.Position;

        int padBytes = (int)((padLength - alignTo % padLength) % padLength);
        if (padBytes == 0) return;

        Span<byte> pad = stackalloc byte[padBytes];
        pad.Fill(0xAD);
        stream.Write(pad);
    }

    protected PackageBuildTransientFile WriteFile(PackageBuildInputFile input)
    {
        using var inputStream = input.MakeInputStream();

        CompressionMethod compression = Build.Compression;
        LSCompressionLevel compressionLevel = Build.CompressionLevel;

        if (!CanCompressFile(input, inputStream))
        {
            compression = CompressionMethod.None;
            compressionLevel = LSCompressionLevel.Fast;
        }

        byte[] uncompressed = new byte[inputStream.Length];
        inputStream.ReadExactly(uncompressed);

        byte[] compressed = CompressionHelpers.Compress(uncompressed, compression, compressionLevel);

        if (Streams[^1].Position + compressed.Length > Build.Version.MaxPackageSize())
        {
            string partPath = Package.MakePartFilename(PackagePath, Streams.Count);
            var nextPart = File.Open(partPath, FileMode.Create, FileAccess.Write);
            Streams.Add(nextPart);
        }

        Stream stream = Streams[^1];
        PackageBuildTransientFile packaged = new()
        {
            Name = input.Path.Replace('\\', '/'),
            UncompressedSize = (ulong)uncompressed.Length,
            SizeOnDisk = (ulong)compressed.Length,
            ArchivePart = (uint)(Streams.Count - 1),
            OffsetInFile = (ulong)stream.Position,
            Flags = CompressionHelpers.MakeCompressionFlags(compression, compressionLevel)
        };

        stream.Write(compressed);

        packaged.Crc = Build.Version.HasCrc() ? Crc32.HashToUInt32(compressed) : 0;

        if (!Build.Flags.HasFlag(PackageFlags.Solid))
        {
            WritePadding(stream);
        }

        return packaged;
    }

    protected List<PackageBuildTransientFile> PackFiles()
    {
        long totalSize = Build.Files.Sum(p => (long)p.Size());
        long currentSize = 0;

        List<PackageBuildTransientFile> writtenFiles = [];
        foreach (var file in Build.Files)
        {
            WriteProgress(file, currentSize, totalSize);
            writtenFiles.Add(WriteFile(file));
            currentSize += file.Size();
        }

        return writtenFiles;
    }

    internal void WriteFileList<TFile>(BinaryWriter metadataWriter, List<PackageBuildTransientFile> files)
        where TFile : struct, ILSPKFile
    {
        foreach (var file in files)
        {
            if (file.ArchivePart == 0)
            {
                file.OffsetInFile -= Metadata.DataOffset;
            }

            file.Flags = (CompressionFlags)((byte)file.Flags & 0x0F);

            var entry = (TFile)TFile.FromCommon(file);
            BinUtils.WriteStruct(metadataWriter, ref entry);
        }
    }

    internal void WriteCompressedFileList<TFile>(BinaryWriter metadataWriter, List<PackageBuildTransientFile> files)
        where TFile : struct, ILSPKFile
    {
        byte[] fileListBuf;
        using (MemoryStream fileList = new())
        using (BinaryWriter fileListWriter = new(fileList))
        {
            foreach (var file in files)
            {
                var entry = (TFile)TFile.FromCommon(file);
                BinUtils.WriteStruct(fileListWriter, ref entry);
            }

            fileListBuf = fileList.ToArray();
        }

        byte[] compressedFileList = CompressionHelpers.Compress(fileListBuf, CompressionMethod.LZ4, LSCompressionLevel.Default);

        metadataWriter.Write((uint)files.Count);

        if (Build.Version.IsGreaterThan(PackageVersion.V13))
        {
            metadataWriter.Write((uint)compressedFileList.Length);
        }
        else
        {
            Metadata.FileListSize = (uint)compressedFileList.Length + 4;
        }

        metadataWriter.Write(compressedFileList);
    }

    protected byte[] ComputeArchiveHash()
    {
        List<PackageBuildInputFile> orderedFileList = [.. Build.Files];
        if (Build.Version.IsLessThan(PackageVersion.V15))
        {
            orderedFileList.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        }

        using MD5 md5 = MD5.Create();
        foreach (var file in orderedFileList)
        {
            using var packagedStream = file.MakeInputStream();
            using BinaryReader reader = new(packagedStream);

            byte[] uncompressed = reader.ReadBytes((int)reader.BaseStream.Length);
            md5.TransformBlock(uncompressed, 0, uncompressed.Length, uncompressed, 0);
        }

        md5.TransformFinalBlock([], 0, 0);
        byte[] hash = md5.Hash!;

        for (var i = 0; i < hash.Length; i++)
        {
            hash[i] += 1;
        }

        return hash;
    }

    public abstract void Write();
}

internal class PackageWriter_V7<THeader, TFile>(PackageBuildData build, string packagePath)
    : PackageWriter(build, packagePath)
    where THeader : struct, ILSPKHeader
    where TFile : struct, ILSPKFile
{
    public override void Write()
    {
        if ((Build.Version.IsEqualTo(PackageVersion.V7) || Build.Version.IsEqualTo(PackageVersion.V9)) && Build.Compression == CompressionMethod.LZ4)
        {
            Build.Compression = CompressionMethod.Zlib;
        }

        Metadata.NumFiles = (uint)Build.Files.Count;
        Metadata.FileListSize = (uint)(Unsafe.SizeOf<TFile>() * Build.Files.Count);

        using var writer = new BinaryWriter(MainStream, Encoding.UTF8, leaveOpen: true);

        Metadata.DataOffset = (uint)Unsafe.SizeOf<THeader>() + Metadata.FileListSize;
        if (Metadata.Version >= 10)
        {
            Metadata.DataOffset += 4;
        }

        int paddingLength = Build.Version.PaddingSize();
        if (Metadata.DataOffset % paddingLength > 0)
        {
            Metadata.DataOffset += (uint)(paddingLength - Metadata.DataOffset % paddingLength);
        }

        var placeholder = new byte[Metadata.DataOffset];
        writer.Write(placeholder);

        var writtenFiles = PackFiles();

        MainStream.Seek(0, SeekOrigin.Begin);
        if (Metadata.Version >= 10)
        {
            writer.Write(PackageHeaderCommon.Signature);
        }
        Metadata.NumParts = (ushort)Streams.Count;
        Metadata.Md5 = ComputeArchiveHash();

        var header = (THeader)THeader.FromCommonHeader(Metadata);
        BinUtils.WriteStruct(writer, ref header);

        WriteFileList<TFile>(writer, writtenFiles);
    }
}

internal class PackageWriter_V13<THeader, TFile>(PackageBuildData build, string packagePath)
    : PackageWriter(build, packagePath)
    where THeader : struct, ILSPKHeader
    where TFile : struct, ILSPKFile
{
    public override void Write()
    {
        var writtenFiles = PackFiles();

        using var writer = new BinaryWriter(MainStream, Encoding.UTF8, leaveOpen: true);

        Metadata.FileListOffset = (ulong)MainStream.Position;
        WriteCompressedFileList<TFile>(writer, writtenFiles);

        Metadata.FileListSize = (uint)(MainStream.Position - (long)Metadata.FileListOffset);
        Metadata.Md5 = ComputeArchiveHash();
        Metadata.NumParts = (ushort)Streams.Count;

        var header = (THeader)THeader.FromCommonHeader(Metadata);
        BinUtils.WriteStruct(writer, ref header);

        writer.Write((uint)(8 + Unsafe.SizeOf<THeader>()));
        writer.Write(PackageHeaderCommon.Signature);
    }
}

internal class PackageWriter_V15<THeader, TFile>(PackageBuildData build, string packagePath)
    : PackageWriter(build, packagePath)
    where THeader : struct, ILSPKHeader
    where TFile : struct, ILSPKFile
{
    public override void Write()
    {
        using (var writer = new BinaryWriter(MainStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(PackageHeaderCommon.Signature);
            var header = (THeader)THeader.FromCommonHeader(Metadata);
            BinUtils.WriteStruct(writer, ref header);
        }

        var writtenFiles = PackFiles();

        using (var writer = new BinaryWriter(MainStream, Encoding.UTF8, leaveOpen: true))
        {
            Metadata.FileListOffset = (ulong)MainStream.Position;
            WriteCompressedFileList<TFile>(writer, writtenFiles);

            Metadata.FileListSize = (uint)(MainStream.Position - (long)Metadata.FileListOffset);
            Metadata.Md5 = Build.Hash ? ComputeArchiveHash() : new byte[0x10];
            Metadata.NumParts = (ushort)Streams.Count;

            MainStream.Seek(4, SeekOrigin.Begin);
            var header = (THeader)THeader.FromCommonHeader(Metadata);
            BinUtils.WriteStruct(writer, ref header);
        }
    }
}

public static class PackageWriterFactory
{
    public static PackageWriter Create(PackageBuildData build, string packagePath)
    {
        int numericVersion = (int)build.Version;

        return numericVersion switch
        {
            (int)PackageVersion.V18 => new PackageWriter_V15<LSPKHeader16, FileEntry18>(build, packagePath),
            (int)PackageVersion.V16 => new PackageWriter_V15<LSPKHeader16, FileEntry15>(build, packagePath),
            (int)PackageVersion.V15 => new PackageWriter_V15<LSPKHeader15, FileEntry15>(build, packagePath),
            (int)PackageVersion.V13 => new PackageWriter_V13<LSPKHeader13, FileEntry10>(build, packagePath),
            (int)PackageVersion.V10 => new PackageWriter_V7<LSPKHeader10, FileEntry10>(build, packagePath),
            (int)PackageVersion.V9 or (int)PackageVersion.V7 => new PackageWriter_V7<LSPKHeader7, FileEntry7>(build, packagePath),
            _ => throw new ArgumentException($"Cannot write version {build.Version} packages")
        };
    }
}

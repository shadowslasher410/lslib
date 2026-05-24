using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;

namespace LSLib.LS;

public class NotAPackageException(string? message = null, Exception? innerException = null)
    : Exception(message, innerException);

public class Package : IDisposable
{
    public string PackagePath { get; }
    internal MemoryMappedFile MetadataFile { get; }
    internal MemoryMappedViewAccessor MetadataView { get; }

    internal MemoryMappedFile[] Parts { get; set; } = null!;
    internal MemoryMappedViewAccessor[] Views { get; set; } = null!;

    public PackageHeaderCommon Metadata { get; set; } = null!;
    public List<PackagedFileInfo> Files { get; set; } = [];

    public PackageVersion Version => (PackageVersion)Metadata.Version;

    internal Package(string path)
    {
        PackagePath = path;
        var file = File.OpenRead(PackagePath);
        MetadataFile = MemoryMappedFile.CreateFromFile(file, null, file.Length, MemoryMappedFileAccess.Read, HandleInheritability.None, false);
        MetadataView = MetadataFile.CreateViewAccessor(0, file.Length, MemoryMappedFileAccess.Read);
    }

    public void OpenPart(int index, string path)
    {
        var file = File.OpenRead(path);
        Parts[index] = MemoryMappedFile.CreateFromFile(file, null, file.Length, MemoryMappedFileAccess.Read, HandleInheritability.None, false);
        Views[index] = Parts[index].CreateViewAccessor(0, file.Length, MemoryMappedFileAccess.Read);
    }

    public void OpenStreams(int numParts)
    {
        // Fixed compilation assignment using modern collection literals
        Parts = new MemoryMappedFile[numParts];
        Views = new MemoryMappedViewAccessor[numParts];

        Parts[0] = MetadataFile;
        Views[0] = MetadataView;

        for (var part = 1; part < numParts; part++)
        {
            string partPath = MakePartFilename(PackagePath, part);
            OpenPart(part, partPath);
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        MetadataView?.Dispose();
        MetadataFile?.Dispose();

        foreach (var view in Views ?? [])
        {
            view?.Dispose();
        }

        foreach (var file in Parts ?? [])
        {
            file?.Dispose();
        }
    }

    public static string MakePartFilename(string path, int part)
    {
        string? dirName = Path.GetDirectoryName(path);
        string baseName = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        return Path.Join(dirName, $"{baseName}_{part}{extension}");
    }
}

// MODERNIZED: Changed to a partial class block.
// This allows you to retain the absolute private encapsulation of your DecompressLZ4 method!
public partial class PackageReader
{
    private bool _metadataOnly;
    private Package _pak = null!;

    private void ReadCompressedFileList<TFile>(MemoryMappedViewAccessor view, long offset)
        where TFile : struct, ILSPKFile
    {
        int numFiles = view.ReadInt32(offset);
        byte[] compressed;

        if (_pak.Metadata.Version > 13)
        {
            int compressedSize = view.ReadInt32(offset + 4);
            compressed = new byte[compressedSize];
            view.ReadArray(offset + 8, compressed, 0, compressedSize);
        }
        else
        {
            int calculatedSize = (int)_pak.Metadata.FileListSize - 4;
            compressed = new byte[calculatedSize];
            view.ReadArray(offset + 4, compressed, 0, calculatedSize);
        }

        int fileBufferSize = Unsafe.SizeOf<TFile>() * numFiles;

        var flags = CompressionHelpers.MakeCompressionFlags(CompressionMethod.LZ4, LSCompressionLevel.Fast);
        byte[] fileBuf = CompressionHelpers.Decompress(compressed, fileBufferSize, flags, chunked: false);

        using var ms = new MemoryStream(fileBuf);
        using var msr = new BinaryReader(ms);

        var entries = new TFile[numFiles];
        BinUtils.ReadStructsBlitted(msr, entries);

        foreach (var entry in entries)
        {
            ushort partNum = entry.ArchivePartNumber();
            _pak.Files.Add(PackagedFileInfo.CreateFromEntry(_pak, entry, _pak.Parts[partNum], _pak.Views[partNum]));
        }
    }

    private void ReadFileList<TFile>(MemoryMappedViewAccessor view, long offset)
        where TFile : struct, ILSPKFile
    {
        var entries = new TFile[_pak.Metadata.NumFiles];
        BinUtils.ReadStructs(view, offset, entries);

        foreach (var entry in entries)
        {
            ushort partNum = entry.ArchivePartNumber();
            var file = PackagedFileInfo.CreateFromEntry(_pak, entry, _pak.Parts[partNum], _pak.Views[partNum]);

            if (file.ArchivePart == 0)
            {
                file.OffsetInFile += _pak.Metadata.DataOffset;
            }

            _pak.Files.Add(file);
        }
    }

    private Package ReadHeaderAndFileList<THeader, TFile>(MemoryMappedViewAccessor view, long offset)
        where THeader : struct, ILSPKHeader
        where TFile : struct, ILSPKFile
    {
        view.Read<THeader>(offset, out var header);

        _pak.Metadata = header.ToCommonHeader();

        if (_metadataOnly) return _pak;

        _pak.OpenStreams((int)_pak.Metadata.NumParts);

        if (_pak.Metadata.Version > 10)
        {
            _pak.Metadata.DataOffset = (uint)(offset + Unsafe.SizeOf<THeader>());
            ReadCompressedFileList<TFile>(view, (long)_pak.Metadata.FileListOffset);
        }
        else
        {
            ReadFileList<TFile>(view, offset + Unsafe.SizeOf<THeader>());
        }

        if ((_pak.Metadata.Flags & PackageFlags.Solid) != 0 && _pak.Files.Count > 0)
        {
            UnpackSolidSegment(view);
        }

        return _pak;
    }

    private void UnpackSolidSegment(MemoryMappedViewAccessor view)
    {
        ulong totalUncompressedSize = 0;
        ulong totalSizeOnDisk = 0;
        ulong firstOffset = 0xffffffff;
        ulong lastOffset = 0;

        foreach (var entry in _pak.Files)
        {
            if (entry is not PackagedFileInfo file) continue;

            totalUncompressedSize += file.UncompressedSize;
            totalSizeOnDisk += file.SizeOnDisk;

            if (file.OffsetInFile < firstOffset)
            {
                firstOffset = file.OffsetInFile;
            }
            if (file.OffsetInFile + file.SizeOnDisk > lastOffset)
            {
                lastOffset = file.OffsetInFile + file.SizeOnDisk;
            }
        }

        if (firstOffset != _pak.Metadata.DataOffset + 7 || lastOffset - firstOffset != totalSizeOnDisk)
        {
            throw new InvalidDataException($"Incorrectly compressed solid archive; offsets {firstOffset}/{lastOffset}, bytes {totalSizeOnDisk}");
        }

        byte[] frame = new byte[lastOffset - _pak.Metadata.DataOffset];
        view.ReadArray(_pak.Metadata.DataOffset, frame, 0, (int)(lastOffset - _pak.Metadata.DataOffset));
        var flags = CompressionHelpers.MakeCompressionFlags(CompressionMethod.LZ4, LSCompressionLevel.Fast);
        byte[] decompressed = CompressionHelpers.Decompress(frame, (int)totalUncompressedSize, flags, chunked: false);
        using var decompressedStream = new MemoryStream(decompressed);

        ulong offset = _pak.Metadata.DataOffset + 7;
        ulong compressedOffset = 0;

        foreach (var entry in _pak.Files)
        {
            if (entry is not PackagedFileInfo file) continue;

            if (file.OffsetInFile != offset)
            {
                throw new InvalidDataException("File list in solid archive not contiguous");
            }

            file.MakeSolid(compressedOffset, decompressedStream);

            offset += file.SizeOnDisk;
            compressedOffset += file.UncompressedSize;
        }
    }

    public Package ReadInternal(string path)
    {
        _pak = new Package(path);
        var view = _pak.MetadataView;

        int headerSize = view.ReadInt32(view.Capacity - 8);
        uint signature = view.ReadUInt32(view.Capacity - 4);
        if (signature == PackageHeaderCommon.Signature)
        {
            return ReadHeaderAndFileList<LSPKHeader13, FileEntry10>(view, view.Capacity - headerSize);
        }

        signature = view.ReadUInt32(0);
        if (signature == PackageHeaderCommon.Signature)
        {
            int version = view.ReadInt32(4);
            return version switch
            {
                10 => ReadHeaderAndFileList<LSPKHeader10, FileEntry10>(view, 4),
                15 => ReadHeaderAndFileList<LSPKHeader15, FileEntry15>(view, 4),
                16 => ReadHeaderAndFileList<LSPKHeader16, FileEntry15>(view, 4),
                18 => ReadHeaderAndFileList<LSPKHeader16, FileEntry18>(view, 4),
                _ => throw new InvalidDataException($"Package version v{version} not supported")
            };
        }

        return view.ReadInt32(0) switch
        {
            7 or 9 => ReadHeaderAndFileList<LSPKHeader7, FileEntry7>(view, 0),
            _ => throw new NotAPackageException("No valid signature found in package file")
        };
    }

    public Package Read(string path, bool metadataOnly = false)
    {
        _metadataOnly = metadataOnly;

        try
        {
            return ReadInternal(path);
        }
        catch (Exception)
        {
            _pak?.Dispose();
            throw;
        }
    }
}
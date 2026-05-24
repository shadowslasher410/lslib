using System.Runtime.CompilerServices;

namespace LSLib.LS;

public enum PackageVersion
{
    V7 = 7,   // D:OS 1
    V9 = 9,   // D:OS 1 EE
    V10 = 10, // D:OS 2
    V13 = 13, // D:OS 2 DE
    V15 = 15, // BG3 EA
    V16 = 16, // BG3 EA Patch4
    V18 = 18  // BG3 Release
}

public static class PackageVersionExtensions
{
    public static bool HasCrc(this PackageVersion ver)
    {
        return ver >= PackageVersion.V10 && ver <= PackageVersion.V16;
    }

    public static long MaxPackageSize(this PackageVersion ver) =>
        ver <= PackageVersion.V15 ? 0x40000000L : 0x100000000L;

    public static int PaddingSize(this PackageVersion ver) =>
        ver <= PackageVersion.V9 ? 0x1000 : 0x40;
}

public class PackageHeaderCommon
{
    public const PackageVersion CurrentVersion = PackageVersion.V18;
    public const uint Signature = 0x4B50534C;

    public uint Version { get; set; }
    public ulong FileListOffset { get; set; }
    // Size of file list; used for legacy (<= v10) packages only
    public uint FileListSize { get; set; }
    // Number of packed files; used for legacy (<= v10) packages only
    public uint NumFiles { get; set; }
    public uint NumParts { get; set; }
    // Offset of packed data in archive part 0; used for legacy (<= v10) packages only
    public uint DataOffset { get; set; }
    public PackageFlags Flags { get; set; }
    public byte Priority { get; set; }
    public byte[]? Md5 { get; set; }
}

internal interface ILSPKHeader
{
    public PackageHeaderCommon ToCommonHeader();
    public static abstract ILSPKHeader FromCommonHeader(PackageHeaderCommon h);
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct LSPKHeader7 : ILSPKHeader
{
    public uint Version;
    public uint DataOffset;
    public uint NumParts;
    public uint FileListSize;
    public byte LittleEndian;
    public uint NumFiles;

    public readonly PackageHeaderCommon ToCommonHeader() => new()
    {
        Version = Version,
        DataOffset = DataOffset,
        FileListOffset = (ulong)Unsafe.SizeOf<LSPKHeader7>(),
        FileListSize = FileListSize,
        NumFiles = NumFiles,
        NumParts = NumParts,
        Flags = 0,
        Priority = 0,
        Md5 = null
    };

    public static ILSPKHeader FromCommonHeader(PackageHeaderCommon h) => new LSPKHeader7
    {
        Version = h.Version,
        DataOffset = h.DataOffset,
        NumParts = h.NumParts,
        FileListSize = h.FileListSize,
        LittleEndian = 0,
        NumFiles = h.NumFiles
    };
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct LSPKHeader10 : ILSPKHeader
{
    public uint Version;
    public uint DataOffset;
    public uint FileListSize;
    public ushort NumParts;
    public byte Flags;
    public byte Priority;
    public uint NumFiles;

    public readonly PackageHeaderCommon ToCommonHeader() => new()
    {
        Version = Version,
        DataOffset = DataOffset,
        FileListOffset = (ulong)Unsafe.SizeOf<LSPKHeader7>(),
        FileListSize = FileListSize,
        NumFiles = NumFiles,
        NumParts = NumParts,
        Flags = (PackageFlags)Flags,
        Priority = Priority,
        Md5 = null
    };

    public static ILSPKHeader FromCommonHeader(PackageHeaderCommon h) => new LSPKHeader10
    {
        Version = h.Version,
        DataOffset = h.DataOffset,
        FileListSize = h.FileListSize,
        NumParts = (ushort)h.NumParts,
        Flags = (byte)h.Flags,
        Priority = h.Priority,
        NumFiles = h.NumFiles
    };
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct LSPKHeader13 : ILSPKHeader
{
    public uint Version;
    public uint FileListOffset;
    public uint FileListSize;
    public ushort NumParts;
    public byte Flags;
    public byte Priority;
    public fixed byte Md5[16];

    public readonly PackageHeaderCommon ToCommonHeader()
    {
        byte[] md5Dest = new byte[16];
        fixed (byte* src = Md5)
        {
            new ReadOnlySpan<byte>(src, 16).CopyTo(md5Dest);
        }

        return new PackageHeaderCommon
        {
            Version = Version,
            DataOffset = 0,
            FileListOffset = FileListOffset,
            FileListSize = FileListSize,
            NumParts = NumParts,
            Flags = (PackageFlags)Flags,
            Priority = Priority,
            Md5 = md5Dest
        };
    }

    public static ILSPKHeader FromCommonHeader(PackageHeaderCommon h)
    {
        var header = new LSPKHeader13
        {
            Version = h.Version,
            FileListOffset = (uint)h.FileListOffset,
            FileListSize = h.FileListSize,
            NumParts = (ushort)h.NumParts,
            Flags = (byte)h.Flags,
            Priority = h.Priority
        };

        if (h.Md5 is not null)
        {
            var destSpan = MemoryMarshal.CreateSpan(ref header.Md5[0], 16);
            h.Md5.AsSpan().CopyTo(destSpan);
        }
        return header;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct LSPKHeader15 : ILSPKHeader
{
    public uint Version;
    public ulong FileListOffset;
    public uint FileListSize;
    public byte Flags;
    public byte Priority;
    public fixed byte Md5[16];

    public readonly PackageHeaderCommon ToCommonHeader()
    {
        byte[] md5Dest = new byte[16];
        fixed (byte* src = Md5)
        {
            new ReadOnlySpan<byte>(src, 16).CopyTo(md5Dest);
        }

        return new PackageHeaderCommon
        {
            Version = Version,
            DataOffset = 0,
            FileListOffset = FileListOffset,
            FileListSize = FileListSize,
            NumParts = 1,
            Flags = (PackageFlags)Flags,
            Priority = Priority,
            Md5 = md5Dest
        };
    }

    public static ILSPKHeader FromCommonHeader(PackageHeaderCommon h)
    {
        var header = new LSPKHeader15
        {
            Version = h.Version,
            FileListOffset = (uint)h.FileListOffset,
            FileListSize = h.FileListSize,
            Flags = (byte)h.Flags,
            Priority = h.Priority
        };
        if (h.Md5 is not null)
        {
            var destSpan = MemoryMarshal.CreateSpan(ref header.Md5[0], 16);
            h.Md5.AsSpan().CopyTo(destSpan);
        }
        return header;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct LSPKHeader16 : ILSPKHeader
{
    public uint Version;
    public ulong FileListOffset;
    public uint FileListSize;
    public byte Flags;
    public byte Priority;
    public fixed byte Md5[16];
    public ushort NumParts;

    public readonly PackageHeaderCommon ToCommonHeader()
    {
        byte[] md5Dest = new byte[16];
        fixed (byte* src = Md5)
        {
            new ReadOnlySpan<byte>(src, 16).CopyTo(md5Dest);
        }

        return new PackageHeaderCommon
        {
            Version = Version,
            FileListOffset = FileListOffset,
            FileListSize = FileListSize,
            NumParts = NumParts,
            Flags = (PackageFlags)Flags,
            Priority = Priority,
            Md5 = md5Dest
        };
    }

    public static ILSPKHeader FromCommonHeader(PackageHeaderCommon h)
    {
        var header = new LSPKHeader16
        {
            Version = h.Version,
            FileListOffset = (uint)h.FileListOffset,
            FileListSize = h.FileListSize,
            Flags = (byte)h.Flags,
            Priority = h.Priority,
            NumParts = (ushort)h.NumParts
        };

        if (h.Md5 is not null)
        {
            var destSpan = MemoryMarshal.CreateSpan(ref header.Md5[0], 16);
            h.Md5.AsSpan().CopyTo(destSpan);
        }
        return header;
    }
}

[Flags]
public enum PackageFlags : byte
{
    AllowMemoryMapping = 0x02,
    Solid = 0x04,
    Preload = 0x08
}

public abstract class PackagedFileInfoCommon
{
    public string Name { get; set; } = string.Empty;
    public uint ArchivePart { get; set; }
    public uint Crc { get; set; }
    public CompressionFlags Flags { get; set; }
    public ulong OffsetInFile { get; set; }
    public ulong SizeOnDisk { get; set; }
    public ulong UncompressedSize { get; set; }
}

internal interface ILSPKFile
{
    public void ToCommon(PackagedFileInfoCommon info);
    public static abstract ILSPKFile FromCommon(PackagedFileInfoCommon info);
    public ushort ArchivePartNumber();
}

[InlineArray(256)]
public struct FileNameBlittable
{
    private byte _element;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct FileEntry7 : ILSPKFile
{
    public FileNameBlittable Name;
    public uint OffsetInFile;
    public uint SizeOnDisk;
    public uint UncompressedSize;
    public uint ArchivePart;

    public readonly void ToCommon(PackagedFileInfoCommon info)
    {
        info.Name = BinUtils.NullTerminatedBytesToString(Name);
        info.ArchivePart = ArchivePart;
        info.Crc = 0;
        info.Flags = UncompressedSize > 0 ? CompressionHelpers.MakeCompressionFlags(CompressionMethod.Zlib, LSCompressionLevel.Default) : 0;
        info.OffsetInFile = OffsetInFile;
        info.SizeOnDisk = SizeOnDisk;
        info.UncompressedSize = UncompressedSize;
    }

    public static ILSPKFile FromCommon(PackagedFileInfoCommon info) => new FileEntry7
    {
        Name = BinUtils.StringToNullTerminatedBlittableBytes(info.Name),
        OffsetInFile = (uint)info.OffsetInFile,
        SizeOnDisk = (uint)info.SizeOnDisk,
        UncompressedSize = info.Flags.Method() == CompressionMethod.None ? 0 : (uint)info.UncompressedSize,
        ArchivePart = info.ArchivePart
    };

    public readonly ushort ArchivePartNumber() => (ushort)ArchivePart;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct FileEntry10 : ILSPKFile
{
    public FileNameBlittable Name;
    public uint OffsetInFile;
    public uint SizeOnDisk;
    public uint UncompressedSize;
    public uint ArchivePart;
    public uint Flags;
    public uint Crc;

    public readonly void ToCommon(PackagedFileInfoCommon info)
    {
        info.Name = BinUtils.NullTerminatedBytesToString(Name);
        info.ArchivePart = ArchivePart;
        info.Crc = Crc;
        info.Flags = (CompressionFlags)Flags;
        info.OffsetInFile = OffsetInFile;
        info.SizeOnDisk = SizeOnDisk;
        info.UncompressedSize = UncompressedSize;
    }

    public static ILSPKFile FromCommon(PackagedFileInfoCommon info) => new FileEntry10
    {
        Name = BinUtils.StringToNullTerminatedBlittableBytes(info.Name),
        OffsetInFile = (uint)info.OffsetInFile,
        SizeOnDisk = (uint)info.SizeOnDisk,
        UncompressedSize = info.Flags.Method() == CompressionMethod.None ? 0 : (uint)info.UncompressedSize,
        ArchivePart = info.ArchivePart,
        Flags = (uint)info.Flags,
        Crc = info.Crc
    };

    public readonly ushort ArchivePartNumber() => (ushort)ArchivePart;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct FileEntry15 : ILSPKFile
{
    public FileNameBlittable Name;
    public ulong OffsetInFile;
    public ulong SizeOnDisk;
    public ulong UncompressedSize;
    public uint ArchivePart;
    public uint Flags;
    public uint Crc;
    public uint Unknown2;

    public readonly void ToCommon(PackagedFileInfoCommon info)
    {
        info.Name = BinUtils.NullTerminatedBytesToString(Name);
        info.ArchivePart = ArchivePart;
        info.Crc = Crc;
        info.Flags = (CompressionFlags)Flags;
        info.OffsetInFile = OffsetInFile;
        info.SizeOnDisk = SizeOnDisk;
        info.UncompressedSize = UncompressedSize;
    }

    public static ILSPKFile FromCommon(PackagedFileInfoCommon info) => new FileEntry15
    {
        Name = BinUtils.StringToNullTerminatedBlittableBytes(info.Name),
        OffsetInFile = info.OffsetInFile,
        SizeOnDisk = info.SizeOnDisk,
        UncompressedSize = info.Flags.Method() == CompressionMethod.None ? 0 : info.UncompressedSize,
        ArchivePart = info.ArchivePart,
        Flags = (uint)info.Flags,
        Crc = info.Crc,
        Unknown2 = 0
    };

    public readonly ushort ArchivePartNumber() => (ushort)ArchivePart;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct FileEntry18 : ILSPKFile
{
    public FileNameBlittable Name;
    public uint OffsetInFile1;
    public ushort OffsetInFile2;
    public byte ArchivePart;
    public byte Flags;
    public uint SizeOnDisk;
    public uint UncompressedSize;

    public readonly void ToCommon(PackagedFileInfoCommon info)
    {
        info.Name = BinUtils.NullTerminatedBytesToString(Name);
        info.ArchivePart = ArchivePart;
        info.Crc = 0;
        info.Flags = (CompressionFlags)Flags;
        info.OffsetInFile = OffsetInFile1 | ((ulong)OffsetInFile2 << 32);
        info.SizeOnDisk = SizeOnDisk;
        info.UncompressedSize = UncompressedSize;
    }

    public static ILSPKFile FromCommon(PackagedFileInfoCommon info) => new FileEntry18
    {
        Name = BinUtils.StringToNullTerminatedBlittableBytes(info.Name),
        OffsetInFile1 = (uint)(info.OffsetInFile & 0xffffffff),
        OffsetInFile2 = (ushort)((info.OffsetInFile >> 32) & 0xffff),
        ArchivePart = (byte)info.ArchivePart,
        Flags = (byte)info.Flags,
        SizeOnDisk = (uint)info.SizeOnDisk,
        UncompressedSize = info.Flags.Method() == CompressionMethod.None ? 0 : (uint)info.UncompressedSize
    };

    public readonly ushort ArchivePartNumber() => ArchivePart;
}
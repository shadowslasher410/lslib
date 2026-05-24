using System.IO.MemoryMappedFiles;

namespace LSLib.LS;

public class PackagedFileInfo : PackagedFileInfoCommon
{
    public required Package Package { get; set; }
    public required MemoryMappedFile PackageFile { get; set; }
    public required MemoryMappedViewAccessor PackageView { get; set; }
    public bool Solid { get; set; }
    public ulong SolidOffset { get; set; }
    public Stream? SolidStream { get; set; }
    public ulong Size() => Flags.Method() == CompressionMethod.None ? SizeOnDisk : UncompressedSize;

    public Stream CreateContentReader()
    {
        if (IsDeletion())
        {
            throw new InvalidOperationException("Cannot open file stream for a deleted file");
        }

        if (Solid)
        {
            if (SolidStream is null)
            {
                throw new InvalidOperationException("Solid stream reference context has not been configured.");
            }
            SolidStream.Seek((long)SolidOffset, SeekOrigin.Begin);
            return new ReadOnlySubstream(SolidStream, (long)SolidOffset, (long)UncompressedSize);
        }

        return CompressionHelpers.Decompress(PackageFile, PackageView, (long)OffsetInFile, (int)SizeOnDisk, (int)UncompressedSize, Flags);
    }

    internal static PackagedFileInfo CreateFromEntry(Package package, ILSPKFile entry, MemoryMappedFile file, MemoryMappedViewAccessor view)
    {
        ArgumentNullException.ThrowIfNull(entry);

        PackagedFileInfo info = new()
        {
            Package = package,
            PackageFile = file,
            PackageView = view,
            Solid = false
        };

        entry.ToCommon(info);
        return info;
    }

    internal void MakeSolid(ulong solidOffset, Stream solidStream)
    {
        Solid = true;
        SolidOffset = solidOffset;
        SolidStream = solidStream ?? throw new ArgumentNullException(nameof(solidStream));
    }

    public bool IsDeletion() => (OffsetInFile & 0x0000ffffffffffff) == 0xbeefdeadbeef;
}

public class PackageBuildInputFile
{
    public string Path { get; set; } = string.Empty;
    public string FilesystemPath { get; set; } = string.Empty;
    public byte[]? Body { get; set; }

    public Stream MakeInputStream() => Body is not null
        ? new MemoryStream(Body)
        : new FileStream(FilesystemPath, FileMode.Open, FileAccess.Read, FileShare.Read);

    public long Size() => Body?.Length ?? new FileInfo(FilesystemPath).Length;

    public static PackageBuildInputFile CreateFromBlob(byte[] body, string path)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentException.ThrowIfNullOrEmpty(path);

        return new PackageBuildInputFile
        {
            Path = path,
            Body = body
        };
    }

    public static PackageBuildInputFile CreateFromFilesystem(string filesystemPath, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(filesystemPath);
        ArgumentException.ThrowIfNullOrEmpty(path);

        return new PackageBuildInputFile
        {
            Path = path,
            FilesystemPath = filesystemPath
        };
    }
}

public class PackageBuildData
{
    public PackageVersion Version { get; set; } = PackageHeaderCommon.CurrentVersion;
    public CompressionMethod Compression { get; set; } = CompressionMethod.None;
    public LSCompressionLevel CompressionLevel { get; set; } = LSCompressionLevel.Default;
    public PackageFlags Flags { get; set; } = 0;
    public bool Hash { get; set; }
    public List<PackageBuildInputFile> Files { get; set; } = [];
    public bool ExcludeHidden { get; set; } = true;
    public byte Priority { get; set; }
}

public class Packager
{
    public delegate void ProgressUpdateDelegate(string status, long numerator, long denominator);

    public ProgressUpdateDelegate ProgressUpdate { get; set; } = delegate { };

    private void WriteProgressUpdate(PackageBuildInputFile file, long numerator, long denominator)
    {
        ProgressUpdate(file.Path, numerator, denominator);
    }

    public void UncompressPackage(Package package, string outputPath, Func<PackagedFileInfo, bool>? filter = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(outputPath);

        string normalizedOutputPath = outputPath.EndsWith(Path.DirectorySeparatorChar)
            ? outputPath
            : outputPath + Path.DirectorySeparatorChar;

        List<PackagedFileInfo> files = package.Files;
        if (filter is not null)
        {
            files = files.FindAll(obj => filter(obj));
        }

        long totalSize = files.Sum(p => (long)p.Size());
        long currentSize = 0;
        int lastReportedPercent = -1;

        foreach (PackagedFileInfo file in files)
        {
            double precisePercent = totalSize == 0 ? 0 : (double)currentSize * 100 / totalSize;
            int currentPercent = (int)Math.Floor(precisePercent);

            if (currentPercent != lastReportedPercent || currentSize == 0 || currentSize == totalSize)
            {
                lastReportedPercent = currentPercent;
                ProgressUpdate(file.Name, currentSize, totalSize);
            }

            currentSize += (long)file.Size();

            if (file.IsDeletion()) continue;

            string outPath = Path.Combine(normalizedOutputPath, file.Name);
            FileManager.TryToCreateDirectory(outPath);

            using Stream inStream = file.CreateContentReader();
            using var outFile = File.Open(outPath, FileMode.Create, FileAccess.Write, FileShare.None);
            inStream.CopyTo(outFile);
        }

        ProgressUpdate("Decompression complete.", totalSize, totalSize);
    }

    public void UncompressPackage(string packagePath, string outputPath, Func<PackagedFileInfo, bool>? filter = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(packagePath);
        ArgumentNullException.ThrowIfNull(outputPath);

        ProgressUpdate("Reading package headers ...", 0, 1);
        PackageReader reader = new();
        using var package = reader.Read(packagePath);
        UncompressPackage(package, outputPath, filter);
    }

    public static bool ShouldInclude(string file, PackageBuildData build)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        ArgumentNullException.ThrowIfNull(build);

        if (!build.ExcludeHidden) return true;

        ReadOnlySpan<char> fileSpan = file.AsSpan();
        foreach (var range in fileSpan.Split(Path.DirectorySeparatorChar))
        {
            if (fileSpan[range].StartsWith('.'))
            {
                return false;
            }
        }
        return true;
    }


    private static void AddFilesFromPath(PackageBuildData build, string path)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentException.ThrowIfNullOrEmpty(path);

        string normalizedPath = path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;

        foreach (string file in Directory.EnumerateFiles(normalizedPath, "*.*", SearchOption.AllDirectories))
        {
            string name = Path.GetRelativePath(normalizedPath, file);
            if (ShouldInclude(file, build))
            {
                build.Files.Add(PackageBuildInputFile.CreateFromFilesystem(file, name));
            }
        }
    }

    public async Task CreatePackage(string packagePath, string inputPath, PackageBuildData build)
    {
        ArgumentException.ThrowIfNullOrEmpty(packagePath);
        ArgumentException.ThrowIfNullOrEmpty(inputPath);
        ArgumentNullException.ThrowIfNull(build);

        FileManager.TryToCreateDirectory(packagePath);

        await Task.Run(() =>
        {
            ProgressUpdate("Enumerating files ...", 0, 1);
            AddFilesFromPath(build, inputPath);

            ProgressUpdate("Creating archive ...", 0, 1);
            using PackageWriter writer = PackageWriterFactory.Create(build, packagePath);
            writer.WriteProgress += WriteProgressUpdate;
            writer.Write();

            ProgressUpdate("Archive package successfully written.", 1, 1);
        });
    }
}
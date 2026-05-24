using LSLib.LS.Enums;
using LSLib.LS.Resources.LSF;

namespace LSLib.LS;

public class ResourceLoadParameters
{
    /// <summary>
    /// Byte-swap the last 8 bytes of GUIDs when serializing to/from string
    /// </summary>
    public bool ByteSwapGuids { get; set; } = true;


    public static ResourceLoadParameters FromGameVersion(Game _)
    {
        return new ResourceLoadParameters();
    }


    public void ToSerializationSettings(NodeSerializationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.DefaultByteSwapGuids = ByteSwapGuids;
    }
}

public class ResourceConversionParameters
{
    /// <summary>
    /// Format of generated PAK files
    /// </summary>
    public PackageVersion PAKVersion { get; set; }

    /// <summary>
    /// Format of generated LSF files
    /// </summary>
    public LSFVersion LSF { get; set; } = LSFVersion.MaxWriteVersion;


    /// <summary>
    /// Store sibling/neighbour node data in LSF files (usually done by savegames and dictionary-like files)
    /// (null = auto-detect based on input resource file)
    /// </summary>
    public LSFMetadataFormat? MetadataFormat { get; set; }

    /// <summary>
    /// Format of generated LSX files
    /// </summary>
    public LSXVersion LSX { get; set; } = LSXVersion.V4;

    /// <summary>
    /// Pretty-print (format) LSX/LSJ files
    /// </summary>
    public bool PrettyPrint { get; set; } = true;

    /// <summary>
    /// LSF/LSB compression method
    /// </summary>
    public CompressionMethod Compression { get; set; } = CompressionMethod.None;

    /// <summary>
    /// LSF/LSB compression level (i.e. size/compression time tradeoff)
    /// </summary>
    public LSCompressionLevel CompressionLevel { get; set; } = LSCompressionLevel.Default;

    /// <summary>
    /// Byte-swap the last 8 bytes of GUIDs when serializing to/from string
    /// </summary>
    public bool ByteSwapGuids { get; set; } = true;

    public static ResourceConversionParameters FromGameVersion(Game game)
    {
        return new ResourceConversionParameters
        {
            PAKVersion = (PackageVersion)game.PAKVersion(),
            LSF = game.LSFVersion(),
            LSX = game.LSXVersion()
        };
    }

    public void ToSerializationSettings(NodeSerializationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.DefaultByteSwapGuids = ByteSwapGuids;
    }

    public void ToSerializationSettings(NodeSerializationSettings settings, Resource res)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(res);

        settings.DefaultByteSwapGuids = ByteSwapGuids;
        settings.LSFMetadata = res.MetadataFormat ?? settings.LSFMetadata;
    }
}

public class ResourceUtils
{
    public delegate void ProgressUpdateDelegate(string status, long numerator, long denominator);
    public ProgressUpdateDelegate ProgressUpdate { get; set; } = delegate { };

    public delegate void ErrorDelegate(string path, Exception e);
    public ErrorDelegate ErrorDelegates { get; set; } = delegate { };

    public static ResourceFormat ExtensionToResourceFormat(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string extension = Path.GetExtension(path).ToLowerInvariant();

        return extension switch
        {
            ".lsx" => ResourceFormat.LSX,
            ".lsb" => ResourceFormat.LSB,
            ".lsf" or ".lsfx" or ".lsbc" or ".lsbs" => ResourceFormat.LSF,
            ".lsj" => ResourceFormat.LSJ,
            _ => throw new ArgumentException($"Unrecognized resource file extension boundary: {extension}", nameof(path)),
        };
    }

    public static Resource LoadResource(string inputPath, ResourceLoadParameters loadParams)
    {
        ArgumentException.ThrowIfNullOrEmpty(inputPath);
        ArgumentNullException.ThrowIfNull(loadParams);

        return LoadResource(inputPath, ExtensionToResourceFormat(inputPath), loadParams);
    }

    public static Resource LoadResource(string inputPath, ResourceFormat format, ResourceLoadParameters loadParams)
    {
        ArgumentException.ThrowIfNullOrEmpty(inputPath);
        ArgumentNullException.ThrowIfNull(loadParams);

        using var stream = File.Open(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return LoadResource(stream, format, loadParams);
    }

    public static Resource LoadResource(Stream stream, ResourceFormat format, ResourceLoadParameters loadParams)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(loadParams);

        return format switch
        {
            ResourceFormat.LSX => LoadLsx(stream, loadParams),
            ResourceFormat.LSB => LoadLsb(stream),
            ResourceFormat.LSF => LoadLsf(stream),
            ResourceFormat.LSJ => LoadLsj(stream, loadParams),
            _ => throw new ArgumentException("Invalid resource format configuration parameter passed into loader.", nameof(format))
        };
    }

    private static Resource LoadLsx(Stream stream, ResourceLoadParameters loadParams)
    {
        using var reader = new LSXReader(stream);
        loadParams.ToSerializationSettings(reader.SerializationSettings);
        return reader.Read();
    }

    private static Resource LoadLsb(Stream stream)
    {
        using var reader = new LSBReader(stream);
        return reader.Read();
    }

    private static Resource LoadLsf(Stream stream)
    {
        using var reader = new LSFReader(stream);
        return reader.Read();
    }

    private static Resource LoadLsj(Stream stream, ResourceLoadParameters loadParams)
    {
        using var reader = new LSJReader(stream);
        loadParams.ToSerializationSettings(reader.SerializationSettings);
        return reader.Read();
    }

    public static void SaveResource(Resource resource, string outputPath, ResourceConversionParameters conversionParams)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        ArgumentNullException.ThrowIfNull(conversionParams);

        SaveResource(resource, outputPath, ExtensionToResourceFormat(outputPath), conversionParams);
    }

    public static void SaveResource(Resource resource, string outputPath, ResourceFormat format, ResourceConversionParameters conversionParams)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        ArgumentNullException.ThrowIfNull(conversionParams);

        FileManager.TryToCreateDirectory(outputPath);

        using var file = File.Open(outputPath, FileMode.Create, FileAccess.Write);
        switch (format)
        {
            case ResourceFormat.LSX:
                {
                    var writer = new LSXWriter(file)
                    {
                        Version = conversionParams.LSX,
                        PrettyPrint = conversionParams.PrettyPrint
                    };
                    conversionParams.ToSerializationSettings(writer.SerializationSettings, resource);
                    writer.Write(resource);
                    break;
                }

            case ResourceFormat.LSB:
                {
                    var writer = new LSBWriter(file);
                    writer.Write(resource);
                    break;
                }

            case ResourceFormat.LSF:
                {
                    var writer = new LSFWriter(file)
                    {
                        Version = conversionParams.LSF,
                        MetadataFormat = conversionParams.MetadataFormat ?? resource.MetadataFormat ?? LSFMetadataFormat.None,
                        Compression = conversionParams.Compression,
                        CompressionLevel = conversionParams.CompressionLevel
                    };
                    writer.Write(resource);
                    break;
                }

            case ResourceFormat.LSJ:
                {
                    var writer = new LSJWriter(file)
                    {
                        PrettyPrint = conversionParams.PrettyPrint
                    };
                    conversionParams.ToSerializationSettings(writer.SerializationSettings, resource);
                    writer.Write(resource);
                    break;
                }

            default:
                throw new ArgumentException("Invalid resource format configuration parameter passed into writer.", nameof(format));
        }
    }

    private static bool IsA(string path, ResourceFormat format)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string extension = Path.GetExtension(path).ToLowerInvariant();
        return format switch
        {
            ResourceFormat.LSX => extension == ".lsx",
            ResourceFormat.LSB => extension == ".lsb",
            ResourceFormat.LSF => extension == ".lsf" || extension == ".lsbc" || extension == ".lsfx" || extension == ".lsbs",
            ResourceFormat.LSJ => extension == ".lsj",
            _ => false,
        };
    }

    private static void EnumerateFiles(List<string> paths, string rootPath, string currentPath, ResourceFormat format)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        ArgumentException.ThrowIfNullOrEmpty(currentPath);

        foreach (string filePath in Directory.GetFiles(currentPath))
        {
            if (IsA(filePath, format))
            {
                string relativePath = filePath.Length >= rootPath.Length ? filePath[rootPath.Length..] : filePath;

                if (relativePath.Length > 0 && (relativePath[0] == '/' || relativePath[0] == '\\'))
                {
                    relativePath = relativePath[1..];
                }

                paths.Add(relativePath);
            }
        }

        foreach (string directoryPath in Directory.GetDirectories(currentPath))
        {
            EnumerateFiles(paths, rootPath, directoryPath, format);
        }
    }

    public void ConvertResources(string inputDir, string outputDir, ResourceFormat inputFormat, ResourceFormat outputFormat,
        ResourceLoadParameters loadParams, ResourceConversionParameters conversionParams)
    {
        ArgumentException.ThrowIfNullOrEmpty(inputDir);
        ArgumentException.ThrowIfNullOrEmpty(outputDir);
        ArgumentNullException.ThrowIfNull(loadParams);
        ArgumentNullException.ThrowIfNull(conversionParams);

        ProgressUpdate("Enumerating files ...", 0, 1);
        var paths = new List<string>();
        EnumerateFiles(paths, inputDir, inputDir, inputFormat);

        ProgressUpdate("Converting resources ...", 0, 1);
        for (int i = 0; i < paths.Count; i++)
        {
            string path = paths[i];
            string inPath = Path.Join(inputDir, path);

            string outExtension = outputFormat.ToString().ToLowerInvariant();
            string outPath = Path.Join(outputDir, Path.ChangeExtension(path, outExtension));

            FileManager.TryToCreateDirectory(outPath);

            ProgressUpdate($"Converting: {inPath}", i, paths.Count);
            try
            {
                Resource resource = LoadResource(inPath, inputFormat, loadParams);
                SaveResource(resource, outPath, outputFormat, conversionParams);
            }
            catch (Exception ex)
            {
                ErrorDelegates(inPath, ex);
            }
        }
    }
}
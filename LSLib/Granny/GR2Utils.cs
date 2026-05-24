using LSLib.Granny.GR2;
using LSLib.Granny.Model;
using LSLib.LS;

namespace LSLib.Granny;

public sealed class GR2Utils
{
    public delegate void ConversionErrorDelegate(string inputPath, string outputPath, Exception exc);
    public delegate void ProgressUpdateDelegate(string status, long numerator, long denominator);

    public ConversionErrorDelegate ConversionError { get; set; } = static (_, _, _) => { };
    public ProgressUpdateDelegate ProgressUpdate { get; set; } = static (_, _, _) => { };

    public static ExportFormat FileExtensionToModelFormat(string extension)
    {
        ArgumentException.ThrowIfNullOrEmpty(extension);

        if (extension.Equals(".gr2", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".lsm", StringComparison.OrdinalIgnoreCase))
        {
            return ExportFormat.GR2;
        }
        if (extension.Equals(".dae", StringComparison.OrdinalIgnoreCase))
        {
            return ExportFormat.DAE;
        }
        if (extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase))
        {
            return ExportFormat.GLTF;
        }
        if (extension.Equals(".glb", StringComparison.OrdinalIgnoreCase))
        {
            return ExportFormat.GLB;
        }

        throw new ArgumentException($"Unrecognized model file extension mapping: {extension}", nameof(extension));
    }

    public static ExportFormat PathExtensionToModelFormat(string path)
    {
        string extension = Path.GetExtension(path);
        return FileExtensionToModelFormat(extension);
    }

    public static Root LoadModel(string inputPath)
    {
        var options = new ExporterOptions
        {
            InputFormat = PathExtensionToModelFormat(inputPath)
        };
        return LoadModel(inputPath, options);
    }

    public static Root LoadModel(string inputPath, ExporterOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(inputPath);
        ArgumentNullException.ThrowIfNull(options);

        var normalizedInput = inputPath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

        switch (options.InputFormat)
        {
            case ExportFormat.GR2:
                {
                    using var fs = new FileStream(normalizedInput, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var root = new Root();
                    var gr2 = new GR2Reader(fs);
                    gr2.Read(root);
                    root.PostLoad(gr2.Tag);
                    return root;
                }

            case ExportFormat.DAE:
                {
                    var importer = new ColladaImporter { Options = options };
                    return importer.Import(normalizedInput);
                }

            case ExportFormat.GLTF:
            case ExportFormat.GLB:
                {
                    var importer = new GLTFImporter { Options = options };
                    return importer.Import(normalizedInput);
                }

            default:
                throw new ArgumentException("Invalid model configuration format provided to pipeline.");
        }
    }

    public static void SaveModel(Root model, string outputPath, Exporter exporter)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        ArgumentNullException.ThrowIfNull(exporter);

        var normalizedOutput = outputPath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

        var options = exporter.Options;
        if (options is not null)
        {
            options.InputPath = null!;
            options.Input = model;
            options.OutputPath = normalizedOutput;
        }

        exporter.Export();
    }

    private static IEnumerable<string> EnumerateFiles(string path, ExportFormat format)
    {
        var cleanPath = Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar;
        var searchPattern = $"*.{format.ToString().ToLowerInvariant()}";

        return Directory.EnumerateFiles(cleanPath, searchPattern, SearchOption.AllDirectories);
    }

    public void ConvertModels(string inputDirectoryPath, string outputDirectoryPath, Exporter exporter)
    {
        ArgumentException.ThrowIfNullOrEmpty(inputDirectoryPath);
        ArgumentException.ThrowIfNullOrEmpty(outputDirectoryPath);
        ArgumentNullException.ThrowIfNull(exporter);

        var cleanInputFolder = Path.TrimEndingDirectorySeparator(inputDirectoryPath) + Path.DirectorySeparatorChar;
        var cleanOutputFolder = Path.TrimEndingDirectorySeparator(outputDirectoryPath) + Path.DirectorySeparatorChar;
        string outputExtension = exporter.Options.OutputFormat.ToString().ToLowerInvariant();

        ProgressUpdate("Enumerating assets directory metadata ...", 0, 1);

        var inputFilePaths = EnumerateFiles(cleanInputFolder, exporter.Options.InputFormat).ToArray();
        int totalFiles = inputFilePaths.Length;

        ProgressUpdate("Converting asset data resources ...", 0, 1);
        for (var i = 0; i < totalFiles; i++)
        {
            string inputFilePath = inputFilePaths[i];

            string relativePath = inputFilePath[cleanInputFolder.Length..];
            string outputFilePath = Path.Combine(cleanOutputFolder, Path.ChangeExtension(relativePath, outputExtension));

            FileManager.TryToCreateDirectory(outputFilePath);

            ProgressUpdate($"Converting: {relativePath}", i, totalFiles);
            try
            {
                Root model = LoadModel(inputFilePath, exporter.Options);
                SaveModel(model, outputFilePath, exporter);
            }
            catch (Exception exc)
            {
                ConversionError(inputFilePath, outputFilePath, exc);
            }
        }
    }
}
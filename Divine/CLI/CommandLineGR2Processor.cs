using LSLib.Divine;
using LSLib.Divine.CLI;
using LSLib.Granny;
using LSLib.Granny.Model;

namespace Divine.CLI;

internal static class CommandLineGR2Processor
{
    private static readonly Dictionary<string, bool> GR2Options = CommandLineActions.GR2Options;

    public static void Convert(string file = "") => ConvertResource(file);

    public static void BatchConvert() =>
        BatchConvertResources(
            CommandLineActions.SourcePath,
            Program.Argv.InputFormat ?? throw new InvalidOperationException("Batch conversion requires an explicit input format parameter.")
        );


    public static ExporterOptions UpdateExporterSettings()
    {
        ExporterOptions exporterOptions = new()
        {
            InputPath = CommandLineActions.SourcePath,
            OutputPath = CommandLineActions.DestinationPath,

            InputFormat = Program.Argv.InputFormat is not null
                ? GR2Utils.FileExtensionToModelFormat($".{Program.Argv.InputFormat}")
                : GR2Utils.PathExtensionToModelFormat(CommandLineActions.SourcePath),

            OutputFormat = Program.Argv.OutputFormat is not null
                ? GR2Utils.FileExtensionToModelFormat($".{Program.Argv.OutputFormat}")
                : GR2Utils.PathExtensionToModelFormat(CommandLineActions.DestinationPath),

            FlipUVs = GR2Options.GetValueOrDefault("flip-uvs"),
            BuildDummySkeleton = GR2Options.GetValueOrDefault("build-dummy-skeleton"),
            CompactIndices = GR2Options.GetValueOrDefault("compact-tris"),
            DeduplicateVertices = GR2Options.GetValueOrDefault("deduplicate-vertices"),
            ApplyBasisTransforms = GR2Options.GetValueOrDefault("apply-basis-transforms"),
            UseObsoleteVersionTag = GR2Options.GetValueOrDefault("force-legacy-version"),
            ConformGR2Path = !string.IsNullOrEmpty(CommandLineActions.ConformPath) ? CommandLineActions.ConformPath : null,
            MirrorSkeleton = GR2Options.GetValueOrDefault("mirror-skeletons"),
            FlipMesh = GR2Options.GetValueOrDefault("x-flip-meshes"),
            TransformSkeletons = GR2Options.GetValueOrDefault("y-up-skeletons"),
            IgnoreUVNaN = GR2Options.GetValueOrDefault("ignore-uv-nan"),
            EnableQTangents = !GR2Options.GetValueOrDefault("disable-qtangents")
        };

        if (exporterOptions.ConformGR2Path is not null && GR2Options.GetValueOrDefault("conform-copy"))
        {
            exporterOptions.ConformSkeletons = false;
            exporterOptions.ConformSkeletonsCopy = true;
        }

        exporterOptions.LoadGameSettings(CommandLineActions.Game);

        return exporterOptions;
    }

    private static void ConvertResource(string file)
    {
        var exporter = new Exporter
        {
            Options = UpdateExporterSettings()
        };

        if (!string.IsNullOrEmpty(file))
        {
            exporter.Options.InputPath = file;
        }

#if !DEBUG
        try
        {
#endif
        exporter.Export();
        CommandLineLogger.LogInfo("Export completed successfully.");
#if !DEBUG
        }
        catch (Exception e)
        {
            CommandLineLogger.LogFatal($"Export failed: {e.Message}{Environment.NewLine}{e.StackTrace}", 2);
        }
#endif
    }

    private static void BatchConvertResources(string sourcePath, string inputFormat)
    {
        string[] files = Directory.GetFiles(sourcePath, $"*.{inputFormat}");

        if (files.Length == 0)
        {
            CommandLineLogger.LogFatal($"Batch convert failed: *.{inputFormat} not found in source path", 1);
        }

        foreach (string file in files)
        {
            UpdateExporterSettings();
            Convert(file);
        }
    }
}
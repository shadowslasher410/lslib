using System.Text.RegularExpressions;
using Divine.CLI;
using LSLib.LS;
using LSLib.LS.Enums;

namespace LSLib.Divine.CLI;

internal static class CommandLineActions
{
    public static string SourcePath { get; set; } = string.Empty;
    public static string DestinationPath { get; set; } = string.Empty;
    public static string PackagedFilePath { get; set; } = string.Empty;
    public static string? ConformPath { get; set; }
    public static string VTConfigPath { get; set; } = string.Empty;
    public static string VTRootPath { get; set; } = string.Empty;

    public static Game Game { get; set; }
    public static LogLevel LogLevel { get; set; }
    public static ResourceFormat InputFormat { get; set; }
    public static ResourceFormat OutputFormat { get; set; }
    public static PackageVersion PackageVersion { get; set; }
    public static int PackagePriority { get; set; }
    public static bool LegacyGuids { get; set; }
    public static bool FastBuild { get; set; }
    public static bool VTValidate { get; set; }
    public static Dictionary<string, bool> GR2Options { get; set; } = [];

    private static readonly HashSet<string> BatchActions = ["extract-packages", "convert-models", "convert-resources"];
    private static readonly HashSet<string> GraphicsActions = ["convert-model", "convert-models"];

    public static void Run(CommandLineArguments args)
    {
        ArgumentNullException.ThrowIfNull(args);
        SetUpAndValidate(args);
        Process(args);
    }

    private static void SetUpAndValidate(CommandLineArguments args)
    {
        LogLevel = CommandLineArguments.GetLogLevelByString(args.LogLevel);
        CommandLineLogger.LogDebug($"Using log level: {LogLevel}");

        Game = CommandLineArguments.GetGameByString(args.Game);
        CommandLineLogger.LogDebug($"Using game: {Game}");

        LegacyGuids = args.LegacyGuids;
        FastBuild = args.FastBuild;
        VTValidate = args.VTValidate;

        if (BatchActions.Contains(args.Action))
        {
            if (args.InputFormat is null || args.OutputFormat is null)
            {
                if (args.InputFormat is null && args.Action != "extract-packages")
                {
                    CommandLineLogger.LogFatal("Cannot perform batch action without --input-format and --output-format arguments", 1);
                }
            }

            InputFormat = CommandLineArguments.GetResourceFormatByString(args.InputFormat);
            CommandLineLogger.LogDebug($"Using input format: {InputFormat}");

            if (args.Action != "extract-packages")
            {
                OutputFormat = CommandLineArguments.GetResourceFormatByString(args.OutputFormat);
                CommandLineLogger.LogDebug($"Using output format: {OutputFormat}");
            }
        }

        if (args.Action == "create-package")
        {
            PackagePriority = args.PackagePriority;
            PackageVersion = Game.PAKVersion();
            CommandLineLogger.LogDebug($"Using package version: {PackageVersion}");
        }

        if (args.Action == "build-vt")
        {
            VTConfigPath = TryToValidatePath(args.Source);
            VTRootPath = TryToValidatePath(args.VTRoot);
        }

        if (GraphicsActions.Contains(args.Action))
        {
            GR2Options = CommandLineArguments.GetGR2Options(args.Options);

            if (LogLevel is LogLevel.DEBUG or LogLevel.ALL)
            {
                CommandLineLogger.LogDebug("Using graphics options:");

                foreach (var (key, value) in GR2Options)
                {
                    CommandLineLogger.LogDebug($"   {key} = {value}");
                }
            }

            if (args.ConformPath is { Length: > 0 })
            {
                ConformPath = TryToValidatePath(args.ConformPath);

                if (!Path.Exists(ConformPath))
                {
                    CommandLineLogger.LogFatal($"Skeleton source GR2 does not exist: {args.ConformPath}", 1);
                }
            }
        }

        SourcePath = TryToValidatePath(args.Source);

        if (args.Action is not "list-package" and not "build-vt")
        {
            DestinationPath = TryToValidatePath(args.Destination);
        }

        if (args.Action == "extract-single-file")
        {
            PackagedFilePath = args.PackagedPath ?? string.Empty;
        }
    }

    private static void Process(CommandLineArguments args)
    {
        Func<PackagedFileInfo, bool> filter;

        if (args.Expression is { Length: > 0 })
        {
            Regex? expression = null;
            if (args.UseRegex)
            {
                try
                {
                    expression = new Regex(args.Expression, RegexOptions.Singleline | RegexOptions.Compiled);
                }
                catch (ArgumentException)
                {
                    CommandLineLogger.LogFatal($"Cannot parse RegEx expression: {args.Expression}", -1);
                }
            }
            else
            {
                string pattern = $"^{Regex.Escape(args.Expression).Replace(@"\*", ".*").Replace(@"\?", ".")}$";
                expression = new Regex(pattern, RegexOptions.Singleline | RegexOptions.Compiled);
            }

            filter = obj => expression is not null && obj.Name.Like(expression);
        }
        else
        {
            filter = _ => true;
        }

        switch (args.Action)
        {
            case "create-package":
                CommandLinePackageProcessor.Create();
                break;

            case "extract-package":
                CommandLinePackageProcessor.Extract(filter);
                break;

            case "extract-single-file":
                CommandLinePackageProcessor.ExtractSingleFile();
                break;

            case "list-package":
                CommandLinePackageProcessor.ListFiles(filter);
                break;

            case "convert-model":
                CommandLineGR2Processor.UpdateExporterSettings();
                CommandLineGR2Processor.Convert();
                break;

            case "convert-resource":
                CommandLineDataProcessor.Convert();
                break;

            case "convert-loca":
                CommandLineDataProcessor.ConvertLoca();
                break;

            case "extract-packages":
                CommandLinePackageProcessor.BatchExtract(filter);
                break;

            case "convert-models":
                CommandLineGR2Processor.BatchConvert();
                break;

            case "convert-resources":
                CommandLineDataProcessor.BatchConvert();
                break;

            case "build-vt":
                CommandLineDataProcessor.BuildVirtualTextureSet();
                break;

            default:
                throw new ArgumentException($"Unhandled action: {args.Action}");
        }
    }

    public static string TryToValidatePath(string path)
    {
        CommandLineLogger.LogDebug($"Using path: {path}");

        if (string.IsNullOrWhiteSpace(path))
        {
            CommandLineLogger.LogFatal($"Cannot parse path from input: {path}", 1);
        }

        if (Uri.TryCreate(path, UriKind.RelativeOrAbsolute, out var uri))
        {
            if (!Path.IsPathRooted(path) || !uri.IsFile)
            {
                CommandLineLogger.LogFatal($"Cannot proceed without absolute path [E2]: {path}", 1);
            }
        }
        else
        {
            CommandLineLogger.LogFatal($"Cannot proceed without absolute path [E1]: {path}", 1);
        }

        return Path.GetFullPath(path);
    }
}
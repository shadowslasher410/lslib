using LSLib.Divine.CLI;
using System.CommandLine;
using System.Globalization;

namespace LSLib.Divine;

internal static class Program
{
    public static CommandLineArguments Argv { get; private set; } = new();

    private static async Task<int> Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.ReadOnly(new CultureInfo(CultureInfo.CurrentCulture.Name)
        {
            NumberFormat = { NumberDecimalSeparator = "." }
        });
        var logLevelOptions = new Option<string>("--log-level", "-l")
        {
            Description = "Set verbosity level of log output",
            DefaultValueFactory = _ => "info",
            Arity = ArgumentArity.ExactlyOne,
            Required = false
        };
        logLevelOptions.AcceptOnlyFromAmong("off", "fatal", "error", "warn", "info", "debug", "trace", "all");

        var gameOptions = new Option<string>("--game", "-g")
        {
            Description = "Set target game when generating output",
            DefaultValueFactory = _ => string.Empty,
            Arity = ArgumentArity.ExactlyOne,
            Required = true
        };
            gameOptions.AcceptOnlyFromAmong("dos", "dosee", "dos2", "dos2de", "bg3");


        var inputFormatOptions = new Option<string>("--input-format", "-i")
        {
            Description = "Set input format for batch operations",
            DefaultValueFactory = _ => string.Empty,
            Arity = ArgumentArity.ExactlyOne,
            Required = false
        };
            inputFormatOptions.AcceptOnlyFromAmong("dae", "glb", "gltf", "gr2", "lsv", "pak", "lsj", "lsx", "lsb", "lsf");

        var outputFormatOptions = new Option<string>("--output-format", "-o")
        {
            Description = "Set output format for batch operations",
            DefaultValueFactory = _ => string.Empty,
            Arity = ArgumentArity.ExactlyOne,
            Required = false
        };
            outputFormatOptions.AcceptOnlyFromAmong("dae", "glb", "gltf", "gr2", "lsv", "pak", "lsj", "lsx", "lsb", "lsf");


        var actionOptions = new Option<string>("--action", "-a")
        {
            Description = "Set action to execute",
            DefaultValueFactory = _ => "extract-package",
            Arity = ArgumentArity.ExactlyOne,
            Required = true
        };
        actionOptions.AcceptOnlyFromAmong("create-package", "list-package", "extract-single-file", "extract-package", "extract-packages", "convert-model", "convert-models", "convert-resource", "convert-resources", "convert-loca", "build-vt");


        var pakCompressionOptions = new Option<string>("--compression-method", "-c")
        {
            Description = "Set compression method",
            DefaultValueFactory = _ => "lz4hc",
            Arity = ArgumentArity.ExactlyOne,
            Required = false
        };
        pakCompressionOptions.AcceptOnlyFromAmong("zlib", "zlibfast", "lz4", "lz4hc", "none");


        var gr2Options = new Option<string[]>("--gr2-options", "-e")
        {
            Description = "Set extra options for GR2/DAE conversion",
            AllowMultipleArgumentsPerToken = true,
            Arity = ArgumentArity.OneOrMore,
            Required = false
        };
        gr2Options.AcceptOnlyFromAmong("export-normals", "export-tangents", "export-uvs", "export-colors", "deduplicate-vertices", "deduplicate-uvs", "recalculate-normals", "recalculate-tangents", "recalculate-iwt", "flip-uvs", "ignore-uv-nan", "disable-qtangents", "y-up-skeletons", "force-legacy-version", "compact-tris", "build-dummy-skeleton", "apply-basis-transforms", "mirror-skeletons", "x-flip-meshes", "conform", "conform-copy");


        var sourceOption = new Option<string>("--source", "-s")
        {
            Description = "Set source file path or directory",
            DefaultValueFactory = _ => string.Empty,
            Arity = ArgumentArity.ExactlyOne,
            Required = true
        };


        var expressionOption = new Option<string>("--expression", "-x")
        {
            Description = "Set glob expression for extract and list actions",
            DefaultValueFactory = _ => "*",
            Arity = ArgumentArity.ExactlyOne,
            Required = false
        };


        var destinationOption = new Option<string>("--destination", "-d")
        {
            Description = "Set destination file path or directory",
            DefaultValueFactory = _ => string.Empty,
            Arity = ArgumentArity.ZeroOrOne,
            Required = false
        };


        var packagedPathOption = new Option<string>("--packaged-path", "-f")
        {
            Description = "File to extract from package",
            DefaultValueFactory = _ => string.Empty,
            Arity = ArgumentArity.ZeroOrOne,
            Required = false
        };


        var packagePriorityOption = new Option<string>("--package-priority")
        {
            Description = "Set a custom package priority",
            DefaultValueFactory = _ => string.Empty,
            Arity = ArgumentArity.ZeroOrOne,
            Required = false
        };


        var conformPathOption = new Option<string>("--conform-path")
        {
            Description = "Set conform to original path",
            DefaultValueFactory = _ => string.Empty,
            Arity = ArgumentArity.ExactlyOne,
            Required = false
        };

        var vtRootOption = new Option<string>("--vt-root")
        {
            Description = "Tileset build mod root path",
            DefaultValueFactory = _ => string.Empty,
            Arity = ArgumentArity.ExactlyOne,
            Required = false
        };
        var legacyGuidsOption = new Option<bool>("--legacy-guids")
        {
            Description = "Use legacy GUID serialization format when serializing LSX/LSJ files",
            DefaultValueFactory = _ => false,
            Required = false
        };


        var fastbuildOption = new Option<bool>("--fast-build")
        {
            Description = "Faster VT build, but lower compression ratio",
            DefaultValueFactory = _ => false,
            Required = false
        };


        var vtValidateOption = new Option<bool>("--vt-validate")
        {
            Description = "Validate generated VT files",
            DefaultValueFactory = _ => false,
            Required = false
        };


        var usePackageNameOption = new Option<bool>("--use-package-name")
        {
            Description = "Use package name for destination folder",
            DefaultValueFactory = _ => false,
            Required = false
        };


        var useRegexOption = new Option<bool>("--use-regex")
        {
            Description = "Use Regular Expressions for expression type",
            DefaultValueFactory = _ => false,
            Required = false
        };
        RootCommand rootCommand = new("Divine CLI Compiler and Extraction Utility Engine")
        {
            logLevelOptions,
            gameOptions,
            inputFormatOptions,
            outputFormatOptions,
            actionOptions,
            pakCompressionOptions,
            gr2Options,
            sourceOption,
            destinationOption,
            packagedPathOption,
            expressionOption,
            conformPathOption,
            packagePriorityOption,
            legacyGuidsOption,
            fastbuildOption,
            vtValidateOption,
            usePackageNameOption,
            useRegexOption,
            vtRootOption
        };
        rootCommand.SetAction(parseResult =>
        {
            string LogLevel = parseResult.GetValue(logLevelOptions)!;
            string Game = parseResult.GetValue(gameOptions)!;
            string Source = parseResult.GetValue(sourceOption)!;
            string Destination = parseResult.GetValue(destinationOption)!;
            string PackagedPath = parseResult.GetValue(packagedPathOption)!;
            string InputFormat = parseResult.GetValue(inputFormatOptions)!;
            string OutputFormat = parseResult.GetValue(outputFormatOptions)!;
            string Action = parseResult.GetValue(actionOptions)!;
            string PakCompressionMethod = parseResult.GetValue(pakCompressionOptions)!;
            string[] GR2Options = parseResult.GetValue(gr2Options) ?? [];
            string Expression = parseResult.GetValue(expressionOption)!;
            string ConformPath = parseResult.GetValue(conformPathOption)!;
            string PackagePriority = parseResult.GetValue(packagePriorityOption)!;
            string VTRoot = parseResult.GetValue(vtRootOption)!;
            bool LegacyGuids = parseResult.GetValue(legacyGuidsOption);
            bool FastBuild = parseResult.GetValue(fastbuildOption);
            bool VTValidate = parseResult.GetValue(vtValidateOption);
            bool UsePackageName = parseResult.GetValue(usePackageNameOption);
            bool UseRegex = parseResult.GetValue(useRegexOption);
        });

        return await rootCommand.Parse(args).InvokeAsync();
    }
}
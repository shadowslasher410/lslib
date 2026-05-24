using System.CommandLine;
using LSLib.LS;
using LSLib.VirtualTextures;
using LSTools.VTex;

var buildRootOption = new Option<string>("--build-root")
{
    Description = "The root directory context for the virtual texture build."
};
var configXmlOption = new Option<string>("--config-xml")
{
    Description = "Filename or relative path of the configuration XML."
};

var rootCommand = new RootCommand("LSLib Virtual Tile Set Generator CLI Wrapper")
{
    buildRootOption,
    configXmlOption
};

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    string buildRoot = parseResult.GetValue(buildRootOption)!;
    string configFilename = parseResult.GetValue(configXmlOption)!;

    Console.WriteLine($"LSLib Virtual Tile Set Generator (v{Common.MajorVersion}.{Common.MinorVersion}.{Common.PatchVersion})");

    try
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        var context = new VTexBuildContext { RootPath = buildRoot, ConfigFilename = configFilename };

        var descriptor = new TileSetDescriptor
        {
            RootPath = context.RootPath
        };
        descriptor.Load(context.ResolvedConfigPath);
        cancellationToken.ThrowIfCancellationRequested();

        var builder = new TileSetBuilder(descriptor.Config);
        foreach (var texture in descriptor.Textures)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<string> layerPaths = [.. texture.Layers.Select(name => !string.IsNullOrEmpty(name) ? Path.Combine(descriptor.SourceTexturePath, name) : string.Empty)];

            builder.AddTexture(texture.Name, layerPaths);
        }

        builder.OnStepStarted = (stepName) => Console.WriteLine($"[Pipeline] Starting: {stepName}");
        builder.OnStepProgress = (current, total) => Console.Write($"\rProcessing tiles: {current} / {total}");

        Console.WriteLine("Dividing textures into virtual tiers and writing page files...");

        builder.TileSet = new VirtualTileSet();
        var targetGtpDirectory = Path.GetDirectoryName(descriptor.VirtualTexturePath) ?? descriptor.RootPath;

        Console.WriteLine("\nWriting master metadata (.gts) container definition...");

        builder.TileSet?.Save(descriptor.VirtualTexturePath);

        Console.WriteLine("Virtual texture build completed successfully.");
        return 0;
    }
    catch (OperationCanceledException)
    {
        WriteErrorLine("Virtual texture generation was aborted by user cancellation signal.");
        return 130;
    }
    catch (Exception e) when (e is InvalidDataException or FileNotFoundException)
    {
        WriteErrorLine(e.Message);
        return 1;
    }
});


return await rootCommand.Parse(args).InvokeAsync();

static void WriteErrorLine(string message)
{
    var originalColor = Console.ForegroundColor;
    try
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"Error: {message}");
    }
    finally
    {
        Console.ForegroundColor = originalColor;
    }
}

namespace LSTools.VTex
{
    public class VTexBuildContext
    {
        public required string RootPath { get; set; }
        public required string ConfigFilename { get; set; }
        public string ResolvedConfigPath
        {
            get => field ??= Path.Combine(RootPath, ConfigFilename);
            private set;
        }
    }
}

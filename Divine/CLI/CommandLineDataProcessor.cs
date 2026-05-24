using LSLib.LS;
using LSLib.LS.Enums;
using LSLib.VirtualTextures;

namespace LSLib.Divine.CLI;

internal static class CommandLineDataProcessor
{
    public static void Convert()
    {
        var conversionParams = ResourceConversionParameters.FromGameVersion(CommandLineActions.Game);
        var loadParams = ResourceLoadParameters.FromGameVersion(CommandLineActions.Game);
        loadParams.ByteSwapGuids = !CommandLineActions.LegacyGuids;

        ConvertResource(CommandLineActions.SourcePath, CommandLineActions.DestinationPath, loadParams, conversionParams);
    }

    public static void BatchConvert()
    {
        var conversionParams = ResourceConversionParameters.FromGameVersion(CommandLineActions.Game);
        var loadParams = ResourceLoadParameters.FromGameVersion(CommandLineActions.Game);
        loadParams.ByteSwapGuids = !CommandLineActions.LegacyGuids;

        BatchConvertResource(CommandLineActions.SourcePath, CommandLineActions.DestinationPath, CommandLineActions.InputFormat, CommandLineActions.OutputFormat, loadParams, conversionParams);
    }

    private static void ConvertResource(string sourcePath, string destinationPath,
        ResourceLoadParameters loadParams, ResourceConversionParameters conversionParams)
    {
        try
        {
            ResourceFormat resourceFormat = ResourceUtils.ExtensionToResourceFormat(destinationPath);
            CommandLineLogger.LogDebug($"Using destination extension: {resourceFormat}");

            Resource resource = ResourceUtils.LoadResource(sourcePath, loadParams);
            ResourceUtils.SaveResource(resource, destinationPath, resourceFormat, conversionParams);

            CommandLineLogger.LogInfo($"Wrote resource to: {destinationPath}");
        }
        catch (Exception e)
        {
            CommandLineLogger.LogFatal($"Failed to convert resource: {e.Message}", 2);
            CommandLineLogger.LogTrace($"{e.StackTrace}");
        }
    }

    public static void BuildVirtualTextureSet()
    {
        try
        {

            var descriptor = new TileSetDescriptor
            {
                RootPath = CommandLineActions.VTRootPath
            };
            descriptor.Config.FastBuild = CommandLineActions.FastBuild;
            descriptor.Config.Validate = CommandLineActions.VTValidate;
            descriptor.Load(CommandLineActions.VTConfigPath);

            var builder = new TileSetBuilder(descriptor.Config);
            foreach (var texture in descriptor.Textures)
            {

                List<string> layerPaths = [.. texture.Layers.Select(name => !string.IsNullOrEmpty(name) ? Path.Combine(descriptor.SourceTexturePath, name) : string.Empty)];

                builder.AddTexture(texture.Name, layerPaths);
            }

            builder.OnStepStarted = (stepName) => Console.WriteLine($"[Pipeline] Starting: {stepName}");
            builder.OnStepProgress = (current, total) => Console.Write($"\rProcessing tiles: {current} / {total}");

            CommandLineLogger.LogDebug("Dividing textures into virtual tiers and writing page files...");

            builder.TileSet = new VirtualTileSet();
            var targetGtpDirectory = Path.GetDirectoryName(descriptor.VirtualTexturePath) ?? descriptor.RootPath;

            CommandLineLogger.LogDebug("\nWriting master metadata (.gts) container definition...");

            builder.TileSet?.Save(descriptor.VirtualTexturePath);
            CommandLineLogger.LogDebug("Tileset built successfully.");
        }
        catch (Exception e) when (e is InvalidDataException or FileNotFoundException)
        {
            CommandLineLogger.LogFatal($"Failed to build tileset: {e.Message}", 2);
        }
        catch (Exception e)
        {
            CommandLineLogger.LogFatal($"Failed to build tileset: {e.Message}", 2);
            CommandLineLogger.LogTrace($"{e.StackTrace}");
        }
    }

    public static void ConvertLoca() => ConvertLoca(CommandLineActions.SourcePath, CommandLineActions.DestinationPath);

    private static void ConvertLoca(string sourcePath, string destinationPath)
    {
        try
        {
            var loca = LocaUtils.Load(sourcePath);
            LocaUtils.Save(loca, destinationPath);
            CommandLineLogger.LogInfo($"Wrote localization to: {destinationPath}");
        }
        catch (Exception e)
        {
            CommandLineLogger.LogFatal($"Failed to convert localization file: {e.Message}", 2);
            CommandLineLogger.LogTrace($"{e.StackTrace}");
        }
    }

    private static void BatchConvertResource(string sourcePath, string destinationPath, ResourceFormat inputFormat, ResourceFormat outputFormat,
        ResourceLoadParameters loadParams, ResourceConversionParameters conversionParams)
    {
        try
        {
            CommandLineLogger.LogDebug($"Using destination extension: {outputFormat}");

            ResourceUtils resourceUtils = new();
            resourceUtils.ConvertResources(sourcePath, destinationPath, inputFormat, outputFormat, loadParams, conversionParams);

            CommandLineLogger.LogInfo($"Wrote resources to: {destinationPath}");
        }
        catch (Exception e)
        {
            CommandLineLogger.LogFatal($"Failed to batch convert resources: {e.Message}", 2);
            CommandLineLogger.LogTrace($"{e.StackTrace}");
        }
    }
}
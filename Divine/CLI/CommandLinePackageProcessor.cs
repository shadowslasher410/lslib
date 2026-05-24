using LSLib.Divine;
using LSLib.Divine.CLI;
using LSLib.LS;

namespace Divine.CLI;

internal class CommandLinePackageProcessor
{
    private static readonly CommandLineArguments Args = Program.Argv;

    public static void Create()
    {
        CreatePackageResource();
    }

    public static void ListFiles(Func<PackagedFileInfo, bool> filter = null!)
    {
        if (CommandLineActions.SourcePath is null)
        {
            CommandLineLogger.LogFatal("Cannot list package without source path", 1);
        }
        else
        {
            ListPackageFiles(CommandLineActions.SourcePath, filter);
        }
    }

    public static void ExtractSingleFile()
    {
        ExtractSingleFile(CommandLineActions.SourcePath, CommandLineActions.DestinationPath, CommandLineActions.PackagedFilePath);
    }

    private static void ExtractSingleFile(string packagePath, string destinationPath, string packagedPath)
    {
        try
        {
            var reader = new PackageReader();
            using var package = reader.Read(packagePath);

            var file = package.Files.Find(fileInfo => 
                string.Equals(fileInfo.Name, packagedPath, StringComparison.OrdinalIgnoreCase) && !fileInfo.IsDeletion());
            
            if (file is null)
            {
                file = package.Files.Find(fileInfo => 
                    string.Equals(Path.GetFileName(fileInfo.Name), packagedPath, StringComparison.OrdinalIgnoreCase));
                
                if (file is null)
                {
                    CommandLineLogger.LogError($"Package doesn't contain file named '{packagedPath}'");
                    return;
                }
            }

            using var fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);
            using var source = file.CreateContentReader();
            source.CopyTo(fs);
        }
        catch (NotAPackageException)
        {
            CommandLineLogger.LogError("Failed to list package contents because the package is not an Original Sin package or savegame archive");
        }
        catch (Exception e)
        {
            CommandLineLogger.LogFatal($"Failed to list package: {e.Message}", 2);
            CommandLineLogger.LogTrace($"{e.StackTrace}");
        }
    }

    private static void ListPackageFiles(string packagePath, Func<PackagedFileInfo, bool> filter = null!)
    {
        try
        {
            var reader = new PackageReader();
            using var package = reader.Read(packagePath);
            var files = package.Files;

            if (filter is not null)
            {
                files = files.FindAll(obj => filter(obj));
            }

            foreach (var fileInfo in files.OrderBy(obj => obj.Name))
            {
                Console.WriteLine($"{fileInfo.Name}\t{fileInfo.Size()}\t{fileInfo.Crc}");
            }
        }
        catch (NotAPackageException)
        {
            CommandLineLogger.LogError("Failed to list package contents because the package is not an Original Sin package or savegame archive");
        }
        catch (Exception e)
        {
            CommandLineLogger.LogFatal($"Failed to list package: {e.Message}", 2);
            CommandLineLogger.LogTrace($"{e.StackTrace}");
        }
    }

    public static void Extract(Func<PackagedFileInfo, bool> filter = null!)
    {
        if (CommandLineActions.SourcePath is null)
        {
            CommandLineLogger.LogFatal("Cannot extract package without source path", 1);
        }
        else
        {
            string extractionPath = GetExtractionPath(CommandLineActions.SourcePath, CommandLineActions.DestinationPath);
            CommandLineLogger.LogInfo($"Extracting package: {CommandLineActions.SourcePath}");
            ExtractPackageResource(CommandLineActions.SourcePath, extractionPath, filter);
        }
    }

    public static void BatchExtract(Func<PackagedFileInfo, bool> filter = null!)
    {
        string[] files = Directory.GetFiles(CommandLineActions.SourcePath, $"*.{Args.InputFormat}");

        foreach (string file in files)
        {
            string extractionPath = GetExtractionPath(file, CommandLineActions.DestinationPath);
            CommandLineLogger.LogInfo($"Extracting package: {file}");
            ExtractPackageResource(file, extractionPath, filter);
        }
    }

    private static string GetExtractionPath(string sourcePath, string destinationPath)
    {
        return Args.UsePackageName 
            ? Path.Combine(destinationPath, Path.GetFileNameWithoutExtension(sourcePath) ?? throw new InvalidOperationException("Failed to evaluate source path metadata.")) 
            : CommandLineActions.DestinationPath;
    }

    private static void CreatePackageResource(string file = "")
    {
        if (string.IsNullOrEmpty(file))
        {
            file = CommandLineActions.DestinationPath;
            CommandLineLogger.LogDebug($"Using destination path: {file}");
        }

        PackageBuildData build = new()
        {
            Version = CommandLineActions.PackageVersion,
            Priority = (byte)CommandLineActions.PackagePriority
        };

        string fileExtension = Path.GetExtension(file)?.ToLowerInvariant() ?? string.Empty;
        string compressionMethodStr = fileExtension == ".lsv" ? "zlib" : Args.PakCompressionMethod;

        Dictionary<string, object> compressionOptions = CommandLineArguments.GetCompressionOptions(compressionMethodStr, build.Version);
        build.Compression = (CompressionMethod)compressionOptions["Compression"];
        build.CompressionLevel = (LSCompressionLevel)compressionOptions["CompressionLevel"];

        CommandLineLogger.LogDebug($"Using compression method: {build.Compression} ({build.CompressionLevel})");

        var packager = new Packager();
        
        packager.CreatePackage(file, CommandLineActions.SourcePath, build).GetAwaiter().GetResult();

        CommandLineLogger.LogInfo("Package created successfully.");
    }

    private static void ExtractPackageResource(string file = "", string folder = "", Func<PackagedFileInfo, bool> filter = null!)
    {
        if (string.IsNullOrEmpty(file))
        {
            file = CommandLineActions.SourcePath;
            CommandLineLogger.LogDebug($"Using source path: {file}");
        }

#if !DEBUG
        try
        {
#endif
            var packager = new Packager();
            string extractionPath = GetExtractionPath(folder, CommandLineActions.DestinationPath);

            CommandLineLogger.LogDebug($"Using extraction path: {extractionPath}");
            packager.UncompressPackage(file, extractionPath, filter);

            CommandLineLogger.LogInfo($"Extracted package to: {extractionPath}");
#if !DEBUG
        }
        catch (NotAPackageException)
        {
            CommandLineLogger.LogError("Failed to extract package because the package is not an Original Sin package or savegame archive");
        }
        catch (Exception e)
        {
            CommandLineLogger.LogFatal($"Failed to extract package: {e.Message}", 2);
            CommandLineLogger.LogTrace($"{e.StackTrace}");
        }
#endif
    }
}

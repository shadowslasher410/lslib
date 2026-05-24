using LSLib.LS.Story;
using LSLib.LS;
using System.CommandLine;
using StoryDecompiler;
using LSLib.LS.Resources.LSF;

var inputOption = new Option<string>("--input", "-i")
{
    Description = "Compiled story/savegame file path (.osi or .lsv)",
    Required = true
};

var outputOption = new Option<string>("--output", "-o")
{
    Description = "Goal destination output directory path",
    Required = true
};

var debugLogOption = new Option<string>("--debug-log", "-d")
{
    Description = "Generate comprehensive story debug log file",
    Arity = ArgumentArity.ExactlyOne,
    DefaultValueFactory = _ => "info",
};
debugLogOption.AcceptOnlyFromAmong("off", "fatal", "error", "warn", "info", "debug", "trace", "all");

var rootCommand = new RootCommand("Osiris Story Decompiler Framework")
{
    inputOption,
    outputOption,
    debugLogOption
};

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    string inputPath = parseResult.GetValue(inputOption)!;
    string outputPath = parseResult.GetValue(outputOption)!;
    string debugLog = parseResult.GetValue(debugLogOption)!;

    var settings = new DecompilerContext
    {
        DebugEnabled = !string.Equals(debugLog, "off", StringComparison.OrdinalIgnoreCase)
    };

    if (!File.Exists(inputPath))
    {
        WriteErrorLine($"Source input file context does not exist: {inputPath}");
        return 1;
    }

    try
    {
        Console.WriteLine($"Initializing Decompiler Pipeline...");
        Console.WriteLine($"Target Input: {inputPath}");
        Console.WriteLine($"Target Output: {outputPath}");
        Console.WriteLine($"Debug Logging Status: {settings.DebugEnabled}");

        // Yield to the thread loop to keep execution non-blocking 
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        Console.WriteLine($"Loading story from {inputPath} ...");
        var story = LoadStory(inputPath);

        Directory.CreateDirectory(outputPath);
        cancellationToken.ThrowIfCancellationRequested();

        if (settings.DebugEnabled)
        {
            Console.WriteLine("Exporting debug log ...");
            string debugLogPath = Path.Combine(outputPath, "debug.log");
            DebugDumpStory(story, debugLogPath);
        }

        Console.WriteLine("Exporting goals ...");
        DecompileStoryGoals(story, outputPath);

        Console.WriteLine("Decompilation processed successfully.");
        return 0;
    }
    catch (OperationCanceledException)
    {
        WriteErrorLine("Decompilation pipeline execution was aborted by user cancellation signal.");
        return 130; // Standard SIGINT exit payload 
    }
    catch (Exception ex)
    {
        WriteErrorLine($"Decompilation pipeline failed: {ex.Message}");
        return 1;
    }
});

// Modern asynchronous console entry pipeline hook matching .NET 10 standards
return await rootCommand.Parse(args).InvokeAsync();

static Stream LoadStoryStreamFromSave(string path)
{
    var reader = new PackageReader();
    using var package = reader.Read(path);

    var globalsFile = package.Files.FirstOrDefault(p =>
        string.Equals(p.Name, "globals.lsf", StringComparison.OrdinalIgnoreCase)) ?? throw new FileNotFoundException("Could not find globals.lsf in savegame archive target package.");
    Resource resource;
    using (var rsrcStream = globalsFile.CreateContentReader())
    using (var rsrcReader = new LSFReader(rsrcStream))
    {
        resource = rsrcReader.Read();
    }

    var storyChildren = resource.Regions["Story"].Children["Story"];
    if (storyChildren.Count == 0)
    {
        throw new InvalidDataException("Missing inner Story collection descriptor within resource metadata.");
    }

    LSLib.LS.Node storyNode = storyChildren[0];

    if (storyNode.Attributes["Story"].Value is not byte[] storyBlob)
    {
        throw new InvalidDataException("Story node target format metadata payload is corrupt or invalid.");
    }

    return new MemoryStream(storyBlob);
}

static Stream LoadStoryStreamFromFile(string path)
{
    return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
}

static Story LoadStory(string path)
{
    string extension = Path.GetExtension(path);

    Stream storyStream = extension.Equals(".lsv", StringComparison.OrdinalIgnoreCase)
        ? LoadStoryStreamFromSave(path)
        : extension.Equals(".osi", StringComparison.OrdinalIgnoreCase)
            ? LoadStoryStreamFromFile(path)
            : throw new NotSupportedException($"Unsupported target story/save filename extension: {extension}");

    using (storyStream)
    {
        return StoryReader.Read(storyStream);
    }
}

static void DebugDumpStory(Story story, string debugLogPath)
{
    using var debugFile = new FileStream(debugLogPath, FileMode.Create, FileAccess.Write);
    using var writer = new StreamWriter(debugFile);
    story.DebugDump(writer);
}

static void DecompileStoryGoals(Story story, string outputDir)
{
    foreach (var (_, goal) in story.Goals)
    {
        string filePath = Path.Combine(outputDir, $"{goal.Name}.txt");
        using var goalFile = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var writer = new StreamWriter(goalFile);
        goal.MakeScript(writer, story);
    }
}

static void WriteErrorLine(string message)
{
    var originalColor = Console.ForegroundColor;
    try
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(message);
    }
    finally
    {
        Console.ForegroundColor = originalColor;
    }
}

namespace StoryDecompiler
{
    public class DecompilerContext
    {
        public bool DebugEnabled
        {
            get;
            set;
        }
    }
}
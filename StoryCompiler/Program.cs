using LSLib.LS.Story;
using LSLib.LS.Story.Compiler;

namespace LSTools.StoryCompiler;

internal static class Program
{
    private static void DebugDump(string storyPath, string debugPath)
    {
        Story story;
        using (var file = new FileStream(storyPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            story = StoryReader.Read(file);
        }

        using var debugFile = new FileStream(debugPath, FileMode.Create, FileAccess.Write);
        using var writer = new StreamWriter(debugFile);
        story.DebugDump(writer);
    }

    private static async Task<int> RunAsync(CommandLineArguments args)
    {
        ILogger logger = args.JsonOutput
            ? new LSTools.StoryCompiler.JsonLogger()
            : new LSTools.StoryCompiler.ConsoleLogger();

        using var modCompiler = new ModCompiler(logger, args.GameDataPath);
        modCompiler.SetWarningOptions(CommandLineArguments.GetWarningOptions(args.Warnings));
        modCompiler.CheckGameObjects = args.CheckGameObjects;
        modCompiler.CheckOnly = args.CheckOnly;
        modCompiler.LoadPackages = !args.NoPackages;
        modCompiler.AllowTypeCoercion = args.AllowTypeCoercion;
        modCompiler.OsiExtender = args.OsiExtender;

        modCompiler.Game = args.Game switch
        {
            "dos2" => TargetGame.DOS2,
            "dos2de" => TargetGame.DOS2DE,
            "bg3" => TargetGame.BG3,
            _ => throw new ArgumentException("Unsupported target game execution profile configuration.")
        };

        List<string> mods = [.. args.Mods];

        if (!await modCompiler.CompileAsync(args.OutputPath, args.DebugInfoOutputPath, mods))
        {
            return 3;
        }

        if (!string.IsNullOrEmpty(args.DebugLogOutputPath) && !args.CheckOnly)
        {
            DebugDump(args.OutputPath, args.DebugLogOutputPath);
        }

        return 0;
    }

    public static async Task<int> Main(string[] args)
    {
        int exitCode = 0;
        var rootCommand = CommandLineArguments.BuildRootCommand(boundArgs =>
        {
            exitCode = RunAsync(boundArgs).GetAwaiter().GetResult();
        });
        var parseResult = rootCommand.Parse(args);
        var commandLineExitCode = await parseResult.InvokeAsync();
        return commandLineExitCode != 0 ? commandLineExitCode : exitCode;
    }
}
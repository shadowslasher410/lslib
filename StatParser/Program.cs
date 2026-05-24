

namespace StatParser;

internal static class Program
{
    private static int Run(CommandLineArguments args)
    {
        using var statChecker = new StatChecker(args.GameDataPath);
        statChecker.LoadPackages = !args.NoPackages;

        List<string> mods = [.. args.Mods];
        List<string> dependencies = [.. args.Dependencies];
        List<string> packagePaths = [.. args.PackagePaths];

        statChecker.Check(mods, dependencies, packagePaths);
        return 0;
    }

    public static async Task<int> Main(string[] args)
    {
        var rootCommand = CommandLineArguments.BuildRootCommand(boundArgs =>
        {
            var exitCode = Run(boundArgs);
            Environment.Exit(exitCode);
        });
        var parseResult = rootCommand.Parse(args);
        return await parseResult.InvokeAsync();
    }
}

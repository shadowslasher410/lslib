using System.CommandLine;

namespace StatParser;

public class CommandLineArguments
{
    public required bool NoPackages { get; init; }
    public required string[] Mods { get; init; } = [];
    public required string[] Dependencies { get; init; } = [];
    public required string GameDataPath { get; init; } = string.Empty;
    public required string[] PackagePaths { get; init; } = [];

    public static RootCommand BuildRootCommand(Action<CommandLineArguments> executionHandler)
    {
        var noPackagesOpt = new Option<bool>("--no-packages")
        {
            Description = "Don't look for goal files inside packages",
            Required = false
        };

        var modOpt = new Option<string[]>("--mod")
        {
            Description = "Mod to add",
            AllowMultipleArgumentsPerToken = true,
            Arity = ArgumentArity.OneOrMore,
            Required = true
        };

        var dependencyOpt = new Option<string[]>("--dependency")
        {
            Description = "Dependencies to add",
            AllowMultipleArgumentsPerToken = true,
            Arity = ArgumentArity.OneOrMore,
            Required = false,
            DefaultValueFactory = _ => []
        };

        var gameDataPathOpt = new Option<string>("--game-data-path")
        {
            Description = "Game data path",
            Arity = ArgumentArity.ExactlyOne,
            Required = false,
            DefaultValueFactory = _ => string.Empty
        };

        var packagePathsOpt = new Option<string[]>("--package-paths")
        {
            Description = "Additional package path(s)",
            AllowMultipleArgumentsPerToken = true,
            Arity = ArgumentArity.OneOrMore,
            Required = false,
            DefaultValueFactory = _ => []
        };

        var rootCommand = new RootCommand("Osiris Stat Parser Engine Utility")
        {
            noPackagesOpt,
            modOpt,
            dependencyOpt,
            gameDataPathOpt,
            packagePathsOpt
        };

        rootCommand.SetAction(parseResult =>
        {
            var boundArguments = new CommandLineArguments
            {
                NoPackages = parseResult.GetValue(noPackagesOpt),
                Mods = parseResult.GetValue(modOpt) ?? [],
                Dependencies = parseResult.GetValue(dependencyOpt) ?? [],
                GameDataPath = parseResult.GetValue(gameDataPathOpt)!,
                PackagePaths = parseResult.GetValue(packagePathsOpt) ?? []
            };

            executionHandler(boundArguments);
        });

        return rootCommand;
    }
}

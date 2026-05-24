using LSLib.LS.Story.Compiler;
using System.Collections.Frozen;
using System.CommandLine;

namespace LSTools.StoryCompiler;

public class CommandLineArguments
{
    public required string[] Warnings { get; init; } = [];
    public required bool JsonOutput { get; init; }
    public required bool CheckOnly { get; init; }
    public required bool CheckGameObjects { get; init; }
    public required bool NoPackages { get; init; }
    public required string Game { get; init; } = "bg3";
    public required string[] Mods { get; init; } = [];
    public required string GameDataPath { get; init; } = string.Empty;
    public required string OutputPath { get; init; } = "story.div.osi";
    public required string DebugInfoOutputPath { get; init; } = string.Empty;
    public required string DebugLogOutputPath { get; init; } = string.Empty;
    public required bool AllowTypeCoercion { get; init; }
    public required bool OsiExtender { get; init; }

    public static RootCommand BuildRootCommand(Action<CommandLineArguments> executionHandler)
    {
        var noWarnOpt = new Option<string[]>("--no-warn") { Description = "Disable specific warnings", DefaultValueFactory = _ => [], AllowMultipleArgumentsPerToken = true, Arity = ArgumentArity.OneOrMore, Required = false };
        var jsonOpt = new Option<bool>("--json") { Description = "Output results in JSON format", Required = false };
        var checkOnlyOpt = new Option<bool>("--check-only") { Description = "Validity check only, don't generate compiled story file", Required = false };
        var checkNamesOpt = new Option<bool>("--check-names") { Description = "Check validity of game object names (slow!)", Required = false };
        var noPackagesOpt = new Option<bool>("--no-packages") { Description = "Don't look for story files and headers inside packages", Required = false };
        var gameOpt = new Option<string>("--game") { Description = "Which game is the story targeting? (dos2, dos2de, bg3)", DefaultValueFactory = _ => "bg3", Required = false };

        gameOpt.Validators.Add(result =>
        {
            string? value = result.GetValueOrDefault<string>();
            if (value is not ("dos2" or "dos2de" or "bg3"))
            {
                result.AddError($"The variant target game '{value}' is invalid. Choose from: dos2, dos2de, bg3.");
            }
        });

        var modOpt = new Option<string[]>("--mod") { Description = "Mod to add", AllowMultipleArgumentsPerToken = true, Arity = ArgumentArity.OneOrMore, Required = true };
        var dataPathOpt = new Option<string>("--game-data-path") { Description = "Game data path", DefaultValueFactory = _ => string.Empty, Arity = ArgumentArity.ExactlyOne, Required = false };
        var outputOpt = new Option<string>("--output", "-o") { Description = "Output path", DefaultValueFactory = _ => "story.div.osi", Arity = ArgumentArity.ExactlyOne, Required = false };
        var debugInfoOpt = new Option<string>("--debug-info") { Description = "Debugging symbols output path", DefaultValueFactory = _ => string.Empty, Arity = ArgumentArity.ExactlyOne, Required = false };
        var debugLogOpt = new Option<string>("--debug-log") { Description = "Debug log output path", DefaultValueFactory = _ => string.Empty, Arity = ArgumentArity.ExactlyOne, Required = false };
        var coercionOpt = new Option<bool>("--allow-type-coercion") { Description = "Allow \"casting\" between unrelated types", Required = false };
        var extenderOpt = new Option<bool>("--osi-extender") { Description = "Compile using Osiris Extender features", Required = false };

        var rootCommand = new RootCommand("Osiris Story Compiler Engine Utility")
        {
            noWarnOpt, jsonOpt, checkOnlyOpt, checkNamesOpt, noPackagesOpt,
            gameOpt, modOpt, dataPathOpt, outputOpt, debugInfoOpt, debugLogOpt,
            coercionOpt, extenderOpt
        };

        rootCommand.SetAction(parseResult =>
        {
            var boundArguments = new CommandLineArguments
            {
                Warnings = parseResult.GetValue(noWarnOpt) ?? [],
                JsonOutput = parseResult.GetValue(jsonOpt),
                CheckOnly = parseResult.GetValue(checkOnlyOpt),
                CheckGameObjects = parseResult.GetValue(checkNamesOpt),
                NoPackages = parseResult.GetValue(noPackagesOpt),
                Game = parseResult.GetValue(gameOpt)!,
                Mods = parseResult.GetValue(modOpt) ?? [],
                GameDataPath = parseResult.GetValue(dataPathOpt)!,
                OutputPath = parseResult.GetValue(outputOpt)!,
                DebugInfoOutputPath = parseResult.GetValue(debugInfoOpt)!,
                DebugLogOutputPath = parseResult.GetValue(debugLogOpt)!,
                AllowTypeCoercion = parseResult.GetValue(coercionOpt),
                OsiExtender = parseResult.GetValue(extenderOpt)
            };

            executionHandler(boundArguments);
        });

        return rootCommand;
    }

    private static readonly FrozenDictionary<string, string> CodeMaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["alias-mismatch"] = DiagnosticCode.GuidAliasMismatch,
        ["guid-prefix"] = DiagnosticCode.GuidPrefixNotKnown,
        ["string-lt"] = DiagnosticCode.StringLtGtComparison,
        ["rule-naming"] = DiagnosticCode.RuleNamingStyle,
        ["db-naming"] = DiagnosticCode.DbNamingStyle,
        ["unused-db"] = DiagnosticCode.UnusedDatabaseWarning,
        ["unwritten-db"] = DiagnosticCode.UnwrittenDatabase,
        ["unresolved-object"] = DiagnosticCode.UnresolvedGameObjectName,
        ["object-name"] = DiagnosticCode.GameObjectNameMismatch,
        ["object-type"] = DiagnosticCode.GameObjectTypeMismatch
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, bool> GetWarningOptions(string[] options)
    {
        var results = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (options is null) return results;

        foreach (var option in options)
        {
            if (CodeMaps.TryGetValue(option, out var diagnosticCode))
            {
                results[diagnosticCode] = false;
            }
            else
            {
                var originalColor = Console.ForegroundColor;
                try
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"Warning class \"{option}\" does not exist.");
                }
                finally
                {
                    Console.ForegroundColor = originalColor;
                }
            }
        }

        return results;
    }
}
using LSLib.LS;
using LSLib.Parser;
using LSLibStats.Stats;
using System.Xml.Linq;

namespace StatParser;

public sealed class StatChecker(string gameDataPath) : IDisposable
{
    private readonly string _gameDataPath = gameDataPath ?? throw new ArgumentNullException(nameof(gameDataPath));
    private readonly ModResources _mods = new();

    private VFS _fs = null!;
    private StatDefinitionRepository _definitions = null!;
    private StatLoadingContext _context = null!;
    private StatFileParserEngine _fileEngine = null!;

    private readonly PropertyDiagnosticContainer _pipelineErrors = new();

    public bool LoadPackages { get; set; } = true;

    public void Dispose()
    {
        _mods.Dispose();
        _fs?.Dispose();
    }

    private void LoadStats(ModInfo mod)
    {
        if (mod?.Stats is null) return;

        foreach (var file in mod.Stats)
        {
            try
            {
                using var statStream = _fs.Open(file);
                var declarations = _fileEngine.ParseStream(file, statStream);

                if (declarations is not null)
                {
                    RegisterDeclarationsToContext(declarations);
                }
            }
            catch (Exception ex)
            {
                _pipelineErrors.Add($"Critical I/O or syntax crash tracing mod source file asset block: {ex.Message}", new CodeLocation(file, 1, 1, 1, 1));
            }
        }
    }

    private XDocument? LoadXml(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            using var stream = _fs.Open(path);
            return XDocument.Load(stream);
        }
        catch (Exception ex)
        {
            _pipelineErrors.Add($"Failed to fetch target structural XML blueprint configuration stream via virtual file system mapping: {ex.Message}", new CodeLocation(path ?? string.Empty, 1, 1, 1, 1));
            return null;
        }
    }

    private void LoadGuidResources(ModInfo mod)
    {
        if (LoadXml(mod.ActionResourcesFile) is { } actionResources)
        {
            var resources = actionResources.Descendants("ActionResource");
            foreach (var res in resources)
            {
                var nameAttr = res.Attribute("Name")?.Value;
                var guidAttr = res.Attribute("UUID")?.Value;
                if (!string.IsNullOrEmpty(nameAttr) && !string.IsNullOrEmpty(guidAttr))
                {
                }
            }
        }

        if (LoadXml(mod.ActionResourceGroupsFile) is { } actionResourceGroups)
        {
            var groups = actionResourceGroups.Descendants("ActionResourceGroup");
            foreach (var grp in groups)
            {
                var nameAttr = grp.Attribute("Name")?.Value;
                var guidAttr = grp.Attribute("UUID")?.Value;
                if (!string.IsNullOrEmpty(nameAttr) && !string.IsNullOrEmpty(guidAttr))
                {
                }
            }
        }
    }

    private void LoadMod(string modName)
    {
        if (!_mods.Mods.TryGetValue(modName, out var mod))
        {
            throw new KeyNotFoundException($"Target statistics mod profiling definition not found: {modName}");
        }

        LoadStats(mod);
        LoadGuidResources(mod);
    }

    private void LoadStatDefinitions(ModResources resources)
    {
        _definitions = new StatDefinitionRepository();

        if (resources.Mods.TryGetValue("Shared", out var sharedMod))
        {
            if (!string.IsNullOrEmpty(sharedMod.ValueListsFile))
            {
                using var enumStream = _fs.Open(sharedMod.ValueListsFile);
                StatEnumerationParser.Parse(enumStream, _definitions.Enumerations);
            }

            if (!string.IsNullOrEmpty(sharedMod.ModifiersFile))
            {
                using var defStream = _fs.Open(sharedMod.ModifiersFile);
                StatEntryTypeParser.Parse(defStream, _definitions.Types);
            }
        }

        string definitionConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LSLibDefinitions.xml");
        if (File.Exists(definitionConfigPath))
        {
            using var localDefinitionsStream = new FileStream(definitionConfigPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            StatFunctorParser.Parse(localDefinitionsStream, _definitions.Functors, _definitions.Boosts, _definitions.DescriptionParams);
        }
    }

    private static void LogCompilationDiagnostic(PropertyDiagnostic diagnostic)
    {
        var originalColor = Console.ForegroundColor;
        try
        {
            bool isError = diagnostic.Message.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                           diagnostic.Message.Contains("violation", StringComparison.OrdinalIgnoreCase);

            Console.ForegroundColor = isError ? ConsoleColor.Red : ConsoleColor.DarkYellow;
            Console.Error.Write(isError ? "[ERROR] " : "[WARN]  ");

            if (diagnostic.Location is not null)
            {
                var baseName = Path.GetFileName(diagnostic.Location.FileName);
                Console.Error.Write($"{baseName}:{diagnostic.Location.StartLine}:{diagnostic.Location.StartColumn} - ");
            }

            Console.Error.WriteLine(diagnostic.Message);

            if (diagnostic.Contexts is not null && diagnostic.Contexts.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                foreach (var ctx in diagnostic.Contexts)
                {
                    Console.Error.WriteLine($"   -> Trace Scope Location: {ctx.Type} | Target Component: '{ctx.Context}'");
                }
            }
        }
        finally
        {
            Console.ForegroundColor = originalColor;
        }
    }

    private void RegisterDeclarationsToContext(List<StatDeclaration> declarations)
    {
        foreach (var declaration in declarations)
        {
            if (!declaration.Properties.TryGetValue("EntityType", out var entityTypeProp)) continue;
            var statType = entityTypeProp.Value.ToString()!;

            if (!_context.DeclarationsByType.TryGetValue(statType, out var declarationsByType))
            {
                declarationsByType = new Dictionary<string, StatDeclaration>(StringComparer.Ordinal);
                _context.DeclarationsByType[statType] = declarationsByType;
            }

            string assetName = declaration.Name;
            if (string.IsNullOrEmpty(assetName) && declaration.Properties.TryGetValue("ItemColorName", out var colorNameProp))
            {
                assetName = colorNameProp.Value.ToString()!;
            }

            if (!string.IsNullOrEmpty(assetName))
            {
                declarationsByType[assetName] = declaration;
            }
        }
    }

    public void Check(IReadOnlyList<string> mods, IReadOnlyList<string> dependencies, IReadOnlyList<string> packagePaths)
    {
        _fs?.Dispose();
        _fs = new VFS();

        if (LoadPackages)
        {
            _fs.AttachGameDirectory(_gameDataPath);
        }
        else
        {
            _fs.AttachRoot(_gameDataPath);
        }

        foreach (var path in packagePaths)
        {
            _fs.AttachPackage(path);
        }
        _fs.FinishBuild();

        var visitor = new ModPathVisitor(_mods, _fs)
        {
            Game = LSLib.LS.Story.Compiler.TargetGame.DOS2DE,
            CollectStats = true,
            CollectGuidResources = true
        };
        visitor.Discover();

        LoadStatDefinitions(visitor.Resources);

        _context = new StatLoadingContext
        {
            Definitions = _definitions,
            DeclarationsByType = new Dictionary<string, Dictionary<string, StatDeclaration>>(StringComparer.Ordinal)
        };

        _fileEngine = new StatFileParserEngine(_context);

        foreach (var modName in dependencies)
        {
            LoadMod(modName);
        }

        foreach (var modName in mods)
        {
            LoadMod(modName);
        }

        var factory = new StatValueValidatorFactory(null!, null!);
        foreach (var (typeName, entriesMap) in _context.DeclarationsByType)
        {
            if (!_definitions.Types.TryGetValue(typeName, out var entryTypeSchema)) continue;

            foreach (var (entryName, declaration) in entriesMap)
            {
                var diagCtx = new DiagnosticContext { CurrentDeclaration = declaration };

                foreach (var (fieldKey, statProperty) in declaration.Properties)
                {
                    if (!entryTypeSchema.Fields.TryGetValue(fieldKey, out var fieldDefinition)) continue;

                    diagCtx.PropertyValueSpan = statProperty.ValueLocation;
                    var fieldValidator = fieldDefinition.GetValidator(factory, _definitions);

                    fieldValidator.Validate(diagCtx, statProperty.ValueLocation, statProperty.Value, _pipelineErrors);

                    if (!_pipelineErrors.Empty)
                    {
                        _pipelineErrors.AddContext(PropertyDiagnosticContextType.Property, fieldKey, statProperty.Location);
                        _pipelineErrors.AddContext(PropertyDiagnosticContextType.Entry, entryName, declaration.Location);
                    }
                }
            }
        }

        if (_pipelineErrors.Messages is not null)
        {
            foreach (var diagnostic in _pipelineErrors.Messages)
            {
                LogCompilationDiagnostic(diagnostic);
            }
        }

        Console.WriteLine($"Osiris Stat Compilation Phase Finished. Total Flagged Diagnostics: {_pipelineErrors.Messages?.Count ?? 0}");
    }
}

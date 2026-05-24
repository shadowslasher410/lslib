using LSLib.LS;
using LSLib.LS.Resources.LSF;
using LSLib.LS.Story;
using LSLib.LS.Story.Compiler;
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace LSTools.StoryCompiler;

public static class CompilerDiagnosticCodes
{
    public const string MissingTemplates = "X01";
    public const string MissingGameObjects = "X02";
    public const string UnknownGameObjectType = "X03";
}

public sealed partial class ModCompiler(ILogger logger, string gameDataPath) : IDisposable
{
    private sealed class GoalScript
    {
        public string Name { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public byte[] ScriptBody { get; set; } = [];
    }

    [GeneratedRegex(@"^([a-zA-Z0-9_]+)\s+([0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex OrphanQueryRegex();

    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly string _gameDataPath = gameDataPath ?? throw new ArgumentNullException(nameof(gameDataPath));
    private readonly Compiler _compiler = new();
    private readonly ModResources _mods = new();
    private readonly List<GoalScript> _goalScripts = [];
    private readonly List<byte[]> _gameObjectLSFs = [];

    private readonly HashSet<string> _typeCoercionWhitelist = [];

    private VFS _fs = null!;
    private int _hasErrors;

    public bool CheckOnly { get; set; }
    public bool CheckGameObjects { get; set; }
    public bool LoadPackages { get; set; } = true;
    public bool AllowTypeCoercion { get; set; }
    public bool OsiExtender { get; set; }
    public TargetGame Game { get; set; } = TargetGame.DOS2;

    public bool HasErrors => Volatile.Read(ref _hasErrors) == 1;

    public void Dispose()
    {
        _mods.Dispose();
        (_fs as IDisposable)?.Dispose();
    }

    private void LoadStoryHeaders(Stream stream)
    {
        var declarations = StoryHeaderLoader.ParseHeader(stream)
            ?? throw new InvalidDataException("Failed to parse story header file data content.");

        var hdrLoader = new StoryHeaderLoader(_compiler.Context);
        hdrLoader.LoadHeader(declarations);
    }


    private void LoadTypeCoercionWhitelist(Stream stream)
    {
        _typeCoercionWhitelist.Clear();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (reader.ReadLine() is { } line)
        {
            var func = line.Trim();
            if (func.Length > 0)
            {
                _typeCoercionWhitelist.Add(func);
            }
        }
    }

    public void SetWarningOptions(Dictionary<string, bool> options)
    {
        if (options is null) return;

        foreach (var (key, value) in options)
        {
            _compiler.Context.Log.WarningSwitches[key] = value;
        }
    }

    private async Task<List<IRGoal>> ParallelBuildIRAsync()
    {
        var concurrentIRs = new ConcurrentQueue<IRGoal>();

        await Parallel.ForEachAsync(_goalScripts, async (script, cancellationToken) =>
        {
            var goalLoader = new IRGenerator(_compiler.Context);
            using var stream = new MemoryStream(script.ScriptBody);
            var ast = goalLoader.ParseGoal(script.Path, stream);

            if (ast is not null)
            {
                var ir = goalLoader.GenerateGoalIR(ast);
                ir.Name = script.Name;
                concurrentIRs.Enqueue(ir);
            }
            else
            {
                var msg = new Diagnostic(goalLoader.LastLocation, MessageLevel.Error, "X00", $"Could not parse goal file {script.Name}");

                lock (_logger)
                {
                    _logger.CompilationDiagnostic(msg);
                }

                Interlocked.Exchange(ref _hasErrors, 1);
            }

            await Task.CompletedTask;
        });

        var sorted = new SortedDictionary<string, IRGoal>(StringComparer.Ordinal);
        while (concurrentIRs.TryDequeue(out var goal))
        {
            sorted[goal.Name] = goal;
        }

        return [.. sorted.Values];
    }

    private async Task ParallelPreprocessAsync()
    {
        await Parallel.ForEachAsync(_goalScripts, (script, cancellationToken) =>
        {
            var scriptText = Encoding.UTF8.GetString(script.ScriptBody);

            if (Preprocessor.Preprocess(scriptText, out string? preprocessed) && preprocessed is not null)
            {
                script.ScriptBody = Encoding.UTF8.GetBytes(preprocessed);
            }

            return ValueTask.CompletedTask;
        });
    }



    private void LoadGameObjects(Resource resource)
    {
        if (!resource.Regions.TryGetValue("Templates", out var templates))
        {
            LogCompilerError(CompilerDiagnosticCodes.MissingTemplates, "Critical structural error: Failed to find required 'Templates' region node within the game asset resource metadata definition.");
            return;
        }

        if (!templates.Children.TryGetValue("GameObjects", out var gameObjects))
        {
            LogCompilerError(CompilerDiagnosticCodes.MissingGameObjects, "Structural validation error: Resource templates do not contain any defined 'GameObjects' child layout data collections.");
            return;
        }

        foreach (var gameObject in gameObjects)
        {
            if (gameObject.Attributes.TryGetValue("MapKey", out var objectGuid) &&
                gameObject.Attributes.TryGetValue("Name", out var objectName) &&
                gameObject.Attributes.TryGetValue("Type", out var objectType))
            {
                // Fixed: Safely extract and assert values are non-null strings
                if (objectGuid.Value is not string guidStr ||
                    objectName.Value is not string nameStr ||
                    objectType.Value is not string typeRaw)
                {
                    continue;
                }

                var typeString = typeRaw.ToLowerInvariant();

                LSLib.LS.Story.Compiler.ValueType? type = typeString switch
                {
                    "item" => _compiler.Context.LookupType("ITEMGUID"),
                    "character" => _compiler.Context.LookupType("CHARACTERGUID"),
                    "trigger" => _compiler.Context.LookupType("TRIGGERGUID"),
                    _ => null
                };

                if (type is not null)
                {
                    // Fixed: Assured key and properties are non-nullable strings
                    _compiler.Context.GameObjects[guidStr] = new GameObjectInfo
                    {
                        Name = $"{nameStr}_{guidStr}",
                        Type = type
                    };
                }
                else
                {
                    // Fixed: Stripped out nullable warning traps from message interpolation
                    var warnMsg = new Diagnostic(
                        location: null,
                        level: MessageLevel.Warning,
                        code: CompilerDiagnosticCodes.UnknownGameObjectType,
                        message: $"Ignored non-compiled asset: Game object key '{guidStr}' contains an unmappable story GUID layout type mapping signature: '{typeRaw}'."
                    );

                    lock (_logger)
                    {
                        _logger.CompilationDiagnostic(warnMsg);
                    }
                }
            }
        }
    }


    private void LogCompilerError(string code, string message)
    {
        var msg = new Diagnostic(location: null, level: MessageLevel.Error, code: code, message: message);
        lock (_logger)
        {
            _logger.CompilationDiagnostic(msg);
        }
        Interlocked.Exchange(ref _hasErrors, 1);
    }

    private void LoadGlobals()
    {
        foreach (var lsf in _gameObjectLSFs)
        {
            using var stream = new MemoryStream(lsf);
            using var reader = new LSFReader(stream);
            LoadGameObjects(reader.Read());
        }
    }

    private void LoadGoals(ModInfo mod)
    {
        if (mod?.Scripts is null) return;

        foreach (var file in mod.Scripts)
        {
            using var scriptStream = _fs.Open(file);
            using var reader = new BinaryReader(scriptStream);

            _goalScripts.Add(new GoalScript
            {
                Name = Path.GetFileNameWithoutExtension(file),
                Path = file,
                ScriptBody = reader.ReadBytes((int)scriptStream.Length)
            });
        }
    }

    private void LoadOrphanQueryIgnores(ModInfo mod)
    {
        if (mod.OrphanQueryIgnoreList is null) return;

        using var ignoreStream = _fs.Open(mod.OrphanQueryIgnoreList);
        using var reader = new StreamReader(ignoreStream, Encoding.UTF8);

        while (reader.ReadLine() is { } ignoreLine)
        {
            var match = OrphanQueryRegex().Match(ignoreLine);
            if (match.Success)
            {
                var signature = new FunctionNameAndArity(
                    match.Groups[1].Value, int.Parse(match.Groups[2].Value));
                _compiler.IgnoreUnusedDatabases.Add(signature);
            }
        }
    }

    private void LoadGameObjects(ModInfo mod)
    {
        foreach (var file in mod.Globals)
        {
            using var globalStream = _fs.Open(file);
            using var reader = new BinaryReader(globalStream);
            _gameObjectLSFs.Add(reader.ReadBytes((int)globalStream.Length));
        }

        foreach (var file in mod.LevelObjects)
        {
            using var objectStream = _fs.Open(file);
            using var reader = new BinaryReader(objectStream);
            _gameObjectLSFs.Add(reader.ReadBytes((int)objectStream.Length));
        }
    }

    private void LoadMod(string modName)
    {
        if (!_mods.Mods.TryGetValue(modName, out var mod))
        {
            throw new KeyNotFoundException($"Target mod metadata profile not found in solution context: {modName}");
        }

        LoadGoals(mod);
        LoadOrphanQueryIgnores(mod);

        if (CheckGameObjects)
        {
            LoadGameObjects(mod);
        }
    }

    public async Task<bool> CompileAsync(string outputPath, string? debugInfoPath, List<string> mods)
    {
        _logger.CompilationStarted();
        Interlocked.Exchange(ref _hasErrors, 0);
        _compiler.Game = Game;
        _compiler.AllowTypeCoercion = AllowTypeCoercion;

        if (mods.Count > 0)
        {
            _logger.TaskStarted("Building VFS");

            (_fs as IDisposable)?.Dispose();

            _fs = new VFS();
            if (LoadPackages)
            {
                _fs.AttachGameDirectory(_gameDataPath);
            }
            else
            {
                _fs.AttachRoot(_gameDataPath);
            }
            _fs.FinishBuild();

            _logger.TaskStarted("Discovering module files");
            var visitor = new ModPathVisitor(_mods, _fs)
            {
                Game = Game,
                CollectStoryGoals = true,
                CollectGlobals = CheckGameObjects,
                CollectLevels = CheckGameObjects
            };
            visitor.Discover();
            _logger.TaskFinished();

            _logger.TaskStarted("Loading module files");
            if (CheckGameObjects)
            {
                var guidType = _compiler.Context.LookupType("GUIDSTRING") 
                    ?? throw new InvalidDataException("Context missing core system type mapping: 'GUIDSTRING'");

                var nullGameObject = new GameObjectInfo
                {
                    Name = "NULL_00000000-0000-0000-0000-000000000000",
                    Type = guidType
                };
                _compiler.Context.GameObjects.Add("00000000-0000-0000-0000-000000000000", nullGameObject);
            }

            foreach (var modName in mods)
            {
                LoadMod(modName);
            }

            string? storyHeaderFile = null;
            string? typeCoercionWhitelistFile = null;

            foreach (var modName in mods.AsEnumerable().Reverse())
            {
                storyHeaderFile ??= _mods.Mods[modName].StoryHeaderFile;
                typeCoercionWhitelistFile ??= _mods.Mods[modName].TypeCoercionWhitelistFile;
            }

            if (storyHeaderFile is not null)
            {
                using var storyStream = _fs.Open(storyHeaderFile);
                LoadStoryHeaders(storyStream);
            }
            else
            {
                _logger.CompilationDiagnostic(new Diagnostic(null, MessageLevel.Error, "X00", "Unable to locate story header file (story_header.div)"));
                Interlocked.Exchange(ref _hasErrors, 1);
            }

            if (typeCoercionWhitelistFile is not null)
            {
                using var typeCoercionStream = _fs.Open(typeCoercionWhitelistFile);
                LoadTypeCoercionWhitelist(typeCoercionStream);
                _compiler.TypeCoercionWhitelist = _typeCoercionWhitelist;
            }

            _logger.TaskFinished();
        }

        if (CheckGameObjects)
        {
            _logger.TaskStarted("Loading game objects");
            LoadGlobals();
            _logger.TaskFinished();
        }
        else
        {
            _compiler.Context.Log.WarningSwitches[DiagnosticCode.UnresolvedGameObjectName] = false;
        }

        if (OsiExtender)
        {
            _logger.TaskStarted("Precompiling scripts");
            await ParallelPreprocessAsync();
            _logger.TaskFinished();
        }

            _logger.TaskStarted("Generating IR");
            var orderedGoalAsts = await ParallelBuildIRAsync();
            foreach (var goal in orderedGoalAsts)
            {
                _compiler.AddGoal(goal);
            }
            _logger.TaskFinished();

            var compilerGoalsList = orderedGoalAsts;
            var iter = 1;

            bool updated;
            do
            {
                _logger.TaskStarted($"Propagating rule types {iter}");
                updated = _compiler.PropagateRuleTypes(compilerGoalsList);
                _logger.TaskFinished();

                if (iter++ > 10)
                {
                    _compiler.Context.Log.Error(null, DiagnosticCode.InternalError, "Maximal number of rule propagation retries exceeded");
                    break;
                }
            } while (updated);

            _logger.TaskStarted("Checking for unresolved references");
            _compiler.VerifyIR(compilerGoalsList);
            _logger.TaskFinished();

            foreach (var message in _compiler.Context.Log.Log)
        {
            _logger.CompilationDiagnostic(message);
            if (message.Level == MessageLevel.Error)
            {
                Interlocked.Exchange(ref _hasErrors, 1);
            }
        }

        if (!HasErrors && !CheckOnly)
        {
            _logger.TaskStarted("Generating story nodes");
            var emitter = new StoryEmitter(_compiler.Context);
            if (debugInfoPath is not null)
            {
                emitter.EnableDebugInfo();
            }

            var story = emitter.EmitStory();
            _logger.TaskFinished();

            _logger.TaskStarted("Saving story binary");
            using (var file = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                StoryWriter.Write(file, story, false);
            }
            _logger.TaskFinished();

            if (debugInfoPath is not null)
            {
                _logger.TaskStarted("Saving debug info");
            
                if (emitter.DebugInfo is { } validDebugInfo)
                {
                    using var file = new FileStream(debugInfoPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    DebugInfoSaver.Save(file, validDebugInfo);
                }
                else
                {
                    _logger.CompilationDiagnostic(new Diagnostic(null, MessageLevel.Warning, "X01", "Debug info path requested but emitter did not provide debug info data."));
                }
                _logger.TaskFinished();
            }
        }

        _logger.CompilationFinished(!HasErrors);
        return !HasErrors;
    }

}
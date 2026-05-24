using Google.Protobuf;
using LSLib.LS;
using LSLib.LS.Story.Compiler;
using LSTools.StoryCompiler;
using LSLib.Parser;
using NSubstitute;
using System.CommandLine;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Xunit;

namespace LSTools.Tests;

public class CommandLineArgumentsTests
{
    [Fact]
    public void BuildRootCommand_ValidArguments_BindsPropertyValuesCorrectly()
    {
       CommandLineArguments? boundResult = null;
        var rootCommand = CommandLineArguments.BuildRootCommand(args => boundResult = args);

        string[] rawArgs = [
            "--mod", "SharedMod", "EngineMod",
            "--game", "dos2de",
            "--check-only",
            "--no-warn", "alias-mismatch", "unused-db",
            "--output", "C:\\Output\\story.osi"
        ];

        var parseResult = rootCommand.Parse(rawArgs);
        var exitCode = parseResult.Invoke();

        Assert.Equal(0, exitCode);
        Assert.NotNull(boundResult);
        Assert.Equal("dos2de", boundResult.Game);
        Assert.True(boundResult.CheckOnly);
        Assert.Equal("C:\\Output\\story.osi", boundResult.OutputPath);
        Assert.Equal(new[] { "SharedMod", "EngineMod" }, boundResult.Mods);
        Assert.Equal(new[] { "alias-mismatch", "unused-db" }, boundResult.Warnings);

        Assert.False(boundResult.JsonOutput);
        Assert.Empty(boundResult.GameDataPath);
    }

    [Theory]
    [InlineData("dos3")]
    [InlineData("bg4")]
    [InlineData("invalidGame")]
    public void BuildRootCommand_InvalidGameOption_TriggersCustomValidatorError(string invalidGameName)
    {
        var rootCommand = CommandLineArguments.BuildRootCommand(_ => { });
        string[] rawArgs = ["--mod", "Core", "--game", invalidGameName];

        var parseResult = rootCommand.Parse(rawArgs);

        Assert.NotEmpty(parseResult.Errors);
        var expectedMessage = $"The variant target game '{invalidGameName}' is invalid. Choose from: dos2, dos2de, bg3.";
        Assert.Contains(parseResult.Errors, e => e.Message == expectedMessage);
    }

    [Fact]
    public void BuildRootCommand_MissingRequiredModOption_FailsValidationWithMissingMessage()
    {
        var rootCommand = CommandLineArguments.BuildRootCommand(_ => { });
        string[] rawArgs = ["--game", "bg3", "--check-only"];

        var parseResult = rootCommand.Parse(rawArgs);

        Assert.NotEmpty(parseResult.Errors);
        Assert.Contains(parseResult.Errors, e => e.Message.Contains("Option '--mod' is required"));
    }

    [Fact]
    public void GetWarningOptions_ValidWarningStrings_MapsToCorrectDiagnosticCodes()
    {
        string[] inputWarnings = ["alias-mismatch", "unused-db", "object-type"];

        var warningMap = CommandLineArguments.GetWarningOptions(inputWarnings);

        Assert.NotNull(warningMap);
        Assert.Equal(3, warningMap.Count);

        Assert.False(warningMap[DiagnosticCode.GuidAliasMismatch]);
        Assert.False(warningMap[DiagnosticCode.UnusedDatabaseWarning]);
        Assert.False(warningMap[DiagnosticCode.GameObjectTypeMismatch]);
    }

    [Fact]
    public void GetWarningOptions_UnrecognizedWarningClass_ReturnsEmptyMappingsAndWritesToConsole()
    {
        string[] invalidInput = ["unknown-warning-flag"];
        using var stringWriter = new StringWriter();
        var originalConsoleOut = Console.Out;
        Console.SetOut(stringWriter);

        try
        {
            var warningMap = CommandLineArguments.GetWarningOptions(invalidInput);

            // Assert
            Assert.Empty(warningMap);
            Assert.Contains("Warning class \"unknown-warning-flag\" does not exist.", stringWriter.ToString());
        }
        finally
        {
            Console.SetOut(originalConsoleOut);
        }
    }
}
public class DebugInfoSaver
{
    private static DatabaseDebugInfoMsg ToProtobuf(DatabaseDebugInfo debugInfo)
    {
        var msg = new DatabaseDebugInfoMsg
        {
            Id = debugInfo.Id,
            Name = debugInfo.Name
        };
        msg.ParamTypes.AddRange(debugInfo.ParamTypes);
        return msg;
    }

    private static GoalDebugInfoMsg ToProtobuf(GoalDebugInfo debugInfo)
    {
        var msg = new GoalDebugInfoMsg
        {
            Id = debugInfo.Id,
            Name = debugInfo.Name,
            Path = debugInfo.Path
        };

        msg.InitActions.AddRange(debugInfo.InitActions.Select(ToProtobuf));
        msg.ExitActions.AddRange(debugInfo.ExitActions.Select(ToProtobuf));
        return msg;
    }

    private static RuleVariableDebugInfoMsg ToProtobuf(RuleVariableDebugInfo debugInfo) => new()
    {
        Index = debugInfo.Index,
        Name = debugInfo.Name,
        Type = debugInfo.Type,
        Unused = debugInfo.Unused
    };

    private static ActionDebugInfoMsg ToProtobuf(ActionDebugInfo debugInfo) => new()
    {
        Line = debugInfo.Line
    };

    private static RuleDebugInfoMsg ToProtobuf(RuleDebugInfo debugInfo)
    {
        var msg = new RuleDebugInfoMsg
        {
            Id = debugInfo.Id,
            GoalId = debugInfo.GoalId,
            Name = debugInfo.Name,
            ConditionsStartLine = debugInfo.ConditionsStartLine,
            ConditionsEndLine = debugInfo.ConditionsEndLine,
            ActionsStartLine = debugInfo.ActionsStartLine,
            ActionsEndLine = debugInfo.ActionsEndLine
        };

        msg.Variables.AddRange(debugInfo.Variables.Select(ToProtobuf));
        msg.Actions.AddRange(debugInfo.Actions.Select(ToProtobuf));
        return msg;
    }

    private static NodeDebugInfoMsg ToProtobuf(NodeDebugInfo debugInfo)
    {
        var msg = new NodeDebugInfoMsg
        {
            Id = debugInfo.Id,
            RuleId = debugInfo.RuleId,
            Line = (uint)debugInfo.Line,
            DatabaseId = debugInfo.DatabaseId,
            Name = debugInfo.Name,
            Type = (NodeDebugInfoMsg.Types.NodeType)debugInfo.Type,
            ParentNodeId = debugInfo.ParentNodeId,
            FunctionName = debugInfo.FunctionName?.Name ?? string.Empty,
            FunctionArity = debugInfo.FunctionName is not null ? (uint)debugInfo.FunctionName.Arity : 0
        };

        foreach (var (key, value) in debugInfo.ColumnToVariableMaps)
        {
            msg.ColumnMaps.Add((uint)key, (uint)value);
        }

        return msg;
    }

    private static FunctionParamDebugInfoMsg ToProtobuf(FunctionParamDebugInfo debugInfo) => new()
    {
        TypeId = debugInfo.TypeId,
        Name = debugInfo.Name ?? string.Empty,
        Out = debugInfo.Out
    };

    private static FunctionDebugInfoMsg ToProtobuf(FunctionDebugInfo debugInfo)
    {
        var msg = new FunctionDebugInfoMsg
        {
            Name = debugInfo.Name,
            TypeId = debugInfo.TypeId
        };

        msg.Params.AddRange(debugInfo.Params.Select(ToProtobuf));
        return msg;
    }

    private static StoryDebugInfoMsg ToProtobuf(StoryDebugInfo debugInfo)
    {
        var msg = new StoryDebugInfoMsg { Version = debugInfo.Version };

        msg.Databases.AddRange(debugInfo.Databases.Values.Select(ToProtobuf));
        msg.Goals.AddRange(debugInfo.Goals.Values.Select(ToProtobuf));
        msg.Rules.AddRange(debugInfo.Rules.Values.Select(ToProtobuf));
        msg.Nodes.AddRange(debugInfo.Nodes.Values.Select(ToProtobuf));
        msg.Functions.AddRange(debugInfo.Functions.Values.Select(ToProtobuf));

        return msg;
    }

    public static void Save(Stream stream, StoryDebugInfo debugInfo)
    {
        var msg = ToProtobuf(debugInfo);

        using var memoryStream = new MemoryStream();
        msg.WriteTo(memoryStream);
        byte[] proto = memoryStream.ToArray();

        var flags = CompressionHelpers.MakeCompressionFlags(CompressionMethod.LZ4, LSCompressionLevel.Fast);
        byte[] compressed = CompressionHelpers.Compress(proto, flags);

        stream.Write(compressed.AsSpan());

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((uint)proto.Length);
    }
}


public class DebugInfoSaverTests
{
    [Fact]
    public void Save_ValidDebugInfoTree_SerializesCorrectlyWithLZ4CompressionAndLengthTrailer()
    {
        var mockDebugInfo = new StoryDebugInfo
        {
            Version = 6,
            Databases = new Dictionary<uint, DatabaseDebugInfo>
            {
                { 101, new DatabaseDebugInfo { Id = 101, Name = "DB_CampFlags", ParamTypes = [1, 3] } }
            },
            Goals = new Dictionary<uint, GoalDebugInfo>
            {
                { 1, new GoalDebugInfo
                    {
                        Id = 1,
                        Name = "GL_MainStory",
                        Path = "Story/Main.txt",
                        InitActions = [ new ActionDebugInfo { Line = 42 } ],
                        ExitActions = []
                    }
                }
            },
            Rules = new Dictionary<uint, RuleDebugInfo>
            {
                { 50, new RuleDebugInfo
                    {
                        Id = 50,
                        GoalId = 1,
                        Name = "Rule_OnItemPickup",
                        ConditionsStartLine = 10,
                        ConditionsEndLine = 15,
                        ActionsStartLine = 16,
                        ActionsEndLine = 20,
                        Variables = [ new RuleVariableDebugInfo { Index = 0, Name = "_Item", Type = 2, Unused = false } ],
                        Actions = [ new ActionDebugInfo { Line = 17 } ]
                    }
                }
            },
            Nodes = new Dictionary<uint, NodeDebugInfo>
            {
                { 999, new NodeDebugInfo
                    {
                        Id = 999,
                        RuleId = 50,
                        Line = 12,
                        DatabaseId = 101,
                        Name = "Node_Trigger",
                        Type = NodeDebugInfo.NodeType.Rule,
                        ParentNodeId = 0,
                        FunctionName = null,
                        ColumnToVariableMaps = new Dictionary<int, int> { { 1, 10 }, { 2, 20 } }
                    }
                }
            },
            Functions = new Dictionary<string, FunctionDebugInfo>()
        };

        using var outputStream = new MemoryStream();

        DebugInfoSaver.Save(outputStream, mockDebugInfo);
        byte[] outputBuffer = outputStream.ToArray();

        Assert.True(outputBuffer.Length > sizeof(uint), "Output buffer must be larger than the uint trailer sizing.");

        int lengthTrailerOffset = outputBuffer.Length - sizeof(uint);
        uint uncompressedSize = BitConverter.ToUInt32(outputBuffer, lengthTrailerOffset);
        Assert.True(uncompressedSize > 0, "Trailer must report a positive uncompressed size integer token.");

        byte[] compressedDataOnly = outputBuffer[..lengthTrailerOffset];
        byte[] decompressedProtoBytes = CompressionHelpers.Decompress(compressedDataOnly, (int)uncompressedSize);

        var deserializedMsg = StoryDebugInfoMsg.Parser.ParseFrom(decompressedProtoBytes);

        Assert.Equal(6u, deserializedMsg.Version);

        var databaseMsg = Assert.Single(deserializedMsg.Databases);
        Assert.Equal(101u, databaseMsg.Id);
        Assert.Equal("DB_CampFlags", databaseMsg.Name);
        Assert.Equal(new uint[] { 1, 3 }, databaseMsg.ParamTypes);

        var goalMsg = Assert.Single(deserializedMsg.Goals);
        Assert.Equal("GL_MainStory", goalMsg.Name);
        Assert.Equal("Story/Main.txt", goalMsg.Path);
        var initActionMsg = Assert.Single(goalMsg.InitActions);
        Assert.Equal(42u, initActionMsg.Line);

        var ruleMsg = Assert.Single(deserializedMsg.Rules);
        Assert.Equal("Rule_OnItemPickup", ruleMsg.Name);
        Assert.Equal(10u, ruleMsg.ConditionsStartLine);
        var varMsg = Assert.Single(ruleMsg.Variables);
        Assert.Equal("_Item", varMsg.Name);

        var nodeMsg = Assert.Single(deserializedMsg.Nodes);
        Assert.Equal("Node_Trigger", nodeMsg.Name);
        Assert.Equal(string.Empty, nodeMsg.FunctionName);
        Assert.Equal(2, nodeMsg.ColumnMaps.Count);
        Assert.Equal(10u, nodeMsg.ColumnMaps[1]);
        Assert.Equal(20u, nodeMsg.ColumnMaps[2]);
    }

    [Fact]
    public void Save_EmptyDebugInfoContext_GeneratesMinimumSizeCompressedPayloadWithVersionOnly()
    {
        var emptyDebugInfo = new StoryDebugInfo
        {
            Version = 1,
            Databases = [],
            Goals = [],
            Rules = [],
            Nodes = [],
            Functions = []
        };

        using var outputStream = new MemoryStream();

        DebugInfoSaver.Save(outputStream, emptyDebugInfo);
        byte[] outputBuffer = outputStream.ToArray();

        int lengthTrailerOffset = outputBuffer.Length - sizeof(uint);
        uint uncompressedSize = BitConverter.ToUInt32(outputBuffer, lengthTrailerOffset);

        byte[] compressedDataOnly = outputBuffer[..lengthTrailerOffset];
        byte[] decompressedProtoBytes = CompressionHelpers.Decompress(compressedDataOnly, (int)uncompressedSize);
        var deserializedMsg = StoryDebugInfoMsg.Parser.ParseFrom(decompressedProtoBytes);

        Assert.Equal(1u, deserializedMsg.Version);
        Assert.Empty(deserializedMsg.Databases);
        Assert.Empty(deserializedMsg.Goals);
        Assert.Empty(deserializedMsg.Rules);
        Assert.Empty(deserializedMsg.Nodes);
        Assert.Empty(deserializedMsg.Functions);
    }
}

public class DebugInfoLoaderTests
{
    [Fact]
    public void Load_ValidSavedStream_SuccessfullyRestoresFullObjectGraph()
    {
        var sourceInfo = new StoryDebugInfo
        {
            Version = 6,
            Databases = new Dictionary<uint, DatabaseDebugInfo>
            {
                { 42, new DatabaseDebugInfo { Id = 42, Name = "DB_PlayerOrigin", ParamTypes = [5, 9] } }
            },
            Goals = new Dictionary<uint, GoalDebugInfo>
            {
                { 12, new GoalDebugInfo
                    {
                        Id = 12,
                        Name = "GL_Act1_Camp",
                        Path = "Mods/Story/Act1.txt",
                        InitActions = [ new ActionDebugInfo { Line = 100 } ],
                        ExitActions = [ new ActionDebugInfo { Line = 200 } ]
                    }
                }
            },
            Rules = new Dictionary<uint, RuleDebugInfo>
            {
                { 77, new RuleDebugInfo
                    {
                        Id = 77,
                        GoalId = 12,
                        Name = "Rule_OnCampRest",
                        ConditionsStartLine = 50,
                        ConditionsEndLine = 55,
                        ActionsStartLine = 56,
                        ActionsEndLine = 60,
                        Variables = [ new RuleVariableDebugInfo { Index = 0, Name = "_Player", Type = 1, Unused = false } ],
                        Actions = [ new ActionDebugInfo { Line = 57 } ]
                    }
                }
            },
            Nodes = new Dictionary<uint, NodeDebugInfo>
            {
                { 5, new NodeDebugInfo
                    {
                        Id = 5,
                        RuleId = 77,
                        Line = 52,
                        DatabaseId = 42,
                        Name = "Node_CampCheck",
                        Type = NodeDebugInfo.NodeType.Database,
                        ParentNodeId = 1,
                        FunctionName = new FunctionName { Name = "Proc_StartCampEvent", Arity = 2 },
                        ColumnToVariableMaps = new Dictionary<int, int> { { 0, 100 }, { 1, 101 } }
                    }
                }
            },
            Functions = new Dictionary<string, FunctionDebugInfo>
            {
                { "Proc_StartCampEvent", new FunctionDebugInfo
                    {
                        Name = "Proc_StartCampEvent",
                        TypeId = 0,
                        Params = [ new FunctionParamDebugInfo { TypeId = 3, Name = "_EventId", Out = false } ]
                    }
                }
            }
        };

        using var memoryStream = new MemoryStream();

        DebugInfoSaver.Save(memoryStream, sourceInfo);
        memoryStream.Position = 0;

        StoryDebugInfo loadedInfo = DebugInfoLoader.Load(memoryStream);

        Assert.NotNull(loadedInfo);
        Assert.Equal(sourceInfo.Version, loadedInfo.Version);

        Assert.True(loadedInfo.Databases.ContainsKey(42));
        var db = loadedInfo.Databases[42];
        Assert.Equal("DB_PlayerOrigin", db.Name);
        Assert.Equal(sourceInfo.Databases[42].ParamTypes, db.ParamTypes);

        Assert.True(loadedInfo.Goals.ContainsKey(12));
        var goal = loadedInfo.Goals[12];
        Assert.Equal("GL_Act1_Camp", goal.Name);
        Assert.Equal("Mods/Story/Act1.txt", goal.Path);
        Assert.Equal(100u, goal.InitActions[0].Line);
        Assert.Equal(200u, goal.ExitActions[0].Line);

        Assert.True(loadedInfo.Rules.ContainsKey(77));
        var rule = loadedInfo.Rules[77];
        Assert.Equal("Rule_OnCampRest", rule.Name);
        Assert.Equal(50u, rule.ConditionsStartLine);
        Assert.Equal("_Player", rule.Variables[0].Name);

        Assert.True(loadedInfo.Nodes.ContainsKey(5));
        var node = loadedInfo.Nodes[5];
        Assert.Equal("Node_CampCheck", node.Name);
        Assert.Equal(NodeDebugInfo.NodeType.Database, node.Type);
        Assert.NotNull(node.FunctionName);
        Assert.Equal("Proc_StartCampEvent", node.FunctionName.Name);
        Assert.Equal(2, node.FunctionName.Arity);
        Assert.Equal(100, node.ColumnToVariableMaps[0]);
        Assert.Equal(101, node.ColumnToVariableMaps[1]);

        Assert.True(loadedInfo.Functions.ContainsKey("Proc_StartCampEvent"));
        var func = loadedInfo.Functions["Proc_StartCampEvent"];
        Assert.Equal("_EventId", func.Params[0].Name);
        Assert.False(func.Params[0].Out);
    }

    [Fact]
    public void Load_EmptySerializedContext_LoadsCorrectlyWithEmptyCollections()
    {
        var sourceInfo = new StoryDebugInfo
        {
            Version = 1,
            Databases = [],
            Goals = [],
            Rules = [],
            Nodes = [],
            Functions = []
        };

        using var memoryStream = new MemoryStream();
        DebugInfoSaver.Save(memoryStream, sourceInfo);
        memoryStream.Position = 0;

        var loadedInfo = DebugInfoLoader.Load(memoryStream);

        Assert.NotNull(loadedInfo);
        Assert.Equal(1u, loadedInfo.Version);
        Assert.Empty(loadedInfo.Databases);
        Assert.Empty(loadedInfo.Goals);
        Assert.Empty(loadedInfo.Rules);
        Assert.Empty(loadedInfo.Nodes);
        Assert.Empty(loadedInfo.Functions);
    }

    [Fact]
    public void Load_StreamIsTooShort_ThrowsInvalidDataException()
    {
        byte[] malformedBytes = [0x01, 0x02];
        using var stream = new MemoryStream(malformedBytes);

        var exception = Assert.Throws<InvalidDataException>(() => DebugInfoLoader.Load(stream));
        Assert.Contains("The file format is corrupt", exception.Message);
    }
}

[Collection("ConsoleTests")]
public class LoggerTests : IDisposable
{
    private readonly StringWriter _consoleOutputMock;
    private readonly TextWriter _originalConsoleOut;

    public LoggerTests()
    {
        _consoleOutputMock = new StringWriter();
        _originalConsoleOut = Console.Out;
        Console.SetOut(_consoleOutputMock);
    }

    [Fact]
    public void ConsoleLogger_LifecyclePipeline_OutputsExpectedSizingAndTimings()
    {

        var logger = new ConsoleLogger();

        logger.CompilationStarted();
        logger.TaskStarted("Parsing Goal Metadata");
        logger.TaskFinished();
        logger.CompilationFinished(succeeded: true);

        string output = _consoleOutputMock.ToString();

        Assert.Contains("Parsing Goal Metadata ... ", output);
        Assert.Contains("ms", output);
        Assert.Contains("Compilation took:", output);
    }

    [Fact]
    public void ConsoleLogger_CompilationDiagnosticErrorWithLocation_PrintsFormattedConsoleBlocks()
    {
        var logger = new ConsoleLogger();
        var errorDiagnostic = new Diagnostic
        {
            Level = MessageLevel.Error,
            Code = "E0042",
            Message = "Database parameter count mismatch.",
            Location = new CodeLocation
            {
                FileName = "MainStory.txt",
                StartLine = 12,
                StartColumn = 5,
                EndLine = 12,
                EndColumn = 20
            }
        };

        logger.CompilationDiagnostic(errorDiagnostic);
        string output = _consoleOutputMock.ToString();

        Assert.Contains("ERR! ", output);
        Assert.Contains("MainStory.txt:12:5: ", output);
        Assert.Contains("[E0042] Database parameter count mismatch.", output);
    }

    [Fact]
    public void ConsoleLogger_CompilationDiagnosticWarningWithoutLocation_SkipsFilePrefix()
    {
        var logger = new ConsoleLogger();
        var warnDiagnostic = new Diagnostic
        {
            Level = MessageLevel.Warning,
            Code = "W1002",
            Message = "Global variable shadow mismatch.",
            Location = null
        };

        logger.CompilationDiagnostic(warnDiagnostic);
        string output = _consoleOutputMock.ToString();

        Assert.Contains("WARN ", output);
        Assert.Contains("[W1002] Global variable shadow mismatch.", output);
        Assert.DoesNotContain(":", output[..output.IndexOf('[')]); // Verifies no orphan punctuation is left
    }

    [Fact]
    public void JsonLogger_CompleteLifecycleExecution_GeneratesValidCompiledJsonStructure()
    {
        var logger = new JsonLogger();
        var warningDiagnostic = new Diagnostic
        {
            Level = MessageLevel.Warning,
            Code = "W1002",
            Message = "Unused local variable mapping.",
            Location = new CodeLocation
            {
                FileName = "SharedData.txt",
                StartLine = 45,
                StartColumn = 1,
                EndLine = 45,
                EndColumn = 10
            }
        };

        logger.CompilationStarted();
        logger.TaskStarted("ValidationPass");
        logger.TaskFinished();
        logger.CompilationDiagnostic(warningDiagnostic);
        logger.CompilationFinished(succeeded: false);

        string outputJson = _consoleOutputMock.ToString();

        Assert.NotEmpty(outputJson);
        using var jsonDocument = JsonDocument.Parse(outputJson);
        JsonElement root = jsonDocument.RootElement;

        Assert.False(root.GetProperty("successful").GetBoolean());

        JsonElement statsElement = root.GetProperty("stats");
        Assert.True(statsElement.TryGetProperty("ValidationPass", out JsonElement timingNode));
        Assert.Equal(JsonValueKind.Number, timingNode.ValueKind);

        JsonElement messagesElement = root.GetProperty("messages");
        Assert.Equal(JsonValueKind.Array, messagesElement.ValueKind);

        JsonElement firstMessage = messagesElement[0];
        Assert.Equal("W1002", firstMessage.GetProperty("code").GetString());
        Assert.Equal("Warning", firstMessage.GetProperty("level").GetString());
        Assert.Equal("Unused local variable mapping.", firstMessage.GetProperty("message").GetString());

        JsonElement locationElement = firstMessage.GetProperty("location");
        Assert.Equal("SharedData.txt", locationElement.GetProperty("file").GetString());
        Assert.Equal(45, locationElement.GetProperty("StartLine").GetInt32());
    }

    [Fact]
    public void JsonLogger_DiagnosticWithNullLocation_WritesExplicitNullPropertyField()
    {
        var logger = new JsonLogger();
        var globalError = new Diagnostic
        {
            Level = MessageLevel.Error,
            Code = "E9999",
            Message = "Fatal compile panic.",
            Location = null
        };

        logger.CompilationDiagnostic(globalError);
        logger.CompilationFinished(succeeded: false);
        string outputJson = _consoleOutputMock.ToString();

        using var jsonDocument = JsonDocument.Parse(outputJson);
        JsonElement firstMessage = jsonDocument.RootElement.GetProperty("messages")[0];

        Assert.Equal(JsonValueKind.Null, firstMessage.GetProperty("location").ValueKind);
    }

    [Fact]
    public void DiagnosticConverter_ReadMethod_ThrowsNotImplementedException()
    {
        var converter = new DiagnosticConverter();

        Assert.Throws<NotImplementedException>(() =>
        {
            var reader = new Utf8JsonReader(ReadOnlySpan<byte>.Empty);
            converter.Read(ref reader, typeof(Diagnostic), new JsonSerializerOptions());
        });
    }

    [Fact]
    public void JsonLoggerOutputConverter_ReadMethod_ThrowsNotImplementedException()
    {
        var converter = new JsonLoggerOutputConverter();

        Assert.Throws<NotImplementedException>(() =>
        {
            var reader = new Utf8JsonReader(ReadOnlySpan<byte>.Empty);
            converter.Read(ref reader, typeof(JsonLoggerOutput), new JsonSerializerOptions());
        });
    }

    public void Dispose()
    {
        Console.SetOut(_originalConsoleOut);
        _consoleOutputMock.Dispose();

        GC.SuppressFinalize(this);
    }
}

[CollectionDefinition("ConsoleTests", DisableParallelization = true)]
public class ConsoleCollectionDefinition { }

public class ModCompilerTests : IDisposable
{
    private readonly ILogger _mockLogger;
    private readonly string _dummyDataPath;
    private readonly ModCompiler _compiler;

    public ModCompilerTests()
    {
        _mockLogger = Substitute.For<ILogger>();
        _dummyDataPath = Path.Combine(Path.GetTempPath(), $"ModCompData_{Guid.NewGuid()}");

        _compiler = new ModCompiler(_mockLogger, _dummyDataPath)
        {
            Game = TargetGame.BG3,
            CheckOnly = true
        };
    }

    [Fact]
    public void LoadTypeCoercionWhitelist_ValidStreamInput_PopulatesInternalHashSet()
    {
        var whitelistContent = "FixedFuncA\n  \n  FixedFuncB\r\nFuncWithTrailingSpaces  ";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(whitelistContent));

        var method = typeof(ModCompiler).GetMethod("LoadTypeCoercionWhitelist",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(method);
        method.Invoke(_compiler, [stream]);

        var field = typeof(ModCompiler).GetField("_typeCoercionWhitelist",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var whitelist = field?.GetValue(_compiler) as HashSet<string>;
        Assert.NotNull(whitelist);
        Assert.Contains("FixedFuncA", whitelist);
        Assert.Contains("FixedFuncB", whitelist);
        Assert.Contains("FuncWithTrailingSpaces", whitelist);
        Assert.DoesNotContain("", whitelist);
    }

    [Fact]
    public void LoadGameObjects_ResourceMissingTemplatesRegion_LogsCriticalDiagnosticAndSetsErrorFlag()
    {
        var emptyResource = new Resource();

        var method = typeof(ModCompiler).GetMethod("LoadGameObjects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(method);
        method.Invoke(_compiler, [emptyResource]);

        Assert.True(_compiler.HasErrors);
        _mockLogger.Received(1).CompilationDiagnostic(Arg.Is<Diagnostic>(d =>
            d.Level == MessageLevel.Error &&
            d.Code == CompilerDiagnosticCodes.MissingTemplates
        ));
    }

    [Fact]
    public void LoadGameObjects_ResourceMissingGameObjectsChildNode_LogsErrorDiagnosticAndSetsErrorFlag()
    {
        var invalidResource = new Resource();
        var templatesRegion = new Region();
        invalidResource.Regions["Templates"] = templatesRegion;

        var method = typeof(ModCompiler).GetMethod("LoadGameObjects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(method);
        method.Invoke(_compiler, [invalidResource]);

        Assert.True(_compiler.HasErrors);
        _mockLogger.Received(1).CompilationDiagnostic(Arg.Is<Diagnostic>(d =>
            d.Level == MessageLevel.Error &&
            d.Code == CompilerDiagnosticCodes.MissingGameObjects
        ));
    }

    [Fact]
    public void LoadGameObjects_UnmappableAssetGuidType_LogsWarningAndDoesNotSetErrorFlag()
    {
        var validResource = new Resource();
        var templatesRegion = new Region();
        var gameObjectsList = new NodeList();

        var badGameObjectNode = new LSLib.LS.Node();
        badGameObjectNode.Attributes["MapKey"] = new NodeAttribute(NodeAttribute.DataType.String) { Value = "GUID_SAMPLE_123" };
        badGameObjectNode.Attributes["Name"] = new NodeAttribute(NodeAttribute.DataType.String) { Value = "Trap_Fire_A" };
        badGameObjectNode.Attributes["Type"] = new NodeAttribute(NodeAttribute.DataType.String) { Value = "projectile" }; // invalid

        gameObjectsList.Add(badGameObjectNode);
        templatesRegion.Children["GameObjects"] = gameObjectsList;
        validResource.Regions["Templates"] = templatesRegion;

        var method = typeof(ModCompiler).GetMethod("LoadGameObjects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(method);
        method.Invoke(_compiler, [validResource]);

        Assert.False(_compiler.HasErrors);
        _mockLogger.Received(1).CompilationDiagnostic(Arg.Is<Diagnostic>(d =>
            d.Level == MessageLevel.Warning &&
            d.Code == CompilerDiagnosticCodes.UnknownGameObjectType &&
            d.Message.Contains("GUID_SAMPLE_123")
        ));
    }

    [Fact]
    public void LogCompilerError_InvokedConcurrently_ThreadSafeInterlockedStateMaintained()
    {
        int dynamicParallelLoadCount = 20;

        Parallel.For(0, dynamicParallelLoadCount, i =>
        {
            var method = typeof(ModCompiler).GetMethod("LogCompilerError",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            method?.Invoke(_compiler, ["X00", $"Thread safe check error {i}"]);
        });

        Assert.True(_compiler.HasErrors);
        _mockLogger.Received(dynamicParallelLoadCount).CompilationDiagnostic(Arg.Is<Diagnostic>(d =>
            d.Level == MessageLevel.Error &&
            d.Code == "X00"
        ));
    }

    public void Dispose()
    {
        _compiler.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class ModCompilerPipelineTests : IDisposable
{
    private readonly ILogger _mockLogger;
    private readonly string _dummyDataPath;
    private readonly ModCompiler _compilerInstance;

    public ModCompilerPipelineTests()
    {
        _mockLogger = Substitute.For<ILogger>();
        _dummyDataPath = Path.Combine(Path.GetTempPath(), $"ModPipelineTest_{Guid.NewGuid()}");

        _compilerInstance = new ModCompiler(_mockLogger, _dummyDataPath)
        {
            Game = TargetGame.BG3,
            CheckOnly = true
        };
    }

    [Fact]
    public void LoadOrphanQueryIgnores_ValidRegexMatches_PopulatesCompilerIgnoreSet()
    {
        var vfsMock = Substitute.For<VFS>();
        var modInfo = new ModInfo { OrphanQueryIgnoreList = "Meta/Orphans.txt" };

        var rawFileLines = "DB_CampState_HasPickedUpItem 2\r\nInvalidLineWithoutArity\r\nDB_IsPlayerCharacter 1";
        var fileStream = new MemoryStream(Encoding.UTF8.GetBytes(rawFileLines));
        vfsMock.Open("Meta/Orphans.txt").Returns(fileStream);

        typeof(ModCompiler)
            .GetField("_fs", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.SetValue(_compilerInstance, vfsMock);

        var method = typeof(ModCompiler).GetMethod("LoadOrphanQueryIgnores",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(method);
        method.Invoke(_compilerInstance, [modInfo]);

        var internalCompilerField = typeof(ModCompiler)
            .GetField("_compiler", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(_compilerInstance) as Compiler;

        Assert.NotNull(internalCompilerField);
        var ignoredDatabases = internalCompilerField.IgnoreUnusedDatabases;

        Assert.Equal(2, ignoredDatabases.Count);
        Assert.Contains(ignoredDatabases, x => x.Name == "DB_CampState_HasPickedUpItem" && x.Arity == 2);
        Assert.Contains(ignoredDatabases, x => x.Name == "DB_IsPlayerCharacter" && x.Arity == 1);
        Assert.DoesNotContain(ignoredDatabases, x => x.Name == "InvalidLineWithoutArity");
    }

    [Fact]
    public void LoadMod_MissingModKeyNameInRegistry_ThrowsKeyNotFoundException()
    {
        var exception = Assert.Throws<TargetInvocationException>(() =>
        {
            var method = typeof(ModCompiler).GetMethod("LoadMod",
                BindingFlags.NonPublic | BindingFlags.Instance);

            method?.Invoke(_compilerInstance, ["GhostModPayload"]);
        });

        Assert.IsType<KeyNotFoundException>(exception.InnerException);
        Assert.Contains("Target mod metadata profile not found in solution context: GhostModPayload", exception.InnerException.Message);
    }

    [Fact]
    public async Task CompileAsync_EmptyModListPassed_BypassesVfsAndFinishesSuccessfully()
    {
        string outPath = Path.Combine(_dummyDataPath, "story.osi");
        var emptyModsList = new List<string>();

        bool compilationSuccess = await _compilerInstance.CompileAsync(outPath, debugInfoPath: null, emptyModsList);

        Assert.True(compilationSuccess);
        Assert.False(_compilerInstance.HasErrors);

        _mockLogger.Received(1).CompilationStarted();
        _mockLogger.Received(1).CompilationFinished(true);
        _mockLogger.DidNotReceive().TaskStarted("Building VFS");
    }

    [Fact]
    public void LoadGameObjects_BuffersModFilesList_FillsInternalByteCacheList()
    {
        var vfsMock = Substitute.For<VFS>();
        var modInfo = new ModInfo
        {
            Globals = ["G1.lsf"],
            LevelObjects = ["L1.lsf", "L2.lsf"]
        };

        vfsMock.Open("G1.lsf").Returns(new MemoryStream([0xAA, 0xBB]));
        vfsMock.Open("L1.lsf").Returns(new MemoryStream([0xCC]));
        vfsMock.Open("L2.lsf").Returns(new MemoryStream([0xDD, 0xEE]));

        typeof(ModCompiler).GetField("_fs", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(_compilerInstance, vfsMock);

        var method = typeof(ModCompiler).GetMethod("LoadGameObjects",
            BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(ModInfo)], null);

        Assert.NotNull(method);
        method.Invoke(_compilerInstance, [modInfo]);

        var lsfCacheField = typeof(ModCompiler).GetField("_gameObjectLSFs", BindingFlags.NonPublic | BindingFlags.Instance);
        var cachedBytesList = lsfCacheField?.GetValue(_compilerInstance) as List<byte[]>;

        Assert.NotNull(cachedBytesList);
        Assert.Equal(3, cachedBytesList.Count);
        Assert.Equal([0xAA, 0xBB], cachedBytesList[0]);
        Assert.Equal([0xCC], cachedBytesList[1]);
        Assert.Equal([0xDD, 0xEE], cachedBytesList[2]);
    }

    public void Dispose()
    {
        _compilerInstance.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class StoryCompilerProgramTests : IDisposable
{
    private readonly string _pathToExe;
    private readonly string _testRoot;

    public StoryCompilerProgramTests()
    {
        string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "StoryCompiler.exe"
            : "StoryCompiler";

        _pathToExe = Path.Combine(AppContext.BaseDirectory, exeName);

        _testRoot = Path.Combine(Path.GetTempPath(), $"StoryProgTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRoot);

        EnsureExecutablePermissions();
    }

    [Fact]
    public void Main_InvalidGameArgument_ReturnsStandardCommandLineValidationErrorExitCode()
    {
        var psi = new ProcessStartInfo
        {
            FileName = _pathToExe,
            Arguments = "--mod CoreMod --game dos3",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        Assert.NotNull(process);

        string errorOutput = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.Equal(1, process.ExitCode);
        Assert.Contains("The variant target game 'dos3' is invalid.", errorOutput);
    }

    [Fact]
    public void Main_MissingMandatoryModFlag_FailsValidationAndExitsWithCodeOne()
    {
        var psi = new ProcessStartInfo
        {
            FileName = _pathToExe,
            Arguments = "--game bg3 --check-only",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        Assert.NotNull(process);

        string errorOutput = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.Equal(1, process.ExitCode);
        Assert.Contains("Option '--mod' is required", errorOutput);
    }

    [Fact]
    public void Main_NonexistentGameDataPath_TriggersInternalCompilerLoadErrorAndExitsWithCodeThree()
    {
        string invalidDataPath = Path.Combine(_testRoot, "NonExistentGameFolder");
        string outOsi = Path.Combine(_testRoot, "story.div.osi");

        var psi = new ProcessStartInfo
        {
            FileName = _pathToExe,
            Arguments = $"--mod VirtualMod --game-data-path \"{invalidDataPath}\" --output \"{outOsi}\" --check-only",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        Assert.NotNull(process);

        string consoleOutput = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        Assert.Equal(3, process.ExitCode);
        Assert.Contains("Compilation took:", consoleOutput);
    }

    [Fact]
    public void Main_JsonOutputFlagPassed_SwitchesLoggerOutputEncodingToStructuredJson()
    {
        string invalidDataPath = Path.Combine(_testRoot, "NonExistentGameFolder");

        var psi = new ProcessStartInfo
        {
            FileName = _pathToExe,
            Arguments = $"--mod VirtualMod --game-data-path \"{invalidDataPath}\" --json --check-only",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        Assert.NotNull(process);

        string jsonOutput = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        Assert.Equal(3, process.ExitCode);
        Assert.StartsWith("{", jsonOutput.Trim());
        Assert.EndsWith("}", jsonOutput.Trim());
        Assert.Contains("\"successful\":false", jsonOutput);
    }

    private void EnsureExecutablePermissions()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        try
        {
            using var proc = Process.Start("chmod", $"+x \"{_pathToExe}\"");
            proc?.WaitForExit();
        }
        catch
        {
            // Suppress
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, true);
        }

        GC.SuppressFinalize(this);
    }
}
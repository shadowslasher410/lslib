using System;
using System.Collections.Generic;
using System.Text;
using LSTools.StatParser;
using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace LSTools.Tests;

public class ProgramOrchestratorTests
{
    [Fact]
    public async Task MainPipeline_ValidCLIArgsPassed_SuccessfullyBindsAndInterceptsExecutionPayload()
    {
        CommandLineArguments? capturedArgs = null;
        bool executionHandlerInvoked = false;

        var rootCommand = CommandLineArguments.BuildRootCommand(boundArgs =>
        {
            capturedArgs = boundArgs;
            executionHandlerInvoked = true;

        });

        string[] simulationCLIArgs = [
            "--mod", "CustomWeapons", "SpellsExpansion",
            "--dependency", "Shared",
            "--game-data-path", "C:\\Games\\BG3\\Data",
            "--no-packages"
        ];

        var parseResult = rootCommand.Parse(simulationCLIArgs);
        int invocationResult = await parseResult.InvokeAsync();

        Assert.Equal(0, invocationResult);
        Assert.True(executionHandlerInvoked, "The underlying tool execution action pipeline should have been triggered.");

        Assert.NotNull(capturedArgs);
        Assert.True(capturedArgs.NoPackages);
        Assert.Equal("C:\\Games\\BG3\\Data", capturedArgs.GameDataPath);
        Assert.Equal(new[] { "CustomWeapons", "SpellsExpansion" }, capturedArgs.Mods);
        Assert.Equal(new[] { "Shared" }, capturedArgs.Dependencies);
        Assert.Empty(capturedArgs.PackagePaths);
    }

    [Fact]
    public async Task MainPipeline_MalformedArgumentsPassed_FailsValidationBeforeActionDelegateTriggers()
    {
        bool executionHandlerInvoked = false;
        var rootCommand = CommandLineArguments.BuildRootCommand(_ => { executionHandlerInvoked = true; });

        string[] malformedCLIArgs = ["--game-data-path", "D:\\Data"];

        var parseResult = rootCommand.Parse(malformedCLIArgs);
        int invocationResult = await parseResult.InvokeAsync();

        Assert.Equal(1, invocationResult);
        Assert.False(executionHandlerInvoked, "The core handler execution route must be entirely bypassed on validation drops.");
        Assert.NotEmpty(parseResult.Errors);
    }
}
    [Collection("StatParserTests")]
    public class StatParserCommandLineTests : IDisposable
    {
        private readonly string _pathToExe;
        private readonly string _testSandboxRoot;

        public StatParserCommandLineTests()
        {
            string exeExtensionName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "StatParser.exe"
                : "StatParser";

            _pathToExe = Path.Combine(AppContext.BaseDirectory, exeExtensionName);
            _testSandboxRoot = Path.Combine(Path.GetTempPath(), $"StatParserSandbox_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testSandboxRoot);

            EnsureUnixExecutablePermissions();
        }

        [Fact]
        public void BuildRootCommand_ValidArguments_BindsPropertyValuesInMemory()
        {
            CommandLineArguments? boundOutputArgs = null;
            var rootCommand = CommandLineArguments.BuildRootCommand(bound => boundOutputArgs = bound);

            string[] rawCLIArgs = [
                "--mod", "CustomClassMod", "WeaponsOverhaul",
            "--dependency", "Shared", "Core",
            "--game-data-path", "C:\\BaldursGate3\\Data",
            "--package-paths", "C:\\CustomPackages\\Asset.pak",
            "--no-packages"
            ];

            var parseResult = rootCommand.Parse(rawCLIArgs);
            var pipelineExitStatus = parseResult.Invoke();

            Assert.Equal(0, pipelineExitStatus);
            Assert.NotNull(boundOutputArgs);
            Assert.True(boundOutputArgs.NoPackages);
            Assert.Equal("C:\\BaldursGate3\\Data", boundOutputArgs.GameDataPath);
            Assert.Equal(new[] { "CustomClassMod", "WeaponsOverhaul" }, boundOutputArgs.Mods);
            Assert.Equal(new[] { "Shared", "Core" }, boundOutputArgs.Dependencies);
            Assert.Equal(new[] { "C:\\CustomPackages\\Asset.pak" }, boundOutputArgs.PackagePaths);
        }

        [Fact]
        public void BuildRootCommand_MissingRequiredModFlag_FailsValidationWithErrorsCollection()
        {
            var rootCommand = CommandLineArguments.BuildRootCommand(_ => { });
            string[] incompleteArgs = ["--game-data-path", "C:\\Data"];

            var result = rootCommand.Parse(incompleteArgs);

            Assert.NotEmpty(result.Errors);
            Assert.Contains(result.Errors, error => error.Message.Contains("Option '--mod' is required"));
        }

        [Fact]
        public void Process_MissingRequiredArguments_PrintsUsageToStandardErrorAndExitsWithCodeOne()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _pathToExe,
                Arguments = "--game-data-path \"/Mock/Path\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var runtimeProcess = Process.Start(startInfo);
            Assert.NotNull(runtimeProcess);

            string stdErrorStream = runtimeProcess.StandardError.ReadToEnd();
            runtimeProcess.WaitForExit();

            Assert.Equal(1, runtimeProcess.ExitCode);
            Assert.Contains("Option '--mod' is required", stdErrorStream);
        }

        [Fact]
        public void Process_NonexistentModPassed_TriggersInternalPipelineExceptionAndGracefulExit()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _pathToExe,
                Arguments = "--mod GhostMod --game-data-path \"/Invalid/Data/Path\" --no-packages",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var runtimeProcess = Process.Start(startInfo);
            Assert.NotNull(runtimeProcess);

            string errorConsoleDump = runtimeProcess.StandardError.ReadToEnd();
            runtimeProcess.WaitForExit();

            Assert.True(runtimeProcess.ExitCode != 0);
        }

        private void EnsureUnixExecutablePermissions()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

            try
            {
                using var permissionsProcess = Process.Start("chmod", $"+x \"{_pathToExe}\"");
                permissionsProcess?.WaitForExit();
            }
            catch
            {
                // Suppress
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(_testSandboxRoot))
            {
                Directory.Delete(_testSandboxRoot, true);
            }
            GC.SuppressFinalize(this);
        }
    }
    [Collection("StatParserTests")]
    public class StatCheckerComponentTests : IDisposable
    {
        private readonly string _mockGameDataPath;
        private readonly StatChecker _checkerInstance;

        public StatCheckerComponentTests()
        {
            _mockGameDataPath = Path.Combine(Path.GetTempPath(), $"StatCheckComponent_{Guid.NewGuid()}");
            Directory.CreateDirectory(_mockGameDataPath);

            _checkerInstance = new StatChecker(_mockGameDataPath)
            {
                LoadPackages = false
            };
        }

        [Fact]
        public void LoadMod_ModKeyDoesNotExistInRegistry_ThrowsTargetKeyNotFoundException()
        {
            var methodInfo = typeof(StatChecker).GetMethod("LoadMod",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.NotNull(methodInfo);

            var targetInvocationEx = Assert.Throws<TargetInvocationException>(() =>
            {
                methodInfo.Invoke(_checkerInstance, ["MissingModNameKey"]);
            });

            Assert.IsType<KeyNotFoundException>(targetInvocationEx.InnerException);
            Assert.Contains("Target statistics mod profiling definition not found: MissingModNameKey", targetInvocationEx.InnerException?.Message);
        }

        [Fact]
        public void LoadXml_NullOrWhitespacePathProvided_ReturnsNullAndBypassesVirtualFileSystem()
        {
            var methodInfo = typeof(StatChecker).GetMethod("LoadXml",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.NotNull(methodInfo);

            var emptyStringResult = methodInfo.Invoke(_checkerInstance, [""]);
            var nullStringResult = methodInfo.Invoke(_checkerInstance, [(string?)null]);

            Assert.Null(emptyStringResult);
            Assert.Null(nullStringResult);
        }

        [Fact]
        public void StatChecker_ConstructorPassedNullGameDataPath_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new StatChecker(null!));
        }

        public void Dispose()
        {
            _checkerInstance.Dispose();
            if (Directory.Exists(_mockGameDataPath))
            {
                Directory.Delete(_mockGameDataPath, true);
            }
            GC.SuppressFinalize(this);
        }
    }

    [CollectionDefinition("StatParserTests", DisableParallelization = true)]
    public class StatParserTestCollection { }
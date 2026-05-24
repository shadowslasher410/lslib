using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LSTools.Tests
{
    public class StoryDecompilerProcessTests : IDisposable
    {
        private readonly string _pathToExe;
        private readonly string _testRoot;

        public StoryDecompilerProcessTests()
        {
            string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "StoryDecompiler.exe"
                : "StoryDecompiler";

            _pathToExe = Path.Combine(AppContext.BaseDirectory, exeName);

            _testRoot = Path.Combine(Path.GetTempPath(), $"StoryDecTest_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testRoot);

            EnsureExecutablePermissions();
        }

        [Fact]
        public void Process_MissingRequiredOptions_PrintsUsageAndExitsWithCodeOne()
        {
            var psi = new ProcessStartInfo
            {
                FileName = _pathToExe,
                Arguments = "",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            Assert.NotNull(process);

            string errOutput = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.Equal(1, process.ExitCode);
            Assert.Contains("Option '--input' is required", errOutput);
            Assert.Contains("Option '--output' is required", errOutput);
        }

        [Fact]
        public void Process_InputFileDoesNotExist_WritesErrorToConsoleAndExitsWithCodeOne()
        {
            string nonexistentFile = Path.Combine(_testRoot, "ghost_story.osi");
            string outDir = Path.Combine(_testRoot, "output");

            var psi = new ProcessStartInfo
            {
                FileName = _pathToExe,
                Arguments = $"--input \"{nonexistentFile}\" --output \"{outDir}\"",
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
            Assert.Contains($"Source input file context does not exist: {nonexistentFile}", errorOutput);
        }

        [Fact]
        public void Process_InvalidExtensionPassed_WritesNotSupportedExceptionToConsole()
        {
            string invalidExtensionFile = Path.Combine(_testRoot, "mod_data.txt");
            File.WriteAllText(invalidExtensionFile, "Not a true compiled story asset file");
            string outDir = Path.Combine(_testRoot, "output");

            var psi = new ProcessStartInfo
            {
                FileName = _pathToExe,
                Arguments = $"-i \"{invalidExtensionFile}\" -o \"{outDir}\"",
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
            Assert.Contains("Unsupported target story/save filename extension: .txt", errorOutput);
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

}

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LSTools.Tests;

public class VTexToolProcessTests : IDisposable
{
    private readonly string _pathToExe;
    private readonly string _testRoot;

    public VTexToolProcessTests()
    {
        string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "VTexTool.exe"
            : "VTexTool";

        _pathToExe = Path.Combine(AppContext.BaseDirectory, exeName);

        _testRoot = Path.Combine(Path.GetTempPath(), $"VTexTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRoot);

        EnsureExecutablePermissions();
    }

    [Fact]
    public void Process_MissingArguments_PrintsUsageAndExitsWithCodeOne()
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

        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        Assert.Equal(1, process.ExitCode);
        Assert.Contains("Usage: VTexTool <build_root> <configuration_xml>", output);
    }

    [Fact]
    public void Process_InvalidConfigurationFile_WritesErrorToConsoleAndExitsWithCodeOne()
    {
        string corruptXmlPath = Path.Combine(_testRoot, "corrupt.xml");
        File.WriteAllText(corruptXmlPath, "<invalid><xml>");

        var psi = new ProcessStartInfo
        {
            FileName = _pathToExe,
            Arguments = $"\"{_testRoot}\" \"{corruptXmlPath}\"",
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
        Assert.NotEmpty(errorOutput);
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
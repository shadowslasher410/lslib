using LSLib.LS;
using LSLib.LS.Enums;

namespace LSLib.Divine.CLI;

public sealed class CommandLineArguments
{
    public string LogLevel { get; set; } = "info";
    public string Game { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string PackagedPath { get; set; } = string.Empty;
    public string? InputFormat { get; set; } 
    public string? OutputFormat { get; set; }
    public string Action { get; set; } = "extract-package";
    public string PakCompressionMethod { get; set; } = "lz4hc";
    public string[] Options { get; set; } = [];
    public string Expression { get; set; } = "*";
    public string ConformPath { get; set; } = string.Empty;
    public int PackagePriority { get; set; }
    public bool LegacyGuids { get; set; }
    public bool FastBuild { get; set; }
    public bool VTValidate { get; set; }
    public bool UsePackageName { get; set; }
    public bool UseRegex { get; set; }
    public string VTRoot { get; set; } = string.Empty;

    public static LogLevel GetLogLevelByString(string logLevel) => logLevel switch
    {
        "off" => LSLib.LS.Enums.LogLevel.OFF,
        "fatal" => LSLib.LS.Enums.LogLevel.FATAL,
        "error" => LSLib.LS.Enums.LogLevel.ERROR,
        "warn" => LSLib.LS.Enums.LogLevel.WARN,
        "info" => LSLib.LS.Enums.LogLevel.INFO,
        "debug" => LSLib.LS.Enums.LogLevel.DEBUG,
        "trace" => LSLib.LS.Enums.LogLevel.TRACE,
        "all" => LSLib.LS.Enums.LogLevel.ALL,
        _ => LSLib.LS.Enums.LogLevel.INFO
    };

    public static Game GetGameByString(string game) => game switch
    {
        "bg3" => LSLib.LS.Enums.Game.BaldursGate3,
        "dos" => LSLib.LS.Enums.Game.DivinityOriginalSin,
        "dosee" => LSLib.LS.Enums.Game.DivinityOriginalSinEE,
        "dos2" => LSLib.LS.Enums.Game.DivinityOriginalSin2,
        "dos2de" => LSLib.LS.Enums.Game.DivinityOriginalSin2DE,
        "unset" => LSLib.LS.Enums.Game.Unset,
        _ => throw new ArgumentException($"Unknown game: \"{game}\"")
    };

    public static ResourceFormat GetResourceFormatByString(string? resourceFormat) => (resourceFormat?.ToLowerInvariant()) switch
    {
        "lsb" => ResourceFormat.LSB,
        "lsf" => ResourceFormat.LSF,
        "lsj" => ResourceFormat.LSJ,
        "lsx" => ResourceFormat.LSX,
        _ => throw new ArgumentException($"Unknown resource format: \"{resourceFormat}\"")
    };

    public static Dictionary<string, object> GetCompressionOptions(string compressionOption, PackageVersion packageVersion)
    {
        var (compression, level) = compressionOption switch
        {
            "zlibfast" => (LSLib.LS.CompressionMethod.Zlib, LSLib.LS.LSCompressionLevel.Fast),
            "zlib" => (LSLib.LS.CompressionMethod.Zlib, LSLib.LS.LSCompressionLevel.Default),
            "lz4" => (LSLib.LS.CompressionMethod.LZ4, LSLib.LS.LSCompressionLevel.Fast),
            "lz4hc" => (LSLib.LS.CompressionMethod.LZ4, LSLib.LS.LSCompressionLevel.Default),
            _ => (LSLib.LS.CompressionMethod.None, LSLib.LS.LSCompressionLevel.Default)
        };

        if (compression == LSLib.LS.CompressionMethod.LZ4 && packageVersion <= PackageVersion.V9)
        {
            compression = LSLib.LS.CompressionMethod.Zlib;
            level = LSLib.LS.LSCompressionLevel.Default;
        }

        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["Compression"] = compression,
            ["CompressionLevel"] = level
        };
    }

    public static Dictionary<string, bool> GetGR2Options(string[]? options)
    {
        Dictionary<string, bool> results = new(StringComparer.OrdinalIgnoreCase)
        {
            ["deduplicate-vertices"] = true,
            ["flip-uvs"] = true,
            ["ignore-uv-nan"] = true,
            ["disable-qtangents"] = false,
            ["y-up-skeletons"] = true,
            ["force-legacy-version"] = false,
            ["compact-tris"] = true,
            ["build-dummy-skeleton"] = true,
            ["apply-basis-transforms"] = true,
            ["mirror-skeletons"] = false,
            ["x-flip-meshes"] = false,
            ["conform"] = false,
            ["conform-copy"] = false,
            ["export-normals"] = true,
            ["export-tangents"] = true,
            ["export-uvs"] = true,
            ["export-colors"] = true,
            ["recalculate-normals"] = false,
            ["recalculate-tangents"] = false,
            ["recalculate-iwt"] = false,
            ["deduplicate-uvs"] = true
        };

        if (options is null)
        {
            return results;
        }

        foreach (string option in options)
        {
            if (results.ContainsKey(option))
            {
                results[option] = true;
            }
        }

        return results;
    }
}

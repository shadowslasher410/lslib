using LSLib.Granny.GR2;
using LSLib.LS.Enums;

namespace LSLib.Granny;

public enum ExportFormat
{
    GR2,
    DAE,
    GLTF,
    GLB
}

public enum DivinityModelInfoFormat
{
    None,
    UserDefinedProperties,
    LSMv0,
    LSMv1,
    LSMv3
}

public sealed class ExporterOptions
{
    public string InputPath { get; set; } = string.Empty;
    public object? Input { get; set; }
    public ExportFormat InputFormat { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public ExportFormat OutputFormat { get; set; }

    public bool Is64Bit { get; set; }
    public bool AlternateSignature { get; set; }
    public uint VersionTag { get; set; } = Header.DefaultTag;
    public bool FlipUVs { get; set; } = true;
    public bool BuildDummySkeleton { get; set; }
    public bool CompactIndices { get; set; } = true;
    public bool DeduplicateVertices { get; set; } = true;
    public bool ApplyBasisTransforms { get; set; } = true;
    public bool UseObsoleteVersionTag { get; set; }
    public string? ConformGR2Path { get; set; }
    public bool ConformSkeletons { get; set; } = true;
    public bool ConformSkeletonsCopy { get; set; }
    public bool ConformAnimations { get; set; } = true;
    public bool ConformMeshBoneBindings { get; set; } = true;
    public bool ConformModels { get; set; } = true;
    public DivinityModelInfoFormat ModelInfoFormat { get; set; } = DivinityModelInfoFormat.None;
    public uint ModelType { get; set; }
    public bool FlipMesh { get; set; }
    public bool MirrorSkeleton { get; set; }
    public bool TransformSkeletons { get; set; } = true;
    public bool IgnoreUVNaN { get; set; }
    public bool RemoveTrivialAnimationKeys { get; set; }
    public bool RecalculateOBBs { get; set; } = true;
    public bool EnableQTangents { get; set; } = true;

    public List<string> DisabledAnimations { get; init; } = [];
    public List<string> DisabledModels { get; init; } = [];
    public List<string> DisabledSkeletons { get; init; } = [];

    public void LoadGameSettings(Game game)
    {
        switch (game)
        {
            case Game.DivinityOriginalSin:
                Is64Bit = false;
                AlternateSignature = false;
                VersionTag = Header.Tag_DOS;
                ModelInfoFormat = DivinityModelInfoFormat.None;
                break;
            case Game.DivinityOriginalSinEE:
                Is64Bit = true;
                AlternateSignature = true;
                VersionTag = Header.Tag_DOSEE;
                ModelInfoFormat = DivinityModelInfoFormat.UserDefinedProperties;
                break;
            case Game.DivinityOriginalSin2:
                Is64Bit = true;
                AlternateSignature = true;
                VersionTag = Header.Tag_DOSEE;
                ModelInfoFormat = DivinityModelInfoFormat.LSMv1;
                break;
            case Game.BaldursGate3:
                Is64Bit = true;
                AlternateSignature = false;
                VersionTag = Header.Tag_DOSEE;
                ModelInfoFormat = DivinityModelInfoFormat.LSMv3;
                break;
            case Game.DivinityOriginalSin2DE:
            default:
                Is64Bit = true;
                AlternateSignature = true;
                VersionTag = Header.Tag_DOS2DE;
                ModelInfoFormat = DivinityModelInfoFormat.LSMv1;
                break;
        }
    }
}

public static class Utils
{
    public static void Warn(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Console.Error.WriteLine("WARNING: {0}", message);
    }

    public static void Info(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Console.WriteLine(message);
    }
}
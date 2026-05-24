using System;
using LSLib.Granny.GR2;
using LSLib.LS;

namespace LSLib.Granny.Model;

public class ArtToolInfo
{
    public string FromArtToolName = string.Empty;
    public int ArtToolMajorRevision;
    public int ArtToolMinorRevision;

    [Serialization(MinVersion = 0x80000011)]
    public int ArtToolPointerSize;
    public float UnitsPerMeter;

    [Serialization(ArraySize = 3)]
    public float[] Origin = [0f, 0f, 0f];

    [Serialization(ArraySize = 3)]
    public float[] RightVector = [1f, 0f, 0f];

    [Serialization(ArraySize = 3)]
    public float[] UpVector = [0f, 1f, 0f];

    [Serialization(ArraySize = 3)]
    public float[] BackVector = [0f, 0f, 1f];

    [Serialization(Type = MemberType.VariantReference, MinVersion = 0x80000011)]
    public object? ExtendedData;

    public static ArtToolInfo CreateDefault()
    {
        return new ArtToolInfo
        {
            FromArtToolName = string.Empty,
            ArtToolMajorRevision = 1,
            ArtToolMinorRevision = 0,
            ArtToolPointerSize = 64,
            UnitsPerMeter = 1.0f,
            Origin = [0f, 0f, 0f],
            RightVector = [1f, 0f, 0f],
            UpVector = [0f, 1f, 0f],
            BackVector = [0f, 0f, 1f]
        };
    }

    public void SetYUp()
    {
        RightVector = [1f, 0f, 0f];
        UpVector = [0f, 1f, 0f];
        BackVector = [0f, 0f, -1f];
    }

    public void SetZUp()
    {
        RightVector = [1f, 0f, 0f];
        UpVector = [0f, 0f, 1f];
        BackVector = [0f, 1f, 0f];
    }
}

public class ExporterInfo
{
    public string ExporterName = string.Empty;
    public int ExporterMajorRevision;
    public int ExporterMinorRevision;
    public int ExporterCustomization;
    public int ExporterBuildNumber;

    [Serialization(Type = MemberType.VariantReference, MinVersion = 0x80000011)]
    public object? ExtendedData;

    public static ExporterInfo MakeCurrent()
    {
        return new ExporterInfo
        {
            ExporterName = $"LSLib GR2 Exporter v{Common.LibraryVersion()}",
            ExporterMajorRevision = Common.MajorVersion,
            ExporterMinorRevision = Common.MinorVersion,
            ExporterBuildNumber = 0,
            ExporterCustomization = Common.PatchVersion
        };
    }
}
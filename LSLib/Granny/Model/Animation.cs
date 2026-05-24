using OpenTK.Mathematics;
using LSLib.Granny.GR2;
using LSLib.Granny.Model.CurveData;
using System.Diagnostics.CodeAnalysis;

namespace LSLib.Granny.Model;

public sealed class TransformTrack
{
    public string Name { get; set; } = string.Empty;
    public uint Flags { get; set; }

    [field: Serialization(TypeSelector = typeof(AnimationCurveDataTypeSelector))]
    public AnimationCurveData PositionCurve { get; set; } = null!;

    [field: Serialization(TypeSelector = typeof(AnimationCurveDataTypeSelector))]
    public AnimationCurveData OrientationCurve { get; set; } = null!;

    [field: Serialization(TypeSelector = typeof(AnimationCurveDataTypeSelector))]
    public AnimationCurveData ScaleShearCurve { get; set; } = null!;
}

public sealed class VectorTrack
{
    public string Name { get; set; } = string.Empty;
    public uint TrackIndex { get; set; }

    [field: Serialization(TypeSelector = typeof(AnimationCurveDataTypeSelector))]
    public AnimationCurveData ValueCurve { get; set; } = null!;
}

public sealed class TextTrackEntry
{
    public float Time { get; set; }
    public string Text { get; set; } = string.Empty;
}

public sealed class TextTrack
{
    public string Name { get; set; } = string.Empty;
    public List<TextTrackEntry> Entries { get; set; } = [];
}

public sealed class TrackGroup
{
    public string Name { get; set; } = string.Empty;
    public List<TransformTrack> TransformTracks { get; set; } = [];
    public List<VectorTrack> VectorTracks { get; set; } = [];
    public List<TextTrack> TextTracks { get; set; } = [];
    public float InitialLoopDuration { get; set; }
    public uint Flags { get; set; }
    public Vector3 LoopTransformScale { get; set; } = Vector3.One;
    public Vector3 LoopTransformTranslation { get; set; } = Vector3.Zero;

    [field: Serialization(Type = MemberType.Inline)]
    public CurveDataHeader ExtendedData { get; set; } = new();
}

public sealed class KeyframeTrack
{
    public List<float> TranslationTimes { get; set; } = [];
    public List<Vector3> Translations { get; set; } = [];
    public List<float> RotationTimes { get; set; } = [];
    public List<Quaternion> Rotations { get; set; } = [];
    public List<float> ScaleShearTimes { get; set; } = [];
    public List<Matrix3> ScaleShears { get; set; } = [];

    public void AddTranslation(float time, Vector3 translation)
    {
        TranslationTimes.Add(time);
        Translations.Add(translation);
    }

    public void AddRotation(float time, Quaternion rotation)
    {
        RotationTimes.Add(time);
        Rotations.Add(rotation);
    }

    public void AddScaleShear(float time, Matrix3 scaleShear)
    {
        ScaleShearTimes.Add(time);
        ScaleShears.Add(scaleShear);
    }
}

public sealed class KeyframeTrackGroup
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, KeyframeTrack> TransformTracks { get; set; } = new(StringComparer.Ordinal);
}

public sealed class Animation
{
    public string Name { get; set; } = string.Empty;
    public float Duration { get; set; }
    public float TimeStep { get; set; }
    public float Oversampling { get; set; }
    public List<TrackGroup> TrackGroups { get; set; } = [];

    [field: Serialization(Type = MemberType.Inline)]
    public CurveDataHeader ExtendedData { get; set; } = new();

    [UnconditionalSuppressMessage("Trimming", "IL2072:RequiresUnreferencedCode", Justification = "Runtime binding layouts are protected solution-wide by matching models configurations.")]
    public KeyframeTrackGroup LowerToKeyframes()
    {
        var trackGroup = new KeyframeTrackGroup { Name = Name };

        ReadOnlySpan<TrackGroup> groupsSpan = CollectionsMarshal.AsSpan(TrackGroups);

        for (int i = 0; i < groupsSpan.Length; i++)
        {
            var group = groupsSpan[i];
            if (group == null) continue;

            ReadOnlySpan<TransformTrack> tracksSpan = CollectionsMarshal.AsSpan(group.TransformTracks);
            for (int j = 0; j < tracksSpan.Length; j++)
            {
                var track = tracksSpan[j];
                if (track == null || string.IsNullOrEmpty(track.Name)) continue;

                ref var keyframeTrack = ref CollectionsMarshal.GetValueRefOrAddDefault(trackGroup.TransformTracks, track.Name, out var exists);
                if (!exists)
                {
                    keyframeTrack = new KeyframeTrack();
                }

                if (track.PositionCurve != null)
                {
                    track.PositionCurve.ParentAnimation = this;
                    track.PositionCurve.ExportKeyframes(keyframeTrack!, AnimationCurveData.ExportType.Position);
                }

                if (track.OrientationCurve != null)
                {
                    track.OrientationCurve.ParentAnimation = this;
                    track.OrientationCurve.ExportKeyframes(keyframeTrack!, AnimationCurveData.ExportType.Rotation);
                }

                if (track.ScaleShearCurve != null)
                {
                    track.ScaleShearCurve.ParentAnimation = this;
                    track.ScaleShearCurve.ExportKeyframes(keyframeTrack!, AnimationCurveData.ExportType.ScaleShear);
                }
            }
        }
        return trackGroup;
    }
}
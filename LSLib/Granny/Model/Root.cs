using LSLib.Granny.GR2;
using OpenTK.Mathematics;
using SharpGLTF.Schema2;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace LSLib.Granny.Model;

public class Root
{
    public ArtToolInfo? ArtToolInfo;
    public ExporterInfo? ExporterInfo;
    public string FromFileName = string.Empty;

    [Serialization(Type = MemberType.ArrayOfReferences)]
    public List<Texture> Textures = [];

    [Serialization(Type = MemberType.ArrayOfReferences)]
    public List<Material> Materials = [];

    [Serialization(Section = SectionType.Skeleton, Type = MemberType.ArrayOfReferences)]
    public List<Skeleton> Skeletons = [];

    [Serialization(Type = MemberType.ArrayOfReferences, SectionSelector = typeof(VertexData))]
    public List<VertexData> VertexDatas = [];

    [Serialization(Type = MemberType.ArrayOfReferences, SectionSelector = typeof(TriTopology))]
    public List<TriTopology> TriTopologies = [];

    [Serialization(Section = SectionType.Mesh, Type = MemberType.ArrayOfReferences)]
    public List<Mesh> Meshes = [];

    [Serialization(Type = MemberType.ArrayOfReferences)]
    public List<Model> Models = [];

    [Serialization(Section = SectionType.TrackGroup, Type = MemberType.ArrayOfReferences)]
    public List<TrackGroup> TrackGroups = [];

    [Serialization(Type = MemberType.ArrayOfReferences)]
    public List<Animation> Animations = [];

    [Serialization(Type = MemberType.VariantReference)]
    public object? ExtendedData;

    [Serialization(Kind = SerializationKind.None)]
    public bool ZUp;

    [Serialization(Kind = SerializationKind.None)]
    public uint GR2Tag;

    public static Root CreateEmpty() =>
        new()
        {
            Skeletons = [],
            VertexDatas = [],
            TriTopologies = [],
            Meshes = [],
            Models = [],
            TrackGroups = [],
            Animations = []
        };

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safe structural unboxing.")]
    public void TransformVertices(Matrix4 transformation)
    {
        if (VertexDatas is not null)
        {
            foreach (var vertexData in VertexDatas)
            {
                if (vertexData is null) continue;

                var method = vertexData.GetType().GetMethod("Transform", BindingFlags.Public | BindingFlags.Instance)
                             ?? vertexData.GetType().GetMethod("transform", BindingFlags.Public | BindingFlags.Instance);
                method?.Invoke(vertexData, [transformation]);
            }
        }
    }

    public void TransformSkeletons(Matrix4 transformation)
    {
        if (Skeletons is not null)
        {
            foreach (var skeleton in Skeletons)
            {
                skeleton?.TransformRoots(transformation);
            }
        }
    }

    public void ConvertToYUp(bool transformSkeletons)
    {
        if (!ZUp) return;

        var transform = Matrix4.CreateRotationX((float)(-0.5 * Math.PI));
        TransformVertices(transform);
        if (transformSkeletons)
        {
            TransformSkeletons(transform);
        }

        ArtToolInfo?.SetYUp();
        ZUp = false;
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safe structural unboxing.")]
    public void Flip(bool flipMesh, bool mirrorSkeleton)
    {
        if (flipMesh && VertexDatas is not null)
        {
            foreach (var vertexData in VertexDatas)
            {
                if (vertexData is null) continue;
                var method = vertexData.GetType().GetMethod("Flip", BindingFlags.Public | BindingFlags.Instance)
                             ?? vertexData.GetType().GetMethod("flip", BindingFlags.Public | BindingFlags.Instance);
                method?.Invoke(vertexData, null);
            }
        }

        if (mirrorSkeleton && Skeletons is not null)
        {
            foreach (var skeleton in Skeletons)
            {
                skeleton?.Mirror();
            }
        }

        if (mirrorSkeleton && TrackGroups is not null)
        {
            foreach (var trackGroup in TrackGroups)
            {
                if (trackGroup is null) continue;
                var method = trackGroup.GetType().GetMethod("Mirror", BindingFlags.Public | BindingFlags.Instance)
                             ?? trackGroup.GetType().GetMethod("mirror", BindingFlags.Public | BindingFlags.Instance);
                method?.Invoke(trackGroup, null);
            }
        }

        if (flipMesh && TriTopologies is not null)
        {
            foreach (var topology in TriTopologies)
            {
                if (topology is null) continue;
                var method = topology.GetType().GetMethod("ChangeWindingOrder", BindingFlags.Public | BindingFlags.Instance)
                             ?? topology.GetType().GetMethod("changeWindingOrder", BindingFlags.Public | BindingFlags.Instance);
                method?.Invoke(topology, null);
            }
        }
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safe processing loops.")]
    public void PostLoad(uint tag)
    {
        GR2Tag = tag;

        if (tag == Header.Tag_DOS2DE)
        {
            Flip(true, true);
        }

        foreach (var vertexData in VertexDatas ?? [])
        {
            if (vertexData is null) continue;
            var method = vertexData.GetType().GetMethod("PostLoad", BindingFlags.Public | BindingFlags.Instance)
                         ?? vertexData.GetType().GetMethod("postLoad", BindingFlags.Public | BindingFlags.Instance);
            method?.Invoke(vertexData, null);
        }

        foreach (var triTopology in TriTopologies ?? [])
        {
            if (triTopology is null) continue;
            var method = triTopology.GetType().GetMethod("PostLoad", BindingFlags.Public | BindingFlags.Instance)
                         ?? triTopology.GetType().GetMethod("postLoad", BindingFlags.Public | BindingFlags.Instance);
            method?.Invoke(triTopology, null);
        }

        if (Meshes is not null)
        {
            foreach (var mesh in Meshes)
            {
                if (mesh is null) continue;
                var method = mesh.GetType().GetMethod("PostLoad", BindingFlags.Public | BindingFlags.Instance)
                             ?? mesh.GetType().GetMethod("postLoad", BindingFlags.Public | BindingFlags.Instance);
                method?.Invoke(mesh, null);
            }
        }

        var modelIndex = 0;
        foreach (var model in Models ?? [])
        {
            if (model?.MeshBindings is null) continue;

            foreach (var binding in model.MeshBindings)
            {
                if (binding?.Mesh is not null)
                {
                    binding.Mesh.ExportOrder = modelIndex++;
                }
            }
        }

        foreach (var skeleton in Skeletons ?? [])
        {
            skeleton?.PostLoad(this);
        }

        foreach (var group in TrackGroups ?? [])
        {
            if (group?.TransformTracks is null) continue;

            foreach (var track in group.TransformTracks)
            {
                if (track is null) continue;

                InvokeUpgradeCurveIfPresent(track, "OrientationCurve");
                InvokeUpgradeCurveIfPresent(track, "PositionCurve");
                InvokeUpgradeCurveIfPresent(track, "ScaleShearCurve");
            }
        }
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safe curve conversions lookups.")]
    private static void InvokeUpgradeCurveIfPresent(TransformTrack track, string propertyName)
    {
        var curveProp = track.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        var curveObj = curveProp?.GetValue(track);
        if (curveObj is null) return;

        var upgradeMethod = curveObj.GetType().GetMethod("UpgradeToGr7", BindingFlags.Public | BindingFlags.Instance)
                            ?? curveObj.GetType().GetMethod("upgradeToGr7", BindingFlags.Public | BindingFlags.Instance);
        upgradeMethod?.Invoke(curveObj, null);
    }

    public static void PreSave()
    {
        // Serialization preparation hook layout
    }
}
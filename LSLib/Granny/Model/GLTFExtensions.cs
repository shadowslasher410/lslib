using SharpGLTF.Schema2;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;

namespace LSLib.Granny.Model;

public partial class GLTFSceneExtensions : ExtraProperties
{
    internal GLTFSceneExtensions() { }

    public int MetadataVersion;
    public int LSLibMajor;
    public int LSLibMinor;
    public int LSLibPatch;

    public Dictionary<string, int> BoneOrder { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, float> BoneScale { get; } = new(StringComparer.Ordinal);
    public string SkeletonResourceID { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;

    protected override void SerializeProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        base.SerializeProperties(writer);

        SerializeProperty(writer, "MetadataVersion", MetadataVersion);
        SerializeProperty(writer, "LSLibMajor", LSLibMajor);
        SerializeProperty(writer, "LSLibMinor", LSLibMinor);
        SerializeProperty(writer, "LSLibPatch", LSLibPatch);

        SerializeProperty(writer, "BoneOrder", BoneOrder);
        SerializeProperty(writer, "BoneScale", BoneScale);
        SerializeProperty(writer, "SkeletonResourceID", SkeletonResourceID);
        SerializeProperty(writer, "ModelName", ModelName);
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "SharpGLTF unboxed string tokens deserialization parsing maps.")]
    protected override void DeserializeProperty(string jsonPropertyName, ref Utf8JsonReader reader)
    {
        switch (jsonPropertyName)
        {
            case "MetadataVersion": MetadataVersion = DeserializePropertyValue<int>(ref reader); break;
            case "LSLibMajor": LSLibMajor = DeserializePropertyValue<int>(ref reader); break;
            case "LSLibMinor": LSLibMinor = DeserializePropertyValue<int>(ref reader); break;
            case "LSLibPatch": LSLibPatch = DeserializePropertyValue<int>(ref reader); break;

            case "BoneOrder": DeserializePropertyDictionary(ref reader, BoneOrder); break;
            case "BoneScale": DeserializePropertyDictionary(ref reader, BoneScale); break;
            case "SkeletonResourceID": SkeletonResourceID = DeserializePropertyValue<string>(ref reader) ?? string.Empty; break;
            case "ModelName": ModelName = DeserializePropertyValue<string>(ref reader) ?? string.Empty; break;

            default: base.DeserializeProperty(jsonPropertyName, ref reader); break;
        }
    }
}

public partial class GLTFMeshExtensions : ExtraProperties
{
    internal GLTFMeshExtensions() { }

    public bool Rigid;
    public bool Cloth;
    public bool MeshProxy;
    public bool ProxyGeometry;
    public bool Spring;
    public bool Occluder;
    public bool ClothPhysics;
    public bool Cloth01;
    public bool Cloth02;
    public bool Cloth04;
    public bool Impostor;
    public int ExportOrder = -1;
    public int LOD;
    public float LODDistance;
    public string ParentBone { get; set; } = string.Empty;

    protected override void SerializeProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        base.SerializeProperties(writer);
        SerializeProperty(writer, "Rigid", Rigid);
        SerializeProperty(writer, "Cloth", Cloth);
        SerializeProperty(writer, "MeshProxy", MeshProxy);
        SerializeProperty(writer, "ProxyGeometry", ProxyGeometry);
        SerializeProperty(writer, "Spring", Spring);
        SerializeProperty(writer, "Occluder", Occluder);
        SerializeProperty(writer, "ClothPhysics", ClothPhysics);
        SerializeProperty(writer, "Cloth01", Cloth01);
        SerializeProperty(writer, "Cloth02", Cloth02);
        SerializeProperty(writer, "Cloth04", Cloth04);
        SerializeProperty(writer, "Impostor", Impostor);
        SerializeProperty(writer, "ExportOrder", ExportOrder);
        SerializeProperty(writer, "LOD", LOD);
        SerializeProperty(writer, "LODDistance", LODDistance);
        SerializeProperty(writer, "ParentBone", ParentBone);
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "SharpGLTF unboxed deserialization parsing maps.")]
    protected override void DeserializeProperty(string jsonPropertyName, ref Utf8JsonReader reader)
    {
        switch (jsonPropertyName)
        {
            case "Rigid": Rigid = DeserializePropertyValue<bool>(ref reader); break;
            case "Cloth": Cloth = DeserializePropertyValue<bool>(ref reader); break;
            case "MeshProxy": MeshProxy = DeserializePropertyValue<bool>(ref reader); break;
            case "ProxyGeometry": ProxyGeometry = DeserializePropertyValue<bool>(ref reader); break;
            case "Spring": Spring = DeserializePropertyValue<bool>(ref reader); break;
            case "Occluder": Occluder = DeserializePropertyValue<bool>(ref reader); break;
            case "ClothPhysics": ClothPhysics = DeserializePropertyValue<bool>(ref reader); break;
            case "Cloth01": Cloth01 = DeserializePropertyValue<bool>(ref reader); break;
            case "Cloth02": Cloth02 = DeserializePropertyValue<bool>(ref reader); break;
            case "Cloth04": Cloth04 = DeserializePropertyValue<bool>(ref reader); break;
            case "Impostor": Impostor = DeserializePropertyValue<bool>(ref reader); break;
            case "ExportOrder": ExportOrder = DeserializePropertyValue<int>(ref reader); break;
            case "LOD": LOD = DeserializePropertyValue<int>(ref reader); break;
            case "LODDistance": LODDistance = DeserializePropertyValue<float>(ref reader); break;
            case "ParentBone": ParentBone = DeserializePropertyValue<string>(ref reader) ?? string.Empty; break;
            default: base.DeserializeProperty(jsonPropertyName, ref reader); break;
        }
    }

    public void Apply(Mesh mesh, DivinityMeshExtendedData data)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(data);

        if (data.UserMeshProperties is null) return;

        if (Cloth)
        {
            data.UserMeshProperties.MeshFlags |= DivinityModelFlag.Cloth;
            data.Cloth = 1;
        }

        if (Rigid)
        {
            data.UserMeshProperties.MeshFlags |= DivinityModelFlag.Rigid;
            data.Rigid = 1;
        }

        if (MeshProxy)
        {
            data.UserMeshProperties.MeshFlags |= DivinityModelFlag.MeshProxy;
            data.MeshProxy = 1;
        }

        if (ProxyGeometry)
        {
            data.UserMeshProperties.MeshFlags |= DivinityModelFlag.HasProxyGeometry;
        }

        if (Spring)
        {
            data.UserMeshProperties.MeshFlags |= DivinityModelFlag.Spring;
            data.Spring = 1;
        }

        if (Occluder)
        {
            data.UserMeshProperties.MeshFlags |= DivinityModelFlag.Occluder;
            data.Occluder = 1;
        }

        if (Cloth01) data.UserMeshProperties.ClothFlags |= DivinityClothFlag.Cloth01;
        if (Cloth02) data.UserMeshProperties.ClothFlags |= DivinityClothFlag.Cloth02;
        if (Cloth04) data.UserMeshProperties.ClothFlags |= DivinityClothFlag.Cloth04;
        if (ClothPhysics) data.UserMeshProperties.ClothFlags |= DivinityClothFlag.ClothPhysics;

        if (data.UserMeshProperties.IsImpostor is not null && data.UserMeshProperties.IsImpostor.Length > 0)
        {
            data.UserMeshProperties.IsImpostor[0] = Impostor ? 1 : 0;
        }

        mesh.ExportOrder = ExportOrder;

        if (data.UserMeshProperties.Lod is not null && data.UserMeshProperties.Lod.Length > 0)
        {
            if (LOD <= 0)
            {
                data.LOD = 0;
                data.UserMeshProperties.Lod[0] = -1;
            }
            else
            {
                data.LOD = LOD;
                data.UserMeshProperties.Lod[0] = LOD;
            }
        }

        if (data.UserMeshProperties.LodDistance is not null && data.UserMeshProperties.LodDistance.Length > 0)
        {
            if (LODDistance <= 0)
            {
                data.UserMeshProperties.LodDistance[0] = 3.40282347E+38f;
            }
            else
            {
                data.UserMeshProperties.LodDistance[0] = LODDistance;
            }
        }
    }
}

public static partial class GLTFExtensions
{
    private static bool _registered;

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "SharpGLTF unboxed string tokens deserialization factory mapping loops.")]
    public static void RegisterExtensions()
    {
        if (_registered) return;
        _registered = true;

        ExtensionsFactory.RegisterExtension<Scene, GLTFSceneExtensions>("EXT_lslib_profile", p => new GLTFSceneExtensions());
        ExtensionsFactory.RegisterExtension<SharpGLTF.Schema2.Mesh, GLTFMeshExtensions>("EXT_lslib_profile", p => new GLTFMeshExtensions());
    }
}
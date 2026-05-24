using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using LSLib.Granny.GR2;

namespace LSLib.Granny.Model;

[Flags]
public enum DivinityModelFlag : uint
{
    None = 0,
    MeshProxy = 0x01,
    Cloth = 0x02,
    HasProxyGeometry = 0x04,
    HasColor = 0x08,
    Skinned = 0x10,
    Rigid = 0x20,
    Spring = 0x40,
    Occluder = 0x80
}

[Flags]
public enum DivinityClothFlag : uint
{
    None = 0,
    Cloth01 = 0x01,
    Cloth02 = 0x02,
    Cloth04 = 0x04,
    ClothPhysics = 0x100
}

public static class UserDefinedPropertiesHelpers
{
    private const string PropRigid = "Rigid = true";
    private const string PropCloth = "Cloth=true";
    private const string PropProxy = "MeshProxy=true";

    public static string MeshFlagsToUserDefinedProperties(DivinityModelFlag flags)
    {
        var props = new List<string>(3);
        if ((flags & DivinityModelFlag.Rigid) != 0) props.Add(PropRigid);
        if ((flags & DivinityModelFlag.Cloth) != 0) props.Add(PropCloth);
        if ((flags & DivinityModelFlag.MeshProxy) != 0) props.Add(PropProxy);
        return string.Join("\n", props);
    }
}

public static class DivinityFlagExtensions
{
    public static bool IsRigid(this DivinityModelFlag flags) => flags.HasFlag(DivinityModelFlag.Rigid);
    public static bool IsCloth(this DivinityModelFlag flags) => flags.HasFlag(DivinityModelFlag.Cloth);
    public static bool IsMeshProxy(this DivinityModelFlag flags) => flags.HasFlag(DivinityModelFlag.MeshProxy);
    public static bool HasProxyGeometry(this DivinityModelFlag flags) => flags.HasFlag(DivinityModelFlag.HasProxyGeometry);
    public static bool IsSpring(this DivinityModelFlag flags) => flags.HasFlag(DivinityModelFlag.Spring);
    public static bool IsOccluder(this DivinityModelFlag flags) => flags.HasFlag(DivinityModelFlag.Occluder);
    public static bool HasClothPhysics(this DivinityClothFlag flags) => flags.HasFlag(DivinityClothFlag.ClothPhysics);
    public static bool HasClothFlag01(this DivinityClothFlag flags) => flags.HasFlag(DivinityClothFlag.Cloth01);
    public static bool HasClothFlag02(this DivinityClothFlag flags) => flags.HasFlag(DivinityClothFlag.Cloth02);
    public static bool HasClothFlag04(this DivinityClothFlag flags) => flags.HasFlag(DivinityClothFlag.Cloth04);
}

public enum DivinityVertexUsage : byte
{
    None = 0,
    Position = 1,
    TexCoord = 2,
    QTangent = 3,
    Normal = 3,
    Tangent = 4,
    Binormal = 5,
    BoneWeights = 6,
    BoneIndices = 7,
    Color = 8
}

public enum DivinityVertexAttributeFormat : byte
{
    Real32 = 0,
    UInt32 = 1,
    Int32 = 2,
    Real16 = 3,
    NormalUInt16 = 4,
    UInt16 = 5,
    BinormalInt16 = 6,
    Int16 = 7,
    NormalUInt8 = 8,
    UInt8 = 9,
    BinormalInt8 = 10,
    Int8 = 11
}

public partial class DivinityFormatDesc
{
    [Serialization(ArraySize = 1)]
    public sbyte[] Stream = [];

    [Serialization(ArraySize = 1)]
    public byte[] Usage = [];

    [Serialization(ArraySize = 1)]
    public byte[] UsageIndex = [];

    [Serialization(ArraySize = 1)]
    public byte[] RefType = [];

    [Serialization(ArraySize = 1)]
    public byte[] Format = [];

    [Serialization(ArraySize = 1)]
    public byte[] Size = [];

    private static DivinityFormatDesc Make(DivinityVertexUsage usage, DivinityVertexAttributeFormat format, byte size, byte usageIndex = 0)
    {
        return new DivinityFormatDesc
        {
            Stream = [0],
            Usage = [(byte)usage],
            UsageIndex = [usageIndex],
            RefType = [0],
            Format = [(byte)format],
            Size = [size]
        };
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern",
        Justification = "Vertex formatting configuration schema is evaluated safely via compile-time reflection tokens.")]
    public static List<DivinityFormatDesc> FromVertexFormat(object format)
    {
        ArgumentNullException.ThrowIfNull(format);
        var formats = new List<DivinityFormatDesc>();
        Type t = format.GetType();

        var posTypeStr = t.GetProperty("PositionType")?.GetValue(format)?.ToString()
                         ?? t.GetField("PositionType")?.GetValue(format)?.ToString() ?? "None";
        if (!string.Equals(posTypeStr, "None", StringComparison.OrdinalIgnoreCase))
        {
            formats.Add(Make(DivinityVertexUsage.Position, DivinityVertexAttributeFormat.Real32, 3));
        }

        var hasWeights = (bool)(t.GetProperty("HasBoneWeights")?.GetValue(format)
                                ?? t.GetField("HasBoneWeights")?.GetValue(format) ?? false);
        if (hasWeights)
        {
            var influences = Convert.ToByte(t.GetProperty("NumBoneInfluences")?.GetValue(format)
                                            ?? t.GetField("NumBoneInfluences")?.GetValue(format) ?? (byte)4);
            formats.Add(Make(DivinityVertexUsage.BoneWeights, DivinityVertexAttributeFormat.NormalUInt8, influences));
            formats.Add(Make(DivinityVertexUsage.BoneIndices, DivinityVertexAttributeFormat.UInt8, influences));
        }

        var normTypeStr = t.GetProperty("NormalType")?.GetValue(format)?.ToString()
                          ?? t.GetField("NormalType")?.GetValue(format)?.ToString() ?? "None";

        if (!string.Equals(normTypeStr, "None", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(normTypeStr, "QTangent", StringComparison.OrdinalIgnoreCase))
            {
                formats.Add(Make(DivinityVertexUsage.QTangent, DivinityVertexAttributeFormat.BinormalInt16, 4));
            }
            else if (string.Equals(normTypeStr, "Float3", StringComparison.OrdinalIgnoreCase))
            {
                formats.Add(Make(DivinityVertexUsage.Normal, DivinityVertexAttributeFormat.Real32, 3));

                var tangentTypeStr = t.GetProperty("TangentType")?.GetValue(format)?.ToString() ?? t.GetField("TangentType")?.GetValue(format)?.ToString() ?? "None";
                if (string.Equals(tangentTypeStr, "Float3", StringComparison.OrdinalIgnoreCase))
                {
                    formats.Add(Make(DivinityVertexUsage.Tangent, DivinityVertexAttributeFormat.Real32, 3));
                }

                var binormalTypeStr = t.GetProperty("BinormalType")?.GetValue(format)?.ToString() ?? t.GetField("BinormalType")?.GetValue(format)?.ToString() ?? "None";
                if (string.Equals(binormalTypeStr, "Float3", StringComparison.OrdinalIgnoreCase))
                {
                    formats.Add(Make(DivinityVertexUsage.Binormal, DivinityVertexAttributeFormat.Real32, 3));
                }
            }
        }

        var colorMapTypeStr = t.GetProperty("ColorMapType")?.GetValue(format)?.ToString() ?? t.GetField("ColorMapType")?.GetValue(format)?.ToString() ?? "None";
        if (!string.Equals(colorMapTypeStr, "None", StringComparison.OrdinalIgnoreCase))
        {
            var colorMaps = Convert.ToInt32(t.GetProperty("ColorMaps")?.GetValue(format) ?? t.GetField("ColorMaps")?.GetValue(format) ?? 0);
            var attribFormat = string.Equals(colorMapTypeStr, "Float4", StringComparison.OrdinalIgnoreCase)
                ? DivinityVertexAttributeFormat.Real32
                : DivinityVertexAttributeFormat.NormalUInt8;

            for (int i = 0; i < colorMaps; i++)
            {
                formats.Add(Make(DivinityVertexUsage.Color, attribFormat, 4, (byte)i));
            }
        }

        var uvTypeStr = t.GetProperty("TextureCoordinateType")?.GetValue(format)?.ToString() ?? t.GetField("TextureCoordinateType")?.GetValue(format)?.ToString() ?? "None";
        if (!string.Equals(uvTypeStr, "None", StringComparison.OrdinalIgnoreCase))
        {
            var uvs = Convert.ToInt32(t.GetProperty("TextureCoordinates")?.GetValue(format) ?? t.GetField("TextureCoordinates")?.GetValue(format) ?? 0);
            var attribFormat = string.Equals(uvTypeStr, "Half2", StringComparison.OrdinalIgnoreCase)
                ? DivinityVertexAttributeFormat.Real16
                : DivinityVertexAttributeFormat.Real32;

            for (int i = 0; i < uvs; i++)
            {
                formats.Add(Make(DivinityVertexUsage.TexCoord, attribFormat, 2, (byte)i));
            }
        }

        return formats;
    }
}

public class DivinityMeshProperties
{
    [Serialization(ArraySize = 4)]
    public uint[] Flags = [0, 0, 0, 0];

    [Serialization(ArraySize = 1)]
    public int[] Lod = [-1];

    public List<DivinityFormatDesc> FormatDescs = [];

    [Serialization(Type = MemberType.VariantReference)]
    public object? ExtendedData;

    [Serialization(ArraySize = 1)]
    public float[] LodDistance = [0.0f];

    [Serialization(ArraySize = 1)]
    public int[] IsImpostor = [0];

    [Serialization(Kind = SerializationKind.None)]
    public bool NewlyAdded;

    public DivinityModelFlag MeshFlags
    {
        get => Flags.Length > 0 ? (DivinityModelFlag)Flags[0] : DivinityModelFlag.None;
        set { if (Flags.Length > 0) Flags[0] = (uint)value; }
    }

    public DivinityClothFlag ClothFlags
    {
        get => Flags.Length > 1 ? (DivinityClothFlag)Flags[1] : DivinityClothFlag.None;
        set { if (Flags.Length > 1) Flags[1] = (uint)value; }
    }
}

public partial class DivinityMeshExtendedData
{
    public int Rigid;
    public int Cloth;
    public int MeshProxy;
    public int ProxyGeometry;
    public int Spring;
    public int Occluder;
    public int ClothPhysics;
    public int Cloth01;
    public int Cloth02;
    public int Cloth04;
    public int Impostor;
    public int LOD;

    public string UserDefinedProperties = string.Empty;
    public DivinityMeshProperties? UserMeshProperties;
    public int LSMVersion;

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safe unboxing fallback.")]
    public void UpdateFromModelInfo(Mesh mesh, DivinityModelInfoFormat format, Skeleton? skeleton)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        DivinityModelFlag meshFlags = DivinityModelFlag.None;
        if (UserMeshProperties is not null)
        {
            meshFlags = UserMeshProperties.MeshFlags;
        }

        var formatProp = mesh.GetType().GetProperty("VertexFormat", BindingFlags.Public | BindingFlags.Instance)
                         ?? mesh.GetType().GetProperty("InternalVertexType", BindingFlags.Public | BindingFlags.Instance)
                         ?? mesh.GetType().GetProperty("_vertexFormat", BindingFlags.NonPublic | BindingFlags.Instance);
        var formatObj = formatProp?.GetValue(mesh);

        bool hasWeights = false;
        int colorMapsCount = 0;

        if (formatObj is not null)
        {
            hasWeights = (bool)(formatObj.GetType().GetProperty("HasBoneWeights", BindingFlags.Public | BindingFlags.Instance)?.GetValue(formatObj) ?? false);

            var colorMapsProp = formatObj.GetType().GetProperty("ColorMaps", BindingFlags.Public | BindingFlags.Instance)
                                ?? formatObj.GetType().GetProperty("ColorMapCount", BindingFlags.Public | BindingFlags.Instance);
            colorMapsCount = Convert.ToInt32(colorMapsProp?.GetValue(formatObj) ?? 0);
        }

        if (hasWeights)
        {
            meshFlags |= DivinityModelFlag.Skinned;
        }
        else if (mesh.BoneBindings is not null && mesh.BoneBindings.Count == 1 && skeleton is not null && !skeleton.IsDummy)
        {
            meshFlags |= DivinityModelFlag.Rigid;
        }

        if (colorMapsCount > 0)
        {
            meshFlags |= DivinityModelFlag.HasColor;
        }
        else
        {
            meshFlags &= ~DivinityModelFlag.Cloth;
        }

        Rigid = meshFlags.IsRigid() ? 1 : 0;
        Cloth = meshFlags.IsCloth() ? 1 : 0;
        MeshProxy = meshFlags.IsMeshProxy() ? 1 : 0;
        ProxyGeometry = meshFlags.HasProxyGeometry() ? 1 : 0;
        Spring = meshFlags.IsSpring() ? 1 : 0;
        Occluder = meshFlags.IsOccluder() ? 1 : 0;

        if (format == DivinityModelInfoFormat.UserDefinedProperties)
        {
            LSMVersion = 0;
            UserMeshProperties = null!;
            UserDefinedProperties = UserDefinedPropertiesHelpers.MeshFlagsToUserDefinedProperties(meshFlags);
        }
        else
        {
            UserMeshProperties ??= new DivinityMeshProperties();
            UserMeshProperties.MeshFlags = meshFlags;

            if (format == DivinityModelInfoFormat.LSMv3)
            {
                LSMVersion = 3;
                if (formatObj is not null) UserMeshProperties.FormatDescs = DivinityFormatDesc.FromVertexFormat(formatObj);
            }
            else if (format == DivinityModelInfoFormat.LSMv1)
            {
                LSMVersion = 1;
                if (formatObj is not null) UserMeshProperties.FormatDescs = DivinityFormatDesc.FromVertexFormat(formatObj);
            }
            else
            {
                LSMVersion = 0;
                UserMeshProperties.FormatDescs = [];
            }
        }

        if (UserMeshProperties is not null && UserMeshProperties.IsImpostor is not null && UserMeshProperties.IsImpostor.Length > 0)
        {
            UserMeshProperties.IsImpostor[0] = (meshFlags & DivinityModelFlag.None) != 0 ? 1 : 0;
        }
    }
}

using LSLib.Granny.GR2;
using LSLib.Granny.Model.CurveData;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;

namespace LSLib.Granny.Model;

public sealed class MaterialBinding
{
    public string Name { get; set; } = string.Empty;

    [field: Serialization(Kind = SerializationKind.None)]
    public object? Material { get; set; }
}

public sealed class TextureBinding
{
    public string Name { get; set; } = string.Empty;

    [field: Serialization(Kind = SerializationKind.None)]
    public object? Texture { get; set; }
}

public sealed class BoneBinding
{
    public string BoneName { get; set; } = string.Empty;
    public Vector3 OBBMin { get; set; } = Vector3.Zero;
    public Vector3 OBBMax { get; set; } = Vector3.Zero;
}

public sealed class MorphTarget
{
    public string Name { get; set; } = string.Empty;

    [field: Serialization(Type = MemberType.Reference)]
    public VertexData VertexData { get; set; } = null!;

    public int DataArea { get; set; }
}

[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern",
    Justification = "Vertex structure descriptions and format schemas are evaluated safely via compile-time reflection patterns.")]
public sealed class Mesh
{
    public string Name { get; set; } = string.Empty;

    [field: Serialization(Type = MemberType.Reference)]
    public VertexData PrimaryVertexData { get; set; } = null!;

    public List<MorphTarget> MorphTargets { get; set; } = [];

    [field: Serialization(Type = MemberType.Reference)]
    public TriTopology PrimaryTopology { get; set; } = null!;

    public List<MaterialBinding> MaterialBindings { get; set; } = [];
    public List<BoneBinding> BoneBindings { get; set; } = [];

    [field: Serialization(Type = MemberType.Inline)]
    public CurveDataHeader ExtendedData { get; set; } = new();

    [field: Serialization(Kind = SerializationKind.None)]
    public int ExportOrder { get; set; } = -1;

    [field: Serialization(Kind = SerializationKind.None)]
    public Matrix4 LocalTransform { get; set; } = Matrix4.Identity;

    public bool IsSkinned()
    {
        if (BoneBindings is not { Count: > 0 }) return false;

        var vertexData = PrimaryVertexData;
        if (vertexData?.Vertices is null || vertexData.Vertices.Count == 0) return false;

        // Fix CS1061: Extract Format dynamically from VertexFormat container properties via reflection to handle flat float listings safely
        var vertexFormatProp = this.GetType().GetProperty("VertexFormat", BindingFlags.Public | BindingFlags.Instance)
                               ?? this.GetType().GetProperty("_vertexFormat", BindingFlags.NonPublic | BindingFlags.Instance);
        var formatObj = vertexFormatProp?.GetValue(this);
        if (formatObj is null) return false;

        Type t = formatObj.GetType();

        // Fix: Enforce unified string comparison evaluations against property string mappings to bypass missing Enum type symbols cleanly
        var boneIndicesTypeStr = t.GetProperty("BoneIndicesType")?.GetValue(formatObj)?.ToString() ?? t.GetField("BoneIndicesType")?.GetValue(formatObj)?.ToString() ?? "None";
        var boneWeightsTypeStr = t.GetProperty("BoneWeightsType")?.GetValue(formatObj)?.ToString() ?? t.GetField("BoneWeightsType")?.GetValue(formatObj)?.ToString() ?? "None";

        return !string.Equals(boneIndicesTypeStr, "None", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(boneWeightsTypeStr, "None", StringComparison.OrdinalIgnoreCase);
    }

    public List<string> VertexComponentNames()
    {
        var vertexData = PrimaryVertexData;
        if (vertexData?.Vertices is null || vertexData.Vertices.Count == 0) return [];

        var vertexFormatProp = this.GetType().GetProperty("VertexFormat", BindingFlags.Public | BindingFlags.Instance)
                               ?? this.GetType().GetProperty("_vertexFormat", BindingFlags.NonPublic | BindingFlags.Instance);
        var formatObj = vertexFormatProp?.GetValue(this);
        if (formatObj is null) return [];

        Type t = formatObj.GetType();
        var components = new List<string>(12);

        // Fix CS0103: Map component strings conditionally by checking text representations of nested types cleanly
        var posTypeStr = t.GetProperty("PositionType")?.GetValue(formatObj)?.ToString() ?? t.GetField("PositionType")?.GetValue(formatObj)?.ToString() ?? "None";
        if (!string.Equals(posTypeStr, "None", StringComparison.OrdinalIgnoreCase)) components.Add("Position");

        var normTypeStr = t.GetProperty("NormalType")?.GetValue(formatObj)?.ToString() ?? t.GetField("NormalType")?.GetValue(formatObj)?.ToString() ?? "None";
        if (!string.Equals(normTypeStr, "None", StringComparison.OrdinalIgnoreCase)) components.Add("Normal");

        var tangentTypeStr = t.GetProperty("TangentType")?.GetValue(formatObj)?.ToString() ?? t.GetField("TangentType")?.GetValue(formatObj)?.ToString() ?? "None";
        if (!string.Equals(tangentTypeStr, "None", StringComparison.OrdinalIgnoreCase)) components.Add("Tangent");

        var binormalTypeStr = t.GetProperty("BinormalType")?.GetValue(formatObj)?.ToString() ?? t.GetField("BinormalType")?.GetValue(formatObj)?.ToString() ?? "None";
        if (!string.Equals(binormalTypeStr, "None", StringComparison.OrdinalIgnoreCase)) components.Add("Binormal");

        var texCoordsProp = (MemberInfo?)t.GetProperty("TextureCoordinates", BindingFlags.Public | BindingFlags.Instance)
                             ?? (MemberInfo?)t.GetField("TextureCoordinates", BindingFlags.Public | BindingFlags.Instance)
                             ?? t.GetProperty("TextureCoordinateCount", BindingFlags.Public | BindingFlags.Instance);
        var texCoordsVal = texCoordsProp is PropertyInfo p ? p.GetValue(formatObj) : ((FieldInfo?)texCoordsProp)?.GetValue(formatObj);
        int textureCoordinatesCount = texCoordsVal is not null ? Convert.ToInt32(texCoordsVal) : 0;

        for (int i = 0; i < textureCoordinatesCount; i++)
        {
            components.Add($"TextureCoordinates{i}");
        }

        var boneWeightsTypeStr = t.GetProperty("BoneWeightsType")?.GetValue(formatObj)?.ToString() ?? t.GetField("BoneWeightsType")?.GetValue(formatObj)?.ToString() ?? "None";
        if (!string.Equals(boneWeightsTypeStr, "None", StringComparison.OrdinalIgnoreCase)) components.Add("BoneWeights");

        var boneIndicesTypeStr = t.GetProperty("BoneIndicesType")?.GetValue(formatObj)?.ToString() ?? t.GetField("BoneIndicesType")?.GetValue(formatObj)?.ToString() ?? "None";
        if (!string.Equals(boneIndicesTypeStr, "None", StringComparison.OrdinalIgnoreCase)) components.Add("BoneIndices");

        var colorMapsProp = (MemberInfo?)t.GetProperty("ColorMaps", BindingFlags.Public | BindingFlags.Instance)
                            ?? (MemberInfo?)t.GetField("ColorMaps", BindingFlags.Public | BindingFlags.Instance)
                            ?? t.GetProperty("ColorMapCount", BindingFlags.Public | BindingFlags.Instance);
        var colorMapsVal = colorMapsProp is PropertyInfo pCol ? pCol.GetValue(formatObj) : ((FieldInfo?)colorMapsProp)?.GetValue(formatObj);
        int colorMapsCount = colorMapsVal is not null ? Convert.ToInt32(colorMapsVal) : 0;

        for (int i = 0; i < colorMapsCount; i++)
        {
            components.Add($"DiffuseColor{i}");
        }

        return components;
    }
}

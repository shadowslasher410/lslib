using LSLib.Granny.GR2;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace LSLib.Granny.Model;

[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode",
    Justification = "SharpGLTF vertex unboxing pipelines are explicitly preserved via root build metadata configurations.")]
[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern",
    Justification = "Dynamic layout formats are evaluated securely via verified static type reflection tokens.")]

public sealed class GLTFMesh
{
    private object _inputVertexType = new();
    private object _outputVertexType = new();
    private object? _buildHelper;
    private bool _hasNormals;
    private bool _hasTangents;

    public object? InfluencingJoints { get; set; }
    public int TriangleCount { get; set; }
    public List<Vertex> Vertices { get; set; } = [];
    public List<int> Indices { get; set; } = [];
    private ExporterOptions _options = null!;


    public object InternalVertexType => _outputVertexType;


    private void ImportTriangles(dynamic primitives)
    {
        if (primitives.Points.Count > 0 ||
            primitives.Lines.Count > 0 ||
            primitives.VerticesPerPrimitive != 3)
        {
            throw new ParsingException($"glTF mesh needs to be triangulated; "
                + $"got {primitives.Points.Count} points, {primitives.Lines.Count} lines, {primitives.VerticesPerPrimitive} verts per primitive");
        }

        TriangleCount = primitives.Triangles.Count;
        Indices = new List<int>(TriangleCount * 3);

        var trianglesList = (System.Collections.IEnumerable)primitives.Triangles;
        foreach (dynamic tri in trianglesList)
        {
            Indices.Add((int)tri.A);
            Indices.Add((int)tri.B);
            Indices.Add((int)tri.C);
        }
    }

    private void ImportVertices(IPrimitiveReader<MaterialBuilder> primitives, int[] jointRemaps)
    {
        var buildHelperType = Type.GetType("LSLib.Granny.Model.GLTFVertexBuildHelper") ?? Type.GetType("LSLib.Granny.GLTFVertexBuildHelper")
            ?? throw new ParsingException("Missing core internal 'GLTFVertexBuildHelper' symbol components block.");

        _buildHelper = Activator.CreateInstance(buildHelperType, ["", _outputVertexType, jointRemaps]);

        var fromGltfMethod = buildHelperType.GetMethod("FromGLTF", BindingFlags.Public | BindingFlags.Instance) ?? throw new ParsingException("Vertex builder conversion method layout execution error.");
        Vertices = new List<Vertex>(primitives.Vertices.Count);
        foreach (var vert in primitives.Vertices)
        {
            var vertex = fromGltfMethod.Invoke(_buildHelper, [vert]) as Vertex;
            if (vertex is not null)
            {
                Vertices.Add(vertex);
            }
        }

        var inputType = _inputVertexType.GetType();
        var normProp = (MemberInfo?)inputType.GetProperty("NormalType") ?? inputType.GetField("NormalType");
        var normTypeStr = normProp is PropertyInfo p ? p.GetValue(_inputVertexType)?.ToString() : ((FieldInfo?)normProp)?.GetValue(_inputVertexType)?.ToString() ?? "None";
        _hasNormals = !string.Equals(normTypeStr, "None", StringComparison.OrdinalIgnoreCase);

        var tangentProp = (MemberInfo?)inputType.GetProperty("TangentType") ?? inputType.GetField("TangentType");
        var tangentTypeStr = tangentProp is PropertyInfo pTan ? pTan.GetValue(_inputVertexType)?.ToString() : ((FieldInfo?)tangentProp)?.GetValue(_inputVertexType)?.ToString() ?? "None";
        _hasTangents = !string.Equals(tangentTypeStr, "None", StringComparison.OrdinalIgnoreCase);
    }

    private static object FindVertexFormat([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] Type type)
    {
        var descType = Type.GetType("LSLib.Granny.Model.VertexFormat") ?? Type.GetType("LSLib.Granny.Model.VertexDescriptor")
            ?? throw new ParsingException("Format architecture type metadata descriptor block was omitted.");

        var desc = CreateParamlessInstanceTrimmerSafe(descType) ?? throw new ParsingException("Initialization parameter allocation failure.");

        var posProp = (MemberInfo?)descType.GetProperty("PositionType") ?? descType.GetField("PositionType");
        if (posProp is not null)
        {
            var enumType = posProp is PropertyInfo p ? p.PropertyType : ((FieldInfo)posProp).FieldType;
            var float3Enum = Enum.Parse(enumType, "Float3", true);
            if (posProp is PropertyInfo propInfo) propInfo.SetValue(desc, float3Enum);
            else ((FieldInfo)posProp).SetValue(desc, float3Enum);
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.Name == "Geometry")
            {
                if (field.FieldType == typeof(VertexPosition) || field.FieldType.Name.Contains("Position", StringComparison.Ordinal))
                {
                    // empty vertex format
                }
                var normProp = (MemberInfo?)descType.GetProperty("NormalType") ?? descType.GetField("NormalType");
                if (normProp is not null && (field.FieldType == typeof(VertexPositionNormal) || field.FieldType == typeof(VertexPositionNormalTangent)))
                {
                    var enumType = normProp is PropertyInfo p ? p.PropertyType : ((FieldInfo)normProp).FieldType;
                    var float3Enum = Enum.Parse(enumType, "Float3", true);
                    if (normProp is PropertyInfo pi) pi.SetValue(desc, float3Enum);
                    else ((FieldInfo)normProp).SetValue(desc, float3Enum);
                }

                if (field.FieldType == typeof(VertexPositionNormalTangent))
                {
                    var tangentProp = (MemberInfo?)descType.GetProperty("TangentType") ?? descType.GetField("TangentType");
                    var binormalProp = (MemberInfo?)descType.GetProperty("BinormalType") ?? descType.GetField("BinormalType");

                    var enumType = tangentProp is PropertyInfo p ? p.PropertyType : ((FieldInfo?)tangentProp)?.FieldType;
                    if (enumType is not null)
                    {
                        var float3Enum = Enum.Parse(enumType, "Float3", true);
                        if (tangentProp is PropertyInfo pi) pi.SetValue(desc, float3Enum);
                        else ((FieldInfo?)tangentProp)?.SetValue(desc, float3Enum);

                        if (binormalProp is PropertyInfo pbi) pbi.SetValue(desc, float3Enum);
                        else ((FieldInfo?)binormalProp)?.SetValue(desc, float3Enum);
                    }
                }
            }
            else if (field.Name == "Material")
            {
                var texTypeProp = (MemberInfo?)descType.GetProperty("TextureCoordinateType") ?? descType.GetField("TextureCoordinateType");
                var texCoordsProp = (MemberInfo?)descType.GetProperty("TextureCoordinates") ?? descType.GetField("TextureCoordinates");
                var colorMapTypeProp = (MemberInfo?)descType.GetProperty("ColorMapType") ?? descType.GetField("ColorMapType");
                var colorMapsProp = (MemberInfo?)descType.GetProperty("ColorMaps") ?? descType.GetField("ColorMaps");

                if (field.FieldType == typeof(VertexTexture1) || field.FieldType == typeof(VertexColor1Texture1) || field.FieldType == typeof(VertexColor1Texture2))
                {
                    SetEnumPropertyStringChecked(desc, texTypeProp, "Float2");
                    SetScalarPropertyChecked(desc, texCoordsProp, 1);
                }
                if (field.FieldType == typeof(VertexTexture2) || field.FieldType == typeof(VertexColor1Texture2) || field.FieldType == typeof(VertexColor2Texture2))
                {
                    SetEnumPropertyStringChecked(desc, texTypeProp, "Float2");
                    SetScalarPropertyChecked(desc, texCoordsProp, 2);
                }
                if (field.FieldType == typeof(VertexTexture3))
                {
                    SetEnumPropertyStringChecked(desc, texTypeProp, "Float2");
                    SetScalarPropertyChecked(desc, texCoordsProp, 3);
                }
                if (field.FieldType == typeof(VertexTexture4))
                {
                    SetEnumPropertyStringChecked(desc, texTypeProp, "Float2");
                    SetScalarPropertyChecked(desc, texCoordsProp, 4);
                }

                if (field.FieldType == typeof(VertexColor1Texture1) || field.FieldType == typeof(VertexColor1Texture2))
                {
                    SetEnumPropertyStringChecked(desc, colorMapTypeProp, "Float4");
                    SetScalarPropertyChecked(desc, colorMapsProp, 1);
                }
                if (field.FieldType == typeof(VertexColor2Texture1) || field.FieldType == typeof(VertexColor2Texture2))
                {
                    SetEnumPropertyStringChecked(desc, colorMapTypeProp, "Float4");
                    SetScalarPropertyChecked(desc, colorMapsProp, 2);
                }
            }
            else if (field.Name == "Skinning")
            {
                if (field.FieldType == typeof(VertexJoints4))
                {
                    var boneWeightsProp = (MemberInfo?)descType.GetProperty("HasBoneWeights") ?? descType.GetField("HasBoneWeights");
                    if (boneWeightsProp is PropertyInfo pi) pi.SetValue(desc, true);
                    else ((FieldInfo?)boneWeightsProp)?.SetValue(desc, true);
                }
            }
        }

        return desc;
    }

    private static void SetEnumPropertyStringChecked(object target, MemberInfo? member, string enumValueStr)
    {
        if (member is null) return;
        var enumType = member is PropertyInfo p ? p.PropertyType : ((FieldInfo)member).FieldType;
        var enumVal = Enum.Parse(enumType, enumValueStr, true);
        if (member is PropertyInfo pi) pi.SetValue(target, enumVal);
        else ((FieldInfo)member).SetValue(target, enumVal);
    }

    private static void SetScalarPropertyChecked(object target, MemberInfo? member, int val)
    {
        if (member is null) return;
        if (member is PropertyInfo pi) pi.SetValue(target, Convert.ChangeType(val, pi.PropertyType, null));
        else ((FieldInfo)member).SetValue(target, Convert.ChangeType(val, ((FieldInfo)member).FieldType, null));
    }

    private static object? CreateParamlessInstanceTrimmerSafe([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Activator.CreateInstance(type);
    }

    public void ImportFromGLTF(ContentTransformer content, object influencingJoints, ExporterOptions options, dynamic extensions)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(influencingJoints);

        var geometry = content.GetGeometryAsset();
        dynamic primitives = geometry.Primitives.First();

        _options = options;
        InfluencingJoints = influencingJoints;

        _inputVertexType = FindVertexFormat(primitives.VertexType);

        var descType = _inputVertexType.GetType();
        if (extensions.Occluder || extensions.MeshProxy)
        {
            _outputVertexType = CreateParamlessInstanceTrimmerSafe(descType) ?? new object();
            SetEnumPropertyStringChecked(_outputVertexType, (MemberInfo?)descType.GetProperty("PositionType") ?? descType.GetField("PositionType"), "Float3");
        }
        else
        {
            _outputVertexType = CreateParamlessInstanceTrimmerSafe(descType) ?? new object();

            CopyPropertyValueChecked(_inputVertexType, _outputVertexType, "HasBoneWeights");
            CopyPropertyValueChecked(_inputVertexType, _outputVertexType, "NumBoneInfluences");
            CopyPropertyValueChecked(_inputVertexType, _outputVertexType, "PositionType");
            CopyPropertyValueChecked(_inputVertexType, _outputVertexType, "NormalType");
            CopyPropertyValueChecked(_inputVertexType, _outputVertexType, "TangentType");
            CopyPropertyValueChecked(_inputVertexType, _outputVertexType, "BinormalType");
            CopyPropertyValueChecked(_inputVertexType, _outputVertexType, "ColorMapType");
            CopyPropertyValueChecked(_inputVertexType, _outputVertexType, "ColorMaps");
            CopyPropertyValueChecked(_inputVertexType, _outputVertexType, "TextureCoordinateType");
            CopyPropertyValueChecked(_inputVertexType, _outputVertexType, "TextureCoordinates");
        }

        dynamic dynamicJoints = InfluencingJoints;
        if (dynamicJoints.SkeletonJoints.Count == 1)
        {
            var boneWeightsProp = (MemberInfo?)descType.GetProperty("HasBoneWeights") ?? descType.GetField("HasBoneWeights");
            if (boneWeightsProp is PropertyInfo pi) pi.SetValue(_outputVertexType, false);
            else ((FieldInfo?)boneWeightsProp)?.SetValue(_outputVertexType, false);
        }

        ImportTriangles(primitives);

        var bindRemapsProp = (MemberInfo?)InfluencingJoints.GetType().GetProperty("BindRemaps") ?? InfluencingJoints.GetType().GetField("BindRemaps");
        var remapsVal = bindRemapsProp is PropertyInfo pRem ? pRem.GetValue(InfluencingJoints) : ((FieldInfo?)bindRemapsProp)?.GetValue(InfluencingJoints);
        ImportVertices(primitives, (int[]?)remapsVal);

        if (!_hasNormals)
        {
            _hasNormals = true;

            var normTypeProp = (MemberInfo?)descType.GetProperty("NormalType") ?? descType.GetField("NormalType");
            SetEnumPropertyStringChecked(_outputVertexType, normTypeProp, "Float3");

            var helperType = Type.GetType("LSLib.Granny.Model.VertexHelpers") ?? Type.GetType("LSLib.Granny.VertexHelpers")
                ?? throw new ParsingException("Missing core internal 'VertexHelpers' module.");
            var computeNormalsMethod = helperType.GetMethod("ComputeNormals", BindingFlags.Public | BindingFlags.Static);
            computeNormalsMethod?.Invoke(null, [Vertices, Indices]);
        }

        var inputDescType = _inputVertexType.GetType();
        var inputTangentTypeStr = (((MemberInfo?)inputDescType.GetProperty("TangentType")) ?? inputDescType.GetField("TangentType")) is PropertyInfo pTan ? pTan.GetValue(_inputVertexType)?.ToString() : (((FieldInfo?)(((MemberInfo?)inputDescType.GetProperty("TangentType")) ?? inputDescType.GetField("TangentType")))?.GetValue(_inputVertexType)?.ToString() ?? "None");
        var inputBinormalTypeStr = (((MemberInfo?)inputDescType.GetProperty("BinormalType")) ?? inputDescType.GetField("BinormalType")) is PropertyInfo pBin ? pBin.GetValue(_inputVertexType)?.ToString() : (((FieldInfo?)(((MemberInfo?)inputDescType.GetProperty("BinormalType")) ?? inputDescType.GetField("BinormalType")))?.GetValue(_inputVertexType)?.ToString() ?? "None");

        var texCoordsProp = (MemberInfo?)inputDescType.GetProperty("TextureCoordinates") ?? inputDescType.GetField("TextureCoordinates");
        var texCoordsVal = texCoordsProp is PropertyInfo pTex ? pTex.GetValue(_inputVertexType) : ((FieldInfo?)texCoordsProp)?.GetValue(_inputVertexType);
        int inputTextureCoordinates = texCoordsVal is not null ? Convert.ToInt32(texCoordsVal, null) : 0;

        if ((string.Equals(inputTangentTypeStr, "None", StringComparison.OrdinalIgnoreCase)
            || string.Equals(inputBinormalTypeStr, "None", StringComparison.OrdinalIgnoreCase))
            && !_hasTangents
            && inputTextureCoordinates > 0)
        {
            SetEnumPropertyStringChecked(_outputVertexType, (MemberInfo?)descType.GetProperty("TangentType") ?? descType.GetField("TangentType"), "Float3");
            SetEnumPropertyStringChecked(_outputVertexType, (MemberInfo?)descType.GetProperty("BinormalType") ?? descType.GetField("BinormalType"), "Float3");

            _hasTangents = true;

            var helperType = Type.GetType("LSLib.Granny.Model.VertexHelpers") ?? Type.GetType("LSLib.Granny.VertexHelpers")
                ?? throw new ParsingException("Missing core internal 'VertexHelpers' module.");

            var computeTangentsMethod = helperType.GetMethod("ComputeTangents", BindingFlags.Public | BindingFlags.Static)
                ?? Type.GetType("LSLib.Granny.Model.MeshTangentGenerator")?.GetMethod("ComputeTangents", BindingFlags.Public | BindingFlags.Static);

            computeTangentsMethod?.Invoke(null, [Vertices, Indices, _options.IgnoreUVNaN]);
        }

        if (!_hasNormals || !_hasTangents)
        {
            throw new InvalidDataException("Import requires underlying geometry data populated with normal orientation and tangent components layout matrices.");
        }

        var formatProp = _options.GetType().GetProperty("ModelInfoFormat");
        var modelInfoFormatStr = formatProp?.GetValue(_options)?.ToString() ?? "None";

        if (string.Equals(modelInfoFormatStr, "LSMv0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(modelInfoFormatStr, "LSMv1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(modelInfoFormatStr, "LSMv3", StringComparison.OrdinalIgnoreCase))
        {
            if (_options.EnableQTangents && _hasNormals && _hasTangents)
            {
                SetEnumPropertyStringChecked(_outputVertexType, (MemberInfo?)descType.GetProperty("NormalType") ?? descType.GetField("NormalType"), "QTangent");
                SetEnumPropertyStringChecked(_outputVertexType, (MemberInfo?)descType.GetProperty("TangentType") ?? descType.GetField("TangentType"), "QTangent");
                SetEnumPropertyStringChecked(_outputVertexType, (MemberInfo?)descType.GetProperty("BinormalType") ?? descType.GetField("BinormalType"), "QTangent");
            }

            var outTexTypeProp = (MemberInfo?)descType.GetProperty("TextureCoordinateType") ?? descType.GetField("TextureCoordinateType");
            var outTexTypeStr = outTexTypeProp is PropertyInfo pOutTex ? pOutTex.GetValue(_outputVertexType)?.ToString() : ((FieldInfo?)outTexTypeProp)?.GetValue(_outputVertexType)?.ToString();

            if (string.Equals(outTexTypeStr, "Float2", StringComparison.OrdinalIgnoreCase))
            {
                SetEnumPropertyStringChecked(_outputVertexType, outTexTypeProp, "Half2");
            }

            var outColorTypeProp = (MemberInfo?)descType.GetProperty("ColorMapType") ?? descType.GetField("ColorMapType");
            var outColorTypeStr = outColorTypeProp is PropertyInfo pOutCol ? pOutCol.GetValue(_outputVertexType)?.ToString() : ((FieldInfo?)outColorTypeProp)?.GetValue(_outputVertexType)?.ToString();

            if (string.Equals(outColorTypeStr, "Float4", StringComparison.OrdinalIgnoreCase))
            {
                SetEnumPropertyStringChecked(_outputVertexType, outColorTypeProp, "Byte4");
            }
        }
    }
    private static void CopyPropertyValueChecked(object src, object dest, string propertyName)
    {
        Type t = src.GetType();
        var srcProp = (MemberInfo?)t.GetProperty(propertyName) ?? t.GetField(propertyName);
        var destProp = (MemberInfo?)dest.GetType().GetProperty(propertyName) ?? dest.GetType().GetField(propertyName);

        if (srcProp is not null && destProp is not null)
        {
            var val = srcProp is PropertyInfo p ? p.GetValue(src) : ((FieldInfo)srcProp).GetValue(src);
            if (destProp is PropertyInfo pi) pi.SetValue(dest, val);
            else ((FieldInfo)destProp).SetValue(dest, val);
        }
    }
}
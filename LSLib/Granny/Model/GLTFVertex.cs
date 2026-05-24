using LSLib.Granny.GR2;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using TKQuat = OpenTK.Mathematics.Quaternion;
using TKVec2 = OpenTK.Mathematics.Vector2;
using TKVec3 = OpenTK.Mathematics.Vector3;
using TKVec4 = OpenTK.Mathematics.Vector4;

namespace LSLib.Granny.Model;

internal static class GLTFConversionHelpers
{
    public static TKVec2 ToOpenTK(this System.Numerics.Vector2 v) => new(v.X, v.Y);

    public static TKVec3 ToOpenTK(this System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    public static TKVec4 ToOpenTK(this System.Numerics.Vector4 v) => new(v.X, v.Y, v.Z, v.W);

    public static TKQuat ToOpenTK(this System.Numerics.Quaternion v) => new(v.X, v.Y, v.Z, v.W);

    public static System.Numerics.Vector2 ToNumerics(this TKVec2 v) => new(v.X, v.Y);

    public static System.Numerics.Vector3 ToNumerics(this TKVec3 v) => new(v.X, v.Y, v.Z);

    public static System.Numerics.Vector4 ToNumerics(this TKVec4 v) => new(v.X, v.Y, v.Z, v.W);

    public static System.Numerics.Quaternion ToNumerics(this TKQuat v) => new(v.X, v.Y, v.Z, v.W);
}

public interface IGLTFVertexBuilder
{
    public void ToGLTF(IVertexBuilder gltfVert, Vertex gr2Vert);
    public void FromGLTF(IVertexBuilder gltfVert, Vertex gr2Vert);
}

public interface IGLTFVertexSkinBuilder
{
    public void ToGLTF(IVertexBuilder gltfVert, Vertex gr2Vert, int[] remaps);
    public void FromGLTF(IVertexBuilder gltfVert, Vertex gr2Vert, int[] remaps);
}

public class GLTFVertexNoneBuilder : IGLTFVertexBuilder
{
    public void ToGLTF(IVertexBuilder gltfVert, Vertex gr2Vert) { }
    public void FromGLTF(IVertexBuilder gltfVert, Vertex gr2Vert) { }
}

public class GLTFVertexGeometryBuilderPosition : IGLTFVertexBuilder
{
    public void ToGLTF(IVertexBuilder gltfVert, Vertex gr2Vert)
    {
        ArgumentNullException.ThrowIfNull(gltfVert);
        ArgumentNullException.ThrowIfNull(gr2Vert);

        var pos = gr2Vert.Position;
        var v = new VertexPosition(pos.X, pos.Y, pos.Z);
        gltfVert.SetGeometry(v);
    }

    public void FromGLTF(IVertexBuilder gltfVert, Vertex gr2Vert)
    {
        ArgumentNullException.ThrowIfNull(gltfVert);
        ArgumentNullException.ThrowIfNull(gr2Vert);

        dynamic geom = gltfVert.GetGeometry();
        var pos = (System.Numerics.Vector3)geom.GetPosition();
        gr2Vert.Position = new TKVec3(pos.X, pos.Y, pos.Z);
    }
}

    public class GLTFVertexGeometryBuilderPositionNormal : IGLTFVertexBuilder
    {
        public void ToGLTF(IVertexBuilder gltfVert, Vertex gr2Vert)
        {
            ArgumentNullException.ThrowIfNull(gltfVert);
            ArgumentNullException.ThrowIfNull(gr2Vert);

            var pos = gr2Vert.Position;
            var n = gr2Vert.Normal;
            var v = new VertexPositionNormal(
                pos.X, pos.Y, pos.Z,
                n.X, n.Y, n.Z
            );
            gltfVert.SetGeometry(v);
        }

        public void FromGLTF(IVertexBuilder gltfVert, Vertex gr2Vert)
        {
            ArgumentNullException.ThrowIfNull(gltfVert);
            ArgumentNullException.ThrowIfNull(gr2Vert);

            dynamic geom = gltfVert.GetGeometry();
            var pos = (System.Numerics.Vector3)geom.GetPosition();
            var n = (System.Numerics.Vector3)geom.GetNormal();

            gr2Vert.Position = new TKVec3(pos.X, pos.Y, pos.Z);
            gr2Vert.Normal = new TKVec3(n.X, n.Y, n.Z);
        }
    }

public class GLTFVertexGeometryBuilderPositionNormalTangent : IGLTFVertexBuilder
{
    public void ToGLTF(IVertexBuilder gltfVert, Vertex gr2Vert)
    {
        ArgumentNullException.ThrowIfNull(gltfVert);
        ArgumentNullException.ThrowIfNull(gr2Vert);

        var pos = gr2Vert.Position;
        var n = gr2Vert.Normal;
        var t = gr2Vert.Tangent;
        var b = gr2Vert.Binormal;
        float w = (TKVec3.Dot(TKVec3.Cross(n, t), b) < 0.0F) ? -1.0F : 1.0F;

        var v = new VertexPositionNormalTangent(
            pos.ToNumerics(),
            n.ToNumerics(),
            new System.Numerics.Vector4(t.X, t.Y, t.Z, w)
        );
        gltfVert.SetGeometry(v);
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safe unboxing fallback.")]
    public void FromGLTF(IVertexBuilder gltfVert, Vertex gr2Vert)
    {
        ArgumentNullException.ThrowIfNull(gltfVert);
        ArgumentNullException.ThrowIfNull(gr2Vert);

        dynamic geom = gltfVert.GetGeometry();
        gr2Vert.Position = ((System.Numerics.Vector3)geom.GetPosition()).ToOpenTK();
        gr2Vert.Normal = ((System.Numerics.Vector3)geom.GetNormal()).ToOpenTK();

        Type geomType = geom.GetType();
        var tryGetTangentMethod = geomType.GetMethod("TryGetTangent", BindingFlags.Public | BindingFlags.Instance);
        if (tryGetTangentMethod is not null)
        {
            var parameters = new object[] { null! };
            bool hasTangent = (bool)(tryGetTangentMethod.Invoke(geom, parameters) ?? false);
            if (hasTangent && parameters[0] is System.Numerics.Vector4 tangentVec)
            {
                gr2Vert.Tangent = new TKVec3(tangentVec.X, tangentVec.Y, tangentVec.Z);
                gr2Vert.Binormal = (TKVec3.Cross(gr2Vert.Normal, gr2Vert.Tangent) * tangentVec.W).Normalized();
            }
        }
    }

    public class GLTFVertexMaterialBuilderTexture1 : IGLTFVertexBuilder
    {
        public void ToGLTF(IVertexBuilder gltfVert, Vertex gr2Vert)
        {
            ArgumentNullException.ThrowIfNull(gltfVert);
            ArgumentNullException.ThrowIfNull(gr2Vert);

            var uv0 = gr2Vert.UVs is { Count: > 0 } ? gr2Vert.UVs[0] : OpenTK.Mathematics.Vector2.Zero;
            var v = new VertexTexture1(uv0.ToNumerics());
            gltfVert.SetMaterial(v);
        }

        public void FromGLTF(IVertexBuilder gltfVert, Vertex gr2Vert)
        {
            ArgumentNullException.ThrowIfNull(gltfVert);
            ArgumentNullException.ThrowIfNull(gr2Vert);

            var geom = (VertexTexture1)gltfVert.GetMaterial();
            gr2Vert.UVs ??= [];
            if (gr2Vert.UVs.Count == 0) gr2Vert.UVs.Add(geom.TexCoord.ToOpenTK());
            else gr2Vert.UVs[0] = geom.TexCoord.ToOpenTK();
        }
    }

    public class GLTFVertexMaterialBuilderTexture2 : IGLTFVertexBuilder
    {
        public void ToGLTF(IVertexBuilder gltfVert, Vertex gr2Vert)
        {
            ArgumentNullException.ThrowIfNull(gltfVert);
            ArgumentNullException.ThrowIfNull(gr2Vert);

            var uv0 = gr2Vert.UVs is { Count: > 0 } ? gr2Vert.UVs[0] : OpenTK.Mathematics.Vector2.Zero;
            var uv1 = gr2Vert.UVs is { Count: > 1 } ? gr2Vert.UVs[1] : OpenTK.Mathematics.Vector2.Zero;
            var v = new VertexTexture2(uv0.ToNumerics(), uv1.ToNumerics());
            gltfVert.SetMaterial(v);
        }

        public void FromGLTF(IVertexBuilder gltfVert, Vertex gr2Vert)
        {
            ArgumentNullException.ThrowIfNull(gltfVert);
            ArgumentNullException.ThrowIfNull(gr2Vert);

            var geom = (VertexTexture2)gltfVert.GetMaterial();
            gr2Vert.UVs ??= [];
            while (gr2Vert.UVs.Count < 2) gr2Vert.UVs.Add(OpenTK.Mathematics.Vector2.Zero);
            gr2Vert.UVs[0] = geom.TexCoord0.ToOpenTK();
            gr2Vert.UVs[1] = geom.TexCoord1.ToOpenTK();
        }
    }

    public class GLTFVertexMaterialBuilderTexture3 : IGLTFVertexBuilder
    {
        public void ToGLTF(IVertexBuilder gltfVert, Vertex gr2Vert)
        {
            ArgumentNullException.ThrowIfNull(gltfVert);
            ArgumentNullException.ThrowIfNull(gr2Vert);

            var uv0 = gr2Vert.UVs is { Count: > 0 } ? gr2Vert.UVs[0] : OpenTK.Mathematics.Vector2.Zero;
            var uv1 = gr2Vert.UVs is { Count: > 1 } ? gr2Vert.UVs[1] : OpenTK.Mathematics.Vector2.Zero;
            var uv2 = gr2Vert.UVs is { Count: > 2 } ? gr2Vert.UVs[2] : OpenTK.Mathematics.Vector2.Zero;
            var v = new VertexTexture3(uv0.ToNumerics(), uv1.ToNumerics(), uv2.ToNumerics());
            gltfVert.SetMaterial(v);
        }

        public void FromGLTF(IVertexBuilder gltfVert, Vertex gr2Vert)
        {
            ArgumentNullException.ThrowIfNull(gltfVert);
            ArgumentNullException.ThrowIfNull(gr2Vert);

            var geom = (VertexTexture3)gltfVert.GetMaterial();
            gr2Vert.UVs ??= [];
            while (gr2Vert.UVs.Count < 3) gr2Vert.UVs.Add(OpenTK.Mathematics.Vector2.Zero);
            gr2Vert.UVs[0] = geom.TexCoord0.ToOpenTK();
            gr2Vert.UVs[1] = geom.TexCoord1.ToOpenTK();
            gr2Vert.UVs[2] = geom.TexCoord2.ToOpenTK();
        }
    }

    public class GLTFVertexMaterialBuilderTexture4 : IGLTFVertexBuilder
    {
        public void ToGLTF(IVertexBuilder gltfVert, Vertex gr2Vert)
        {
            ArgumentNullException.ThrowIfNull(gltfVert);
            ArgumentNullException.ThrowIfNull(gr2Vert);

            var uv0 = gr2Vert.UVs is { Count: > 0 } ? gr2Vert.UVs[0] : OpenTK.Mathematics.Vector2.Zero;
            var uv1 = gr2Vert.UVs is { Count: > 1 } ? gr2Vert.UVs[1] : OpenTK.Mathematics.Vector2.Zero;
            var uv2 = gr2Vert.UVs is { Count: > 2 } ? gr2Vert.UVs[2] : OpenTK.Mathematics.Vector2.Zero;
            var uv3 = gr2Vert.UVs is { Count: > 3 } ? gr2Vert.UVs[3] : OpenTK.Mathematics.Vector2.Zero;
            var v = new VertexTexture4(uv0.ToNumerics(), uv1.ToNumerics(), uv2.ToNumerics(), uv3.ToNumerics());
            gltfVert.SetMaterial(v);
        }

        public void FromGLTF(IVertexBuilder gltfVert, Vertex gr2Vert)
        {
            ArgumentNullException.ThrowIfNull(gltfVert);
            ArgumentNullException.ThrowIfNull(gr2Vert);

            var geom = (VertexTexture4)gltfVert.GetMaterial();
            gr2Vert.UVs ??= [];
            while (gr2Vert.UVs.Count < 4) gr2Vert.UVs.Add(OpenTK.Mathematics.Vector2.Zero);
            gr2Vert.UVs[0] = geom.TexCoord0.ToOpenTK();
            gr2Vert.UVs[1] = geom.TexCoord1.ToOpenTK();
            gr2Vert.UVs[2] = geom.TexCoord2.ToOpenTK();
            gr2Vert.UVs[3] = geom.TexCoord3.ToOpenTK();
        }
    }

    public class GLTFVertexMaterialBuilderColor1Texture1 : IGLTFVertexBuilder
    {
        public void ToGLTF(IVertexBuilder gltfVert, Vertex gr2Vert)
        {
            ArgumentNullException.ThrowIfNull(gltfVert);
            ArgumentNullException.ThrowIfNull(gr2Vert);

            var col0 = gr2Vert.Colors is { Count: > 0 } ? gr2Vert.Colors[0] : OpenTK.Mathematics.Vector4.One;
            var uv0 = gr2Vert.UVs is { Count: > 0 } ? gr2Vert.UVs[0] : OpenTK.Mathematics.Vector2.Zero;
            var v = new VertexColor1Texture1(col0.ToNumerics(), uv0.ToNumerics());
            gltfVert.SetMaterial(v);
        }

        public void FromGLTF(IVertexBuilder gltfVert, Vertex gr2Vert)
        {
            ArgumentNullException.ThrowIfNull(gltfVert);
            ArgumentNullException.ThrowIfNull(gr2Vert);

            var geom = (VertexColor1Texture1)gltfVert.GetMaterial();

            gr2Vert.UVs ??= [];
            if (gr2Vert.UVs.Count == 0) gr2Vert.UVs.Add(geom.TexCoord.ToOpenTK());
            else gr2Vert.UVs[0] = geom.TexCoord.ToOpenTK();

            gr2Vert.Colors ??= [];
            if (gr2Vert.Colors.Count == 0) gr2Vert.Colors.Add(geom.Color.ToOpenTK());
            else gr2Vert.Colors[0] = geom.Color.ToOpenTK();
        }
    }

    public class GLTFVertexMaterialBuilderColor1Texture2 : IGLTFVertexBuilder
    {
        public void ToGLTF(IVertexBuilder gltfVert, Vertex gr2Vert)
        {
            ArgumentNullException.ThrowIfNull(gltfVert);
            ArgumentNullException.ThrowIfNull(gr2Vert);

            var col0 = gr2Vert.Colors is { Count: > 0 } ? gr2Vert.Colors[0] : OpenTK.Mathematics.Vector4.One;
            var uv0 = gr2Vert.UVs is { Count: > 0 } ? gr2Vert.UVs[0] : OpenTK.Mathematics.Vector2.Zero;
            var uv1 = gr2Vert.UVs is { Count: > 1 } ? gr2Vert.UVs[1] : OpenTK.Mathematics.Vector2.Zero;
            var v = new VertexColor1Texture2(col0.ToNumerics(), uv0.ToNumerics(), uv1.ToNumerics());
            gltfVert.SetMaterial(v);
        }

        public void FromGLTF(IVertexBuilder gltfVert, Vertex gr2Vert)
        {
            ArgumentNullException.ThrowIfNull(gltfVert);
            ArgumentNullException.ThrowIfNull(gr2Vert);

            var geom = (VertexColor1Texture2)gltfVert.GetMaterial();

            gr2Vert.UVs ??= [];
            while (gr2Vert.UVs.Count < 2) gr2Vert.UVs.Add(OpenTK.Mathematics.Vector2.Zero);
            gr2Vert.UVs[0] = geom.TexCoord0.ToOpenTK();
            gr2Vert.UVs[1] = geom.TexCoord1.ToOpenTK();

            gr2Vert.Colors ??= [];
            if (gr2Vert.Colors.Count == 0) gr2Vert.Colors.Add(geom.Color.ToOpenTK());
            else gr2Vert.Colors[0] = geom.Color.ToOpenTK();
        }
    }

    public class GLTFVertexMaterialBuilderColor2Texture1 : IGLTFVertexBuilder
    {
        public void ToGLTF(IVertexBuilder gltfVert, Vertex gr2Vert)
        {
            ArgumentNullException.ThrowIfNull(gltfVert);
            ArgumentNullException.ThrowIfNull(gr2Vert);

            var col0 = gr2Vert.Colors is { Count: > 0 } ? gr2Vert.Colors[0] : OpenTK.Mathematics.Vector4.One;
            var col1 = gr2Vert.Colors is { Count: > 1 } ? gr2Vert.Colors[1] : OpenTK.Mathematics.Vector4.One;
            var uv0 = gr2Vert.UVs is { Count: > 0 } ? gr2Vert.UVs[0] : OpenTK.Mathematics.Vector2.Zero;
            var v = new VertexColor2Texture1(col0.ToNumerics(), col1.ToNumerics(), uv0.ToNumerics());
            gltfVert.SetMaterial(v);
        }

        public void FromGLTF(IVertexBuilder gltfVert, Vertex gr2Vert)
        {
            ArgumentNullException.ThrowIfNull(gltfVert);
            ArgumentNullException.ThrowIfNull(gr2Vert);

            var geom = (VertexColor2Texture1)gltfVert.GetMaterial();

            gr2Vert.UVs ??= [];
            if (gr2Vert.UVs.Count == 0) gr2Vert.UVs.Add(geom.TexCoord.ToOpenTK());
            else gr2Vert.UVs[0] = geom.TexCoord.ToOpenTK();

            gr2Vert.Colors ??= [];
            while (gr2Vert.Colors.Count < 2) gr2Vert.Colors.Add(OpenTK.Mathematics.Vector4.One);
            gr2Vert.Colors[0] = geom.Color0.ToOpenTK();
            gr2Vert.Colors[1] = geom.Color1.ToOpenTK();
        }
    }

    public class GLTFVertexMaterialBuilderColor2Texture2 : IGLTFVertexBuilder
    {
        public void ToGLTF(IVertexBuilder gltfVert, Vertex gr2Vert)
        {
            ArgumentNullException.ThrowIfNull(gltfVert);
            ArgumentNullException.ThrowIfNull(gr2Vert);

            var col0 = gr2Vert.Colors is { Count: > 0 } ? gr2Vert.Colors[0] : OpenTK.Mathematics.Vector4.One;
            var col1 = gr2Vert.Colors is { Count: > 1 } ? gr2Vert.Colors[1] : OpenTK.Mathematics.Vector4.One;
            var uv0 = gr2Vert.UVs is { Count: > 0 } ? gr2Vert.UVs[0] : OpenTK.Mathematics.Vector2.Zero;
            var uv1 = gr2Vert.UVs is { Count: > 1 } ? gr2Vert.UVs[1] : OpenTK.Mathematics.Vector2.Zero;
            var v = new VertexColor2Texture2(col0.ToNumerics(), col1.ToNumerics(), uv0.ToNumerics(), uv1.ToNumerics());
            gltfVert.SetMaterial(v);
        }

        public void FromGLTF(IVertexBuilder gltfVert, Vertex gr2Vert)
        {
            ArgumentNullException.ThrowIfNull(gltfVert);
            ArgumentNullException.ThrowIfNull(gr2Vert);

            var geom = (VertexColor2Texture2)gltfVert.GetMaterial();

            gr2Vert.UVs ??= [];
            while (gr2Vert.UVs.Count < 2) gr2Vert.UVs.Add(OpenTK.Mathematics.Vector2.Zero);
            gr2Vert.UVs[0] = geom.TexCoord0.ToOpenTK();
            gr2Vert.UVs[1] = geom.TexCoord1.ToOpenTK();

            gr2Vert.Colors ??= [];
            while (gr2Vert.Colors.Count < 2) gr2Vert.Colors.Add(OpenTK.Mathematics.Vector4.One);
            gr2Vert.Colors[0] = geom.Color0.ToOpenTK();
            gr2Vert.Colors[1] = geom.Color1.ToOpenTK();
        }
    }

    public class GLTFVertexNoneSkinBuilder : IGLTFVertexSkinBuilder
    {
        public void ToGLTF(IVertexBuilder gltfVert, Vertex gr2Vert, int[] remaps) { }
        public void FromGLTF(IVertexBuilder gltfVert, Vertex gr2Vert, int[] remaps) { }
    }

    public class GLTFVertexSkinningBuilder : IGLTFVertexSkinBuilder
    {
        public void ToGLTF(IVertexBuilder gltfVert, Vertex gr2Vert, int[] remaps)
        {
            ArgumentNullException.ThrowIfNull(gltfVert);
            ArgumentNullException.ThrowIfNull(gr2Vert);
            ArgumentNullException.ThrowIfNull(remaps);

            int idx0 = gr2Vert.Indices.Length > 0 ? gr2Vert.Indices[0] : 0;
            int idx1 = gr2Vert.Indices.Length > 1 ? gr2Vert.Indices[1] : 0;
            int idx2 = gr2Vert.Indices.Length > 2 ? gr2Vert.Indices[2] : 0;
            int idx3 = gr2Vert.Indices.Length > 3 ? gr2Vert.Indices[3] : 0;

            float w0 = gr2Vert.Weights.Length > 0 ? gr2Vert.Weights[0] : 0f;
            float w1 = gr2Vert.Weights.Length > 1 ? gr2Vert.Weights[1] : 0f;
            float w2 = gr2Vert.Weights.Length > 2 ? gr2Vert.Weights[2] : 0f;
            float w3 = gr2Vert.Weights.Length > 3 ? gr2Vert.Weights[3] : 0f;

            var v = new VertexJoints4(
                (remaps[idx0], w0 / 255.0f),
                (remaps[idx1], w1 / 255.0f),
                (remaps[idx2], w2 / 255.0f),
                (remaps[idx3], w3 / 255.0f)
            );
            gltfVert.SetSkinning(v);
        }

        public void FromGLTF(IVertexBuilder gltfVert, Vertex gr2Vert, int[] remaps)
        {
            ArgumentNullException.ThrowIfNull(gltfVert);
            ArgumentNullException.ThrowIfNull(gr2Vert);
            ArgumentNullException.ThrowIfNull(remaps);

            var skin = (VertexJoints4)gltfVert.GetSkinning();
            Span<byte> compressedWeights = stackalloc byte[4];

            var helperType = Type.GetType("LSLib.Granny.Model.VertexHelpers") ?? Type.GetType("LSLib.Granny.VertexHelpers")
                ?? throw new ParsingException("Missing core internal 'VertexHelpers' module dependencies.");
            var compressMethod = helperType.GetMethod("CompressBoneWeights", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            compressMethod?.Invoke(null, [new float[] { skin.Weights.X, skin.Weights.Y, skin.Weights.Z, skin.Weights.W }, compressedWeights.ToArray()]);

            gr2Vert.Indices = new int[4];
            gr2Vert.Weights = new float[4];

            gr2Vert.Indices[0] = remaps[(byte)skin.Joints[0]];
            gr2Vert.Indices[1] = remaps[(byte)skin.Joints[1]];
            gr2Vert.Indices[2] = remaps[(byte)skin.Joints[2]];
            gr2Vert.Indices[3] = remaps[(byte)skin.Joints[3]];

            gr2Vert.Weights[0] = compressedWeights[0];
            gr2Vert.Weights[1] = compressedWeights[1];
            gr2Vert.Weights[2] = compressedWeights[2];
            gr2Vert.Weights[3] = compressedWeights[3];

            var finalizeMethod = gr2Vert.GetType().GetMethod("FinalizeInfluences", BindingFlags.Public | BindingFlags.Instance)
                                 ?? gr2Vert.GetType().GetMethod("finalizeInfluences", BindingFlags.Public | BindingFlags.Instance);
            finalizeMethod?.Invoke(gr2Vert, null);
        }
    }

    public interface IMorphedMeshBuilder<TMaterial> : IMeshBuilder<TMaterial>
    {
        float[] GetMorphWeights();
    }

    public class MorphedMeshBuilder<TMaterial, TvG, TvM, TvS>(string name) : MeshBuilder<TMaterial, TvG, TvM, TvS>(name), IMorphedMeshBuilder<TMaterial>
        where TvG : struct, IVertexGeometry
        where TvM : struct, IVertexMaterial
        where TvS : struct, IVertexSkinning
    {
        internal float[] MorphWeights = [];
        public float[] GetMorphWeights() => MorphWeights;
    }

    public interface IGLTFMeshBuildWrapper
    {
        public IMorphedMeshBuilder<MaterialBuilder> Build(Mesh m);
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "Safe structural builder mapping.")]
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safe unboxing lookups.")]
    public class GLTFMeshBuildWrapper<TvG, TvM, TvS> : IGLTFMeshBuildWrapper
        where TvG : struct, IVertexGeometry
        where TvM : struct, IVertexMaterial
        where TvS : struct, IVertexSkinning
    {
        private readonly GLTFVertexBuildHelper _buildHelper;
        private readonly MorphedMeshBuilder<MaterialBuilder, TvG, TvM, TvS> _builder;
        private readonly dynamic _primitives;
        private List<object> _morphTargets = [];
        private List<IVertexBuilder> _vertices = [];

        public GLTFMeshBuildWrapper(GLTFVertexBuildHelper helper, string exportedId)
        {
            _buildHelper = helper ?? throw new ArgumentNullException(nameof(helper));
            _builder = new MorphedMeshBuilder<MaterialBuilder, TvG, TvM, TvS>(exportedId);

            var material = new MaterialBuilder("Dummy");
            _primitives = _builder.UsePrimitive(material);
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safe.")]
        private void BuildVertices(Mesh mesh)
        {
            var verticesObj = mesh.PrimaryVertexData?.Vertices;
            if (verticesObj is null) return;

            if (verticesObj is System.Collections.IEnumerable enumerableList && verticesObj is null)
            {
                _vertices = [];
                foreach (var v in enumerableList)
                {
                    if (v is null) continue;
                    var vert = _primitives.VertexFactory();
                    _buildHelper.ToGLTF(vert, (Vertex)v);
                    _vertices.Add(vert);
                }
            }
            else
            {
                var vertexFormatProp = mesh.GetType().GetProperty("VertexFormat", BindingFlags.Public | BindingFlags.Instance)
                                       ?? mesh.GetType().GetProperty("InternalVertexType", BindingFlags.Public | BindingFlags.Instance);
                var formatObj = vertexFormatProp?.GetValue(mesh);
                int strideSize = 1;
                if (formatObj is not null)
                {
                    var sizeProp = (MemberInfo?)formatObj.GetType().GetProperty("Size", BindingFlags.Public | BindingFlags.Instance)
                                   ?? formatObj.GetType().GetField("Size", BindingFlags.Public | BindingFlags.Instance);
                    var sizeVal = sizeProp is PropertyInfo p ? p.GetValue(formatObj) : ((FieldInfo?)sizeProp)?.GetValue(formatObj);
                    strideSize = Math.Max(1, (sizeVal is not null ? Convert.ToInt32(sizeVal) : sizeof(float)) / sizeof(float));
                }

                var flatFloats = (List<float>)(object)verticesObj;
                int totalVerts = flatFloats.Count / strideSize;
                _vertices = new List<IVertexBuilder>(totalVerts);

                var vertexClassType = Type.GetType("LSLib.Granny.Model.Vertex") ?? Type.GetType("LSLib.Granny.Vertex");
                if (vertexClassType is not null)
                {
                    for (int i = 0; i < totalVerts; i++)
                    {
                        var vert = _primitives.VertexFactory();
                        var tempVert = (Vertex)Activator.CreateInstance(vertexClassType)!;
                        _buildHelper.ToGLTF(vert, tempVert);
                        _vertices.Add(vert);
                    }
                }
            }
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safe unboxing fallback.")]
        private static float BuildMorphTarget(Mesh m, MorphTarget target, dynamic gltfTarget)
        {
            if (target.DataArea != 1)
            {
                throw new InvalidDataException("Only delta morph targets are supported across unmanaged asset conversions.");
            }

            var setsProp = target.VertexData.GetType().GetProperty("VertexAnnotationSets")
                           ?? target.VertexData.GetType().GetProperty("_vertexAnnotationSets", BindingFlags.NonPublic | BindingFlags.Instance);

            if (setsProp?.GetValue(target.VertexData) is not System.Collections.IList annotations || annotations.Count != 2)
            {
                throw new InvalidDataException("Unsupported morph target annotation sets layout.");
            }

            dynamic annotation0 = annotations[0]!;
            dynamic annotation1 = annotations[1]!;

            if (annotation0.Name != "MaxVertDisplacement" || annotation1.Name != "BlendShapeIndexMapping")
            {
                throw new InvalidDataException("Unsupported morph target annotation attributes mapping profiles.");
            }

            var maxDisplacement = (List<float>)annotation0.VertexAnnotations;
            var indexMapping = (List<ushort>)annotation1.VertexAnnotations;

            int mVerticesCount = 0;
            if (m.PrimaryVertexData?.Vertices is System.Collections.ICollection coll) mVerticesCount = coll.Count;
            else if (m.PrimaryVertexData?.Vertices is List<float> lf) mVerticesCount = lf.Count / 12;

            if (indexMapping.Count != mVerticesCount)
            {
                throw new InvalidDataException("Morph target vertex count bounds mismatch exception.");
            }

            var vertexClassType = Type.GetType("LSLib.Granny.Model.Vertex") ?? Type.GetType("LSLib.Granny.Vertex") ?? typeof(Vertex);

            for (var i = 0; i < indexMapping.Count; ++i)
            {
                int index = indexMapping[i];

                var vertex = (Vertex)Activator.CreateInstance(vertexClassType)!;
                var displacement = (Vertex)Activator.CreateInstance(vertexClassType)!;

                if (m.PrimaryVertexData?.Vertices is System.Collections.IList nativeList && nativeList.Count > i && nativeList[i]?.GetType() == vertexClassType)
                {
                    vertex = (Vertex)nativeList[i]!;
                }

                if (target.VertexData?.Vertices is System.Collections.IList targetList && targetList.Count > index && targetList[index]?.GetType() == vertexClassType)
                {
                    displacement = (Vertex)targetList[index]!;
                }

                gltfTarget.SetVertexDelta(vertex.Position.ToNumerics(), new VertexGeometryDelta(
                    displacement.Position.ToNumerics(),
                    displacement.Normal.ToNumerics(),
                    displacement.Tangent.ToNumerics()
                ));
            }
            return maxDisplacement[0];
        }

        public IMorphedMeshBuilder<MaterialBuilder> Build(Mesh m)
        {
            ArgumentNullException.ThrowIfNull(m);
            BuildVertices(m);

            // Fix CS1061: Enforce high-performance unboxed API additions to insert vertices safely without dynamic deconstruction faults
            foreach (var vertex in _vertices)
            {
                _primitives.AddVertex(vertex);
            }

            var inds = m.PrimaryTopology.Indices;
            for (var i = 0; i < inds.Count; i += 3)
            {
                _primitives.AddTriangle(inds[i], inds[i + 1], inds[i + 2]);
            }

            if (m.MorphTargets is { Count: > 0 } && m.PrimaryVertexData.Vertices.Count > 0)
            {
                _morphTargets = [];
                var weights = new float[m.MorphTargets.Count];
                for (var i = 0; i < m.MorphTargets.Count; i++)
                {
                    var target = _builder.UseMorphTarget(i);
                    _morphTargets.Add(target);
                    var maxDisplacement = BuildMorphTarget(m, m.MorphTargets[i], target);
                    weights[i] = maxDisplacement;
                }

                _builder.MorphWeights = weights;
            }

            return _builder;
        }
    }

    public class GLTFVertexBuildHelper
    {
        public string ExportedId { get; set; } = string.Empty;
        public object VertexFormat { get; set; } = null!;
        public int[] JointRemaps { get; set; } = [];

        public IGLTFVertexBuilder GeometryBuilder { get; set; } = null!;
        public IGLTFVertexBuilder MaterialBuilder { get; set; } = null!;
        public IGLTFVertexSkinBuilder SkinningBuilder { get; set; } = null!;

        public Type GeometryDataType { get; set; } = null!;
        public Type MaterialDataType { get; set; } = null!;
        public Type SkinningDataType { get; set; } = null!;

        private readonly int _uvs;
        private readonly int _colorMaps;
        private readonly bool _hasNormals;
        private readonly bool _hasTangents;

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safe format mapping loops.")]
        public GLTFVertexBuildHelper(string exportedId, object vertexFormat, int[] jointRemaps)
        {
            ExportedId = exportedId;
            VertexFormat = vertexFormat ?? throw new ArgumentNullException(nameof(vertexFormat));
            JointRemaps = jointRemaps ?? [];

            Type t = VertexFormat.GetType();

            var normProp = (MemberInfo?)t.GetProperty("NormalType") ?? t.GetField("NormalType");
            string normTypeStr = normProp is PropertyInfo pNorm
                ? pNorm.GetValue(vertexFormat)?.ToString() ?? "None"
                : ((FieldInfo?)normProp)?.GetValue(vertexFormat)?.ToString() ?? "None";
            _hasNormals = !string.Equals(normTypeStr, "None", StringComparison.OrdinalIgnoreCase);


            var tangentProp = (MemberInfo?)t.GetProperty("TangentType") ?? t.GetField("TangentType");
            string tangentTypeStr = tangentProp is PropertyInfo pTan
                 ? pTan.GetValue(vertexFormat)?.ToString() ?? "None"
                 : ((FieldInfo?)tangentProp)?.GetValue(vertexFormat)?.ToString() ?? "None";
            _hasTangents = !string.Equals(tangentTypeStr, "None", StringComparison.OrdinalIgnoreCase);

            var texCoordsProp = (MemberInfo?)t.GetProperty("TextureCoordinates") ?? t.GetField("TextureCoordinates");
            var texCoordsVal = texCoordsProp is PropertyInfo pTex ? pTex.GetValue(VertexFormat) : ((FieldInfo?)texCoordsProp)?.GetValue(VertexFormat);
            _uvs = texCoordsVal is not null ? Convert.ToInt32(texCoordsVal) : 0;

            var colorMapsProp = (MemberInfo?)t.GetProperty("ColorMaps") ?? t.GetField("ColorMaps");
            var colorMapsVal = colorMapsProp is PropertyInfo pCol ? pCol.GetValue(VertexFormat) : ((FieldInfo?)colorMapsProp)?.GetValue(VertexFormat);
            _colorMaps = colorMapsVal is not null ? Convert.ToInt32(colorMapsVal) : 0;

            SelectGeometryBuilder();
            SelectMaterialBuilder();
            SelectSkinningBuilder();
        }

        public void ToGLTF(IVertexBuilder gltfVert, Vertex gr2Vert)
        {
            GeometryBuilder?.ToGLTF(gltfVert, gr2Vert);
            MaterialBuilder?.ToGLTF(gltfVert, gr2Vert);
        }

        private void SelectGeometryBuilder()
        {
            if (_hasNormals)
            {
                if (_hasTangents)
                {
                    GeometryDataType = typeof(VertexPositionNormalTangent);
                    GeometryBuilder = new GLTFVertexGeometryBuilderPositionNormalTangent();
                }
                else
                {
                    GeometryDataType = typeof(VertexPositionNormal);
                    GeometryBuilder = new GLTFVertexGeometryBuilderPositionNormal();
                }
            }
            else
            {
                GeometryDataType = typeof(VertexPosition);
                GeometryBuilder = new GLTFVertexGeometryBuilderPosition();
            }
        }

        private void SelectMaterialBuilder()
        {
            if (_uvs == 0 && _colorMaps == 0)
            {
                MaterialDataType = typeof(VertexEmpty);
                MaterialBuilder = new GLTFVertexNoneBuilder();
            }
            else if (_uvs == 1 && _colorMaps == 0)
            {
                MaterialDataType = typeof(VertexTexture1);
                MaterialBuilder = new GLTFVertexMaterialBuilderTexture1();
            }
            else if (_uvs == 2 && _colorMaps == 0)
            {
                MaterialDataType = typeof(VertexTexture2);
                MaterialBuilder = new GLTFVertexMaterialBuilderTexture2();
            }
            else if (_uvs == 3 && _colorMaps == 0)
            {
                MaterialDataType = typeof(VertexTexture3);
                MaterialBuilder = new GLTFVertexMaterialBuilderTexture3();
            }
            else if (_uvs == 4 && _colorMaps == 0)
            {
                MaterialDataType = typeof(VertexTexture4);
                MaterialBuilder = new GLTFVertexMaterialBuilderTexture4();
            }
            else if (_uvs == 1 && _colorMaps == 1)
            {
                MaterialDataType = typeof(VertexColor1Texture1);
                MaterialBuilder = new GLTFVertexMaterialBuilderColor1Texture1();
            }
            else if (_uvs == 2 && _colorMaps == 1)
            {
                MaterialDataType = typeof(VertexColor1Texture2);
                MaterialBuilder = new GLTFVertexMaterialBuilderColor1Texture2();
            }
            else if (_uvs == 2 && _colorMaps == 2)
            {
                MaterialDataType = typeof(VertexColor2Texture2);
                MaterialBuilder = new GLTFVertexMaterialBuilderColor2Texture2();
            }
            else
            {
                throw new InvalidDataException($"Unsupported vertex configuration format scenario for glTF export: UVs {_uvs}, Color maps {_colorMaps}");
            }
        }

        private void SelectSkinningBuilder()
        {
            Type t = VertexFormat.GetType();
            var boneWeightsProp = (MemberInfo?)t.GetProperty("HasBoneWeights") ?? t.GetField("HasBoneWeights");
            var boneWeightsVal = boneWeightsProp is PropertyInfo pBone ? pBone.GetValue(VertexFormat) : ((FieldInfo?)boneWeightsProp)?.GetValue(VertexFormat);
            bool hasBoneWeights = Convert.ToBoolean(boneWeightsVal);

            if (hasBoneWeights)
            {
                SkinningDataType = typeof(VertexJoints4);
                SkinningBuilder = new GLTFVertexSkinningBuilder();
            }
            else
            {
                SkinningDataType = typeof(VertexEmpty);
                SkinningBuilder = new GLTFVertexNoneSkinBuilder();
            }
        }

        public IGLTFMeshBuildWrapper InternalCreateBuilder<TvG, TvM, TvS>()
            where TvG : struct, IVertexGeometry
            where TvM : struct, IVertexMaterial
            where TvS : struct, IVertexSkinning
        {
            return new GLTFMeshBuildWrapper<TvG, TvM, TvS>(this, ExportedId);
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2060:RequiresUnreferencedCode", Justification = "Safe construction pass.")]
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safe metadata reflection.")]
        public IGLTFMeshBuildWrapper CreateBuilder()
        {
            var method = GetType().GetMethod("InternalCreateBuilder", BindingFlags.Public | BindingFlags.Instance) ?? throw new ParsingException("Critical factory error: Core generic method builder symbol cannot be resolved.");
            var genericMethod = method.MakeGenericMethod([GeometryDataType, MaterialDataType, SkinningDataType]);
            return (IGLTFMeshBuildWrapper)(genericMethod.Invoke(this, null)
                ?? throw new ParsingException("Failed to construct a dynamic IGLTFMeshBuildWrapper context instantiation context."));
        }

        public void FromGLTF(IVertexBuilder gltf, Vertex gr)
        {
            GeometryBuilder?.FromGLTF(gltf, gr);
            MaterialBuilder?.FromGLTF(gltf, gr);
            SkinningBuilder?.FromGLTF(gltf, gr, JointRemaps);
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safe unboxing fallback.")]
        public Vertex FromGLTF(IVertexBuilder gltf)
        {
            ArgumentNullException.ThrowIfNull(gltf);

            var vertexType = Type.GetType("LSLib.Granny.Model.Vertex") ?? Type.GetType("LSLib.Granny.Vertex")
                ?? throw new ParsingException("Vertex class structure descriptor target not found.");

            var gr = Activator.CreateInstance(vertexType) as Vertex
                ?? throw new ParsingException("Dynamic type structural vertex instance allocation failure.");

            GeometryBuilder.FromGLTF(gltf, gr);
            MaterialBuilder.FromGLTF(gltf, gr);
            SkinningBuilder.FromGLTF(gltf, gr, JointRemaps);
            return gr;
        }
    }

    public sealed class GLTFMeshExporter
    {
        private readonly Mesh _exportedMesh;
        private readonly GLTFVertexBuildHelper _buildHelper;

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safe proxy creation.")]
        public GLTFMeshExporter(Mesh mesh, string exportedId, int[] jointRemaps)
        {
            _exportedMesh = mesh ?? throw new ArgumentNullException(nameof(mesh));

            var formatProp = mesh.GetType().GetProperty("VertexFormat", BindingFlags.Public | BindingFlags.Instance)
                             ?? mesh.GetType().GetProperty("InternalVertexType", BindingFlags.Public | BindingFlags.Instance)
                             ?? mesh.GetType().GetProperty("_vertexFormat", BindingFlags.NonPublic | BindingFlags.Instance);
            var formatObj = formatProp?.GetValue(mesh) ?? throw new ParsingException("Failed to extract vertex format description schema from mesh layer.");

            _buildHelper = new GLTFVertexBuildHelper(exportedId, formatObj, jointRemaps);
        }

        public IMorphedMeshBuilder<MaterialBuilder> Export()
        {
            var builder = _buildHelper.CreateBuilder();
            return builder.Build(_exportedMesh);
        }
    }
}
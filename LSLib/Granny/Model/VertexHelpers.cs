using LSLib.Granny.GR2;
using OpenTK.Mathematics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace LSLib.Granny.Model;

public static class VertexHelpers
{
    private sealed class OBB
    {
        public Vector3 Min { get; set; } = new(1000.0f, 1000.0f, 1000.0f);
        public Vector3 Max { get; set; } = new(-1000.0f, -1000.0f, -1000.0f);
        public int NumVerts { get; set; }
    }

    public static void CompressBoneWeights(Span<float> weights, Span<byte> compressedWeights)
    {
        Span<float> errors = stackalloc float[weights.Length];
        int influenceCount = weights.Length;
        float influenceSum = 0.0f;

        for (int i = 0; i < weights.Length; i++)
        {
            influenceSum += weights[i];
        }

        ushort totalEncoded = 0;
        for (var i = 0; i < influenceCount; i++)
        {
            var weight = weights[i] / influenceSum * 255.0f;
            var encodedWeight = (byte)Math.Round(weight);
            totalEncoded += encodedWeight;
            errors[i] = encodedWeight - weight;
            compressedWeights[i] = encodedWeight;
        }

        while (totalEncoded != 0 && totalEncoded != 255)
        {
            int errorIndex = 0;
            if (totalEncoded < 255)
            {
                for (var i = 1; i < influenceCount; i++)
                {
                    if (errors[i] < errors[errorIndex]) errorIndex = i;
                }
                compressedWeights[errorIndex]++;
                errors[errorIndex]++;
                totalEncoded++;
            }
            else
            {
                for (var i = 1; i < influenceCount; i++)
                {
                    if (errors[i] > errors[errorIndex]) errorIndex = i;
                }
                compressedWeights[errorIndex]--;
                errors[errorIndex]--;
                totalEncoded--;
            }
        }
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern",
       Justification = "Texture coordinates metric attributes are extracted safely via static metadata lookups.")]
    public static void ComputeTangents(IList<Vertex> vertices, IList<int> indices, bool ignoreNaNUV)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);

        if (vertices.Count > 0)
        {
            var v = vertices[0];
            if (v.Format == null)
            {
                throw new InvalidOperationException("At least one UV set is required to recompute tangents");
            }

            var formatType = v.Format.GetType();
            var textureCoordinatesProp = (MemberInfo?)formatType.GetProperty("TextureCoordinates", BindingFlags.Public | BindingFlags.Instance)
                             ?? (MemberInfo?)formatType.GetField("TextureCoordinates", BindingFlags.Public | BindingFlags.Instance)
                             ?? formatType.GetProperty("TextureCoordinateCount", BindingFlags.Public | BindingFlags.Instance);

            var value = textureCoordinatesProp is PropertyInfo p
                ? p.GetValue(v.Format)
                : ((FieldInfo?)textureCoordinatesProp)?.GetValue(v.Format);
            int uvCount = value is not null ? Convert.ToInt32(value) : 0;

            if (uvCount == 0)
            {
                throw new InvalidOperationException("At least one UV set is required to recompute tangents");
            }
        }

        var vertList = vertices as List<Vertex> ?? [.. vertices];
        ReadOnlySpan<Vertex> vertsSpan = CollectionsMarshal.AsSpan(vertList);
        ReadOnlySpan<int> idxSpan = CollectionsMarshal.AsSpan(indices as List<int> ?? [.. indices]);

        for (int i = 0; i < vertsSpan.Length; i++)
        {
            var v = vertsSpan[i];
            if (v != null)
            {
                v.Tangent = Vector3.Zero;
                v.Binormal = Vector3.Zero;
            }
        }

        int triCount = idxSpan.Length / 3;
        for (int i = 0; i < triCount; i++)
        {
            var i1 = idxSpan[i * 3 + 0];
            var i2 = idxSpan[i * 3 + 1];
            var i3 = idxSpan[i * 3 + 2];

            var vert1 = vertices[i1];
            var vert2 = vertices[i2];
            var vert3 = vertices[i3];

            var v1 = vert1.Position;
            var v2 = vert2.Position;
            var v3 = vert3.Position;

            var w1 = vert1.UVs[0];
            var w2 = vert2.UVs[0];
            var w3 = vert3.UVs[0];

            float x1 = v2.X - v1.X;
            float x2 = v3.X - v1.X;
            float y1 = v2.Y - v1.Y;
            float y2 = v3.Y - v1.Y;
            float z1 = v2.Z - v1.Z;
            float z2 = v3.Z - v1.Z;

            float s1 = w2.X - w1.X;
            float s2 = w3.X - w1.X;
            float t1 = w2.Y - w1.Y;
            float t2 = w3.Y - w1.Y;

            float r = 1.0F / (s1 * t2 - s2 * t1);

            if ((float.IsNaN(r) || float.IsInfinity(r)) && !ignoreNaNUV)
            {
                throw new ParsingException($"Couldn't calculate tangents; the mesh most likely contains non-manifold geometry.{Environment.NewLine}"
                    + $"UV1: {w1}{Environment.NewLine}UV2: {w2}{Environment.NewLine}UV3: {w3}");
            }

            var sdir = new Vector3((t2 * x1 - t1 * x2) * r, (t2 * y1 - t1 * y2) * r, (t2 * z1 - t1 * z2) * r);
            var tdir = new Vector3((s1 * x2 - s2 * x1) * r, (s1 * y2 - s2 * y1) * r, (s1 * z2 - s2 * z1) * r);

            vert1.Tangent += sdir;
            vert2.Tangent += sdir;
            vert3.Tangent += sdir;

            vert1.Binormal += tdir;
            vert2.Binormal += tdir;
            vert3.Binormal += tdir;
        }

        for (int i = 0; i < vertices.Count; i++)
        {
            var v = vertices[i];
            if (v == null) continue;

            var n = v.Normal;
            var t = v.Tangent;
            var b = v.Binormal;

            var tangent = (t - n * Vector3.Dot(n, t)).Normalized();
            var w = (Vector3.Dot(Vector3.Cross(n, t), b) < 0.0F) ? 1.0F : -1.0F;
            var binormal = (Vector3.Cross(n, t) * w).Normalized();

            v.Tangent = tangent;
            v.Binormal = binormal;
        }
    }

    public static Vector3 TriangleNormalFromVertex(IList<Vertex> vertices, IList<int> indices, int vertexIndex)
    {
        var a = vertices[indices[vertexIndex]].Position;
        var b = vertices[indices[(vertexIndex + 1) % 3]].Position;
        var c = vertices[indices[(vertexIndex + 2) % 3]].Position;

        var N = Vector3.Cross(b - a, c - a);
        float lengthProduct = (b - a).Length * (c - a).Length;
        if (lengthProduct == 0f) return Vector3.Zero;

        float sin_alpha = N.Length / lengthProduct;
        return N.Normalized() * MathF.Asin(MathM.Clamp(sin_alpha, -1f, 1f));
    }

    public static void ComputeNormals(IList<Vertex> vertices, IList<int> indices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);

        ReadOnlySpan<int> idxSpan = CollectionsMarshal.AsSpan(indices as List<int> ?? [.. indices]);

        for (var vertexIdx = 0; vertexIdx < vertices.Count; vertexIdx++)
        {
            Vector3 N = Vector3.Zero;
            int numIndices = idxSpan.Length;

            for (int triVertIdx = 0; triVertIdx < numIndices; triVertIdx++)
            {
                if (idxSpan[triVertIdx] == vertexIdx)
                {
                    int baseIdx = (triVertIdx / 3) * 3;
                    N += TriangleNormalFromVertex(vertices, indices, baseIdx);
                }
            }

            N.Normalize();
            vertices[vertexIdx].Normal = N;
        }
    }

    public static void UpdateOBBs(Skeleton skeleton, Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(mesh);
        if (mesh.BoneBindings is not { Count: > 0 }) return;

        var obbs = new List<OBB>(mesh.BoneBindings.Count);
        for (var i = 0; i < mesh.BoneBindings.Count; i++)
        {
            obbs.Add(new OBB());
        }

        var rawVertsList = mesh.PrimaryVertexData?.Vertices;
        if (rawVertsList == null) return;

        throw new NotImplementedException("Vertex parsing matrices require authoritative raw floats translation bindings.");
    }
}

internal static class MathM
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Clamp(float val, float min, float max) => val < min ? min : (val > max ? max : val);
}
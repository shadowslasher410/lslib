using LSLib.Granny.GR2;
using LSLib.Granny.Model.CurveData;
using OpenTK.Mathematics;

namespace LSLib.Granny.Model;

public sealed class TriTopology
{
    public int GroupCount { get; set; }
    public List<int> Indices { get; set; } = [];
    public List<int> MaterialIndices { get; set; } = [];

    [field: Serialization(Type = MemberType.Inline)]
    public CurveDataHeader ExtendedData { get; set; } = new();

    public static object MakeColladaTriangles(
        InputLocalOffset[] inputOffsets,
        Dictionary<int, int> vertexMap,
        Dictionary<int, int> _1,
        List<Dictionary<int, int>> _2,
        List<Dictionary<int, int>> _3)
    {
        ArgumentNullException.ThrowIfNull(inputOffsets);
        ArgumentNullException.ThrowIfNull(vertexMap);
        _ = _1; _ = _2; _ = _3;

        return new object();
    }
}

public sealed class VertexAnnotation
{
    public string Name { get; set; } = string.Empty;
    public List<float> Floats { get; set; } = [];
}

public sealed class VertexData
{
    [field: Serialization(Prototype = typeof(ControlReal32), Kind = SerializationKind.UserMember, Serializer = typeof(SingleListSerializer))]
    public List<float> Vertices { get; set; } = [];

    public List<VertexAnnotation> Annotations { get; set; } = [];

    [field: Serialization(Type = MemberType.Inline)]
    public CurveDataHeader ExtendedData { get; set; } = new();

    [field: Serialization(Kind = SerializationKind.None)]
    public VertexDeduplicator Deduplicator { get; set; } = new();
}

public sealed class VertexDeduplicator
{
    public VertexCollection Vertices { get; set; } = new();
    public Deduplicator<Vector3> Normals { get; set; } = new();
    public List<Deduplicator<Vector2>> UVs { get; set; } = [];
    public List<Deduplicator<Vector4>> Colors { get; set; } = [];
}

public sealed class VertexCollection
{
    public List<Vertex> Uniques { get; set; } = [];
    public Dictionary<int, int> DeduplicationMap { get; set; } = [];
}

public sealed class Deduplicator<T> where T : struct
{
    public List<T> Uniques { get; set; } = [];
    public Dictionary<int, int> DeduplicationMap { get; set; } = [];
}

public sealed class Vertex : IEquatable<Vertex>
{
    [field: Serialization(Kind = SerializationKind.None)]
    public object Format { get; set; } = new object();

    public Vector3 Position { get; set; } = Vector3.Zero;
    public Vector3 Normal { get; set; } = Vector3.Zero;
    public Vector3 Tangent { get; set; } = Vector3.Zero;
    public Vector3 Binormal { get; set; } = Vector3.Zero;

    public int[] Indices { get; set; } = [];
    public float[] Weights { get; set; } = [0f, 0f, 0f, 0f];
    public List<Vector2> UVs { get; set; } = [];
    public List<Vector4> Colors { get; set; } = [];

    public override int GetHashCode()
    {
        var hashCode = new HashCode();

        hashCode.Add(Position);
        hashCode.Add(Normal);
        hashCode.Add(Tangent);
        hashCode.Add(Binormal);

        ReadOnlySpan<int> indicesSpan = Indices;
        for (int i = 0; i < indicesSpan.Length; i++)
        {
            hashCode.Add(indicesSpan[i]);
        }

        ReadOnlySpan<float> weightsSpan = Weights;
        for (int i = 0; i < weightsSpan.Length; i++)
        {
            hashCode.Add(weightsSpan[i]);
        }

        ReadOnlySpan<Vector2> uvsSpan = CollectionsMarshal.AsSpan(UVs);
        for (int i = 0; i < uvsSpan.Length; i++)
        {
            hashCode.Add(uvsSpan[i]);
        }

        ReadOnlySpan<Vector4> colorsSpan = CollectionsMarshal.AsSpan(Colors);
        for (int i = 0; i < colorsSpan.Length; i++)
        {
            hashCode.Add(colorsSpan[i]);
        }

        return hashCode.ToHashCode();
    }

    public override bool Equals(object? obj) => obj is Vertex other && Equals(other);

    public bool Equals(Vertex? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        if (Position != other.Position ||
            Normal != other.Normal ||
            Tangent != other.Tangent ||
            Binormal != other.Binormal)
        {
            return false;
        }

        ReadOnlySpan<int> thisIdx = Indices;
        ReadOnlySpan<int> otherIdx = other.Indices;
        if (thisIdx.Length != otherIdx.Length) return false;
        for (int i = 0; i < thisIdx.Length; i++)
        {
            if (thisIdx[i] != otherIdx[i]) return false;
        }

        ReadOnlySpan<float> thisWgt = Weights;
        ReadOnlySpan<float> otherWgt = other.Weights;
        if (thisWgt.Length != otherWgt.Length) return false;
        for (int i = 0; i < thisWgt.Length; i++)
        {
            if (MathF.Abs(thisWgt[i] - otherWgt[i]) > 0.00001f) return false;
        }

        if (UVs.Count != other.UVs.Count || Colors.Count != other.Colors.Count) return false;

        ReadOnlySpan<Vector2> thisUv = CollectionsMarshal.AsSpan(UVs);
        ReadOnlySpan<Vector2> otherUv = CollectionsMarshal.AsSpan(other.UVs);
        for (int i = 0; i < thisUv.Length; i++)
        {
            if (thisUv[i] != otherUv[i]) return false;
        }

        ReadOnlySpan<Vector4> thisCol = CollectionsMarshal.AsSpan(Colors);
        ReadOnlySpan<Vector4> otherCol = CollectionsMarshal.AsSpan(other.Colors);
        for (int i = 0; i < thisCol.Length; i++)
        {
            if (thisCol[i] != otherCol[i]) return false;
        }

        return true;
    }
}
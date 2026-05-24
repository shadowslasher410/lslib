using OpenTK.Mathematics;
using LSLib.Granny.GR2;
using System.Runtime.CompilerServices;

namespace LSLib.Granny.Model.CurveData;

public enum CurveFormat
{
    DaKeyframes32f = 0,
    DaK32fC32f = 1,
    DaIdentity = 2,
    DaConstant32f = 3,
    D3Constant32f = 4,
    D4Constant32f = 5,
    DaK16uC16u = 6,
    DaK8uC8u = 7,
    D4nK16uC15u = 8,
    D4nK8uC7u = 9,
    D3K16uC16u = 10,
    D3K8uC8u = 11,
    D9I1K16uC16u = 12,
    D9I3K16uC16u = 13,
    D9I1K8uC8u = 14,
    D9I3K8uC8u = 15,
    D3I1K32fC32f = 16,
    D3I1K16uC16u = 17,
    D3I1K8uC8u = 18
}

public static class CurveRegistry
{
    private static readonly Dictionary<string, Type> NameToTypeMap = new(StringComparer.Ordinal)
    {
        [nameof(CurveFormat.DaKeyframes32f)] = typeof(DaKeyframes32f),
        [nameof(CurveFormat.DaK32fC32f)] = typeof(DaK32fC32f),
        [nameof(CurveFormat.DaIdentity)] = typeof(DaIdentity),
        [nameof(CurveFormat.DaConstant32f)] = typeof(DaConstant32f),
        [nameof(CurveFormat.D3Constant32f)] = typeof(D3Constant32f),
        [nameof(CurveFormat.D4Constant32f)] = typeof(D4Constant32f),
        [nameof(CurveFormat.DaK16uC16u)] = typeof(DaK16uC16u),
        [nameof(CurveFormat.DaK8uC8u)] = typeof(DaK8uC8u),
        [nameof(CurveFormat.D4nK16uC15u)] = typeof(D4nK16uC15u),
        [nameof(CurveFormat.D4nK8uC7u)] = typeof(D4nK8uC7u),
        [nameof(CurveFormat.D3K16uC16u)] = typeof(D3K16uC16u),
        [nameof(CurveFormat.D3K8uC8u)] = typeof(D3K8uC8u),
        [nameof(CurveFormat.D9I1K16uC16u)] = typeof(D9I1K16uC16u),
        [nameof(CurveFormat.D9I3K16uC16u)] = typeof(D9I3K16uC16u),
        [nameof(CurveFormat.D9I1K8uC8u)] = typeof(D9I1K8uC8u),
        [nameof(CurveFormat.D9I3K8uC8u)] = typeof(D9I3K8uC8u),
        [nameof(CurveFormat.D3I1K32fC32f)] = typeof(D3I1K32fC32f),
        [nameof(CurveFormat.D3I1K16uC16u)] = typeof(D3I1K16uC16u),
        [nameof(CurveFormat.D3I1K8uC8u)] = typeof(D3I1K8uC8u)
    };

    public static Dictionary<string, Type> GetAllTypes() => NameToTypeMap;

    public static Type Resolve(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (!NameToTypeMap.TryGetValue(name, out var type))
            throw new ParsingException($"Unsupported animation curve structural descriptor identity payload: {name}");

        return type;
    }
}

public sealed class CurveDataHeader
{
    public byte Format { get; set; }
    public byte Degree { get; set; }

    public bool IsFloat() => (CurveFormat)Format switch
    {
        CurveFormat.DaKeyframes32f or
        CurveFormat.DaK32fC32f or
        CurveFormat.DaIdentity or
        CurveFormat.DaConstant32f or
        CurveFormat.D3Constant32f or
        CurveFormat.D4Constant32f => true,
        _ => false
    };

    public int BytesPerKnot() => (CurveFormat)Format switch
    {
        CurveFormat.DaKeyframes32f or
        CurveFormat.DaK32fC32f or
        CurveFormat.D3I1K32fC32f => 4,

        CurveFormat.DaIdentity or
        CurveFormat.DaConstant32f or
        CurveFormat.D3Constant32f or
        CurveFormat.D4Constant32f => throw new ParsingException("Constant structural curve frames omit explicit knot timeline serializations."),

        CurveFormat.DaK16uC16u or
        CurveFormat.D4nK16uC15u or
        CurveFormat.D3K16uC16u or
        CurveFormat.D9I1K16uC16u or
        CurveFormat.D9I3K16uC16u or
        CurveFormat.D3I1K16uC16u => 2,

        CurveFormat.DaK8uC8u or
        CurveFormat.D4nK8uC7u or
        CurveFormat.D3K8uC8u or
        CurveFormat.D9I1K8uC8u or
        CurveFormat.D9I3K8uC8u or
        CurveFormat.D3I1K8uC8u => 1,

        _ => throw new ParsingException($"Unsupported animation curve bit-width format tracker identifier: {Format}")
    };
}

public sealed class ControlUInt8 { public byte UInt8 { get; set; } = 0; }
public sealed class ControlUInt16 { public ushort UInt16 { get; set; } = 0; }
public sealed class ControlReal32 { public float Real32 { get; set; } = 0f; }

public sealed class AnimationCurveDataTypeSelector : IVariantTypeSelector
{
    public static Type SelectTypeStatic(MemberDefinition? member, object? node)
    {
        _ = member; _ = node;
        return typeof(object);
    }

    public Type SelectType(MemberDefinition member, object node) => SelectTypeStatic(member, node);

    public Type SelectType(MemberDefinition member, StructDefinition defn, object parent)
    {
        ArgumentNullException.ThrowIfNull(defn);
        _ = member; _ = parent;

        if (defn.Members is not { Count: > 0 } || string.IsNullOrEmpty(defn.Members[0].Name))
            throw new ParsingException("Structural node resolution failure: target layout parameters metadata dictionary references missing.");

        var fieldName = defn.Members[0].Name;

        if (fieldName.Length <= 16 || !fieldName.AsSpan(0, 16).Equals("CurveDataHeader_", StringComparison.Ordinal))
            throw new ParsingException($"Unrecognized curve data schema descriptor pattern validation tracking sequence: {fieldName}");

        var curveType = fieldName[16..];
        return CurveRegistry.Resolve(curveType);
    }
}

[StructSerialization(MixedMarshal = true)]
public abstract class AnimationCurveData
{
    public enum ExportType
    {
        Position,
        Rotation,
        ScaleShear
    }

    [field: Serialization(Kind = SerializationKind.None)]
    public Animation? ParentAnimation { get; set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static float ConvertOneOverKnotScaleTrunc(ushort oneOverKnotScaleTrunc)
    {
        uint rawBits = (uint)oneOverKnotScaleTrunc << 16;
        return BitConverter.UInt32BitsToSingle(rawBits);
    }

    public float Duration()
    {
        var knots = GetKnots();
        if (knots is not { Count: > 0 }) return 0f;
        return knots[^1];
    }

    public abstract int NumKnots();
    public abstract List<float> GetKnots();

    public virtual List<Vector3> GetPoints() => throw new ParsingException("Curve does not contain position data layout configurations.");
    public virtual List<Matrix3> GetMatrices() => throw new ParsingException("Curve does not contain rotation data layout configurations.");

    public virtual List<Quaternion> GetQuaternions()
    {
        var matrices = GetMatrices() ?? throw new ParsingException("Failed to unwrap rotation translation transformations matrices list.");
        var quats = new List<Quaternion>(matrices.Count);
        foreach (var matrix in matrices)
        {
            for (var i = 0; i < 3; i++)
            {
                for (var j = 0; j < i; j++)
                {
                    if (matrix[i, j] != matrix[j, i])
                        throw new ParsingException("Cannot convert into quaternion: Transformation matrix is not orthogonal!");
                }
            }

            if (MathF.Abs(matrix.Determinant - 1.0f) > 0.001f)
                throw new ParsingException("Cannot convert into quaternion: Transformation matrix is not special orthogonal!");

            quats.Add(matrix.ExtractRotation());
        }

        return quats;
    }

    public void ExportKeyframes(KeyframeTrack track, ExportType type)
    {
        ArgumentNullException.ThrowIfNull(track);

        var numKnots = NumKnots();
        var knots = GetKnots();

        switch (type)
        {
            case ExportType.Position:
                var positions = GetPoints();
                for (var i = 0; i < numKnots; i++) track.AddTranslation(knots[i], positions[i]);
                break;

            case ExportType.Rotation:
                var quats = GetQuaternions();
                for (var i = 0; i < numKnots; i++) track.AddRotation(knots[i], quats[i]);
                break;

            case ExportType.ScaleShear:
                var mats = GetMatrices();
                for (var i = 0; i < numKnots; i++) track.AddScaleShear(knots[i], mats[i]);
                break;
        }
    }
}
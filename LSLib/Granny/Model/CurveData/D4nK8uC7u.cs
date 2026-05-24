using OpenTK.Mathematics;
using LSLib.Granny.GR2;
using System.Runtime.CompilerServices;

namespace LSLib.Granny.Model.CurveData;

public sealed class D4nK8uC7u : AnimationCurveData
{
    private static ReadOnlySpan<float> ScaleTable => [
        1.4142135f, 0.70710677f, 0.35355338f, 0.35355338f,
        0.35355338f, 0.17677669f, 0.17677669f, 0.17677669f,
        -1.4142135f, -0.70710677f, -0.35355338f, -0.35355338f,
        -0.35355338f, -0.17677669f, -0.17677669f, -0.17677669f
    ];

    private static ReadOnlySpan<float> OffsetTable => [
        -0.70710677f, -0.35355338f, -0.53033006f, -0.17677669f,
        0.17677669f, -0.17677669f, -0.088388346f, 0.0f,
        0.70710677f, 0.35355338f, 0.53033006f, 0.17677669f,
        -0.17677669f, 0.17677669f, 0.088388346f, -0.0f
    ];

    [field: Serialization(Type = MemberType.Inline)]
    public CurveDataHeader CurveDataHeader_D4nK8uC7u { get; set; } = new();

    public ushort ScaleOffsetTableEntries { get; set; }
    public float OneOverKnotScale { get; set; }

    [field: Serialization(Prototype = typeof(ControlUInt8), Kind = SerializationKind.UserMember, Serializer = typeof(UInt8ListSerializer))]
    public List<byte> KnotsControls { get; set; } = [];

    public override int NumKnots() => KnotsControls.Count >> 2;

    public override List<float> GetKnots()
    {
        if (OneOverKnotScale == 0f)
        {
            throw new ParsingException("Animation curve evaluation aborted: Knot timeline scale divisor resolves to zero.");
        }

        var numKnots = NumKnots();
        var knots = new List<float>(numKnots);
        for (var i = 0; i < numKnots; i++)
        {
            knots.Add(KnotsControls[i] / OneOverKnotScale);
        }

        return knots;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Quaternion QuatFromControl(byte a, byte b, byte c, ReadOnlySpan<float> scales, ReadOnlySpan<float> offsets)
    {
        int swizzle1 = ((b & 0x80) >> 6) | ((c & 0x80) >> 7);
        int swizzle2 = (swizzle1 + 1) & 3;
        int swizzle3 = (swizzle2 + 1) & 3;
        int swizzle4 = (swizzle3 + 1) & 3;

        float dataA = (a & 0x7f) * scales[swizzle2] + offsets[swizzle2];
        float dataB = (b & 0x7f) * scales[swizzle3] + offsets[swizzle3];
        float dataC = (c & 0x7f) * scales[swizzle4] + offsets[swizzle4];

        float radicand = 1f - (dataA * dataA + dataB * dataB + dataC * dataC);
        float dataD = radicand > 0f ? MathF.Sqrt(radicand) : 0f;

        if ((a & 0x80) != 0)
        {
            dataD = -dataD;
        }

        Span<float> f = stackalloc float[4];
        f[swizzle2] = dataA;
        f[swizzle3] = dataB;
        f[swizzle4] = dataC;
        f[swizzle1] = dataD;

        return new Quaternion(f[0], f[1], f[2], f[3]);
    }

    public override List<Quaternion> GetQuaternions()
    {
        var selector = ScaleOffsetTableEntries;

        Span<float> scaleTable =
        [
            ScaleTable[(selector >> 0) & 0x0F] * 0.0078740157f,
            ScaleTable[(selector >> 4) & 0x0F] * 0.0078740157f,
            ScaleTable[(selector >> 8) & 0x0F] * 0.0078740157f,
            ScaleTable[(selector >> 12) & 0x0F] * 0.0078740157f
        ];

        Span<float> offsetTable =
        [
            OffsetTable[(selector >> 0) & 0x0F],
            OffsetTable[(selector >> 4) & 0x0F],
            OffsetTable[(selector >> 8) & 0x0F],
            OffsetTable[(selector >> 12) & 0x0F]
        ];

        var numKnots = NumKnots();
        var quats = new List<Quaternion>(numKnots);

        for (var i = 0; i < numKnots; i++)
        {
            int baseIdx = numKnots + (i * 3);
            if (baseIdx + 2 >= KnotsControls.Count) break;

            var quat = QuatFromControl(
                KnotsControls[baseIdx + 0],
                KnotsControls[baseIdx + 1],
                KnotsControls[baseIdx + 2],
                scaleTable, offsetTable
            );
            quats.Add(quat);
        }

        return quats;
    }
}
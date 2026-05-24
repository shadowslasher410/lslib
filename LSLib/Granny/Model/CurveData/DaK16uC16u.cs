using OpenTK.Mathematics;
using LSLib.Granny.GR2;

namespace LSLib.Granny.Model.CurveData;

public sealed class DaK16uC16u : AnimationCurveData
{
    [field: Serialization(Type = MemberType.Inline)]
    public CurveDataHeader CurveDataHeader_DaK16uC16u { get; set; } = new();

    public ushort OneOverKnotScaleTrunc { get; set; }

    [field: Serialization(Prototype = typeof(ControlReal32), Kind = SerializationKind.UserMember, Serializer = typeof(SingleListSerializer))]
    public List<float> ControlScaleOffsets { get; set; } = [];

    [field: Serialization(Prototype = typeof(ControlUInt16), Kind = SerializationKind.UserMember, Serializer = typeof(UInt16ListSerializer))]
    public List<ushort> KnotsControls { get; set; } = [];

    public int Components() => ControlScaleOffsets.Count >> 1;

    public override int NumKnots()
    {
        int comps = Components();
        if (comps == -1) return 0;
        return KnotsControls.Count / (comps + 1);
    }

    public override List<float> GetKnots()
    {
        var scale = ConvertOneOverKnotScaleTrunc(OneOverKnotScaleTrunc);
        if (scale == 0f)
        {
            throw new ParsingException("Animation curve evaluation aborted: Knot timeline scale divisor resolves to zero.");
        }

        var numKnots = NumKnots();
        var knots = new List<float>(numKnots);

        for (var i = 0; i < numKnots; i++)
        {
            knots.Add(KnotsControls[i] / scale);
        }

        return knots;
    }

    public override List<Matrix3> GetMatrices()
    {
        int comps = Components();
        if (comps != 9)
        {
            throw new ParsingException($"Matrix transformation mapping aborted: Components mismatch (Expected 9, encountered {comps}).");
        }

        if (ControlScaleOffsets.Count < 18)
        {
            throw new ParsingException("Decompression aborted: ControlScaleOffsets array contains insufficient layout layout vectors.");
        }

        var numKnots = NumKnots();
        var knots = new List<Matrix3>(numKnots);

        ReadOnlySpan<float> scalesOffsets = CollectionsMarshal.AsSpan(ControlScaleOffsets);

        for (var i = 0; i < numKnots; i++)
        {
            int baseIndex = numKnots + (i * 9);
            if (baseIndex + 8 >= KnotsControls.Count) break;

            var mat = new Matrix3(
                KnotsControls[baseIndex + 0] * scalesOffsets[0] + scalesOffsets[9 + 0],
                KnotsControls[baseIndex + 1] * scalesOffsets[1] + scalesOffsets[9 + 1],
                KnotsControls[baseIndex + 2] * scalesOffsets[2] + scalesOffsets[9 + 2],
                KnotsControls[baseIndex + 3] * scalesOffsets[3] + scalesOffsets[9 + 3],
                KnotsControls[baseIndex + 4] * scalesOffsets[4] + scalesOffsets[9 + 4],
                KnotsControls[baseIndex + 5] * scalesOffsets[5] + scalesOffsets[9 + 5],
                KnotsControls[baseIndex + 6] * scalesOffsets[6] + scalesOffsets[9 + 6],
                KnotsControls[baseIndex + 7] * scalesOffsets[7] + scalesOffsets[9 + 7],
                KnotsControls[baseIndex + 8] * scalesOffsets[8] + scalesOffsets[9 + 8]
            );
            knots.Add(mat);
        }

        return knots;
    }

    public override List<Quaternion> GetQuaternions()
    {
        int comps = Components();
        if (comps != 4)
        {
            throw new ParsingException($"Quaternion rotation mapping aborted: Components mismatch (Expected 4, encountered {comps}).");
        }

        if (ControlScaleOffsets.Count < 8)
        {
            throw new ParsingException("Decompression aborted: ControlScaleOffsets array contains insufficient layout layout vectors.");
        }

        var numKnots = NumKnots();
        var quats = new List<Quaternion>(numKnots);

        ReadOnlySpan<float> scalesOffsets = CollectionsMarshal.AsSpan(ControlScaleOffsets);

        for (var i = 0; i < numKnots; i++)
        {
            int baseIndex = numKnots + (i * 4);
            if (baseIndex + 3 >= KnotsControls.Count) break;

            var quat = new Quaternion(
                KnotsControls[baseIndex + 0] * scalesOffsets[0] + scalesOffsets[4 + 0],
                KnotsControls[baseIndex + 1] * scalesOffsets[1] + scalesOffsets[4 + 1],
                KnotsControls[baseIndex + 2] * scalesOffsets[2] + scalesOffsets[4 + 2],
                KnotsControls[baseIndex + 3] * scalesOffsets[3] + scalesOffsets[4 + 3]
            );
            quats.Add(quat);
        }

        return quats;
    }
}
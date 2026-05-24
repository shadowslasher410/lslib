using OpenTK.Mathematics;
using LSLib.Granny.GR2;

namespace LSLib.Granny.Model.CurveData;

public sealed class D3K8uC8u : AnimationCurveData
{
    [field: Serialization(Type = MemberType.Inline)]
    public CurveDataHeader CurveDataHeader_D3K8uC8u { get; set; } = new();

    public ushort OneOverKnotScaleTrunc { get; set; }

    [field: Serialization(ArraySize = 3)]
    public float[] ControlScales { get; set; } = [];

    [field: Serialization(ArraySize = 3)]
    public float[] ControlOffsets { get; set; } = [];

    [field: Serialization(Prototype = typeof(ControlUInt8), Kind = SerializationKind.UserMember, Serializer = typeof(UInt8ListSerializer))]
    public List<byte> KnotsControls { get; set; } = [];

    public override int NumKnots() => KnotsControls.Count >> 2;

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

    public override List<Vector3> GetPoints()
    {
        if (ControlScales is not { Length: >= 3 } || ControlOffsets is not { Length: >= 3 })
        {
            throw new ParsingException("Compressed curve mapping aborted: Scales/Offsets array contains insufficient layout vectors.");
        }

        var numKnots = NumKnots();
        var knots = new List<Vector3>(numKnots);

        float scaleX = ControlScales[0], scaleY = ControlScales[1], scaleZ = ControlScales[2];
        float offsetX = ControlOffsets[0], offsetY = ControlOffsets[1], offsetZ = ControlOffsets[2];

        for (var i = 0; i < numKnots; i++)
        {
            int baseIndex = numKnots + (i * 3);
            if (baseIndex + 2 >= KnotsControls.Count) break;

            var vec = new Vector3(
                KnotsControls[baseIndex + 0] * scaleX + offsetX,
                KnotsControls[baseIndex + 1] * scaleY + offsetY,
                KnotsControls[baseIndex + 2] * scaleZ + offsetZ
            );
            knots.Add(vec);
        }

        return knots;
    }
}
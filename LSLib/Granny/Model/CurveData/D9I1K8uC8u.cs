using OpenTK.Mathematics;
using LSLib.Granny.GR2;

namespace LSLib.Granny.Model.CurveData;

public sealed class D9I1K8uC8u : AnimationCurveData
{
    [field: Serialization(Type = MemberType.Inline)]
    public CurveDataHeader CurveDataHeader_D9I1K8uC8u { get; set; } = new();

    public ushort OneOverKnotScaleTrunc { get; set; }
    public float ControlScale { get; set; }
    public float ControlOffset { get; set; }

    [field: Serialization(Prototype = typeof(ControlUInt8), Kind = SerializationKind.UserMember, Serializer = typeof(UInt8ListSerializer))]
    public List<byte> KnotsControls { get; set; } = [];

    public override int NumKnots() => KnotsControls.Count >> 1;

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

    public override List<Quaternion> GetQuaternions()
    {
        throw new InvalidOperationException("D9I1K8uC8u represents a scaling-shear matrix track and is not a rotation curve!");
    }

    public override List<Matrix3> GetMatrices()
    {
        var numKnots = NumKnots();
        var knots = new List<Matrix3>(numKnots);

        float localScale = ControlScale;
        float localOffset = ControlOffset;

        for (var i = 0; i < numKnots; i++)
        {
            int controlIndex = numKnots + i;
            if (controlIndex >= KnotsControls.Count) break;

            var scaleFactor = KnotsControls[controlIndex] * localScale + localOffset;
            var mat = new Matrix3(
                scaleFactor, 0f, 0f,
                0f, scaleFactor, 0f,
                0f, 0f, scaleFactor
            );
            knots.Add(mat);
        }

        return knots;
    }
}
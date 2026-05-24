using OpenTK.Mathematics;
using LSLib.Granny.GR2;

namespace LSLib.Granny.Model.CurveData;

public sealed class DaConstant32f : AnimationCurveData
{
    [field: Serialization(Type = MemberType.Inline)]
    public CurveDataHeader CurveDataHeader_DaConstant32f { get; set; } = new();

    public short Padding { get; set; }

    [field: Serialization(Prototype = typeof(ControlReal32), Kind = SerializationKind.UserMember, Serializer = typeof(SingleListSerializer))]
    public List<float> Controls { get; set; } = [];

    public override int NumKnots() => 1;

    public override List<float> GetKnots() => [0.0f];

    public override List<Matrix3> GetMatrices()
    {
        if (Controls is not { Count: 9 })
        {
            throw new ParsingException($"Constant matrix evaluation aborted: Controls source collection requires exactly 9 elements, found {Controls?.Count ?? 0}.");
        }

        var m = Controls;
        var mat = new Matrix3(
            m[0], m[1], m[2],
            m[3], m[4], m[5],
            m[6], m[7], m[8]
        );

        return [mat];
    }
}
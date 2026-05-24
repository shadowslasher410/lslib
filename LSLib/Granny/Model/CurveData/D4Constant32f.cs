using OpenTK.Mathematics;
using LSLib.Granny.GR2;

namespace LSLib.Granny.Model.CurveData;

public sealed class D4Constant32f : AnimationCurveData
{
    [field: Serialization(Type = MemberType.Inline)]
    public CurveDataHeader CurveDataHeader_D4Constant32f { get; set; } = new();

    public short Padding { get; set; }

    [field: Serialization(ArraySize = 4)]
    public float[] Controls { get; set; } = [];

    public override int NumKnots() => 1;

    public override List<float> GetKnots() => [0.0f];

    public override List<Matrix3> GetMatrices()
    {
        if (Controls is not { Length: >= 4 })
        {
            throw new ParsingException("Constant 4-component vector processing aborted: Controls source data array contains insufficient layout metrics.");
        }

        var q = new Quaternion(Controls[0], Controls[1], Controls[2], Controls[3]);

        return
        [
            Matrix3.CreateFromQuaternion(q)
        ];
    }

    public override List<Quaternion> GetQuaternions()
    {
        if (Controls is not { Length: >= 4 })
        {
            throw new ParsingException("Constant 4-component vector processing aborted: Controls source data array contains insufficient layout metrics.");
        }

        return
        [
            new(Controls[0], Controls[1], Controls[2], Controls[3])
        ];
    }
}
using OpenTK.Mathematics;
using LSLib.Granny.GR2;

namespace LSLib.Granny.Model.CurveData;

public sealed class D3Constant32f : AnimationCurveData
{
    [field: Serialization(Type = MemberType.Inline)]
    public CurveDataHeader CurveDataHeader_D3Constant32f { get; set; } = new();

    public short Padding { get; set; }

    [field: Serialization(ArraySize = 3)]
    public float[] Controls { get; set; } = [];

    public override int NumKnots() => 1;

    public override List<float> GetKnots() => [0.0f];

    public override List<Vector3> GetPoints()
    {
        if (Controls is not { Length: >= 3 })
        {
            throw new ParsingException("Constant curve mapping execution aborted: Controls source data array contains insufficient layout vectors.");
        }

        return
        [
            new(Controls[0], Controls[1], Controls[2])
        ];
    }
}
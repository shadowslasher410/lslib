using OpenTK.Mathematics;
using LSLib.Granny.GR2;

namespace LSLib.Granny.Model.CurveData;

public sealed class D3I1K32fC32f : AnimationCurveData
{
    [field: Serialization(Type = MemberType.Inline)]
    public CurveDataHeader CurveDataHeader_D3I1K32fC32f { get; set; } = new();

    public ushort Padding { get; set; }

    [field: Serialization(ArraySize = 3)]
    public float[] ControlScales { get; set; } = [];

    [field: Serialization(ArraySize = 3)]
    public float[] ControlOffsets { get; set; } = [];

    [field: Serialization(Prototype = typeof(ControlReal32), Kind = SerializationKind.UserMember, Serializer = typeof(SingleListSerializer))]
    public List<float> KnotsControls { get; set; } = [];

    public override int NumKnots() => KnotsControls.Count >> 1;

    public override List<float> GetKnots()
    {
        var numKnots = NumKnots();
        var knots = new List<float>(numKnots);

        for (var i = 0; i < numKnots; i++)
        {
            knots.Add(KnotsControls[i]);
        }

        return knots;
    }

    public override List<Vector3> GetPoints()
    {
        if (ControlScales is not { Length: >= 3 } || ControlOffsets is not { Length: >= 3 })
        {
            throw new ParsingException("Constant curve processing aborted: Scales/Offsets array contains insufficient dimension vectors.");
        }

        var numKnots = NumKnots();
        var knots = new List<Vector3>(numKnots);

        float scaleX = ControlScales[0], scaleY = ControlScales[1], scaleZ = ControlScales[2];
        float offsetX = ControlOffsets[0], offsetY = ControlOffsets[1], offsetZ = ControlOffsets[2];

        for (var i = 0; i < numKnots; i++)
        {
            int controlIndex = numKnots + i;
            if (controlIndex >= KnotsControls.Count) break;

            float rawControl = KnotsControls[controlIndex];
            var vec = new Vector3(
                rawControl * scaleX + offsetX,
                rawControl * scaleY + offsetY,
                rawControl * scaleZ + offsetZ
            );
            knots.Add(vec);
        }

        return knots;
    }
}

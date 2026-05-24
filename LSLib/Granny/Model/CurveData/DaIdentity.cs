using OpenTK.Mathematics;
using LSLib.Granny.GR2;

namespace LSLib.Granny.Model.CurveData;

public sealed class DaIdentity : AnimationCurveData
{
    [field: Serialization(Type = MemberType.Inline)]
    public CurveDataHeader CurveDataHeader_DaIdentity { get; set; } = new();

    public short Dimension { get; set; }

    public override int NumKnots() => 1;

    public override List<float> GetKnots() => [0.0f];

    public override List<Vector3> GetPoints()
    {
        return [Vector3.Zero];
    }

    public override List<Matrix3> GetMatrices()
    {
        return [Matrix3.Identity];
    }
}
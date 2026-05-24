using OpenTK.Mathematics;
using LSLib.Granny.GR2;

namespace LSLib.Granny.Model.CurveData;

public sealed class DaK32fC32f : AnimationCurveData
{
    [field: Serialization(Type = MemberType.Inline)]
    public CurveDataHeader CurveDataHeader_DaK32fC32f { get; set; } = new();

    public short Padding { get; set; }

    [field: Serialization(Prototype = typeof(ControlReal32), Kind = SerializationKind.UserMember, Serializer = typeof(SingleListSerializer))]
    public List<float> Knots { get; set; } = [];

    [field: Serialization(Prototype = typeof(ControlReal32), Kind = SerializationKind.UserMember, Serializer = typeof(SingleListSerializer))]
    public List<float> Controls { get; set; } = [];

    public ExportType CurveType()
    {
        int knotCount = Knots.Count;
        int ctrlCount = Controls.Count;

        if (knotCount * 3 == ctrlCount) return ExportType.Position;
        if (knotCount * 4 == ctrlCount) return ExportType.Rotation;
        if (knotCount * 9 == ctrlCount) return ExportType.ScaleShear;

        throw new NotSupportedException($"Unsupported DaK32fC32f data alignment thresholds context properties mapping window: Knots={knotCount}, Controls={ctrlCount}");
    }

    public override int NumKnots() => Knots.Count;

    public override List<float> GetKnots()
    {
        int numKnots = NumKnots();
        var result = new List<float>(numKnots);

        result.AddRange(CollectionsMarshal.AsSpan(Knots));
        return result;
    }

    public void SetKnots(List<float> knots)
    {
        ArgumentNullException.ThrowIfNull(knots);
        Knots = new List<float>(knots.Count);
        Knots.AddRange(CollectionsMarshal.AsSpan(knots));
    }

    public override List<Vector3> GetPoints()
    {
        if (CurveType() != ExportType.Position)
            throw new InvalidOperationException("DaK32fC32f: This curve is not a position curve!");

        int numKnots = NumKnots();
        var positions = new List<Vector3>(numKnots);
        ReadOnlySpan<float> ctrlSpan = CollectionsMarshal.AsSpan(Controls);

        for (int i = 0; i < numKnots; i++)
        {
            int baseIdx = i * 3;
            if (baseIdx + 2 >= ctrlSpan.Length) break;

            positions.Add(new Vector3(ctrlSpan[baseIdx + 0], ctrlSpan[baseIdx + 1], ctrlSpan[baseIdx + 2]));
        }

        return positions;
    }

    public void SetPoints(List<Vector3> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        Controls = new List<float>(points.Count * 3);
        ReadOnlySpan<Vector3> ptsSpan = CollectionsMarshal.AsSpan(points);

        for (int i = 0; i < ptsSpan.Length; i++)
        {
            var p = ptsSpan[i];
            Controls.Add(p.X);
            Controls.Add(p.Y);
            Controls.Add(p.Z);
        }
    }

    public override List<Matrix3> GetMatrices()
    {
        if (CurveType() != ExportType.ScaleShear)
            throw new InvalidOperationException("DaK32fC32f: This curve is not a scale/shear curve!");

        int numKnots = NumKnots();
        var scaleShear = new List<Matrix3>(numKnots);
        ReadOnlySpan<float> ctrlSpan = CollectionsMarshal.AsSpan(Controls);

        for (int i = 0; i < numKnots; i++)
        {
            int baseIdx = i * 9;
            if (baseIdx + 8 >= ctrlSpan.Length) break;

            scaleShear.Add(new Matrix3(
                ctrlSpan[baseIdx + 0], ctrlSpan[baseIdx + 1], ctrlSpan[baseIdx + 2],
                ctrlSpan[baseIdx + 3], ctrlSpan[baseIdx + 4], ctrlSpan[baseIdx + 5],
                ctrlSpan[baseIdx + 6], ctrlSpan[baseIdx + 7], ctrlSpan[baseIdx + 8]
            ));
        }

        return scaleShear;
    }

    public void SetMatrices(List<Matrix3> matrices)
    {
        ArgumentNullException.ThrowIfNull(matrices);

        Controls = new List<float>(matrices.Count * 9);
        ReadOnlySpan<Matrix3> matsSpan = CollectionsMarshal.AsSpan(matrices);

        for (int i = 0; i < matsSpan.Length; i++)
        {
            var m = matsSpan[i];
            Controls.Add(m[0, 0]); Controls.Add(m[0, 1]); Controls.Add(m[0, 2]);
            Controls.Add(m[1, 0]); Controls.Add(m[1, 1]); Controls.Add(m[1, 2]);
            Controls.Add(m[2, 0]); Controls.Add(m[2, 1]); Controls.Add(m[2, 2]);
        }
    }

    public override List<Quaternion> GetQuaternions()
    {
        if (CurveType() != ExportType.Rotation)
            throw new InvalidOperationException("DaK32fC32f: This curve is not a rotation curve!");

        int numKnots = NumKnots();
        var rotations = new List<Quaternion>(numKnots);
        ReadOnlySpan<float> ctrlSpan = CollectionsMarshal.AsSpan(Controls);

        for (int i = 0; i < numKnots; i++)
        {
            int baseIdx = i * 4;
            if (baseIdx + 3 >= ctrlSpan.Length) break;

            rotations.Add(new Quaternion(
                ctrlSpan[baseIdx + 0],
                ctrlSpan[baseIdx + 1],
                ctrlSpan[baseIdx + 2],
                ctrlSpan[baseIdx + 3]
            ));
        }

        return rotations;
    }

    public void SetQuaternions(List<Quaternion> quats)
    {
        ArgumentNullException.ThrowIfNull(quats);

        Controls = new List<float>(quats.Count * 4);
        ReadOnlySpan<Quaternion> qSpan = CollectionsMarshal.AsSpan(quats);

        for (int i = 0; i < qSpan.Length; i++)
        {
            var q = qSpan[i];
            Controls.Add(q.X);
            Controls.Add(q.Y);
            Controls.Add(q.Z);
            Controls.Add(q.W);
        }
    }
}
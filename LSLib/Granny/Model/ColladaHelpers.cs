using OpenTK.Mathematics;
using LSLib.Granny.GR2;
using System.Reflection;

namespace LSLib.Granny.Model;

public static class NodeHelpers
{
    public static Matrix4 GetTransformHierarchy(IEnumerable<node> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var accum = Matrix4.Identity;
        foreach (var node in nodes)
        {
            accum = ColladaHelpers.TransformFromNode(node).Transform * accum;
        }
        return accum;
    }

    public static Matrix4 ToMatrix4(this matrix m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var v = m.Values;
        if (v.Length < 16) throw new ParsingException("Insufficient element bounds inside matrix values collection.");

        return new Matrix4(
            (float)v[0], (float)v[1], (float)v[2], (float)v[3],
            (float)v[4], (float)v[5], (float)v[6], (float)v[7],
            (float)v[8], (float)v[9], (float)v[10], (float)v[11],
            (float)v[12], (float)v[13], (float)v[14], (float)v[15]
        );
    }

    public static List<int> StringsToIntegers(string s)
    {
        if (string.IsNullOrEmpty(s)) return [];

        var integersList = new List<int>(s.Length / 6);
        ReadOnlySpan<char> textSpan = s.AsSpan();

        int startingPos = -1;
        for (int i = 0; i < textSpan.Length; i++)
        {
            if (textSpan[i] != ' ')
            {
                if (startingPos == -1) startingPos = i;
            }
            else
            {
                if (startingPos != -1)
                {
                    integersList.Add(int.Parse(textSpan[startingPos..i]));
                    startingPos = -1;
                }
            }
        }

        if (startingPos != -1)
        {
            integersList.Add(int.Parse(textSpan[startingPos..]));
        }

        return integersList;
    }

    public static Matrix4 FloatsToMatrix(float[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Length < 16) throw new ParsingException("Insufficient element bounds inside floats collection to construct transformation row matrix.");

        return new Matrix4(
            items[0], items[1], items[2], items[3],
            items[4], items[5], items[6], items[7],
            items[8], items[9], items[10], items[11],
            items[12], items[13], items[14], items[15]
        );
    }

    public static List<Vector3> SourceToPositions(object source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Type sourceType = source.GetType();
        var floatParamsProp = sourceType.GetProperty("FloatParams", BindingFlags.Public | BindingFlags.Instance)
                              ?? sourceType.GetProperty("floatParams", BindingFlags.Public | BindingFlags.Instance)
                              ?? sourceType.GetField("FloatParams", BindingFlags.Public | BindingFlags.Instance)
                              ?? (MemberInfo?)sourceType.GetField("floatParams", BindingFlags.Public | BindingFlags.Instance);

        var floatParamsDict = (floatParamsProp is PropertyInfo p) ? p.GetValue(source) : ((FieldInfo?)floatParamsProp)?.GetValue(source);
        if (floatParamsDict is not IDictionary<string, List<float>> dict)
        {
            var sourceId = sourceType.GetProperty("Id")?.GetValue(source)?.ToString() ?? sourceType.GetField("id")?.GetValue(source)?.ToString() ?? "unknown";
            throw new ParsingException($"Position source tracking error on ID {sourceId}: float parameters collection is inaccessible.");
        }

        if (!dict.TryGetValue("X", out var x) || !dict.TryGetValue("Y", out var y) || !dict.TryGetValue("Z", out var z))
        {
            var sourceId = sourceType.GetProperty("Id")?.GetValue(source)?.ToString() ?? sourceType.GetField("id")?.GetValue(source)?.ToString() ?? "unknown";
            throw new ParsingException($"Position source tracking error on ID {sourceId}: coordinate layout parameters must specify X, Y, Z dimensional sequences.");
        }

        int count = Math.Min(x.Count, Math.Min(y.Count, z.Count));
        var positions = new List<Vector3>(count);

        for (var i = 0; i < count; i++)
        {
            positions.Add(new Vector3(x[i], y[i], z[i]));
        }

        return positions;
    }
}

public static class ColladaHelpers
{
    public sealed class TransformMatrix
    {
        public Matrix4 Transform { get; set; } = Matrix4.Identity;
        public string? TransformSID { get; set; }
    }

    public static void ApplyMatrixTransform(TransformMatrix transformMat, matrix m)
    {
        ArgumentNullException.ThrowIfNull(transformMat);
        ArgumentNullException.ThrowIfNull(m);

        var values = m.Values;
        if (values.Length < 16) throw new ParsingException("Truncated matrix parameter data bounds.");

        var mat = new Matrix4(
            (float)values[0], (float)values[1], (float)values[2], (float)values[3],
            (float)values[4], (float)values[5], (float)values[6], (float)values[7],
            (float)values[8], (float)values[9], (float)values[10], (float)values[11],
            (float)values[12], (float)values[13], (float)values[14], (float)values[15]
        );
        mat.Transpose();
        transformMat.Transform *= mat;
    }

    public static void ApplyTranslation(TransformMatrix transformMat, TargetableFloat3 translation)
    {
        ArgumentNullException.ThrowIfNull(transformMat);
        ArgumentNullException.ThrowIfNull(translation);

        var v = translation.Values;
        if (v.Length < 3) throw new ParsingException("Invalid translation array metrics parameter.");

        var translationMat = Matrix4.CreateTranslation((float)v[0], (float)v[1], (float)v[2]);
        transformMat.Transform = translationMat * transformMat.Transform;
    }

    public static void ApplyRotation(TransformMatrix transformMat, rotate rotation)
    {
        ArgumentNullException.ThrowIfNull(transformMat);
        ArgumentNullException.ThrowIfNull(rotation);

        var v = rotation.Values;
        if (v.Length < 4) throw new ParsingException("Invalid rotation array metrics parameter.");

        var axis = new Vector3((float)v[0], (float)v[1], (float)v[2]);
        var rotationMat = Matrix4.CreateFromAxisAngle(axis, (float)v[3]);
        transformMat.Transform = rotationMat * transformMat.Transform;
    }

    public static void ApplyScale(TransformMatrix transformMat, TargetableFloat3 scale)
    {
        ArgumentNullException.ThrowIfNull(transformMat);
        ArgumentNullException.ThrowIfNull(scale);

        var v = scale.Values;
        if (v.Length < 3) throw new ParsingException("Invalid scaling array metrics parameter.");

        var scaleMat = Matrix4.CreateScale((float)v[0], (float)v[1], (float)v[2]);
        transformMat.Transform = scaleMat * transformMat.Transform;
    }

    public static TransformMatrix TransformFromNode(node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var transform = new TransformMatrix
        {
            Transform = Matrix4.Identity,
            TransformSID = null
        };

        if (node.ItemsElementName is not null && node.Items is not null)
        {
            int loopLimit = Math.Min(node.ItemsElementName.Length, node.Items.Length);
            for (int i = 0; i < loopLimit; i++)
            {
                var name = node.ItemsElementName[i];
                var item = node.Items[i];

                switch (name)
                {
                    case ItemsChoiceType2.translate when item is TargetableFloat3 translation:
                        ApplyTranslation(transform, translation);
                        break;

                    case ItemsChoiceType2.rotate when item is rotate rotation:
                        ApplyRotation(transform, rotation);
                        break;

                    case ItemsChoiceType2.scale when item is TargetableFloat3 scale:
                        ApplyScale(transform, scale);
                        break;

                    case ItemsChoiceType2.matrix when item is matrix mat:
                        transform.TransformSID = mat.sid;
                        ApplyMatrixTransform(transform, mat);
                        break;
                }
            }
        }

        return transform;
    }
}
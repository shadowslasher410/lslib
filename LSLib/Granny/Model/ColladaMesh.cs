using OpenTK.Mathematics;

namespace LSLib.Granny.Model;

public sealed class ColladaMeshException(string message) : Exception(message);

public sealed class ColladaMesh
{
    public string MeshId { get; set; } = string.Empty;
    public List<Vector3> Positions { get; set; } = [];
    public List<Vector3> Normals { get; set; } = [];
    public List<List<Vector2>> UVs { get; set; } = [];
    public List<List<Vector4>> Colors { get; set; } = [];
    public List<int> Indices { get; set; } = [];

    public static List<float> ParseFloatString(string rawText)
    {
        if (string.IsNullOrEmpty(rawText)) return [];

        var valuesList = new List<float>(rawText.Length / 6);
        ReadOnlySpan<char> textSpan = rawText.AsSpan();

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
                    valuesList.Add(float.Parse(textSpan[startingPos..i], System.Globalization.CultureInfo.InvariantCulture));
                    startingPos = -1;
                }
            }
        }

        if (startingPos != -1)
        {
            valuesList.Add(float.Parse(textSpan[startingPos..], System.Globalization.CultureInfo.InvariantCulture));
        }

        return valuesList;
    }

    public static List<Vector3> RebuildVector3Pool(float[] sourceValues)
    {
        ArgumentNullException.ThrowIfNull(sourceValues);
        if (sourceValues.Length % 3 != 0)
        {
            throw new ColladaMeshException("COLLADA vector data parsing aborted: Source floating point element count is not a multiple of 3.");
        }

        int elementCount = sourceValues.Length / 3;
        var vectors = new List<Vector3>(elementCount);
        ReadOnlySpan<float> sourceSpan = sourceValues.AsSpan();

        for (int i = 0; i < sourceSpan.Length; i += 3)
        {
            vectors.Add(new Vector3(sourceSpan[i], sourceSpan[i + 1], sourceSpan[i + 2]));
        }

        return vectors;
    }

    public void MapIndicesStream(int[] rawPolygonInputs, int inputsStride, int positionOffset, int _)
    {
        ArgumentNullException.ThrowIfNull(rawPolygonInputs);
        ArgumentOutOfRangeException.ThrowIfZero(inputsStride);

        ReadOnlySpan<int> inputSpan = rawPolygonInputs.AsSpan();
        int triangleCount = inputSpan.Length / inputsStride;

        Indices.EnsureCapacity(Indices.Count + triangleCount);

        for (int i = 0; i < inputSpan.Length; i += inputsStride)
        {
            if (i + positionOffset < inputSpan.Length)
            {
                Indices.Add(inputSpan[i + positionOffset]);
            }
        }
    }
}
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;

namespace LSLib.Granny.Model;

public sealed class VertexSerializationException(string message) : Exception(message);

public sealed class VertexSerializer()
{

    public static VertexData Unpack(ReadOnlySpan<byte> rawBytes, uint vertexCount)
    {
        ArgumentOutOfRangeException.ThrowIfZero(vertexCount);
        if (rawBytes.IsEmpty) throw new VertexSerializationException("Vertex data unpacking aborted: Underlying byte stream payload is completely empty.");

        var vertexData = new VertexData
        {
            Vertices = new List<float>((int)(rawBytes.Length / sizeof(float)))
        };

        ReadOnlySpan<float> floatsView = MemoryMarshal.Cast<byte, float>(rawBytes);
        for (int i = 0; i < floatsView.Length; i++)
        {
            vertexData.Vertices.Add(floatsView[i]);
        }

        return vertexData;
    }

    public static byte[] Pack(VertexData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Vertices is not { Count: > 0 }) return [];

        ReadOnlySpan<float> floatsSpan = CollectionsMarshal.AsSpan(data.Vertices);
        ReadOnlySpan<byte> bytesView = MemoryMarshal.AsBytes(floatsSpan);

        return bytesView.ToArray();
    }
}

public static class VertexDecompressor
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 UnpackNormal8Bit(byte x, byte y, byte z)
    {
        var vec = new Vector3(
            (x / 127.5f) - 1.0f,
            (y / 127.5f) - 1.0f,
            (z / 127.5f) - 1.0f
        );
        return vec.Normalized();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 UnpackNormal16Bit(ushort x, ushort y, ushort z)
    {
        var vec = new Vector3(
            (x / 32767.5f) - 1.0f,
            (y / 32767.5f) - 1.0f,
            (z / 32767.5f) - 1.0f
        );
        return vec.Normalized();
    }
}

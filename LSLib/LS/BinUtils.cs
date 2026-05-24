using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;

namespace LSLib.LS;


public class ReadOnlySubstream(Stream sourceStream, long offset, long size) : Stream
{
    private readonly Stream _sourceStream = sourceStream ?? throw new ArgumentNullException(nameof(sourceStream));
    private readonly long _fileOffset = offset;
    private readonly long _size = size;
    private long _curPosition;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _size;

    public override long Position
    {
        get => _curPosition;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _sourceStream.Seek(_fileOffset + _curPosition, SeekOrigin.Begin);
        long readable = _size - _curPosition;
        int bytesToRead = readable < count ? (int)readable : count;
        int read = _sourceStream.Read(buffer, offset, bytesToRead);
        _curPosition += read;
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        _sourceStream.Seek(_fileOffset + _curPosition, SeekOrigin.Begin);
        long readable = _size - _curPosition;
        int bytesToRead = readable < buffer.Length ? (int)readable : buffer.Length;
        int read = _sourceStream.Read(buffer[..bytesToRead]);
        _curPosition += read;
        return read;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _sourceStream.Seek(_fileOffset + _curPosition, SeekOrigin.Begin);
        long readable = _size - _curPosition;
        int bytesToRead = readable < buffer.Length ? (int)readable : buffer.Length;
        _curPosition += bytesToRead;
        return _sourceStream.ReadAsync(buffer[..bytesToRead], cancellationToken);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _sourceStream.Seek(_fileOffset + _curPosition, SeekOrigin.Begin);
        long readable = _size - _curPosition;
        int bytesToRead = readable < count ? (int)readable : count;
        _curPosition += bytesToRead;
        return _sourceStream.ReadAsync(buffer, offset, bytesToRead, cancellationToken);
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() { }
}

public static class BinUtils
{
    public static T ReadStruct<T>(BinaryReader reader) where T : struct
    {
        ArgumentNullException.ThrowIfNull(reader);
        int count = Unsafe.SizeOf<T>();
        byte[] readBuffer = reader.ReadBytes(count);
        if (readBuffer.Length < count) throw new EndOfStreamException($"Required {count} bytes, but reached EOF.");
        return MemoryMarshal.Read<T>(readBuffer);
    }

    public static void ReadStructs<T>(BinaryReader reader, T[] elements) where T : struct
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(elements);
        int elementSize = Unsafe.SizeOf<T>();
        int totalBytes = elementSize * elements.Length;
        byte[] readBuffer = reader.ReadBytes(totalBytes);
        if (readBuffer.Length < totalBytes) throw new EndOfStreamException();

        Span<byte> sourceSpan = readBuffer;
        Span<T> targetSpan = elements;
        MemoryMarshal.Cast<byte, T>(sourceSpan).CopyTo(targetSpan);
    }

    public static void ReadStructsBlitted<T>(BinaryReader reader, T[] elements) where T : struct
    {
        ReadStructs(reader, elements);
    }

    public static void ReadStructs<T>(MemoryMappedViewAccessor view, long offset, T[] elements) where T : struct
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(elements);
        int elementSize = Unsafe.SizeOf<T>();
        int totalBytes = elementSize * elements.Length;
        byte[] readBuffer = new byte[totalBytes];
        view.ReadArray(offset, readBuffer, 0, totalBytes);

        Span<byte> sourceSpan = readBuffer;
        Span<T> targetSpan = elements;
        MemoryMarshal.Cast<byte, T>(sourceSpan).CopyTo(targetSpan);
    }

    public static void WriteStruct<T>(BinaryWriter writer, ref T inStruct) where T : struct
    {
        ArgumentNullException.ThrowIfNull(writer);
        ReadOnlySpan<T> structSpan = MemoryMarshal.CreateReadOnlySpan(ref inStruct, 1);
        ReadOnlySpan<byte> byteSpan = MemoryMarshal.Cast<T, byte>(structSpan);
        writer.Write(byteSpan);
    }

    public static void WriteStructs<T>(BinaryWriter writer, T[] elements) where T : struct
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(elements);
        ReadOnlySpan<byte> byteSpan = MemoryMarshal.Cast<T, byte>(elements);
        writer.Write(byteSpan);
    }

    public static unsafe string NullTerminatedBytesToString(byte[] b)
    {
        ArgumentNullException.ThrowIfNull(b);
        int len = Array.IndexOf(b, (byte)0);
        if (len == 0) return string.Empty;
        if (len < 0) len = b.Length;
        return Encoding.UTF8.GetString(b, 0, len);
    }

    public static unsafe string NullTerminatedBytesToString(FileNameBlittable b)
    {
        ReadOnlySpan<byte> span = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<FileNameBlittable, byte>(ref Unsafe.AsRef(in b)), 256);
        int len = span.IndexOf((byte)0);
        if (len == 0) return string.Empty;
        if (len < 0) len = 256;
        return Encoding.UTF8.GetString(span[..len]);
    }

    public static byte[] StringToNullTerminatedBytes(string s, int length)
    {
        ArgumentNullException.ThrowIfNull(s);
        var b = new byte[length];
        int len = Encoding.UTF8.GetBytes(s, b);
        Array.Clear(b, len, b.Length - len);
        return b;
    }

    public static FileNameBlittable StringToNullTerminatedBlittableBytes(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var b = new FileNameBlittable();
        Span<byte> bs = MemoryMarshal.CreateSpan(ref Unsafe.As<FileNameBlittable, byte>(ref b), 256);
        int len = Encoding.UTF8.GetBytes(s, bs);
        bs[len..].Clear();
        return b;
    }

    public static NodeAttribute ReadAttribute(AttributeType type, BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var attr = new NodeAttribute(type);

        switch (type)
        {
            case AttributeType.None:
                break;
            case AttributeType.Byte:
                attr.Value = reader.ReadByte();
                break;
            case AttributeType.Short:
                attr.Value = reader.ReadInt16();
                break;
            case AttributeType.UShort:
                attr.Value = reader.ReadUInt16();
                break;
            case AttributeType.Int:
                attr.Value = reader.ReadInt32();
                break;
            case AttributeType.UInt:
                attr.Value = reader.ReadUInt32();
                break;
            case AttributeType.Float:
                attr.Value = reader.ReadSingle();
                break;
            case AttributeType.Double:
                attr.Value = reader.ReadDouble();
                break;
            case AttributeType.IVec2:
            case AttributeType.IVec3:
            case AttributeType.IVec4:
                {
                    int columns = attr.Type.GetColumns();
                    var vec = new int[columns];
                    for (int i = 0; i < columns; i++)
                        vec[i] = reader.ReadInt32();
                    attr.Value = vec;
                    break;
                }
            case AttributeType.Vec2:
            case AttributeType.Vec3:
            case AttributeType.Vec4:
                {
                    int columns = attr.Type.GetColumns();
                    var vec = new float[columns];
                    for (int i = 0; i < columns; i++)
                        vec[i] = reader.ReadSingle();
                    attr.Value = vec;
                    break;
                }
            case AttributeType.Mat2:
            case AttributeType.Mat3:
            case AttributeType.Mat3x4:
            case AttributeType.Mat4x3:
            case AttributeType.Mat4:
                {
                    int columns = attr.Type.GetColumns();
                    int rows = attr.Type.GetRows();
                    var mat = new Matrix(rows, columns);
                    attr.Value = mat;
                    for (int col = 0; col < columns; col++)
                    {
                        for (int row = 0; row < rows; row++)
                        {
                            mat[row, col] = reader.ReadSingle();
                        }
                    }
                    break;
                }
            case AttributeType.Bool:
                attr.Value = reader.ReadByte() != 0;
                break;
            case AttributeType.ULongLong:
                attr.Value = reader.ReadUInt64();
                break;
            case AttributeType.Long:
            case AttributeType.Int64:
                attr.Value = reader.ReadInt64();
                break;
            case AttributeType.Int8:
                attr.Value = reader.ReadSByte();
                break;
            case AttributeType.UUID:
                byte[] bytes = reader.ReadBytes(16);
                if (bytes.Length < 16) throw new EndOfStreamException();
                attr.Value = new Guid(bytes);
                break;
            default:
                throw new InvalidFormatException($"ReadAttribute() not implemented for type {type}");
        }

        return attr;
    }

    public static void WriteAttribute(BinaryWriter writer, NodeAttribute attr)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(attr);

        object value = attr.Value ?? throw new InvalidDataException($"Attribute value cannot be null for type {attr.Type}.");

        switch (attr.Type)
        {
            case AttributeType.None:
                break;
            case AttributeType.Byte:
                writer.Write((byte)value);
                break;
            case AttributeType.Short:
                writer.Write((short)value);
                break;
            case AttributeType.UShort:
                writer.Write((ushort)value);
                break;
            case AttributeType.Int:
                writer.Write((int)value);
                break;
            case AttributeType.UInt:
                writer.Write((uint)value);
                break;
            case AttributeType.Float:
                writer.Write((float)value);
                break;
            case AttributeType.Double:
                writer.Write((double)value);
                break;
            case AttributeType.IVec2:
            case AttributeType.IVec3:
            case AttributeType.IVec4:
                foreach (int item in (int[])value)
                    writer.Write(item);
                break;
            case AttributeType.Vec2:
            case AttributeType.Vec3:
            case AttributeType.Vec4:
                foreach (float item in (float[])value)
                    writer.Write(item);
                break;
            case AttributeType.Mat2:
            case AttributeType.Mat3:
            case AttributeType.Mat3x4:
            case AttributeType.Mat4x3:
            case AttributeType.Mat4:
                {
                    var mat = (Matrix)value;
                    for (int col = 0; col < mat.Cols; col++)
                    {
                        for (int row = 0; row < mat.Rows; row++)
                        {
                            writer.Write(mat[row, col]);
                        }
                    }
                    break;
                }
            case AttributeType.Bool:
                writer.Write((byte)((bool)value ? 1 : 0));
                break;
            case AttributeType.ULongLong:
                writer.Write((ulong)value);
                break;
            case AttributeType.Long:
            case AttributeType.Int64:
                writer.Write((long)value);
                break;
            case AttributeType.Int8:
                writer.Write((sbyte)value);
                break;
            case AttributeType.UUID:
                writer.Write(((Guid)value).ToByteArray());
                break;
            default:
                throw new InvalidFormatException($"WriteAttribute() not implemented for type {attr.Type}");
        }
    }
}
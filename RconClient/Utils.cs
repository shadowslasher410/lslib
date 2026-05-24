using System.Buffers.Binary;

namespace LSLib.Rcon;

public sealed class BinaryWriterBE(Stream output) : BinaryWriter(output)
{
    public void WriteBE(ushort value)
    {
        ushort bigEndian = BinaryPrimitives.ReverseEndianness(value);
        Write(bigEndian);
    }

    public void WriteBE(uint value)
    {
        uint bigEndian = BinaryPrimitives.ReverseEndianness(value);
        Write(bigEndian);
    }
}

public sealed class BinaryReaderBE(Stream input) : BinaryReader(input)
{
    public ushort ReadUInt16BE()
    {
        ushort bigEndian = ReadUInt16();
        return BinaryPrimitives.ReverseEndianness(bigEndian);
    }

    public uint ReadUInt32BE()
    {
        uint bigEndian = ReadUInt32();
        return BinaryPrimitives.ReverseEndianness(bigEndian);
    }

    public void ReadBytes(Span<byte> buffer)
    {
        BaseStream.ReadExactly(buffer);
    }

    public new byte[] ReadBytes(int count)
    {
        byte[] buffer = new byte[count];
        BaseStream.ReadExactly(buffer);
        return buffer;
    }
}
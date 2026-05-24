using System.Runtime.InteropServices;

namespace LSLib.Rcon;

public enum PacketId : byte
{
    ConnectedPing = 0x00,
    UnconnectedPing = 0x01,
    ConnectedPong = 0x03,
    OpenConnectionRequest1 = 0x05,
    OpenConnectionResponse1 = 0x06,
    OpenConnectionRequest2 = 0x07,
    OpenConnectionResponse2 = 0x08,
    ConnectionRequest = 0x09,
    ConnectionRequestAccepted = 0x10,
    NewIncomingConnection = 0x13,
    DisconnectionNotification = 0x15,
    UnconnectedPong = 0x1C,
    EncapsulatedData = 0x84,
    ACK = 0xC0
};

public class RakNetConstants
{
    public const byte ProtocolVersion = 6;
    public static readonly byte[] Magic = [
        0x00, 0xff, 0xff, 0x00, 0xfe, 0xfe, 0xfe, 0xfe,
        0xfd, 0xfd, 0xfd, 0xfd, 0x12, 0x34, 0x56, 0x78
    ];
}

public interface IPacket
{
    void Read(BinaryReaderBE reader);
    void Write(BinaryWriterBE writer);
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SequenceNumber : IEquatable<SequenceNumber>, IComparable<SequenceNumber>
{
    public uint Number { get; set; }

    public void Read(BinaryReaderBE reader)
    {
        byte b1 = reader.ReadByte();
        byte b2 = reader.ReadByte();
        byte b3 = reader.ReadByte();

        Number = (uint)b1 | ((uint)b2 << 8) | ((uint)b3 << 16);
    }

    public readonly void Write(BinaryWriterBE writer)
    {
        writer.Write((byte)(Number & 0xff));
        writer.Write((byte)((Number >> 8) & 0xff));
        writer.Write((byte)((Number >> 16) & 0xff));
    }
    public readonly bool Equals(SequenceNumber other) => Number == other.Number;
    public override readonly bool Equals(object? obj) => obj is SequenceNumber other && Equals(other);
    public override readonly int GetHashCode() => Number.GetHashCode();
    public readonly int CompareTo(SequenceNumber other) => Number.CompareTo(other.Number);

    public static implicit operator uint(SequenceNumber seq) => seq.Number;
    public static implicit operator SequenceNumber(uint val) => new() { Number = val };
    public static bool operator ==(SequenceNumber left, SequenceNumber right) => left.Equals(right);
    public static bool operator !=(SequenceNumber left, SequenceNumber right) => !left.Equals(right);
}
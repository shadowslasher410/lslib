namespace LSLib.Rcon;

public enum EncapsulatedReliability : byte
{
    Unreliable = 0,
    UnreliableSequenced = 1,
    Reliable = 2,
    ReliableOrdered = 3,
    ReliableSequenced = 4,
    UnreliableAcked = 5,
    ReliableAcked = 6,
    ReliableOrderedAcked = 7
}

public struct EncapsulatedFlags
{
    public EncapsulatedReliability Reliability { get; set; }
    public bool Split { get; set; }

    public void Read(BinaryReaderBE reader)
    {
        byte flags = reader.ReadByte();
        Split = (flags & 0x10) == 0x10;
        Reliability = (EncapsulatedReliability)(flags >> 5);
    }

    public readonly void Write(BinaryWriterBE writer)
    {
        byte flags = (byte)(((byte)Reliability << 5) | (Split ? 0x10 : 0x00));
        writer.Write(flags);
    }

    public readonly bool IsReliable() => Reliability is
        EncapsulatedReliability.Reliable or
        EncapsulatedReliability.ReliableOrdered or
        EncapsulatedReliability.ReliableSequenced or
        EncapsulatedReliability.ReliableAcked or
        EncapsulatedReliability.ReliableOrderedAcked;

    public readonly bool IsOrdered() => Reliability is
         EncapsulatedReliability.ReliableOrdered or
         EncapsulatedReliability.ReliableOrderedAcked;

    public readonly bool IsSequenced() => Reliability is
        EncapsulatedReliability.UnreliableSequenced or
        EncapsulatedReliability.ReliableSequenced;
}

public class EncapsulatedPacket : IPacket
{
    public EncapsulatedFlags Flags { get; set; }
    public ushort Length { get; set; }
    public SequenceNumber MessageIndex { get; set; }
    public SequenceNumber SequenceIndex { get; set; }
    public SequenceNumber OrderIndex { get; set; }
    public byte OrderChannel { get; set; }
    public uint SplitCount { get; set; }
    public ushort SplitId { get; set; }
    public uint SplitIndex { get; set; }
    public byte[] Payload { get; set; } = [];

    public void Read(BinaryReaderBE reader)
    {
        var tempFlags = Flags;
        tempFlags.Read(reader);
        Flags = tempFlags;

        Length = reader.ReadUInt16BE();

        if (Flags.IsReliable())
        {
            MessageIndex.Read(reader);
        }

        if (Flags.IsSequenced())
        {
            SequenceIndex.Read(reader);
        }

        if (Flags.IsSequenced() || Flags.IsOrdered())
        {
            OrderIndex.Read(reader);
            OrderChannel = reader.ReadByte();
        }

        if (Flags.Split)
        {
            SplitCount = reader.ReadUInt32BE();
            SplitId = reader.ReadUInt16BE();
            SplitIndex = reader.ReadUInt32BE();
        }

        Payload = reader.ReadBytes(Length);
    }

    public void Write(BinaryWriterBE writer)
    {
        Flags.Write(writer);
        writer.WriteBE(Length);

        if (Flags.IsReliable())
        {
            MessageIndex.Write(writer);
        }

        if (Flags.IsSequenced())
        {
            SequenceIndex.Write(writer);
        }

        if (Flags.IsSequenced() || Flags.IsOrdered())
        {
            OrderIndex.Write(writer);
            writer.Write(OrderChannel);
        }

        if (Flags.Split)
        {
            writer.WriteBE(SplitCount);
            writer.WriteBE(SplitId);
            writer.WriteBE(SplitIndex);
        }

        writer.Write(Payload.AsSpan());
    }
}

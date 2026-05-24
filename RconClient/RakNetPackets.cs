namespace LSLib.Rcon;

public struct RakAddress
{
    public uint Address { get; set; }
    public ushort Port { get; set; }

    public void Read(BinaryReaderBE reader)
    {
        byte type = reader.ReadByte();
        if (type != 4) throw new InvalidDataException("Only IPv4 addresses are natively supported.");
        Address = ~reader.ReadUInt32();
        Port = reader.ReadUInt16BE();
    }

    public readonly void Write(BinaryWriterBE writer)
    {
        writer.Write((byte)4);
        writer.Write(~Address);
        writer.WriteBE(Port);
    }
}

public class OpenConnectionRequest1 : IPacket
{
    public byte[] Magic { get; set; } = [];
    public byte Protocol { get; set; }

    public void Read(BinaryReaderBE reader)
    {
        Magic = reader.ReadBytes(16);
        Protocol = reader.ReadByte();
    }

    public void Write(BinaryWriterBE writer)
    {
        writer.Write((byte)PacketId.OpenConnectionRequest1);
        writer.Write(Magic);
        writer.Write(Protocol);

        Span<byte> pad = stackalloc byte[0x482];
        writer.Write(pad);
    }
}

public class OpenConnectionResponse1 : IPacket
{
    public byte[] Magic { get; set; } = [];
    public byte[] ServerId { get; set; } = [];
    public byte Security { get; set; }
    public ushort MTU { get; set; }

    public void Read(BinaryReaderBE reader)
    {
        Magic = reader.ReadBytes(16);
        ServerId = reader.ReadBytes(8);
        Security = reader.ReadByte();
        MTU = reader.ReadUInt16BE();
    }

    public void Write(BinaryWriterBE writer)
    {
        writer.Write((byte)PacketId.OpenConnectionResponse1);
        writer.Write(Magic);
        writer.Write(ServerId);
        writer.Write(Security);
        writer.WriteBE(MTU);

        Span<byte> pad = stackalloc byte[0x480];
        writer.Write(pad);
    }
}

public class OpenConnectionRequest2 : IPacket
{
    public byte[] Magic { get; set; } = [];
    public RakAddress Address { get; set; }
    public ushort MTU { get; set; }
    public byte[] ClientId { get; set; } = [];

    public void Read(BinaryReaderBE reader)
    {
        Magic = reader.ReadBytes(16);
        Address.Read(reader);
        MTU = reader.ReadUInt16BE();
        ClientId = reader.ReadBytes(8);
    }

    public void Write(BinaryWriterBE writer)
    {
        writer.Write((byte)PacketId.OpenConnectionRequest2);
        writer.Write(Magic);
        Address.Write(writer);
        writer.WriteBE(MTU);
        writer.Write(ClientId);
    }
}

public class OpenConnectionResponse2 : IPacket
{
    public byte[] Magic { get; set; } = [];
    public byte[] ServerId { get; set; } = [];
    public RakAddress Address { get; set; }
    public ushort MTU { get; set; }
    public byte Security { get; set; }

    public void Read(BinaryReaderBE reader)
    {
        Magic = reader.ReadBytes(16);
        ServerId = reader.ReadBytes(8);
        Address.Read(reader);
        MTU = reader.ReadUInt16BE();
        Security = reader.ReadByte();
    }

    public void Write(BinaryWriterBE writer)
    {
        writer.Write((byte)PacketId.OpenConnectionResponse2);
        writer.Write(Magic);
        writer.Write(ServerId);
        Address.Write(writer);
        writer.WriteBE(MTU);
        writer.Write(Security);
    }
}

public class ConnectionRequest : IPacket
{
    public byte[] ClientId { get; set; } = [];
    public uint Time { get; set; }
    public byte Security { get; set; }

    public void Read(BinaryReaderBE reader)
    {
        ClientId = reader.ReadBytes(8);
        _ = reader.ReadUInt32();
        Time = reader.ReadUInt32BE();
        Security = reader.ReadByte();
    }

    public void Write(BinaryWriterBE writer)
    {
        writer.Write((byte)PacketId.ConnectionRequest);
        writer.Write(ClientId);
        writer.Write((uint)0);
        writer.Write(Time);
        writer.Write(Security);
    }
}

public class ConnectionRequestAccepted : IPacket
{
    public byte[] Payload { get; set; } = [];

    public void Read(BinaryReaderBE reader)
    {
        Payload = reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position));
    }

    public void Write(BinaryWriterBE writer)
    {
        writer.Write((byte)PacketId.ConnectionRequestAccepted);
        if (Payload.Length > 0)
        {
            writer.Write(Payload);
        }
    }
}

public class NewIncomingConnection : IPacket
{
    public void Read(BinaryReaderBE reader) { }

    public void Write(BinaryWriterBE writer)
    {
        byte[] pkt = [
            0x13,
            0x04, 0x80, 0xff, 0xff, 0xfe, 0x15, 0x0c,
            0x04, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00,
            0x04, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00,
            0x04, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00,
            0x04, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00,
            0x04, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00,
            0x04, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00,
            0x04, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00,
            0x04, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00,
            0x04, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00,
            0x04, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x18, 0x8e, 0x2f, 0x3f,
            0x00, 0x00, 0x00, 0x00,
            0x18, 0x8e, 0x2f, 0x3f
        ];
        writer.Write(pkt);
    }
}

public class DisconnectionNotification : IPacket
{
    public void Read(BinaryReaderBE reader) { }

    public void Write(BinaryWriterBE writer)
    {
        writer.Write((byte)PacketId.DisconnectionNotification);
    }
}

public class ConnectedPing : IPacket
{
    public uint SendTime { get; set; }

    public void Read(BinaryReaderBE reader)
    {
        SendTime = reader.ReadUInt32BE();
    }

    public void Write(BinaryWriterBE writer)
    {
        writer.Write((byte)PacketId.ConnectedPing);
        writer.WriteBE(SendTime);
    }
}

public class ConnectedPong : IPacket
{
    public uint ReceiveTime { get; set; }
    public uint SendTime { get; set; }

    public void Read(BinaryReaderBE reader)
    {
        ReceiveTime = reader.ReadUInt32BE();
        SendTime = reader.ReadUInt32BE();
    }

    public void Write(BinaryWriterBE writer)
    {
        writer.Write((byte)PacketId.ConnectedPong);
        writer.WriteBE(ReceiveTime);
        writer.WriteBE(SendTime);
    }
}

public class DataPacket : IPacket
{
    public byte Id { get; set; }
    public SequenceNumber Sequence { get; set; }
    public IPacket WrappedPacket { get; set; } = null!;

    public void Read(BinaryReaderBE reader)
    {
        var tempSeq = Sequence;
        tempSeq.Read(reader);
        Sequence = tempSeq;

        WrappedPacket.Read(reader);
    }

    public void Write(BinaryWriterBE writer)
    {
        writer.Write(Id);
        Sequence.Write(writer);
        WrappedPacket.Write(writer);
    }
}

public class Acknowledgement : IPacket
{
    public List<SequenceNumber> SequenceNumbers { get; set; } = [];

    public void Read(BinaryReaderBE reader)
    {
        ushort numAcks = reader.ReadUInt16BE();
        SequenceNumbers = new List<SequenceNumber>(numAcks);

        for (int i = 0; i < numAcks; i++)
        {
            byte type = reader.ReadByte();
            if (type == 0)
            {
                SequenceNumber first = new();
                SequenceNumber last = new();
                first.Read(reader);
                last.Read(reader);

                for (uint seq = first.Number; seq <= last.Number; seq++)
                {
                    SequenceNumbers.Add(new SequenceNumber { Number = seq });
                }
            }
            else
            {
                SequenceNumber num = new();
                num.Read(reader);
                SequenceNumbers.Add(num);
            }
        }
    }

    public void Write(BinaryWriterBE writer)
    {
        writer.Write((byte)PacketId.ACK);
        writer.WriteBE((ushort)SequenceNumbers.Count);
        foreach (var seq in SequenceNumbers)
        {
            writer.Write((byte)1);
            seq.Write(writer);
        }
    }
}

using System.Buffers;
using System.Collections.Concurrent;
using System.Net;

namespace LSLib.Rcon;

public sealed class SplitPacket
{
    public ushort Index { get; set; }
    public uint Available { get; set; }
    public byte[][] Buffers { get; set; } = [];
}

public sealed partial class RakNetSession(RakNetSocket socket, IPEndPoint address, byte[] clientId)
{
    private readonly RakNetSocket _socket = socket ?? throw new ArgumentNullException(nameof(socket));
    private readonly IPEndPoint _address = address ?? throw new ArgumentNullException(nameof(address));
    private readonly byte[] _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));

    private uint _nextPacketId;
    private uint _nextReliableId;
    private uint _nextSequenceId;
    private uint _nextOrderId;

    private readonly ConcurrentDictionary<ushort, SplitPacket> _splits = new();
    private readonly ConcurrentDictionary<uint, DataPacket> _pendingAcknowledgements = new();

    public Func<byte, IPacket?> PacketConstructor { get; set; } = _ => null;
    public event Action<RakNetSession, IPacket> PacketReceived = delegate { };
    public event Action<RakNetSession> SessionDisconnected = delegate { };

    private void HandleConnectedPing(ConnectedPing packet)
    {
        var pong = new ConnectedPong
        {
            ReceiveTime = packet.SendTime,
            SendTime = packet.SendTime
        };
        SendEncapsulated(pong, EncapsulatedReliability.Unreliable);
    }

    private void HandleConnectionRequestAccepted(ConnectionRequestAccepted packet)
    {
#if DEBUG
        Console.WriteLine($"Connection handshake accepted by remote host. Payload length verified: {packet.Payload.Length} bytes.");
#endif

        var ackReq = new NewIncomingConnection();
        SendEncapsulated(ackReq, EncapsulatedReliability.ReliableOrdered);
    }

    private void HandleDisconnectionNotification(IPacket? packet)
    {
        if (packet is null) return;

#if DEBUG
        Console.WriteLine("Server gracefully requested termination. Dispatching localized session teardown events.");
#endif

        SessionDisconnected?.Invoke(this);
    }

    private void HandleEncapsulatedPayload(ReadOnlySpan<byte> payload)
    {
        using var encapMemory = new MemoryStream(payload.ToArray());
        using var encapStream = new BinaryReaderBE(encapMemory);
        byte encapId = encapStream.ReadByte();
        HandlePacketDecapsulated(encapId, encapStream);
    }

    private void HandleSplitPacket(EncapsulatedPacket packet)
    {
        var split = _splits.GetOrAdd(packet.SplitId, id => new SplitPacket
        {
            Index = id,
            Available = 0,
            Buffers = new byte[packet.SplitCount][]
        });

        if (split.Buffers.Length != packet.SplitCount)
        {
            throw new InvalidDataException("Packet transmission validation failure: Split chunk count mismatch.");
        }

        if (split.Buffers[packet.SplitIndex] is not null) return;

        split.Buffers[packet.SplitIndex] = packet.Payload;
        split.Available++;

        if (split.Available == (uint)split.Buffers.Length)
        {
            _splits.TryRemove(split.Index, out _);

            int totalRequiredPayloadSize = 0;
            foreach (var buffer in split.Buffers)
            {
                totalRequiredPayloadSize += buffer.Length;
            }

            byte[] serializationBuffer = ArrayPool<byte>.Shared.Rent(totalRequiredPayloadSize);
            try
            {
                var destinationSpan = serializationBuffer.AsSpan(0, totalRequiredPayloadSize);
                int structuralOffsetTracker = 0;

                foreach (byte[] chunk in split.Buffers)
                {
                    chunk.AsSpan().CopyTo(destinationSpan[structuralOffsetTracker..]);
                    structuralOffsetTracker += chunk.Length;
                }

                HandleEncapsulatedPayload(destinationSpan);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(serializationBuffer);
            }
        }
    }

    private void SendAcknowledgement(SequenceNumber sequence)
    {
        var ack = new Acknowledgement
        {
            SequenceNumbers = [sequence]
        };
        _socket.Send(_address, ack);
    }

    private void HandleEncapsulatedPacket(DataPacket data, EncapsulatedPacket packet)
    {
        if (packet.Flags.Split)
        {
            HandleSplitPacket(packet);
        }
        else
        {
            HandleEncapsulatedPayload(packet.Payload.AsSpan());
        }

        if (packet.Flags.IsReliable())
        {
            SendAcknowledgement(data.Sequence);
        }
    }

    private void HandlePacketDecapsulated(byte id, BinaryReaderBE reader)
    {
        var packet = DecodePacketDecapsulated(id, reader);

        switch (packet)
        {
            case ConnectedPing ping:
                HandleConnectedPing(ping);
                break;
            case ConnectionRequestAccepted accepted:
                HandleConnectionRequestAccepted(accepted);
                break;
            case DisconnectionNotification:
                HandleDisconnectionNotification(packet);
                break;
            default:
                if (id >= 0x80 && packet is not null)
                {
                    PacketReceived?.Invoke(this, packet);
                }
                else if (packet is null)
                {
                    throw new InvalidDataException($"Handshake verification failed: Decoded packet payload at ID 0x{id:X2} resolved to null.");
                }
                else
                {
                    throw new InvalidDataException($"Unhandled internal encapsulated packet tracking registration ID: 0x{id:X2}");
                }
                break;
        }
    }

    public void HandlePacket(byte id, BinaryReaderBE reader)
    {
        var packet = DecodePacket(id, reader);

        switch (packet)
        {
            case Acknowledgement ack:
                foreach (var sequence in ack.SequenceNumbers)
                {
                    _pendingAcknowledgements.TryRemove(sequence.Number, out _);
                }
                break;

            case DataPacket data when data.WrappedPacket is EncapsulatedPacket encap:
                HandleEncapsulatedPacket(data, encap);
                break;

            default:
                throw new InvalidDataException($"Unhandled raw session packet sequence registration target ID: 0x{id:X2}");
        }
    }

    private static IPacket DecodePacket(byte id, BinaryReaderBE reader)
    {
        IPacket packet = (id >= 0x80 && id < 0xA0)
            ? new DataPacket { WrappedPacket = new EncapsulatedPacket() }
            : (PacketId)id switch
            {
                PacketId.ACK => new Acknowledgement(),
                _ => throw new InvalidDataException($"Unrecognized root packet ID framework specification moniker: 0x{id:X2}")
            };

        packet.Read(reader);
        return packet;
    }

    private IPacket DecodePacketDecapsulated(byte id, BinaryReaderBE reader)
    {
        IPacket packet = (PacketId)id switch
        {
            PacketId.ConnectedPing => new ConnectedPing(),
            PacketId.ConnectionRequest => new ConnectionRequest(),
            PacketId.ConnectionRequestAccepted => new ConnectionRequestAccepted(),
            PacketId.DisconnectionNotification => new DisconnectionNotification(),
            _ => PacketConstructor(id) ?? throw new InvalidDataException($"Unrecognized nested unencapsulated packet schema mapping code: 0x{id:X2}")
        };

        packet.Read(reader);
        return packet;
    }

    public void SendEncapsulated(IPacket packet, EncapsulatedReliability reliability)
    {
        uint currentPacketSequenceId = _nextPacketId++;

        var dataPkt = new DataPacket
        {
            Id = (byte)PacketId.EncapsulatedData,
            Sequence = new SequenceNumber { Number = currentPacketSequenceId }
        };

        var encapPkt = new EncapsulatedPacket
        {
            Flags = new EncapsulatedFlags { Reliability = reliability }
        };

        if (encapPkt.Flags.IsReliable())
        {
            encapPkt.MessageIndex = new SequenceNumber { Number = _nextReliableId++ };
        }

        if (encapPkt.Flags.IsSequenced())
        {
            encapPkt.SequenceIndex = new SequenceNumber { Number = _nextSequenceId++ };
        }

        if (encapPkt.Flags.IsSequenced() || encapPkt.Flags.IsOrdered())
        {
            encapPkt.OrderChannel = 0;
            encapPkt.OrderIndex = new SequenceNumber { Number = _nextOrderId++ };
        }

        using (var memory = new MemoryStream())
        using (var stream = new BinaryWriterBE(memory))
        {
            packet.Write(stream);
            memory.SetLength(memory.Position);
            encapPkt.Payload = memory.ToArray();
            encapPkt.Length = (ushort)encapPkt.Payload.Length;
        }

        dataPkt.WrappedPacket = encapPkt;

        if (encapPkt.Flags.IsReliable())
        {
            _pendingAcknowledgements[currentPacketSequenceId] = dataPkt;
        }

        _socket.Send(_address, dataPkt);
    }

    public void OnConnected()
    {
        uint currentTimestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var connReq = new ConnectionRequest
        {
            ClientId = _clientId,
            Time = currentTimestamp,
            Security = 0
        };

        SendEncapsulated(connReq, EncapsulatedReliability.ReliableOrdered);
    }
}
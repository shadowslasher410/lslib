using System.Net;
using System.Net.Sockets;

namespace LSLib.Rcon;

public sealed class AsyncUdpClient : IDisposable
{
    private readonly UdpClient _socket;
    public ushort Port { get; }

    public event Action<IPEndPoint, byte[]> PacketReceived = delegate { };

    public AsyncUdpClient()
    {
        ushort assignedPort = (ushort)((Random.Shared.Next() % (65536 - 10000)) + 10000);
        Port = assignedPort;
        _socket = new UdpClient(Port);
    }

    public async Task RunLoopAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult result = await _socket.ReceiveAsync(cancellationToken);
                PacketReceived(result.RemoteEndPoint, result.Buffer);
            }
            catch (SocketException e)
            {
                if (e.ErrorCode == 10054)
                {
                    var originalColor = Console.ForegroundColor;
                    try
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Error.WriteLine("Received connection reset - Rcon server probably not running.");
                    }
                    finally
                    {
                        Console.ForegroundColor = originalColor;
                    }
                    break;
                }
                throw;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
    public void Send(IPEndPoint address, byte[] packet)
    {
        _socket.Send(packet, packet.Length, address);
    }

    public void Dispose()
    {
        _socket.Dispose();
    }
}

public sealed class RakNetSocket
{
    private readonly AsyncUdpClient _socket;
    private readonly byte[] _clientId = new byte[8];
    private RakNetSession? _session;

    public event Action<RakNetSession> SessionEstablished = delegate { };

    public RakNetSocket()
    {
        _socket = new AsyncUdpClient();
        _socket.PacketReceived += OnPacketReceived;
        Random.Shared.NextBytes(_clientId);
    }

    private static IPacket DecodePacket(byte id, BinaryReaderBE reader)
    {
        IPacket packet = (PacketId)id switch
        {
            PacketId.OpenConnectionRequest1 => new OpenConnectionRequest1(),
            PacketId.OpenConnectionResponse1 => new OpenConnectionResponse1(),
            PacketId.OpenConnectionRequest2 => new OpenConnectionRequest2(),
            PacketId.OpenConnectionResponse2 => new OpenConnectionResponse2(),
            _ => throw new InvalidDataException($"Unrecognized network packet type signature parameter: 0x{id:X2}")
        };
        packet.Read(reader);
        return packet;
    }

    private void HandleConnectionResponse1(IPEndPoint address, OpenConnectionResponse1 _)
    {
        uint ipUint = BitConverter.ToUInt32(IPAddress.Loopback.GetAddressBytes(), 0);
        if (BitConverter.IsLittleEndian)
        {
            ipUint = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(ipUint);
        }

        var connReq = new OpenConnectionRequest2
        {
            Magic = RakNetConstants.Magic,
            ClientId = _clientId,
            Address = new RakAddress
            {
                Address = ipUint,
                Port = (ushort)IPAddress.HostToNetworkOrder((short)_socket.Port)
            },
            MTU = 1200
        };
        Send(address, connReq);
    }

    private void HandleConnectionResponse2(IPEndPoint address, OpenConnectionResponse2 _)
    {
        _session = new RakNetSession(this, address, _clientId);
        SessionEstablished(_session);
        _session.OnConnected();
    }

    private void HandlePacket(IPEndPoint address, IPacket packet)
    {
        switch (packet)
        {
            case OpenConnectionResponse1 resp1:
                HandleConnectionResponse1(address, resp1);
                break;
            case OpenConnectionResponse2 resp2:
                HandleConnectionResponse2(address, resp2);
                break;
            default:
                throw new NotSupportedException($"Target network packet type assignment not handled: {packet.GetType().Name}");
        }
    }

    private void OnPacketReceived(IPEndPoint address, byte[] packet)
    {
        using var stream = new MemoryStream(packet);
        using var reader = new BinaryReaderBE(stream);
        byte id = reader.ReadByte();

        if (id < 0x80)
        {
            var decoded = DecodePacket(id, reader);
            HandlePacket(address, decoded);
        }
        else
        {
            if (_session is not null)
            {
                _session.HandlePacket(id, reader);
            }
            else
            {
                throw new InvalidOperationException("Incoming network traffic arrived before an active RakNet session established context initialized.");
            }
        }
    }

    public void Send(IPEndPoint address, IPacket packet)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriterBE(stream);
        packet.Write(writer);
        stream.SetLength(stream.Position);
        _socket.Send(address, stream.ToArray());
    }

    public async Task BeginConnectionAsync(IPEndPoint address, CancellationToken cancellationToken = default)
    {
        var connReq = new OpenConnectionRequest1
        {
            Magic = RakNetConstants.Magic,
            Protocol = RakNetConstants.ProtocolVersion
        };
        Send(address, connReq);
        await _socket.RunLoopAsync(cancellationToken);
    }
}

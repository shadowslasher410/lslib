using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace LSTools.DebuggerFrontend;

public class DAPStream
{
    private readonly Stream _input;
    private readonly StreamReader _inputReader;
    private Stream? _logStream;

    private int OutgoingSeq { get => field++; set; } = 1;
    private int IncomingSeq { get => field++; set; } = 1;

    public Action<DAPMessage> MessageReceived { get; set; } = delegate { };

    public DAPStream()
    {
        _input = Console.OpenStandardInput();
        _inputReader = new StreamReader(_input, Encoding.UTF8);
    }

    public void EnableLogging(Stream logStream) => _logStream = logStream;

    private void ProcessPayload(ReadOnlySpan<char> payload)
    {
        if (_logStream is not null)
        {
            using var writer = new StreamWriter(_logStream, Encoding.UTF8, 0x1000, leaveOpen: true);
            writer.Write(" DAP >>> ");
            writer.Write(payload);
            writer.Write("\r\n");
        }

        DAPMessage? message;
        try
        {
            message = JsonSerializer.Deserialize(payload, DAPJsonContext.Default.DAPMessage)
                      ?? throw new InvalidDataException("Decoded DAP payload evaluated to an invalid empty object state.");
        }
        catch (JsonException ex)
        {
            if (_logStream is not null)
            {
                using var writer = new StreamWriter(_logStream, Encoding.UTF8, 0x1000, leaveOpen: true);
                writer.WriteLine($" DAP !!! Could not decode DAP message: {ex.Message}");
            }

            SendErrorReply(IncomingSeq, "unknown", ex.Message);
            _ = IncomingSeq;
            return;
        }

        if (message.Seq != IncomingSeq)
        {
            throw new InvalidDataException($"DAP sequence number mismatch; got {message.Seq} expected {IncomingSeq}");
        }

        _ = IncomingSeq;
        MessageReceived(message);
    }

    public void RunLoop()
    {
        while (true)
        {
            Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                var line = _inputReader.ReadLine();
                if (line is null && _inputReader.EndOfStream)
                {
                    return;
                }

                if (line is null or { Length: 0 })
                {
                    break;
                }

                ReadOnlySpan<char> lineSpan = line.AsSpan();
                int colonIndex = lineSpan.IndexOf(':');
                if (colonIndex == -1)
                {
                    throw new InvalidDataException($"Malformed header line: {line}");
                }

                string key = lineSpan[..colonIndex].Trim().ToString();
                string value = lineSpan[(colonIndex + 1)..].Trim().ToString();

                headers.Add(key, value);
                if (!string.Equals(key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"{key}={value}");
                }
            }

            if (headers.Count == 0) throw new InvalidDataException("Empty headers.");

            var length = int.Parse(headers["Content-Length"]);
            var payload = new char[length];
            var read = _inputReader.Read(payload, 0, length);
            if (read != length)
            {
                throw new InvalidDataException($"Could not read {length} bytes of payload (got {read})");
            }

            ProcessPayload(payload);
        }
    }

    public void Send(DAPMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var synchronizedMessage = message with { Seq = OutgoingSeq };
        JsonTypeInfo? typeInfo = DAPJsonContext.Default.GetTypeInfo(synchronizedMessage.GetType())
            ?? throw new InvalidOperationException($"The concrete payload type '{synchronizedMessage.GetType().Name}' is missing from the AOT compile metadata register.");

        string encoded = JsonSerializer.Serialize(synchronizedMessage, typeInfo);

        if (_logStream is not null)
        {
            using var writer = new StreamWriter(_logStream, Encoding.UTF8, 0x1000, leaveOpen: true);
            writer.Write(" DAP <<< ");
            writer.Write(encoded);
            writer.Write("\r\n");
        }

        Console.Write($"Content-Length: {Encoding.UTF8.GetByteCount(encoded)}\r\n\r\n");
        Console.Write(encoded);
    }

    private void SendErrorReply(int requestSeq, string command, string errorText)
    {
        Send(new DAPResponse
        {
            Type = "response",
            Seq = 0,
            RequestSeq = requestSeq,
            Success = false,
            Command = command,
            Message = errorText
        });
    }

    public void SendEvent(string command, IDAPMessagePayload body)
    {
        Send(new DAPEvent
        {
            Type = "event",
            Seq = 0,
            EventName = command,
            Body = body
        });
    }

    public void SendReply(DAPRequest request, IDAPMessagePayload response)
    {
        ArgumentNullException.ThrowIfNull(request);

        Send(new DAPResponse
        {
            Type = "response",
            Seq = 0,
            RequestSeq = request.Seq,
            Success = true,
            Command = request.Command,
            Body = response
        });
    }

    public void SendReply(DAPRequest request, string errorText)
    {
        ArgumentNullException.ThrowIfNull(request);

        Send(new DAPResponse
        {
            Type = "response",
            Seq = 0,
            RequestSeq = request.Seq,
            Success = false,
            Command = request.Command,
            Message = errorText
        });
    }
}
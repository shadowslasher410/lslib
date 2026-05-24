using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace LSTools.DebuggerFrontend;

public class DAPUnknownMessageException(string type, string messageType, int seq)
    : Exception($"Unknown message type: {type}, {messageType}")
{
    public string Type { get; } = type ?? "unknown";
    public string MessageType { get; } = messageType ?? "unknown";
    public int Seq { get; } = seq;
}

public static class DAPMessageSerializer
{
    public static DAPMessage Unserialize(ReadOnlySpan<char> payload)
    {
        if (payload.IsEmpty)
        {
            throw new ArgumentException("Deserialization buffer payload cannot be empty.", nameof(payload));
        }

        try
        {
            DAPMessage? message = JsonSerializer.Deserialize(payload, DAPJsonContext.Default.DAPMessage)
                ?? throw new InvalidDataException("Decoded DAP payload evaluated to an invalid null state.");

            if (message is DAPRequest { Arguments: null } req)
            {
                throw new DAPUnknownMessageException(message.Type, req.Command, message.Seq);
            }
            if (message is DAPEvent { Body: null } ev)
            {
                throw new DAPUnknownMessageException(message.Type, ev.EventName, message.Seq);
            }

            return message;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Malformed JSON payload tracked inside translation buffer: {ex.Message}", ex);
        }
    }

    public static string Serialize(DAPMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        JsonTypeInfo? typeInfo = DAPJsonContext.Default.GetTypeInfo(message.GetType())
            ?? throw new InvalidOperationException($"The concrete payload type '{message.GetType().Name}' is missing from the AOT compiler serialization profile metadata registry.");

        return JsonSerializer.Serialize(message, typeInfo);
    }
}
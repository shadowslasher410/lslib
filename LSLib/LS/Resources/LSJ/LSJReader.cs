using System.Text.Json;
using System.Text.Json.Serialization;

namespace LSLib.LS;

[JsonSerializable(typeof(Resource))]
internal partial class LSJReaderJsonContext : JsonSerializerContext
{
}

public sealed class LSJReader(Stream stream) : IDisposable
{
    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));

    public NodeSerializationSettings SerializationSettings { get; set; } = new();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stream.Dispose();
        }
    }

    public Resource Read()
    {
        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNameCaseInsensitive = true
        };

        options.Converters.Add(new LSJResourceConverter(SerializationSettings));

        var context = new LSJReaderJsonContext(options);

        return JsonSerializer.Deserialize(_stream, context.Resource)
            ?? throw new InvalidDataException("LSJ deserializer conversion failure: The evaluated JSON data document root returned null.");
    }
}

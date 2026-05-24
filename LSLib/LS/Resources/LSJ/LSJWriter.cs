using System.Text.Json;
using System.Text.Json.Serialization;

namespace LSLib.LS;

[JsonSerializable(typeof(Resource))]
internal partial class LSJWriterJsonContext : JsonSerializerContext
{
}

public sealed class LSJWriter(Stream stream)
{
    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));

    public bool PrettyPrint { get; set; } = false;
    public NodeSerializationSettings SerializationSettings { get; set; } = new();

    public void Write(Resource rsrc)
    {
        ArgumentNullException.ThrowIfNull(rsrc);

        var options = new JsonSerializerOptions
        {
            WriteIndented = PrettyPrint,
            IndentCharacter = '\t',
            IndentSize = 1
        };

        options.Converters.Add(new LSJResourceConverter(SerializationSettings));

        var context = new LSJWriterJsonContext(options);

        JsonSerializer.Serialize(_stream, rsrc, context.Resource);
    }
}
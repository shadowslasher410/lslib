using LSLib.LS.Story.Compiler;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LSTools.StoryCompiler;

public interface ILogger
{
    void CompilationStarted();
    void CompilationFinished(bool succeeded);
    void TaskStarted(string name);
    void TaskFinished();
    void CompilationDiagnostic(Diagnostic message);
}

public class ConsoleLogger : ILogger
{
    private readonly Stopwatch _compilationTimer = new();
    private readonly Stopwatch _taskTimer = new();

    public void CompilationStarted() => _compilationTimer.Restart();

    public void CompilationFinished(bool succeeded)
    {
        _compilationTimer.Stop();
        Console.WriteLine("Compilation took: {0} ms", _compilationTimer.ElapsedMilliseconds);
    }

    public void TaskStarted(string name)
    {
        Console.Write($"{name} ... ");
        _taskTimer.Restart();
    }

    public void TaskFinished()
    {
        _taskTimer.Stop();
        Console.WriteLine("{0} ms", _taskTimer.ElapsedMilliseconds);
    }

    public void CompilationDiagnostic(Diagnostic message)
    {
        var originalColor = Console.ForegroundColor;

        Console.ForegroundColor = message.Level switch
        {
            MessageLevel.Error => ConsoleColor.Red,
            MessageLevel.Warning => ConsoleColor.DarkYellow,
            _ => originalColor
        };

        Console.Write(message.Level switch
        {
            MessageLevel.Error => "ERR! ",
            MessageLevel.Warning => "WARN ",
            _ => ""
        });

        if (message.Location is not null)
        {
            Console.Write($"{message.Location.FileName}:{message.Location.StartLine}:{message.Location.StartColumn}: ");
        }

        Console.WriteLine("[{0}] {1}", message.Code, message.Message);
        Console.ForegroundColor = originalColor;
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = false,
    Converters = [typeof(JsonLoggerOutputConverter), typeof(DiagnosticConverter)]
)]
[JsonSerializable(typeof(JsonLoggerOutput))]
[JsonSerializable(typeof(Diagnostic))]
internal partial class StoryCompilerJsonContext : JsonSerializerContext { }

public class DiagnosticConverter : JsonConverter<Diagnostic>
{
    public override Diagnostic Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotImplementedException();

    public override void Write(Utf8JsonWriter writer, Diagnostic value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        if (value.Location is not null)
        {
            writer.WritePropertyName("location");
            writer.WriteStartObject();
            writer.WriteString("file", value.Location.FileName);
            writer.WriteNumber("StartLine", value.Location.StartLine);
            writer.WriteNumber("StartColumn", value.Location.StartColumn);
            writer.WriteNumber("EndLine", value.Location.EndLine);
            writer.WriteNumber("EndColumn", value.Location.EndColumn);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("location");
        }

        writer.WriteString("code", value.Code);
        writer.WriteString("level", value.Level.ToString());
        writer.WriteString("message", value.Message);

        writer.WriteEndObject();
    }
}

public class JsonLoggerOutputConverter : JsonConverter<JsonLoggerOutput>
{
    public override JsonLoggerOutput Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotImplementedException();

    public override void Write(Utf8JsonWriter writer, JsonLoggerOutput value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("successful", value.Succeeded);

        writer.WritePropertyName("stats");
        writer.WriteStartObject();
        foreach (var (step, time) in value.StepTimes)
        {
            writer.WriteNumber(step, time);
        }
        writer.WriteEndObject();

        writer.WritePropertyName("messages");
        writer.WriteStartArray();
        foreach (var diagnostic in value.Diagnostics)
        {
            var diagnosticTypeInfo = StoryCompilerJsonContext.Default.Diagnostic;
            JsonSerializer.Serialize(writer, diagnostic, diagnosticTypeInfo);
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
    }
}

public class JsonLoggerOutput
{
    public Dictionary<string, int> StepTimes { get; init; } = [];
    public List<Diagnostic> Diagnostics { get; init; } = [];
    public bool Succeeded { get; set; }
}

public class JsonLogger : ILogger
{
    private readonly Stopwatch _taskTimer = new();
    private readonly JsonLoggerOutput _output = new();
    private string CurrentStep { get; set; } = string.Empty;

    public void CompilationStarted() { }

    public void CompilationFinished(bool succeeded)
    {
        _output.Succeeded = succeeded;

        using var memoryStream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memoryStream))
        {
            var outputTypeInfo = StoryCompilerJsonContext.Default.JsonLoggerOutput;
            JsonSerializer.Serialize(writer, _output, outputTypeInfo);
        }

        Console.Write(Encoding.UTF8.GetString(memoryStream.ToArray()));
    }

    public void TaskStarted(string name)
    {
        CurrentStep = name;
        _taskTimer.Restart();
    }

    public void TaskFinished()
    {
        _taskTimer.Stop();
        _output.StepTimes.Add(CurrentStep, (int)_taskTimer.ElapsedMilliseconds);
    }

    public void CompilationDiagnostic(Diagnostic message) => _output.Diagnostics.Add(message);
}
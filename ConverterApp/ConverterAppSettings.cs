using LSLib.LS;
using LSLib.LS.Enums;
using ReactiveUI;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LSTools.DivineGUI;

public interface ISettingsDataSource
{
    ConverterAppSettings Settings { get; set; }
}

public interface IGameSettingsTarget
{
    void SetGame(Game game);
}

public class ConverterAppSettings : ReactiveObject
{
    public GR2PaneSettings GR2 { get; init; } = new();
    public PackagePaneSettings PAK { get; init; } = new();
    public ResourcePaneSettings Resources { get; init; } = new();
    public VirtualTexturesPaneSettings VirtualTextures { get; init; } = new();
    public OsirisPaneSettings Story { get; init; } = new();
    public DebugPaneSettings Debugging { get; init; } = new();

    public int SelectedGame
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 4; // Defaults to index 4 (Baldur's Gate 3)

    public string Version { get; set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;

    public void SetPropertyChangedEvent(PropertyChangedEventHandler eventHandler)
    {
        PropertyChanged += eventHandler;
        GR2.PropertyChanged += eventHandler;
        PAK.PropertyChanged += eventHandler;
        Resources.PropertyChanged += eventHandler;
        VirtualTextures.PropertyChanged += eventHandler;
        Story.PropertyChanged += eventHandler;
        Debugging.PropertyChanged += eventHandler;
    }
}

public class GR2PaneSettings : ReactiveObject
{
    public string InputPath { get; set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;
    public string OutputPath { get; set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;
    public string BatchInputPath { get; set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;
    public string BatchOutputPath { get; set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;

    public LSLib.Granny.ExportFormat BatchInputFormat
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = LSLib.Granny.ExportFormat.GR2;

    public LSLib.Granny.ExportFormat BatchOutputFormat
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = LSLib.Granny.ExportFormat.DAE;

    public string ConformPath { get; set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;
}

public class PackagePaneSettings : ReactiveObject
{
    public string ExtractInputPath { get; set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;
    public string ExtractOutputPath { get; set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;
    public string CreateInputPath { get; set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;
    public string CreateOutputPath { get; set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;

    [JsonConverter(typeof(PackageVersionJsonConverter))]
    public LSLib.LS.PackageVersion CreatePackageVersion { get; set => this.RaiseAndSetIfChanged(ref field, value); }

    [JsonConverter(typeof(CompressionMethodJsonConverter))]
    public CompressionMethod CreatePackageCompression { get; set => this.RaiseAndSetIfChanged(ref field, value); } = CompressionMethod.LZ4;
}

public class ResourcePaneSettings : ReactiveObject
{
    public string InputPath { get; set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;
    public string OutputPath { get; set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;
    public string BatchInputPath { get; set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;
    public string BatchOutputPath { get; set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;
    public LSLib.Granny.ExportFormat BatchInputFormat { get; set => this.RaiseAndSetIfChanged(ref field, value); }
    public LSLib.Granny.ExportFormat BatchOutputFormat { get; set => this.RaiseAndSetIfChanged(ref field, value); }
}

public class VirtualTexturesPaneSettings : ReactiveObject
{
    public string GTSPath { get; set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;
    public string DestinationPath { get; set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;
}

public class OsirisPaneSettings : ReactiveObject
{
    public string InputPath { get; set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;
    public string OutputPath { get; set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;
    public string FilterText { get; set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;
    public bool FilterMatchCase { get; set => this.RaiseAndSetIfChanged(ref field, value); }
}

public class DebugPaneSettings : ReactiveObject
{
    public string SavePath { get; set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;
}

public sealed class PackageVersionJsonConverter : JsonConverter<LSLib.LS.PackageVersion>
{
    public override LSLib.LS.PackageVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        int index = reader.GetInt32();
        return index switch
        {
            2 => LSLib.LS.PackageVersion.V10,
            3 => LSLib.LS.PackageVersion.V9,
            4 => LSLib.LS.PackageVersion.V7,
            _ => LSLib.LS.PackageVersion.V18
        };
    }

    public override void Write(Utf8JsonWriter writer, LSLib.LS.PackageVersion value, JsonSerializerOptions options)
    {
        int outIndex = value switch
        {
            LSLib.LS.PackageVersion.V10 => 2,
            LSLib.LS.PackageVersion.V9 => 3,
            LSLib.LS.PackageVersion.V7 => 4,
            _ => 0
        };
        writer.WriteNumberValue(outIndex);
    }
}

public sealed class CompressionMethodJsonConverter : JsonConverter<CompressionMethod>
{
    public override CompressionMethod Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        int index = reader.GetInt32();
        return index switch
        {
            0 => CompressionMethod.None,
            1 => CompressionMethod.Zlib,
            3 => CompressionMethod.LZ4,
            _ => CompressionMethod.LZ4
        };
    }

    public override void Write(Utf8JsonWriter writer, CompressionMethod value, JsonSerializerOptions options)
    {
        int outIndex = value switch
        {
            CompressionMethod.None => 0,
            CompressionMethod.Zlib => 1,
            CompressionMethod.LZ4 => 3,
            _ => 3
        };
        writer.WriteNumberValue(outIndex);
    }
}

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml;

namespace LSLib.LS;

[InlineArray(64)]
internal struct KeyBuffer
{
    private byte _element0;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct LocaHeader
{
    public static uint DefaultSignature = 0x41434f4c; // 'LOCA'

    public uint Signature;
    public uint NumEntries;
    public uint TextsOffset;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct LocaEntry
{
    public KeyBuffer Key;
    public ushort Version;
    public uint Length;

    public string KeyString
    {
        get
        {
            ReadOnlySpan<byte> span = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<KeyBuffer, byte>(ref Key), 64);
            int nameLen = span.IndexOf((byte)0);
            if (nameLen == 0) return string.Empty;
            if (nameLen < 0) nameLen = 64;
            return Encoding.UTF8.GetString(span[..nameLen]);
        }
        set
        {
            Span<byte> targetSpan = MemoryMarshal.CreateSpan(ref Unsafe.As<KeyBuffer, byte>(ref Key), 64);
            targetSpan.Clear();
            if (!string.IsNullOrEmpty(value))
            {
                _ = Encoding.UTF8.GetBytes(value, targetSpan);
            }
        }
    }
}

public sealed class LocalizedText
{
    public string Key { get; set; } = string.Empty;
    public ushort Version { get; set; }
    public string Text { get; set; } = string.Empty;
}

public sealed class LocaResource
{
    public List<LocalizedText> Entries { get; set; } = [];
}

public sealed class LocaReader(Stream stream) : IDisposable
{
    private readonly Stream _sourceStream = stream ?? throw new ArgumentNullException(nameof(stream));

    public void Dispose()
    {
        _sourceStream.Dispose();
    }

    public LocaResource Read()
    {
        using var reader = new BinaryReader(_sourceStream, Encoding.UTF8, leaveOpen: true);
        var loca = new LocaResource { Entries = [] };
        LocaHeader header = BinUtils.ReadStruct<LocaHeader>(reader);

        if (header.Signature != LocaHeader.DefaultSignature)
        {
            throw new InvalidDataException("Incorrect signature in localization file binary data packet.");
        }

        var entries = new LocaEntry[header.NumEntries];
        BinUtils.ReadStructs(reader, entries);

        if (_sourceStream.Position != header.TextsOffset)
        {
            _sourceStream.Position = header.TextsOffset;
        }

        loca.Entries.EnsureCapacity(entries.Length);

        foreach (LocaEntry entry in entries)
        {
            byte[] bytes = reader.ReadBytes((int)entry.Length - 1);
            string text = Encoding.UTF8.GetString(bytes);

            loca.Entries.Add(new LocalizedText
            {
                Key = entry.KeyString,
                Version = entry.Version,
                Text = text
            });
            _ = reader.ReadByte();
        }

        return loca;
    }
}

public sealed class LocaWriter(Stream stream)
{
    private readonly Stream _destStream = stream ?? throw new ArgumentNullException(nameof(stream));

    public void Write(LocaResource res)
    {
        ArgumentNullException.ThrowIfNull(res);

        using var writer = new BinaryWriter(_destStream, Encoding.UTF8, leaveOpen: true);
        int headerSize = Unsafe.SizeOf<LocaHeader>();
        int entrySize = Unsafe.SizeOf<LocaEntry>();

        var header = new LocaHeader
        {
            Signature = LocaHeader.DefaultSignature,
            NumEntries = (uint)res.Entries.Count,
            TextsOffset = (uint)(headerSize + entrySize * res.Entries.Count)
        };
        BinUtils.WriteStruct(writer, ref header);

        var entries = new LocaEntry[header.NumEntries];
        for (int i = 0; i < entries.Length; i++)
        {
            LocalizedText entry = res.Entries[i];
            entries[i] = new LocaEntry
            {
                KeyString = entry.Key,
                Version = entry.Version,
                Length = (uint)Encoding.UTF8.GetByteCount(entry.Text) + 1
            };
        }

        BinUtils.WriteStructs(writer, entries);

        foreach (LocalizedText entry in res.Entries)
        {
            byte[] bin = Encoding.UTF8.GetBytes(entry.Text);
            writer.Write(bin);
            writer.Write((byte)0);
        }
    }
}

public sealed class LocaXmlReader(Stream stream) : IDisposable
{
    private readonly Stream _sourceStream = stream ?? throw new ArgumentNullException(nameof(stream));
    private XmlReader? _reader;
    private LocaResource? _resource;

    public void Dispose()
    {
        _sourceStream.Dispose();
    }

    private void ReadElement()
    {
        if (_reader is null || _resource is null) return;

        switch (_reader.Name)
        {
            case "contentList":
                break;

            case "content":
                string key = _reader["contentuid"] ?? string.Empty;
                string? versionStr = _reader["version"];
                ushort version = versionStr is not null ? ushort.Parse(versionStr, CultureInfo.InvariantCulture) : (ushort)1;
                string text = _reader.ReadElementContentAsString();

                _resource.Entries.Add(new LocalizedText
                {
                    Key = key,
                    Version = version,
                    Text = text
                });
                break;

            default:
                throw new InvalidFormatException($"Unknown element encountered during xml loading sweeps: {_reader.Name}");
        }
    }

    public LocaResource Read()
    {
        _resource = new LocaResource { Entries = [] };

        using (_reader = XmlReader.Create(_sourceStream))
        {
            while (_reader.Read())
            {
                if (_reader.NodeType == XmlNodeType.Element)
                {
                    ReadElement();
                }
            }
        }

        return _resource;
    }
}

public sealed class LocaXmlWriter(Stream stream)
{
    private readonly Stream _destStream = stream ?? throw new ArgumentNullException(nameof(stream));

    public void Write(LocaResource res)
    {
        ArgumentNullException.ThrowIfNull(res);

        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "\t"
        };

        using var writer = XmlWriter.Create(_destStream, settings);
        writer.WriteStartElement("contentList");

        foreach (LocalizedText entry in res.Entries)
        {
            writer.WriteStartElement("content");
            writer.WriteAttributeString("contentuid", entry.Key);
            writer.WriteAttributeString("version", entry.Version.ToString(CultureInfo.InvariantCulture));
            writer.WriteString(entry.Text);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.Flush();
    }
}

public enum LocaFormat
{
    Loca,
    Xml
}

public static class LocaUtils
{
    public static LocaFormat ExtensionToFileFormat(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string extension = Path.GetExtension(path).ToLowerInvariant();

        return extension switch
        {
            ".loca" => LocaFormat.Loca,
            ".xml" => LocaFormat.Xml,
            _ => throw new ArgumentException($"Unrecognized file extension encountered within localization paths: {extension}", nameof(path))
        };
    }

    public static LocaResource Load(string inputPath)
    {
        return Load(inputPath, ExtensionToFileFormat(inputPath));
    }

    public static LocaResource Load(string inputPath, LocaFormat format)
    {
        using var stream = File.Open(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Load(stream, format);
    }

    public static LocaResource Load(Stream stream, LocaFormat format)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return format switch
        {
            LocaFormat.Loca => LoadLoca(stream),
            LocaFormat.Xml => LoadXml(stream),
            _ => throw new ArgumentException("Invalid loca format configuration specifier passed down.", nameof(format))
        };
    }

    private static LocaResource LoadLoca(Stream stream)
    {
        using var reader = new LocaReader(stream);
        return reader.Read();
    }

    private static LocaResource LoadXml(Stream stream)
    {
        using var reader = new LocaXmlReader(stream);
        return reader.Read();
    }

    public static void Save(LocaResource resource, string outputPath)
    {
        Save(resource, outputPath, ExtensionToFileFormat(outputPath));
    }

    public static void Save(LocaResource resource, string outputPath, LocaFormat format)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);

        FileManager.TryToCreateDirectory(outputPath);

        using var file = File.Open(outputPath, FileMode.Create, FileAccess.Write);
        switch (format)
        {
            case LocaFormat.Loca:
                {
                    var writer = new LocaWriter(file);
                    writer.Write(resource);
                    break;
                }

            case LocaFormat.Xml:
                {
                    var writer = new LocaXmlWriter(file);
                    writer.Write(resource);
                    break;
                }

            default:
                throw new ArgumentException("Invalid loca format parameter.", nameof(format));
        }
    }
}
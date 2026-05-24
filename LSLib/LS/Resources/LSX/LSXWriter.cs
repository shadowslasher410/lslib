using System.Xml;
using LSLib.LS.Enums;

namespace LSLib.LS;

public class LSXWriter(Stream stream)
{
    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    private XmlWriter? _writer;

    public bool PrettyPrint { get; set; } = false;
    public LSXVersion Version { get; set; } = LSXVersion.V3;
    public NodeSerializationSettings SerializationSettings { get; set; } = new();

    private XmlWriter PrepareWrite(uint? majorVersion)
    {
        if (Version == LSXVersion.V3 && majorVersion is not null && majorVersion == 4)
        {
            throw new InvalidDataException("Cannot resave a BG3 (v4.x) resource in D:OS2 (v3.x) file format, maybe you have the wrong game selected?");
        }

        var settings = new XmlWriterSettings
        {
            Indent = PrettyPrint,
            IndentChars = "\t"
        };

        return XmlWriter.Create(_stream, settings);
    }

    public void Write(Resource rsrc)
    {
        ArgumentNullException.ThrowIfNull(rsrc);

        using var xmlWriter = PrepareWrite(rsrc.Metadata.MajorVersion);
        _writer = xmlWriter;

        _writer.WriteStartElement("save");
        _writer.WriteStartElement("version");

        _writer.WriteAttributeString("major", rsrc.Metadata.MajorVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _writer.WriteAttributeString("minor", rsrc.Metadata.MinorVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _writer.WriteAttributeString("revision", rsrc.Metadata.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _writer.WriteAttributeString("build", rsrc.Metadata.BuildNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _writer.WriteAttributeString("lslib_meta", SerializationSettings.BuildMeta() ?? string.Empty);
        _writer.WriteEndElement();

        WriteRegions(rsrc);

        _writer.WriteEndElement();
    }

    public void Write(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        using var xmlWriter = PrepareWrite(null);
        _writer = xmlWriter;

        WriteNode(node);
    }

    private void WriteRegions(Resource rsrc)
    {
        if (_writer is null) return;

        foreach (var region in rsrc.Regions)
        {
            _writer.WriteStartElement("region");
            _writer.WriteAttributeString("id", region.Key);
            WriteNode(region.Value);
            _writer.WriteEndElement();
        }
    }

    private void WriteTranslatedFSString(TranslatedFSString fs)
    {
        if (_writer is null) return;

        _writer.WriteStartElement("string");
        _writer.WriteAttributeString("value", fs.Value ?? string.Empty);
        WriteTranslatedFSStringInner(fs);
        _writer.WriteEndElement();
    }

    private void WriteTranslatedFSStringInner(TranslatedFSString fs)
    {
        if (_writer is null) return;

        _writer.WriteAttributeString("handle", fs.Handle ?? string.Empty);
        _writer.WriteAttributeString("arguments", fs.Arguments.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (fs.Arguments.Count > 0)
        {
            _writer.WriteStartElement("arguments");
            for (int i = 0; i < fs.Arguments.Count; i++)
            {
                var argument = fs.Arguments[i];
                if (argument is null) continue;

                _writer.WriteStartElement("argument");
                _writer.WriteAttributeString("key", argument.Key ?? string.Empty);
                _writer.WriteAttributeString("value", argument.Value ?? string.Empty);
                WriteTranslatedFSString(argument.String);
                _writer.WriteEndElement();
            }
            _writer.WriteEndElement();
        }
    }

    private void WriteNode(Node node)
    {
        if (_writer is null) return;

        _writer.WriteStartElement("node");
        _writer.WriteAttributeString("id", node.Name ?? string.Empty);

        if (node.KeyAttribute is not null)
        {
            _writer.WriteAttributeString("key", node.KeyAttribute);
        }

        foreach (var attribute in node.Attributes)
        {
            if (attribute.Value is null) continue;

            _writer.WriteStartElement("attribute");
            _writer.WriteAttributeString("id", attribute.Key);

            if (Version >= LSXVersion.V4)
            {
                _writer.WriteAttributeString("type", attribute.Value.Type.ToString());
            }
            else
            {
                _writer.WriteAttributeString("type", ((int)attribute.Value.Type).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (attribute.Value.Type == AttributeType.TranslatedString)
            {
                var ts = (TranslatedString)(attribute.Value.Value ?? new TranslatedString());
                _writer.WriteAttributeString("handle", ts.Handle ?? string.Empty);
                if (ts.Value is not null)
                {
                    _writer.WriteAttributeString("value", ts.Value);
                }
                else
                {
                    _writer.WriteAttributeString("version", ts.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            else if (attribute.Value.Type == AttributeType.TranslatedFSString)
            {
                var fs = (TranslatedFSString)(attribute.Value.Value ?? new TranslatedFSString());
                _writer.WriteAttributeString("value", fs.Value ?? string.Empty);
                WriteTranslatedFSStringInner(fs);
            }
            else
            {
                string rawStr = attribute.Value.Value?.ToString() ?? string.Empty;
                _writer.WriteAttributeString("value", rawStr.Replace("\x1f", "", StringComparison.Ordinal));
            }

            _writer.WriteEndElement();
        }
        if (node.Children.Count > 0)
        {
            _writer.WriteStartElement("children");
            foreach (var childrenList in node.Children.Values)
            {
                if (childrenList is null) continue;
                foreach (var child in childrenList)
                {
                    if (child is not null) WriteNode(child);
                }
            }
            _writer.WriteEndElement();
        }

        _writer.WriteEndElement();
    }
}
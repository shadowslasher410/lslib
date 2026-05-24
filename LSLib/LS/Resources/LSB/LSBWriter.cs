using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace LSLib.LS;

[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "Safe unboxed layout formatting serialization.")]
public class LSBWriter(Stream stream)
{
    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    private BinaryWriter? _writer;
    private readonly Dictionary<string, uint> _staticStrings = new(StringComparer.Ordinal);
    private uint _nextStaticStringId;
    private uint _version;

    public void Write(Resource rsrc)
    {
        ArgumentNullException.ThrowIfNull(rsrc);

        _version = rsrc.Metadata.MajorVersion;

        using var binaryWriter = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);
        _writer = binaryWriter;

        var header = new LSBHeader
        {
            TotalSize = 0,
            BigEndian = 0,
            Unknown = 0,
            Metadata = rsrc.Metadata
        };

        if (rsrc.Metadata.MajorVersion >= 4)
        {
            header.Signature = BinaryPrimitives.ReadUInt32LittleEndian(LSBHeader.SignatureBG3);
        }
        else
        {
            header.Signature = LSBHeader.SignatureFW3;
        }

        BinUtils.WriteStruct(_writer, ref header);

        CollectStaticStrings(rsrc);
        WriteStaticStrings();

        WriteRegions(rsrc);

        header.TotalSize = (uint)_stream.Position;
        _stream.Seek(0, SeekOrigin.Begin);
        BinUtils.WriteStruct(_writer, ref header);
    }

    private void WriteRegions(Resource rsrc)
    {
        if (_writer is null) return;

        _writer.Write((uint)rsrc.Regions.Count);
        long regionMapOffset = _stream.Position;
        foreach (var rgn in rsrc.Regions)
        {
            _writer.Write(_staticStrings[rgn.Key]);
            _writer.Write(0u);
        }

        var regionPositions = new List<uint>(rsrc.Regions.Count);
        foreach (var rgn in rsrc.Regions)
        {
            regionPositions.Add((uint)_stream.Position);
            WriteNode(rgn.Value);
        }

        long endOffset = _stream.Position;
        _stream.Seek(regionMapOffset, SeekOrigin.Begin);
        foreach (uint position in regionPositions)
        {
            _stream.Seek(4, SeekOrigin.Current);
            _writer.Write(position);
        }

        _stream.Seek(endOffset, SeekOrigin.Begin);
    }

    private void WriteNode(Node node)
    {
        if (_writer is null) return;

        _writer.Write(_staticStrings[node.Name]);
        _writer.Write((uint)node.Attributes.Count);
        _writer.Write((uint)node.ChildCount);

        foreach (var attribute in node.Attributes)
        {
            _writer.Write(_staticStrings[attribute.Key]);
            _writer.Write((uint)attribute.Value.Type);
            WriteAttribute(attribute.Value);
        }

        foreach (var childList in node.Children.Values)
        {
            if (childList is null) continue;
            foreach (var child in childList)
            {
                if (child is not null) WriteNode(child);
            }
        }
    }


    private void WriteAttribute(NodeAttribute attr)
    {
        if (_writer is null) return;

        switch (attr.Type)
        {
            case AttributeType.String:
            case AttributeType.Path:
            case AttributeType.FixedString:
            case AttributeType.LSString:
                WriteString((string)(attr.Value ?? string.Empty), true);
                break;

            case AttributeType.WString:
            case AttributeType.LSWString:
                WriteWideString((string)(attr.Value ?? string.Empty), true);
                break;

            case AttributeType.TranslatedString:
                {
                    var str = (TranslatedString)(attr.Value ?? new TranslatedString());
                    if (_version >= 4 && str.Value is null)
                    {
                        _writer.Write(str.Version);
                    }
                    else
                    {
                        WriteString(str.Value ?? string.Empty, true);
                    }

                    WriteString(str.Handle, true);
                    break;
                }

            case AttributeType.ScratchBuffer:
                {
                    var buffer = (byte[])(attr.Value ?? Array.Empty<byte>());
                    _writer.Write((uint)buffer.Length);
                    _writer.Write(buffer);
                    break;
                }

            default:
                BinUtils.WriteAttribute(_writer, attr);
                break;
        }
    }

    private void CollectStaticStrings(Resource rsrc)
    {
        _staticStrings.Clear();
        foreach (var rgn in rsrc.Regions)
        {
            AddStaticString(rgn.Key);
            CollectStaticStrings(rgn.Value);
        }
    }

    private void CollectStaticStrings(Node node)
    {
        AddStaticString(node.Name);

        foreach (var attr in node.Attributes)
        {
            AddStaticString(attr.Key);
        }

        foreach (var childList in node.Children.Values)
        {
            if (childList is null) continue;
            foreach (var child in childList)
            {
                if (child is not null) CollectStaticStrings(child);
            }
        }
    }

    private void AddStaticString(string s)
    {
        if (!_staticStrings.ContainsKey(s))
        {
            _staticStrings.Add(s, _nextStaticStringId++);
        }
    }

    private void WriteStaticStrings()
    {
        if (_writer is null) return;

        _writer.Write((uint)_staticStrings.Count);
        foreach (var s in _staticStrings)
        {
            WriteString(s.Key, false);
            _writer.Write(s.Value);
        }
    }

    private void WriteString(string s, bool nullTerminated)
    {
        if (_writer is null) return;

        byte[] utf = Encoding.UTF8.GetBytes(s);
        int length = utf.Length + (nullTerminated ? 1 : 0);
        _writer.Write(length);
        _writer.Write(utf);
        if (nullTerminated)
            _writer.Write((byte)0);
    }

    private void WriteWideString(string s, bool nullTerminated)
    {
        if (_writer is null) return;

        byte[] unicode = Encoding.Unicode.GetBytes(s);
        int length = (unicode.Length / 2) + (nullTerminated ? 1 : 0);
        _writer.Write(length);
        _writer.Write(unicode);
        if (nullTerminated)
            _writer.Write((ushort)0);
    }
}
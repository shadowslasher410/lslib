using System.Buffers.Binary;

namespace LSLib.LS;

public class LSBReader(Stream stream) : IDisposable
{
    private BinaryReader? _reader;
    private readonly Dictionary<uint, string> _staticStrings = [];
    private bool _isBG3;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            stream.Dispose();
            _reader?.Dispose();
        }
    }

    public Resource Read()
    {
        _reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var header = BinUtils.ReadStruct<LSBHeader>(_reader);
        if (header.Signature != BinaryPrimitives.ReadUInt32LittleEndian(LSBHeader.SignatureBG3) && header.Signature != LSBHeader.SignatureFW3)
            throw new InvalidFormatException(string.Format("Illegal signature in LSB header ({0})", header.Signature));

        if (stream.Length != header.TotalSize)
            throw new InvalidFormatException(string.Format("Invalid LSB file size; expected {0}, got {1}", header.TotalSize, stream.Length));

        if (header.BigEndian != 0)
            throw new InvalidFormatException("Big-endian LSB files are not supported");

        _isBG3 = header.Signature == BinaryPrimitives.ReadUInt32LittleEndian(LSBHeader.SignatureBG3);
        ReadStaticStrings();

        var rsrc = new Resource
        {
            Metadata = header.Metadata
        };
        ReadRegions(rsrc);
        return rsrc;
    }

    private void ReadRegions(Resource rsrc)
    {
        if (_reader is null) return;

        uint regions = _reader.ReadUInt32();
        for (uint i = 0; i < regions; i++)
        {
            uint regionNameId = _reader.ReadUInt32();
            uint regionOffset = _reader.ReadUInt32();

            var rgn = new Region
            {
                RegionName = _staticStrings[regionNameId]
            };
            long lastRegionPos = stream.Position;

            stream.Seek(regionOffset, SeekOrigin.Begin);
            ReadNode(rgn);
            rsrc.Regions[rgn.RegionName] = rgn;
            stream.Seek(lastRegionPos, SeekOrigin.Begin);
        }
    }

    private void ReadNode(Node node)
    {
        if (_reader is null) return;

        uint nodeNameId = _reader.ReadUInt32();
        uint attributeCount = _reader.ReadUInt32();
        uint childCount = _reader.ReadUInt32();
        node.Name = _staticStrings[nodeNameId];

        for (uint i = 0; i < attributeCount; i++)
        {
            uint attrNameId = _reader.ReadUInt32();
            uint attrTypeId = _reader.ReadUInt32();
            if (attrTypeId > (uint)AttributeType.Max)
                throw new InvalidFormatException(string.Format("Unsupported attribute data type: {0}", attrTypeId));

            node.Attributes[_staticStrings[attrNameId]] = ReadAttribute((AttributeType)attrTypeId);
        }

        for (uint i = 0; i < childCount; i++)
        {
            var child = new Node
            {
                Parent = node
            };
            ReadNode(child);
            node.AppendChild(child);
        }
    }

    private NodeAttribute ReadAttribute(AttributeType type)
    {
        if (_reader is null) return new NodeAttribute(type);

        switch (type)
        {
            case AttributeType.String:
            case AttributeType.Path:
            case AttributeType.FixedString:
            case AttributeType.LSString:
                {
                    return new NodeAttribute(type)
                    {
                        Value = ReadString(true)
                    };
                }

            case AttributeType.WString:
            case AttributeType.LSWString:
                {
                    return new NodeAttribute(type)
                    {
                        Value = ReadWideString(true)
                    };
                }

            case AttributeType.TranslatedString:
                {
                    var attr = new NodeAttribute(type);
                    var str = new TranslatedString();

                    if (_isBG3)
                    {
                        str.Version = _reader.ReadUInt16();

                        ushort test = _reader.ReadUInt16();
                        if (test == 0)
                        {
                            stream.Seek(-4, SeekOrigin.Current);
                            str.Version = 0;
                            str.Value = ReadString(true);
                        }
                        else
                        {
                            stream.Seek(-2, SeekOrigin.Current);
                            str.Value = null!;
                        }
                    }
                    else
                    {
                        str.Version = 0;
                        str.Value = ReadString(true);
                    }

                    str.Handle = ReadString(true);
                    attr.Value = str;
                    return attr;
                }

            case AttributeType.ScratchBuffer:
                {
                    var attr = new NodeAttribute(type);
                    int bufferLength = _reader.ReadInt32();
                    attr.Value = _reader.ReadBytes(bufferLength);
                    return attr;
                }

            default:
                return BinUtils.ReadAttribute(type, _reader);
        }
    }

    private void ReadStaticStrings()
    {
        if (_reader is null) return;

        uint strings = _reader.ReadUInt32();
        for (uint i = 0; i < strings; i++)
        {
            string s = ReadString(false);
            uint index = _reader.ReadUInt32();
            if (_staticStrings.ContainsKey(index))
                throw new InvalidFormatException(string.Format("String ID {0} duplicated in static string map", index));
            _staticStrings.Add(index, s);
        }
    }

    private string ReadString(bool nullTerminated)
    {
        if (_reader is null) return string.Empty;

        int length = _reader.ReadInt32() - (nullTerminated ? 1 : 0);
        byte[] bytes = _reader.ReadBytes(length);

        bool hasBogusNullBytes = false;
        while (length > 0 && bytes[length - 1] == 0)
        {
            length--;
            hasBogusNullBytes = true;
        }

        string str = Encoding.UTF8.GetString(bytes, 0, length);

        if (nullTerminated)
        {
            if (_reader.ReadByte() != 0 && !hasBogusNullBytes)
                throw new InvalidFormatException("Illegal null terminated string");
        }

        return str;
    }

    private string ReadWideString(bool nullTerminated)
    {
        if (_reader is null) return string.Empty;

        int length = _reader.ReadInt32() - (nullTerminated ? 1 : 0);
        byte[] bytes = _reader.ReadBytes(length * 2);
        string str = Encoding.Unicode.GetString(bytes);
        if (nullTerminated)
        {
            if (_reader.ReadUInt16() != 0)
                throw new InvalidFormatException("Illegal null terminated widestring");
        }

        return str;
    }
}

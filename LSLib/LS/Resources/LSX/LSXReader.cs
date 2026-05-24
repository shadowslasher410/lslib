using LSLib.LS.Enums;
using System.Diagnostics;
using System.Globalization;
using System.Xml;

namespace LSLib.LS;

public class LSXReader(Stream stream) : IDisposable
{
    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    private XmlReader? _reader;
    private Resource? _resource;
    private Region? _currentRegion;
    private List<Node> _stack = [];
    public int LastLine { get; set; }
    public int LastColumn { get; set; }
    private LSXVersion _version = LSXVersion.V3;
    public NodeSerializationSettings SerializationSettings { get; set; } = new();
    private NodeAttribute? _lastAttribute;
    private int _valueOffset;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stream.Dispose();
            _reader?.Dispose();
        }
    }

    private void ReadTranslatedFSString(TranslatedFSString fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        if (_reader is null) return;

        fs.Value = _reader["value"] ?? string.Empty;
        fs.Handle = _reader["handle"] ?? string.Empty;
        Debug.Assert(fs.Handle is not null);

        int arguments = Convert.ToInt32(_reader["arguments"], System.Globalization.CultureInfo.InvariantCulture);
        fs.Arguments = new List<TranslatedFSStringArgument>(arguments);

        if (arguments > 0)
        {
            while (_reader.Read() && _reader.NodeType != XmlNodeType.Element) { }
            if (_reader.Name != "arguments")
            {
                throw new InvalidFormatException(string.Format("Expected <arguments>: {0}", _reader.Name));
            }

            int processedArgs = 0;
            while (processedArgs < arguments && _reader.Read())
            {
                if (_reader.NodeType == XmlNodeType.Element)
                {
                    if (_reader.Name != "argument")
                    {
                        throw new InvalidFormatException(string.Format("Expected <argument>: {0}", _reader.Name));
                    }

                    var arg = new TranslatedFSStringArgument
                    {
                        Key = _reader["key"] ?? string.Empty,
                        Value = _reader["value"] ?? string.Empty
                    };

                    while (_reader.Read() && _reader.NodeType != XmlNodeType.Element) { }
                    if (_reader.Name != "string")
                    {
                        throw new InvalidFormatException(string.Format("Expected <string>: {0}", _reader.Name));
                    }

                    arg.String = new TranslatedFSString();
                    ReadTranslatedFSString(arg.String);

                    fs.Arguments.Add(arg);
                    processedArgs++;

                    while (_reader.Read() && _reader.NodeType != XmlNodeType.EndElement) { }
                }
            }

            while (_reader.Read() && _reader.NodeType != XmlNodeType.EndElement) { }
            while (_reader.Read() && _reader.NodeType != XmlNodeType.EndElement) { }
            Debug.Assert(processedArgs == arguments);
        }
    }

    private void ReadElement()
    {
        if (_reader is null || _resource is null) return;

        string elementName = _reader.Name;
        switch (elementName)
        {
            case "save":
                if (_stack.Count > 0)
                    throw new InvalidFormatException("Node <save> was unexpected.");
                break;

            case "header":
                _resource.Metadata.Timestamp = Convert.ToUInt64(_reader["time"], CultureInfo.InvariantCulture);
                break;

            case "version":
                _resource.Metadata.MajorVersion = Convert.ToUInt32(_reader["major"], CultureInfo.InvariantCulture);
                _resource.Metadata.MinorVersion = Convert.ToUInt32(_reader["minor"], CultureInfo.InvariantCulture);
                _resource.Metadata.Revision = Convert.ToUInt32(_reader["revision"], CultureInfo.InvariantCulture);
                _resource.Metadata.BuildNumber = Convert.ToUInt32(_reader["build"], CultureInfo.InvariantCulture);
                _version = (_resource.Metadata.MajorVersion >= 4) ? LSXVersion.V4 : LSXVersion.V3;

                var lslibMeta = _reader["lslib_meta"];
                SerializationSettings.InitFromMeta(lslibMeta ?? string.Empty);
                _resource.MetadataFormat = SerializationSettings.LSFMetadata;
                break;

            case "region":
                if (_currentRegion is not null)
                    throw new InvalidFormatException("A <region> can only start at the root level of a resource.");

                Debug.Assert(!_reader.IsEmptyElement);
                var region = new Region
                {
                    RegionName = _reader["id"] ?? throw new InvalidFormatException("Missing required attribute 'id' in <region> element.")
                };
                Debug.Assert(region.RegionName is not null);
                _resource.Regions.Add(region.RegionName, region);
                _currentRegion = region;
                break;

            case "node":
                if (_currentRegion is null)
                    throw new InvalidFormatException("A <node> must be located inside a region.");

                Node node;
                if (_stack.Count == 0)
                {
                    node = _currentRegion;
                }
                else
                {
                    int lineNum = 0;
                    if (_reader is IXmlLineInfo lineInfo && lineInfo.HasLineInfo())
                    {
                        lineNum = lineInfo.LineNumber;
                    }

                    node = new Node
                    {
                        Parent = _stack.Last(),
                        Line = lineNum
                    };
                }

                node.Name = _reader["id"] ?? throw new InvalidFormatException("Missing required attribute 'id' in <node> element.");
                Debug.Assert(node.Name is not null);
                node.Parent?.AppendChild(node);

                node.KeyAttribute = _reader["key"] ?? string.Empty;

                if (!_reader.IsEmptyElement)
                    _stack.Add(node);
                break;

            case "attribute":
                uint attrTypeId;
                string? typeAttr = _reader["type"] ?? throw new InvalidFormatException("Missing required attribute 'type' in <attribute> element.");
                if (!uint.TryParse(typeAttr, out attrTypeId))
                {
                    attrTypeId = (uint)AttributeTypeMaps.TypeToId[typeAttr];
                }

                var attrName = _reader["id"] ?? throw new InvalidFormatException("Missing required attribute 'id' in <attribute> element.");
                if (attrTypeId > (uint)AttributeType.Max)
                    throw new InvalidFormatException(string.Format("Unsupported attribute data type: {0}", attrTypeId));

                Debug.Assert(attrName is not null);

                int attrLineNum = 0;
                if (_reader is IXmlLineInfo attrLineInfo && attrLineInfo.HasLineInfo())
                {
                    attrLineNum = attrLineInfo.LineNumber;
                }

                var attr = new NodeAttribute((AttributeType)attrTypeId)
                {
                    Line = attrLineNum
                };

                var attrValue = _reader["value"];
                if (attrValue is not null)
                {
                    attr.FromString(attrValue, SerializationSettings);
                }
                else
                {
                    switch (attr.Type)
                    {
                        case AttributeType.Vec2: attr.Value = new float[2]; break;
                        case AttributeType.Vec3: attr.Value = new float[3]; break;
                        case AttributeType.Vec4: attr.Value = new float[4]; break;
                        case AttributeType.Mat2: attr.Value = new float[2 * 2]; break;
                        case AttributeType.Mat3: attr.Value = new float[3 * 3]; break;
                        case AttributeType.Mat3x4: attr.Value = new float[3 * 4]; break;
                        case AttributeType.Mat4: attr.Value = new float[4 * 4]; break;
                        case AttributeType.Mat4x3: attr.Value = new float[4 * 3]; break;
                        case AttributeType.TranslatedString: break;
                        case AttributeType.TranslatedFSString: break;
                        default: throw new InvalidOperationException($"Attribute of type {attr.Type} should have an inline value!");
                    }

                    _valueOffset = 0;
                    _lastAttribute = attr;
                }

                if (attr.Type == AttributeType.TranslatedString)
                {
                    attr.Value ??= new TranslatedString();
                    var ts = (TranslatedString)attr.Value;
                    ts.Handle = _reader["handle"] ?? string.Empty;
                    Debug.Assert(ts.Handle is not null);

                    if (attrValue is null)
                    {
                        ts.Version = ushort.Parse(_reader["version"] ?? "0", CultureInfo.InvariantCulture);
                    }
                }
                else if (attr.Type == AttributeType.TranslatedFSString)
                {
                    attr.Value ??= new TranslatedFSString();
                    var fs = (TranslatedFSString)attr.Value;
                    ReadTranslatedFSString(fs);
                }

                _stack.Last().Attributes.Add(attrName, attr);
                break;

            case "float2":
                if (_lastAttribute?.Value is float[] f2Val)
                {
                    f2Val[_valueOffset++] = float.Parse(_reader["x"] ?? "0", CultureInfo.InvariantCulture);
                    f2Val[_valueOffset++] = float.Parse(_reader["y"] ?? "0", CultureInfo.InvariantCulture);
                }
                break;

            case "float3":
                if (_lastAttribute?.Value is float[] f3Val)
                {
                    f3Val[_valueOffset++] = float.Parse(_reader["x"] ?? "0", CultureInfo.InvariantCulture);
                    f3Val[_valueOffset++] = float.Parse(_reader["y"] ?? "0", CultureInfo.InvariantCulture);
                    f3Val[_valueOffset++] = float.Parse(_reader["z"] ?? "0", CultureInfo.InvariantCulture);
                }
                break;

            case "float4":
                if (_lastAttribute?.Value is float[] f4Val)
                {
                    f4Val[_valueOffset++] = float.Parse(_reader["x"] ?? "0", CultureInfo.InvariantCulture);
                    f4Val[_valueOffset++] = float.Parse(_reader["y"] ?? "0", CultureInfo.InvariantCulture);
                    f4Val[_valueOffset++] = float.Parse(_reader["z"] ?? "0", CultureInfo.InvariantCulture);
                    f4Val[_valueOffset++] = float.Parse(_reader["w"] ?? "0", CultureInfo.InvariantCulture);
                }
                break;

            case "mat2":
            case "mat3":
            case "mat4":
                // These are read in the float2/3/4 nodes
                break;

            case "children":
                // Child nodes are handled in the "node" case
                break;

            default:
                throw new InvalidFormatException($"Unknown element signature encountered during parsing pass: {elementName}");
        }
    }

    private void ReadEndElement()
    {
        if (_reader is null) return;

        switch (_reader.Name)
        {
            case "save":
            case "header":
            case "version":
            case "attribute":
            case "children":
                // These elements don't change the stack, just discard them
                break;

            case "region":
                Debug.Assert(_stack.Count == 0);
                Debug.Assert(_currentRegion is not null);
                Debug.Assert(_currentRegion.Name is not null);
                _currentRegion = null;
                break;

            case "node":
                if (_stack.Count > 0)
                {
                    _stack.RemoveAt(_stack.Count - 1);
                }
                break;

            // Value nodes, processed in ReadElement()
            case "float2":
            case "float3":
            case "float4":
            case "mat2":
            case "mat3":
            case "mat4":
                break;

            default:
                throw new InvalidFormatException(string.Format("Unknown XML closing element encountered: {0}", _reader.Name));
        }
    }

    private void ReadInternal()
    {
        var settings = new XmlReaderSettings
        {
            CloseInput = false,
            IgnoreComments = true,
            IgnoreWhitespace = true
        };

        _reader = XmlReader.Create(_stream, settings);
        try
        {
            while (_reader.Read())
            {
                if (_reader.NodeType == XmlNodeType.Element)
                {
                    ReadElement();
                }
                else if (_reader.NodeType == XmlNodeType.EndElement)
                {
                    ReadEndElement();
                }
            }
        }
        catch (Exception)
        {
            if (_reader is IXmlLineInfo lineInfo && lineInfo.HasLineInfo())
            {
                LastLine = lineInfo.LineNumber;
                LastColumn = lineInfo.LinePosition;
            }
            throw;
        }
        finally
        {
            _reader.Dispose();
        }
    }

    public Resource Read()
    {
        _resource = new Resource();
        _currentRegion = null;
        _stack = [];
        LastLine = LastColumn = 0;

        var resultResource = _resource;

        try
        {
            ReadInternal();
        }
        catch (Exception e)
        {
            if (LastLine > 0)
            {
                throw new Exception($"Parsing error at or near line {LastLine}, column {LastColumn}:{Environment.NewLine}{e.Message}", e);
            }
            else
            {
                throw;
            }
        }
        finally
        {
            _resource = null;
            _currentRegion = null;
            _stack = [];
        }

        return resultResource;
    }
}
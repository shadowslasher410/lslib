using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace PhysXTool;

public sealed unsafe class AlignedMemoryBuffer : IDisposable
{
    private const nuint SerialFileAlignment = 128;
    private byte* _allocatedPointer;

    public byte* AlignedPointer { get; private set; }
    public int Size { get; }

    public AlignedMemoryBuffer(ReadOnlySpan<byte> sourceData)
    {
        Size = sourceData.Length;

        _allocatedPointer = (byte*)NativeMemory.AlignedAlloc((nuint)Size, SerialFileAlignment);

        NativeMemory.Clear(_allocatedPointer, (nuint)Size);

        AlignedPointer = _allocatedPointer;

        var destinationSpan = new Span<byte>(AlignedPointer, Size);
        sourceData.CopyTo(destinationSpan);
    }

    public ReadOnlySpan<byte> AsSpan() => new(AlignedPointer, Size);

    public void Dispose()
    {
        if (_allocatedPointer is null) return;
        NativeMemory.AlignedFree(_allocatedPointer);
        _allocatedPointer = null;
        AlignedPointer = null;
    }
}

public sealed class PhysXConverter : IDisposable
{
    private readonly ConcurrentDictionary<PhysicsCollection, AlignedMemoryBuffer> _binaryBuffers = new();
    private bool _isInitialized;

    public bool InitPhysX()
    {
        _isInitialized = true;
        return _isInitialized;
    }

    public void ShutdownPhysX()
    {
        if (!_isInitialized) return;

        foreach (var buffer in _binaryBuffers.Values)
        {
            buffer.Dispose();
        }

        _binaryBuffers.Clear();
        _isInitialized = false;
    }

    public PhysicsCollection LoadCollectionFromXml(ReadOnlySpan<byte> xml)
    {
        if (!_isInitialized) throw new InvalidOperationException("Physics engine stack is uninitialized.");
        if (xml.IsEmpty) throw new ArgumentException("XML data source cannot be empty.");

        using var stream = new MemoryStream(xml.ToArray());
        var doc = XDocument.Load(stream);

        if (doc.Root is null)
        {
            throw new InvalidDataException("Invalid document structure: Failed to extract XML root element.");
        }

        var loader = new PhysXLoader();
        return loader.Load(doc.Root);
    }

    public PhysicsCollection LoadCollectionFromBinary(ReadOnlySpan<byte> bin)
    {
        if (!_isInitialized) throw new InvalidOperationException("Physics engine stack is uninitialized.");

        var alignedBuffer = new AlignedMemoryBuffer(bin);
        var collection = new PhysicsCollection();

        _binaryBuffers[collection] = alignedBuffer;
        return collection;
    }

    public void ReleaseCollection(PhysicsCollection? collection)
    {
        if (collection is null) return;

        if (_binaryBuffers.TryRemove(collection, out var alignedBuffer))
        {
            alignedBuffer.Dispose();
        }
    }

    public static byte[] SaveCollectionToXml(PhysicsCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var exporter = new PhysXExporter();
        XDocument doc = exporter.Export(collection);

        using var memoryStream = new MemoryStream();
        using (var writer = new StreamWriter(memoryStream, Encoding.UTF8, leaveOpen: true))
        {
            doc.Save(writer);
        }

        return memoryStream.ToArray();
    }

    public byte[] SaveCollectionToBinary(PhysicsCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        if (!_binaryBuffers.TryGetValue(collection, out var alignedBuffer))
        {
            return [];
        }

        return alignedBuffer.AsSpan().ToArray();
    }

    public void Dispose()
    {
        ShutdownPhysX();
    }
}

public sealed class PhysXSaverConverter
{
    public static PhysicsCollection LoadCollectionFromXmlString(string xmlContent)
    {
        if (string.IsNullOrWhiteSpace(xmlContent)) return new PhysicsCollection();

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xmlContent));
        var doc = XDocument.Load(stream);
        return doc.Root is null ? new PhysicsCollection() : new PhysXLoader().Load(doc.Root);
    }
}

public sealed class PhysXSaveConverter
{
    public static string ExportCollectionToXmlString(PhysicsCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var exporter = new PhysXExporter();
        XDocument document = exporter.Export(collection);

        using var writer = new StringWriter();
        document.Save(writer);
        return writer.ToString();
    }
}

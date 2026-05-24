using System.Buffers.Binary;
using System.Diagnostics;

namespace LSLib.Granny.GR2;
public sealed class ParsingException(string message) : Exception(message);

public sealed class GR2Reader(Stream stream) : IGR2StreamContext, IDisposable
{
    public Stream Stream { get; private set; } = Stream.Null;
    public Magic Magic { get; private set; } = null!;
    public Dictionary<StructReference, StructDefinition> Types { get; } = [];

    internal Stream InputStream { get; } = stream ?? throw new ArgumentNullException(nameof(stream));
    internal BinaryReader InputReader { get; private set; } = null!;
    internal BinaryReader Reader { get; private set; } = null!;
    internal Header Header { get; private set; } = null!;
    internal List<Section> Sections { get; } = [];

    private readonly Dictionary<uint, object> _cachedStructs = [];

    #if DEBUG_GR2_SERIALIZATION
        private readonly HashSet<StructReference> _debugPendingResolve = [];
    #endif

    public uint Tag => Header.Tag;

    public void Dispose()
    {
        InputReader?.Dispose();
        Reader?.Dispose();
        Stream?.Dispose();
    }

    public void Read(object root)
    {
        ArgumentNullException.ThrowIfNull(root);

        InputReader = new BinaryReader(InputStream, Encoding.UTF8, leaveOpen: true);
        try
        {
            Magic = ReadMagic();

            if (Magic.FormatEntry != Magic.Format.LittleEndian32 && Magic.FormatEntry != Magic.Format.LittleEndian64)
                throw new ParsingException("Only little-endian GR2 files are supported across current virtual texture models.");

            Header = ReadHeader();
            for (int i = 0; i < Header.NumSections; i++)
            {
                var section = new Section
                {
                    Header = ReadSectionHeader()
                };
                Sections.Add(section);
            }

            Debug.Assert(InputStream.Position == Magic.HeadersSize);

            try
            {
                UncompressStream();

                foreach (var section in Sections)
                {
                    ReadSectionRelocations(section);
                }

                if (Magic.IsLittleEndian != BitConverter.IsLittleEndian)
                {
                    foreach (var section in Sections)
                    {
                        ReadSectionMixedMarshallingRelocations(section);
                    }
                }

                var rootStruct = new StructReference
                {
                    Offset = Sections[(int)Header.RootType.Section].Header.OffsetInFile + Header.RootType.Offset
                };

                var rootNodeOffsetReference = new RelocatableReference
                {
                    Offset = Sections[(int)Header.RootNode.Section].Header.OffsetInFile + Header.RootNode.Offset
                };

                Seek(rootNodeOffsetReference);

                var resolvedRootType = rootStruct.Resolve(this);
                ReadStruct(resolvedRootType, MemberType.Inline, root, null!);
            }
            finally
            {
                Reader?.Dispose();
                Stream?.Dispose();
            }
        }
        finally
        {
            InputReader?.Dispose();
        }
    }

    private Magic ReadMagic()
    {
        var magic = new Magic
        {
            Signature = InputReader.ReadBytes(16),
            HeadersSize = InputReader.ReadUInt32(),
            HeaderFormat = InputReader.ReadUInt32(),
            Reserved1 = InputReader.ReadUInt32(),
            Reserved2 = InputReader.ReadUInt32()
        };
        magic.FormatEntry = Magic.FormatFromSignature(magic.Signature);

        if (magic.HeaderFormat != 0)
            throw new ParsingException("Compressed GR2 files are not supported natively in this stream reader.");

        Debug.Assert(magic.Reserved1 == 0);
        Debug.Assert(magic.Reserved2 == 0);

        #if DEBUG_GR2_SERIALIZATION
                Debug.WriteLine(" ===== GR2 Magic ===== ");
                Debug.WriteLine("Format: {0}", magic.FormatEntry);
                Debug.WriteLine("Headers size: {0:X8}, format: {1:X8}", magic.HeadersSize, magic.HeaderFormat);
                Debug.WriteLine("Reserved1-2: {0:X8} {1:X8}", magic.Reserved1, magic.Reserved2);
        #endif
        return magic;
    }

    private Header ReadHeader()
    {
        var header = new Header
        {
            Version = InputReader.ReadUInt32(),
            FileSize = InputReader.ReadUInt32(),
            Crc = InputReader.ReadUInt32(),
            SectionsOffset = InputReader.ReadUInt32(),
            NumSections = InputReader.ReadUInt32(),
            RootType = ReadSectionReferenceUnchecked(),
            RootNode = ReadSectionReferenceUnchecked(),
            Tag = InputReader.ReadUInt32(),
            ExtraTags = new uint[Header.ExtraTagCount]
        };

        for (int i = 0; i < Header.ExtraTagCount; i++)
            header.ExtraTags[i] = InputReader.ReadUInt32();

        if (header.Version >= 7)
        {
            header.StringTableCrc = InputReader.ReadUInt32();
            header.Reserved1 = InputReader.ReadUInt32();
            header.Reserved2 = InputReader.ReadUInt32();
            header.Reserved3 = InputReader.ReadUInt32();
        }

        if (header.Version < 6 || header.Version > 7)
            throw new ParsingException($"Unsupported GR2 version; file is version {header.Version}, supported versions are 6 and 7");

        Debug.Assert(header.FileSize <= InputStream.Length);
        Debug.Assert(header.SectionsOffset == header.Size());
        Debug.Assert(header.RootType.Section < header.NumSections);

        long mappedTypeThreshold = header.SectionsOffset + (header.NumSections * 48);
        if (header.RootType.Offset > header.FileSize || (long)header.RootType.Offset < mappedTypeThreshold)
        {
            Debug.WriteLine($"[WARNING] Structural anomalies tracked inside Root Type offset parameters metadata map: {header.RootType.Offset:X8}");
        }

        Debug.Assert(header.StringTableCrc == 0);
        Debug.Assert(header.Reserved1 == 0);
        Debug.Assert(header.Reserved2 == 0);
        Debug.Assert(header.Reserved3 == 0);

        return header;
    }

    private SectionHeader ReadSectionHeader()
    {
        var header = new SectionHeader
        {
            Compression = InputReader.ReadUInt32(),
            OffsetInFile = InputReader.ReadUInt32(),
            CompressedSize = InputReader.ReadUInt32(),
            UncompressedSize = InputReader.ReadUInt32(),
            Alignment = InputReader.ReadUInt32(),
            First16bit = InputReader.ReadUInt32(),
            First8bit = InputReader.ReadUInt32(),
            RelocationsOffset = InputReader.ReadUInt32(),
            NumRelocations = InputReader.ReadUInt32(),
            MixedMarshallingDataOffset = InputReader.ReadUInt32(),
            NumMixedMarshallingData = InputReader.ReadUInt32()
        };

        Debug.Assert(header.OffsetInFile <= Header.FileSize);

        if (header.Compression != 0)
        {
            Debug.Assert(header.OffsetInFile + header.CompressedSize <= Header.FileSize);
        }
        else
        {
            Debug.Assert(header.CompressedSize == header.UncompressedSize);
            Debug.Assert(header.OffsetInFile + header.UncompressedSize <= Header.FileSize);
        }

        return header;
    }

    public void Seek(RelocatableReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        Stream.Seek((long)reference.Offset, SeekOrigin.Begin);
    }

    public string ReadString() => Reader.ReadString();

    public RelocatableReference ReadReference() => new() { Offset = Reader.ReadUInt64() };

    public StructDefinition ReadStructDefinition() => new();

    private void UncompressStream()
    {
        uint totalUncompressedSize = 0;
        foreach (var section in Sections)
        {
            totalUncompressedSize += section.Header.UncompressedSize;
        }

        byte[] uncompressedMemory = new byte[totalUncompressedSize];
        Stream = new MemoryStream(uncompressedMemory, true);
        Reader = new BinaryReader(Stream, Encoding.UTF8, leaveOpen: true);

        for (int i = 0; i < Sections.Count; i++)
        {
            var section = Sections[i];
            var hdr = section.Header;
            byte[] compressedBlock = new byte[hdr.CompressedSize];

            if (InputStream.Position != hdr.OffsetInFile)
            {
                InputStream.Position = hdr.OffsetInFile;
            }

            int bytesRead = InputStream.Read(compressedBlock, 0, compressedBlock.Length);
            if (bytesRead != compressedBlock.Length)
                throw new EndOfStreamException($"Truncated stream payload encountered during section read index tracking loop: {i}");

            hdr.OffsetInFile = (uint)Stream.Position;
            if (section.Header.Compression == 0)
            {
                Stream.Write(compressedBlock, 0, compressedBlock.Length);
            }
            else if (section.Header.UncompressedSize > 0)
            {
                byte[] decompressedBlock = Granny2Compressor.Decompress((int)hdr.Compression, compressedBlock, (int)hdr.UncompressedSize, (int)hdr.First16bit, (int)hdr.First8bit, (int)hdr.UncompressedSize);
                Stream.Write(decompressedBlock, 0, decompressedBlock.Length);
            }
        }
    }

    private void ReadSectionRelocations(Section section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var header = section.Header;
        if (header.NumRelocations == 0) return;

        if (InputStream.Position != header.RelocationsOffset)
        {
            InputStream.Seek(header.RelocationsOffset, SeekOrigin.Begin);
        }

        if (header.Compression == 4)
        {
            using var rdr = new BinaryReader(InputStream, Encoding.Default, leaveOpen: true);
            uint compressedSize = rdr.ReadUInt32();
            byte[] compressed = rdr.ReadBytes((int)compressedSize);

            byte[] uncompressed = Granny2Compressor.Decompress4(compressed, (int)(header.NumRelocations * 12));
            using var ms = new MemoryStream(uncompressed);
            ReadSectionRelocationsInternal(section, ms);
        }
        else
        {
            ReadSectionRelocationsInternal(section, InputStream);
        }
    }

    private void ReadSectionRelocationsInternal(Section section, Stream relocationsStream)
    {
        using var relocationsReader = new BinaryReader(relocationsStream, Encoding.Default, leaveOpen: true);
        Span<byte> fixupAddressWindow = stackalloc byte[4];

        for (int i = 0; i < section.Header.NumRelocations; i++)
        {
            uint offsetInSection = relocationsReader.ReadUInt32();
            var reference = ReadSectionReferenceUnchecked();

            Stream.Position = section.Header.OffsetInFile + offsetInSection;
            uint fixupAddress = Sections[(int)reference.Section].Header.OffsetInFile + reference.Offset;

            BinaryPrimitives.WriteUInt32LittleEndian(fixupAddressWindow, fixupAddress);
            Stream.Write(fixupAddressWindow);
        }
    }

    private void ReadSectionMixedMarshallingRelocations(Section section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var header = section.Header;
        if (header.NumMixedMarshallingData == 0) return;

        long originalPos = Stream.Position;
        Stream.Seek((long)header.MixedMarshallingDataOffset, SeekOrigin.Begin);

        for (uint i = 0; i < header.NumMixedMarshallingData; i++)
        {
            uint itemOffset = Reader.ReadUInt32();
            uint typeTag = Reader.ReadUInt32();

            long markerPos = Stream.Position;
            Stream.Seek(header.OffsetInFile + itemOffset, SeekOrigin.Begin);

            switch (typeTag)
            {
                case 2:
                    ushort shortValue = Reader.ReadUInt16();
                    Stream.Seek(-2, SeekOrigin.Current);
                    Stream.Write(MemoryMarshal.AsBytes(stackalloc[] { BinaryPrimitives.ReverseEndianness(shortValue) }));
                    break;

                case 4:
                    uint intValue = Reader.ReadUInt32();
                    Stream.Seek(-4, SeekOrigin.Current);
                    Stream.Write(MemoryMarshal.AsBytes(stackalloc[] { BinaryPrimitives.ReverseEndianness(intValue) }));
                    break;
            }

            Stream.Seek(markerPos, SeekOrigin.Begin);
        }

        Stream.Seek(originalPos, SeekOrigin.Begin);
    }

    private SectionReference ReadSectionReferenceUnchecked()
    {
        return new SectionReference
        {
            Section = InputReader.ReadUInt32(),
            Offset = InputReader.ReadUInt32()
        };
    }

    private static void ReadStruct(StructDefinition definition, MemberType type, object targetRoot, object parentContext)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(targetRoot);
        _ = type; _ = parentContext;

        definition.MapType(targetRoot);
    }
}
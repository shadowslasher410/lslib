using System.Diagnostics.CodeAnalysis;

namespace LSLib.Granny.GR2;


public sealed class GR2Writer : IDisposable
{
    public Stream Stream { get; private set; } = Stream.Null;
    public Magic Magic { get; private set; } = new();
    public Header Header { get; private set; } = new();
    public List<Section> Sections { get; } = [];
    public Dictionary<Type, StructDefinition> RegisteredTypes { get; } = [];

    internal BinaryWriter Writer { get; private set; } = null!;

    private readonly Dictionary<object, RelocatableReference> _serializedObjects = [];
    private readonly List<RelocatableReference> _pendingFixups = [];

    public GR2Writer(Stream outputStream)
    {
        Stream = outputStream ?? throw new ArgumentNullException(nameof(outputStream));
        Writer = new BinaryWriter(Stream, Encoding.UTF8, leaveOpen: true);
    }

    public void Dispose()
    {
        Writer?.Dispose();
        Stream?.Dispose();
    }

    public void Write<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>(T root, Magic.Format targetFormat, bool useAlternateSignature, uint fileTag) where T : class
    {
        ArgumentNullException.ThrowIfNull(root);
        WriteInternal(root, typeof(T), targetFormat, useAlternateSignature, fileTag);
    }

    public void Write(object root, Magic.Format targetFormat, bool useAlternateSignature, uint fileTag)
    {
        ArgumentNullException.ThrowIfNull(root);
        WriteInternal(root, root.GetType(), targetFormat, useAlternateSignature, fileTag);
    }

    private void WriteInternal(
        object root,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] Type rootType,
        Magic.Format targetFormat,
        bool useAlternateSignature,
        uint fileTag)
    {
        Magic.SetFormat(targetFormat, useAlternateSignature);

        Header.Version = Header.HeaderVersion;
        Header.Tag = fileTag;

        Sections.Clear();
        for (int i = 0; i < 6; i++)
        {
            Sections.Add(new Section());
        }

        Stream.Seek(0, SeekOrigin.Begin);
        byte[] dummyHeaderBlock = new byte[Magic.MagicSize + Header.Size()];
        Stream.Write(dummyHeaderBlock);

        var rootStructDefinition = LookupStructDefinition(rootType, null);

        Header.RootType = WriteStructDefinition(rootStructDefinition);
        Header.RootNode = WriteStruct(rootStructDefinition, root, SectionType.Main);

        FlushSections();
        WriteRelocationsAndMarshalling();
        WriteHeaderBlock();
    }

    public StructDefinition LookupStructDefinition(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] Type type,
            object? instance)
    {
        ArgumentNullException.ThrowIfNull(type);
        _ = instance;

        ref var structDef = ref CollectionsMarshal.GetValueRefOrAddDefault(RegisteredTypes, type, out var exists);
        if (!exists)
        {
            structDef = new StructDefinition();
            structDef.LoadFromType(type, this);
        }

        return structDef!;
    }

    private SectionReference WriteStructDefinition(StructDefinition def)
    {
        ArgumentNullException.ThrowIfNull(def);

        var sectionRef = new SectionReference(SectionType.StructDefinitions, (uint)Sections[(int)SectionType.StructDefinitions].Header.UncompressedSize);
        var originalPos = Stream.Position;

        long offset = Sections[(int)SectionType.StructDefinitions].Header.OffsetInFile + sectionRef.Offset;
        Stream.Seek(offset, SeekOrigin.Begin);

        Writer.Write(def.Members.Count);
        foreach (var member in def.Members)
        {
            Writer.Write((uint)member.Type);
            WriteStringDirect(member.Name);
            Writer.Write(member.ArraySize);

            for (int i = 0; i < 3; i++)
            {
                Writer.Write(member.Extra[i]);
            }
            Writer.Write(member.Unknown);
        }

        Stream.Seek(originalPos, SeekOrigin.Begin);
        return sectionRef;
    }

    private SectionReference WriteStruct(StructDefinition def, object instance, SectionType preferredSection)
    {
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(instance);

        if (_serializedObjects.TryGetValue(instance, out var existingRef))
        {
            return new SectionReference(preferredSection, (uint)existingRef.Offset);
        }

        var sectionRef = new SectionReference(preferredSection, (uint)Sections[(int)preferredSection].Header.UncompressedSize);
        _serializedObjects.Add(instance, new RelocatableReference { Offset = sectionRef.Offset });

        long originalPos = Stream.Position;
        long targetFileOffset = Sections[(int)preferredSection].Header.OffsetInFile + sectionRef.Offset;
        Stream.Seek(targetFileOffset, SeekOrigin.Begin);

        def.MapType(instance);

        foreach (var member in def.Members)
        {
            if (!member.ShouldSerialize(Header.Version)) continue;

            var fieldInfo = member.LookupFieldInfo(instance);
            if (fieldInfo == null) continue;

            var value = fieldInfo.GetValue(instance);
            if (value == null)
            {
                Writer.Write(Magic.Is32Bit ? 0u : 0ul);
                continue;
            }

            WriteMemberValue(member, value, preferredSection);
        }

        Stream.Seek(originalPos, SeekOrigin.Begin);
        return sectionRef;
    }

    private void WriteMemberValue(MemberDefinition member, object value, SectionType currentSection)
    {
        switch (member.Type)
        {
            case MemberType.Inline:
                if (member.WriteDefinition != null)
                {
                    WriteStruct(member.WriteDefinition, value, currentSection);
                }
                break;

            case MemberType.Reference:
                if (member.WriteDefinition != null)
                {
                    var reference = WriteStruct(member.WriteDefinition, value, currentSection);
                    Writer.Write(Magic.Is32Bit ? reference.Offset : (ulong)reference.Offset);
                }
                break;

            case MemberType.String:
                if (value is string text)
                {
                    var offset = WriteStringDirect(text);
                    Writer.Write(Magic.Is32Bit ? (uint)offset : (ulong)offset);
                }
                break;

            case MemberType.Int8: Writer.Write((sbyte)value); break;
            case MemberType.UInt8: Writer.Write((byte)value); break;
            case MemberType.Int16: Writer.Write((short)value); break;
            case MemberType.UInt16: Writer.Write((ushort)value); break;
            case MemberType.Int32: Writer.Write((int)value); break;
            case MemberType.UInt32: Writer.Write((uint)value); break;
            case MemberType.Real32: Writer.Write((float)value); break;

            default:
                throw new ParsingException($"Unsupported or unhandled data serialization member variable tracking type: {member.Type}");
        }
    }

    private long WriteStringDirect(string text)
    {
        var section = Sections[(int)SectionType.Main];
        long offset = section.Header.UncompressedSize;

        long originalPos = Stream.Position;
        Stream.Seek(section.Header.OffsetInFile + offset, SeekOrigin.Begin);

        byte[] bytes = Encoding.UTF8.GetBytes(text);
        Writer.Write(bytes);
        Writer.Write((byte)0);

        Stream.Seek(originalPos, SeekOrigin.Begin);
        return offset;
    }

    private void FlushSections()
    {
        uint ongoingOffset = (uint)(Magic.MagicSize + Header.Size());

        foreach (var section in Sections)
        {
            var header = section.Header;
            header.OffsetInFile = ongoingOffset;
            header.CompressedSize = header.UncompressedSize;

            ongoingOffset += header.UncompressedSize;

            uint padding = (4 - (ongoingOffset & 3)) & 3;
            ongoingOffset += padding;
        }

        Header.FileSize = ongoingOffset;
        Header.NumSections = (uint)Sections.Count;
        Header.SectionsOffset = Header.Size();
    }

    private void WriteRelocationsAndMarshalling()
    {
        foreach (var fixup in _pendingFixups)
        {
            _ = fixup; 
        }
    }

    private void WriteHeaderBlock()
    {
        Stream.Seek(0, SeekOrigin.Begin);

        Writer.Write(Magic.Signature);
        Writer.Write(Magic.HeadersSize);
        Writer.Write(Magic.HeaderFormat);
        Writer.Write(Magic.Reserved1);
        Writer.Write(Magic.Reserved2);

        Writer.Write(Header.Version);
        Writer.Write(Header.FileSize);
        Writer.Write(Header.Crc);
        Writer.Write(Header.SectionsOffset);
        Writer.Write(Header.NumSections);

        Writer.Write(Header.RootType.Section);
        Writer.Write(Header.RootType.Offset);
        Writer.Write(Header.RootNode.Section);
        Writer.Write(Header.RootNode.Offset);

        Writer.Write(Header.Tag);

        for (int i = 0; i < (int)Header.ExtraTagCount; i++)
        {
            uint extraTagValue = i < Header.ExtraTags.Length ? Header.ExtraTags[i] : 0u;
            Writer.Write(extraTagValue);
        }

        if (Header.Version >= 7)
        {
            Writer.Write(Header.StringTableCrc);
            Writer.Write(Header.Reserved1);
            Writer.Write(Header.Reserved2);
            Writer.Write(Header.Reserved3);
        }

        Header.Crc = Header.CalculateCRC(Stream);
        Stream.Seek(Magic.MagicSize + 8, SeekOrigin.Begin);
        Writer.Write(Header.Crc);
    }
}
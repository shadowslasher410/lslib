using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;
using System.Reflection;


namespace LSLib.Granny.GR2;

#region Anchor Hooks & Abstractions

public interface IGR2StreamContext
{
    Stream Stream { get; }
    Magic Magic { get; }
    Dictionary<StructReference, StructDefinition> Types { get; }
    void Seek(RelocatableReference reference);
    string ReadString();
    RelocatableReference ReadReference();
    StructDefinition ReadStructDefinition();
}

public interface IGR2WriterContext
{
    Stream Stream { get; }
    Magic Magic { get; }
    StructDefinition LookupStructDefinition(Type type, object? context);
    void WriteReference(RelocatableReference reference);
    void String(string value);
}

public enum SerializationKind
{
    None,
    Builtin,
    UserRaw,
    UserMember,
    UserElement
}

public enum SectionType : uint
{
    Main = 0,
    TrackGroup = 1,
    Skeleton = 2,
    Mesh = 3,
    StructDefinitions = 4,
    FirstVertexData = 5,
    Invalid = 0xffffffff
}

public enum MemberType : uint
{
    None = 0,
    Inline = 1,
    Reference = 2,
    ReferenceToArray = 3,
    ArrayOfReferences = 4,
    VariantReference = 5,
    ReferenceToVariantArray = 7,
    String = 8,
    Transform = 9,
    Real32 = 10,
    Int8 = 11,
    UInt8 = 12,
    BinormalInt8 = 13,
    NormalUInt8 = 14,
    Int16 = 15,
    UInt16 = 16,
    BinormalInt16 = 17,
    NormalUInt16 = 18,
    Int32 = 19,
    UInt32 = 20,
    Real16 = 21,
    EmptyReference = 22,
    Max = EmptyReference,
    Invalid = 0xffffffff
}

#endregion

#region Text Resource Data Models

public sealed class GrannyString
{
    public string Value { get; set; } = string.Empty;

    public GrannyString() { }
    public GrannyString(string s) => Value = s ?? string.Empty;
    public override string ToString() => Value;
}

#endregion

#region Runtime Translation Enum Flags & Magic

public sealed class Magic
{
    private static ReadOnlySpan<byte> LittleEndian32Magic => [0x29, 0xDE, 0x6C, 0xC0, 0xBA, 0xA4, 0x53, 0x2B, 0x25, 0xF5, 0xB7, 0xA5, 0xF6, 0x66, 0xE2, 0xEE];
    private static ReadOnlySpan<byte> LittleEndian32Magic2 => [0x29, 0x75, 0x31, 0x82, 0xBA, 0x02, 0x11, 0x77, 0x25, 0x3A, 0x60, 0x2F, 0xF6, 0x6A, 0x8C, 0x2E];
    private static ReadOnlySpan<byte> LittleEndian32MagicV6 => [0xB8, 0x67, 0xB0, 0xCA, 0xF8, 0x6D, 0xB1, 0x0F, 0x84, 0x72, 0x8C, 0x7E, 0x5E, 0x19, 0x00, 0x1E];
    private static ReadOnlySpan<byte> BigEndian32Magic => [0x0E, 0x11, 0x95, 0xB5, 0x6A, 0xA5, 0xB5, 0x4B, 0xEB, 0x28, 0x28, 0x50, 0x25, 0x78, 0xB3, 0x04];
    private static ReadOnlySpan<byte> BigEndian32Magic2 => [0x0E, 0x74, 0xA2, 0x0A, 0x6A, 0xEB, 0xEB, 0x64, 0xEB, 0x4E, 0x1E, 0xAB, 0x25, 0x91, 0xDB, 0x8F];
    private static ReadOnlySpan<byte> LittleEndian64Magic => [0xE5, 0x9B, 0x49, 0x5E, 0x6F, 0x63, 0x1F, 0x14, 0x1E, 0x13, 0xEB, 0xA9, 0x90, 0xBE, 0xED, 0xC4];
    private static ReadOnlySpan<byte> LittleEndian64Magic2 => [0xE5, 0x2F, 0x4A, 0xE1, 0x6F, 0xC2, 0x8A, 0xEE, 0x1E, 0xD2, 0xB4, 0x4C, 0x90, 0xD7, 0x55, 0xAF];
    private static ReadOnlySpan<byte> BigEndian64Magic => [0x31, 0x95, 0xD4, 0xE3, 0x20, 0xDC, 0x4F, 0x62, 0xCC, 0x36, 0xD0, 0x3A, 0xB1, 0x82, 0xFF, 0x89];
    private static ReadOnlySpan<byte> BigEndian64Magic2 => [0x31, 0xC2, 0x4E, 0x7C, 0x20, 0x40, 0xA3, 0x25, 0xCC, 0xE1, 0xC2, 0x7A, 0xB1, 0x32, 0x49, 0xF3];

    public const uint MagicSize = 0x20;

    public enum Format
    {
        LittleEndian32,
        BigEndian32,
        LittleEndian64,
        BigEndian64
    }

    public bool Is32Bit => FormatEntry == Format.LittleEndian32 || FormatEntry == Format.BigEndian32;
    public bool Is64Bit => FormatEntry == Format.LittleEndian64 || FormatEntry == Format.BigEndian64;
    public bool IsLittleEndian => FormatEntry == Format.LittleEndian32 || FormatEntry == Format.LittleEndian64;

    public byte[] Signature { get; set; } = [];
    public uint HeadersSize { get; set; }
    public uint HeaderFormat { get; set; }
    public uint Reserved1 { get; set; }
    public uint Reserved2 { get; set; }
    public Format FormatEntry { get; set; }

    public static Format FormatFromSignature(ReadOnlySpan<byte> sig)
    {
        if (sig.SequenceEqual(LittleEndian32Magic) || sig.SequenceEqual(LittleEndian32Magic2) || sig.SequenceEqual(LittleEndian32MagicV6))
            return Format.LittleEndian32;
        if (sig.SequenceEqual(BigEndian32Magic) || sig.SequenceEqual(BigEndian32Magic2))
            return Format.BigEndian32;
        if (sig.SequenceEqual(LittleEndian64Magic) || sig.SequenceEqual(LittleEndian64Magic2))
            return Format.LittleEndian64;
        if (sig.SequenceEqual(BigEndian64Magic) || sig.SequenceEqual(BigEndian64Magic2))
            return Format.BigEndian64;

        throw new InvalidDataException("Incorrect signature block passed into file format resolver.");
    }

    public static byte[] SignatureFromFormat(Format format) => format switch
    {
        Format.LittleEndian32 => LittleEndian32Magic.ToArray(),
        Format.LittleEndian64 => LittleEndian64Magic.ToArray(),
        Format.BigEndian32 => BigEndian32Magic.ToArray(),
        Format.BigEndian64 => BigEndian64Magic.ToArray(),
        _ => throw new ArgumentOutOfRangeException(nameof(format), "Invalid magic platform token specified.")
    };

    public void SetFormat(Format format, bool alternateSignature)
    {
        FormatEntry = format;
        var sourceSpan = alternateSignature ? format switch
        {
            Format.LittleEndian32 => LittleEndian32Magic2,
            Format.LittleEndian64 => LittleEndian64Magic2,
            Format.BigEndian32 => BigEndian32Magic2,
            Format.BigEndian64 => BigEndian64Magic2,
            _ => throw new InvalidDataException()
        } : format switch
        {
            Format.LittleEndian32 => LittleEndian32Magic,
            Format.LittleEndian64 => LittleEndian64Magic,
            Format.BigEndian32 => BigEndian32Magic,
            Format.BigEndian64 => BigEndian64Magic,
            _ => throw new InvalidDataException()
        };
        Signature = sourceSpan.ToArray();
    }
}

#endregion

#region Granny Header & Sections Management

public sealed class Header
{
    public const uint DefaultTag = 0x80000037;
    public const uint Tag_DOS = 0x80000037;
    public const uint Tag_DOSEE = 0x80000039;
    public const uint Tag_DOS2DE = 0xE57F0039;

    public const uint HeaderVersion = 7;
    public const uint HeaderSize_V6 = 0x38;
    public const uint HeaderSize_V7 = 0x48;
    public const uint ExtraTagCount = 4;

    public uint Version { get; set; }
    public uint FileSize { get; set; }
    public uint Crc { get; set; }
    public uint SectionsOffset { get; set; }
    public uint NumSections { get; set; }
    public SectionReference RootType { get; set; } = new();
    public SectionReference RootNode { get; set; } = new();
    public uint Tag { get; set; }
    public uint[] ExtraTags { get; set; } = [];
    public uint StringTableCrc { get; set; }
    public uint Reserved1 { get; set; }
    public uint Reserved2 { get; set; }
    public uint Reserved3 { get; set; }

    public uint Size() => Version switch
    {
        6 => HeaderSize_V6,
        7 => HeaderSize_V7,
        _ => throw new InvalidDataException("Unknown structural configuration format tracked inside section headers.")
    };

    public uint CalculateCRC(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var originalPos = stream.Position;
        var totalHeaderSize = Size() + Magic.MagicSize;
        stream.Seek(totalHeaderSize, SeekOrigin.Begin);

        int bufferSize = (int)(FileSize - totalHeaderSize);
        if (bufferSize <= 0) return 0;

        byte[] pooledArray = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            var targetSpan = pooledArray.AsSpan(0, bufferSize);
            int bytesRead = stream.Read(targetSpan);
            if (bytesRead != bufferSize)
                throw new EndOfStreamException("Truncated stream allocation tracked during header checksum verifications.");

            uint resultCrc = Crc32.HashToUInt32(targetSpan);
            stream.Seek(originalPos, SeekOrigin.Begin);
            return resultCrc;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pooledArray);
        }
    }
}

public sealed class SectionHeader
{
    public uint Compression { get; set; }
    public uint OffsetInFile { get; set; }
    public uint CompressedSize { get; set; }
    public uint UncompressedSize { get; set; }
    public uint Alignment { get; set; }
    public uint First16bit { get; set; }
    public uint First8bit { get; set; }
    public uint RelocationsOffset { get; set; }
    public uint NumRelocations { get; set; }
    public uint MixedMarshallingDataOffset { get; set; }
    public uint NumMixedMarshallingData { get; set; }
}

public sealed class Section
{
    public SectionHeader Header { get; set; } = new();
}

#endregion

#region Memory Relocation Tracking System

public sealed class SectionReference : IEquatable<SectionReference>
{
    public uint Section { get; set; } = (uint)SectionType.Invalid;
    public uint Offset { get; set; } = 0;

    public bool IsValid => Section != (uint)SectionType.Invalid;

    public SectionReference() { }
    public SectionReference(SectionType section, uint offset)
    {
        Section = (uint)section;
        Offset = offset;
    }

    public override bool Equals(object? obj) => obj is SectionReference other && Equals(other);
    public bool Equals(SectionReference? other) => other is not null && other.Section == Section && other.Offset == Offset;
    public override int GetHashCode() => HashCode.Combine(Section, Offset);
}

public class RelocatableReference : IEquatable<RelocatableReference>
{
    public ulong Offset { get; set; } = 0;
    public bool IsValid => Offset != 0;

    public override bool Equals(object? obj) => obj is RelocatableReference other && Equals(other);
    public bool Equals(RelocatableReference? other) => other is not null && other.Offset == Offset;
    public override int GetHashCode() => Offset.GetHashCode();
}

public sealed class StructReference : RelocatableReference
{
    public StructDefinition? Type { get; set; }

    public StructDefinition Resolve(IGR2StreamContext gr2)
    {
        ArgumentNullException.ThrowIfNull(gr2);
        Debug.Assert(IsValid);

        if (Type == null)
        {
            if (gr2.Types.TryGetValue(this, out var discoveredType))
            {
                Type = discoveredType;
            }
        }

        if (Type == null)
        {
            var originalPos = gr2.Stream.Position;
            gr2.Seek(this);
            Type = gr2.ReadStructDefinition();
            gr2.Stream.Seek(originalPos, SeekOrigin.Begin);
            gr2.Types[this] = Type;
        }
        return Type;
    }

    public void Read(IGR2StreamContext gr2) => Resolve(gr2);

    [UnconditionalSuppressMessage("Trimming", "IL2077:RequiresUnreferencedCode", Justification = "Safely evaluated via compile-time layout models.")]
    public void PreSave(IGR2WriterContext writer, Type runtimeType) => Type = writer.LookupStructDefinition(runtimeType, null);
    public void Save(IGR2WriterContext writer) => writer.WriteReference(this);
}

public sealed class StringReference : RelocatableReference
{
    public string Value { get; set; } = string.Empty;

    public string Resolve(IGR2StreamContext gr2)
    {
        ArgumentNullException.ThrowIfNull(gr2);
        Debug.Assert(IsValid);

        if (string.IsNullOrEmpty(Value))
        {
            var originalPos = gr2.Stream.Position;
            gr2.Seek(this);
            Value = gr2.ReadString();
            gr2.Stream.Seek(originalPos, SeekOrigin.Begin);
        }
        return Value;
    }

    public void Read(IGR2StreamContext gr2) => Resolve(gr2);
    public void PreSave(IGR2WriterContext _, string value) => Value = value ?? string.Empty;
    public void Save(IGR2WriterContext writer) => writer.String(Value);
}

public class ArrayReference : RelocatableReference
{
    public uint Size { get; set; }
}

public sealed class ArrayIndicesReference : ArrayReference
{
    public List<RelocatableReference> Items { get; set; } = [];

    public List<RelocatableReference> Resolve(IGR2StreamContext gr2)
    {
        ArgumentNullException.ThrowIfNull(gr2);
        Debug.Assert(IsValid);

        if (Items.Count == 0 && Size > 0)
        {
            var originalPos = gr2.Stream.Position;
            gr2.Seek(this);
            Items = new List<RelocatableReference>((int)Size);
            for (int i = 0; i < Size; i++)
            {
                Items.Add(gr2.ReadReference());
            }
            gr2.Stream.Seek(originalPos, SeekOrigin.Begin);
        }
        return Items;
    }

    public void Read(IGR2StreamContext gr2) => Resolve(gr2);
    public void PreSave(IGR2WriterContext _, List<RelocatableReference> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items.Clear();
        Items.AddRange(items);
        Size = (uint)Items.Count;
    }

    public void Save(IGR2WriterContext writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteReference(this);

        var originalPos = writer.Stream.Position;
        writer.Stream.Seek((long)Offset, SeekOrigin.Begin);
        foreach (var item in Items)
        {
            writer.WriteReference(item);
        }
        writer.Stream.Seek(originalPos, SeekOrigin.Begin);
    }
}

#endregion

#region Metadata Field Member Definitions

public sealed class MemberDefinition
{
    public const uint ExtraTagCount = 3;

    public MemberType Type { get; set; } = MemberType.Invalid;
    public string Name { get; set; } = string.Empty;
    public string GrannyName { get; set; } = string.Empty;
    public StructReference Definition { get; set; } = new();
    public uint ArraySize { get; set; }
    public uint[] Extra { get; set; } = new uint[ExtraTagCount];
    public uint Unknown { get; set; }

    public bool HasCachedField { get; set; } = false;
    public FieldInfo? CachedField { get; set; }

    public INodeSerializer? Serializer { get; set; }
    public IVariantTypeSelector? TypeSelector { get; set; }
    public ISectionSelector? SectionSelector { get; set; }
    public SerializationKind SerializationKind { get; set; } = SerializationKind.Builtin;

    public StructDefinition? WriteDefinition { get; set; }
    public SectionType PreferredSection { get; set; } = SectionType.Invalid;
    public bool DataArea { get; set; } = false;
    public Type? Prototype { get; set; }
    public uint MinVersion { get; set; } = 0;
    public uint MaxVersion { get; set; } = 0;

    public bool IsValid => Type != MemberType.None;
    public bool IsScalar => Type > MemberType.ReferenceToVariantArray;

    public uint ElementSize(GR2Reader gr2)
    {
        ArgumentNullException.ThrowIfNull(gr2);
        return Type switch
        {
            MemberType.Inline => Definition.Resolve(gr2).Size(gr2),
            MemberType.Int8 or MemberType.BinormalInt8 or MemberType.UInt8 or MemberType.NormalUInt8 => 1,
            MemberType.Int16 or MemberType.BinormalInt16 or MemberType.UInt16 or MemberType.NormalUInt16 or MemberType.Real16 => 2,
            MemberType.Reference or MemberType.String => gr2.Magic.Is32Bit ? 4u : 8u,
            MemberType.Real32 or MemberType.Int32 or MemberType.UInt32 => 4,
            MemberType.VariantReference => gr2.Magic.Is32Bit ? 8u : 16u,
            MemberType.ArrayOfReferences or MemberType.ReferenceToArray => gr2.Magic.Is32Bit ? 8u : 12u,
            MemberType.ReferenceToVariantArray => gr2.Magic.Is32Bit ? 12u : 20u,
            MemberType.Transform => 17u * 4u,
            _ => throw new ParsingException($"Unhandled serialization type tag processed in engine calculations: {Type}")
        };
    }

    public uint TotalSize(GR2Reader gr2) => (ArraySize == 0 ? 1 : ArraySize) * ElementSize(gr2);

    public uint ElementMarshallingSize() => Type switch
    {
        MemberType.Inline or MemberType.Reference or MemberType.VariantReference or MemberType.EmptyReference => 0,
        MemberType.Int8 or MemberType.BinormalInt8 or MemberType.UInt8 or MemberType.NormalUInt8 => 1,
        MemberType.Int16 or MemberType.BinormalInt16 or MemberType.UInt16 or MemberType.NormalUInt16 or MemberType.Real16 => 2,
        MemberType.Transform or MemberType.Real32 or MemberType.Int32 or MemberType.UInt32 or MemberType.ReferenceToArray or MemberType.ArrayOfReferences or MemberType.ReferenceToVariantArray => 4,
        _ => throw new ParsingException($"Unhandled marshalling serialization member variant conversion track: {Type}")
    };

    public uint TotalMarshallingSize() => (ArraySize == 0 ? 1 : ArraySize) * ElementMarshallingSize();
    public bool ShouldSerialize(uint version) => (MinVersion == 0 || MinVersion <= version) && (MaxVersion == 0 || MaxVersion >= version);

    [UnconditionalSuppressMessage("Trimming", "IL2072:RequiresUnreferencedCode", Justification = "Reflection types are tracked explicitly at compile time.")]
    private void LoadAttributes(FieldInfo info, GR2Writer? writer)
    {
        var attribute = info.GetCustomAttribute<SerializationAttribute>(true);
        if (attribute != null)
        {
            if (attribute.Section != SectionType.Invalid) PreferredSection = attribute.Section;
            DataArea = attribute.DataArea;
            if (attribute.Type != MemberType.Invalid) Type = attribute.Type;

            if (attribute.TypeSelector != null) TypeSelector = CustomActivationAdapter.CreateTypeSelector(attribute.TypeSelector);
            if (attribute.SectionSelector != null) SectionSelector = CustomActivationAdapter.CreateSectionSelector(attribute.SectionSelector);
            if (attribute.Serializer != null) Serializer = CustomActivationAdapter.CreateSerializer(attribute.Serializer);

            if (writer != null && attribute.Prototype != null) WriteDefinition = writer.LookupStructDefinition(attribute.Prototype, attribute.Prototype);
            if (!string.IsNullOrEmpty(attribute.Name)) GrannyName = attribute.Name;

            Prototype = attribute.Prototype;
            SerializationKind = attribute.Kind;
            ArraySize = attribute.ArraySize;
            MinVersion = attribute.MinVersion;
            MaxVersion = attribute.MaxVersion;
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075:RequiresUnreferencedCode",
        Justification = "Field info metadata maps are fully preserved via static configuration bindings elsewhere in model matrices.")]
    public FieldInfo? LookupFieldInfo(object instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (HasCachedField) return CachedField;

        var field = instance.GetType().GetField(Name);
        if (field != null) AssignFieldInfo(field);
        return field;
    }

    public void AssignFieldInfo(FieldInfo field)
    {
        ArgumentNullException.ThrowIfNull(field);
        Debug.Assert(!HasCachedField);

        CachedField = field;
        HasCachedField = true;
        LoadAttributes(field, null);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2062:RequiresUnreferencedCode", Justification = "Struct fields are fully preserved by core mapping descriptors.")]
    [UnconditionalSuppressMessage("Trimming", "IL2067:RequiresUnreferencedCode", Justification = "Struct fields are fully preserved by core mapping descriptors.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072:RequiresUnreferencedCode", Justification = "Struct fields are fully preserved by core mapping descriptors.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075:RequiresUnreferencedCode", Justification = "Interface lookups are fully preserved by solution models.")]
    [UnconditionalSuppressMessage("Trimming", "IL2077:RequiresUnreferencedCode", Justification = "Struct fields are fully preserved by core mapping descriptors.")]
    public static MemberDefinition CreateFromFieldInfo(FieldInfo info, GR2Writer? writer)
    {
        ArgumentNullException.ThrowIfNull(info);
        var member = new MemberDefinition();
        var type = info.FieldType;
        member.Name = info.Name;
        member.GrannyName = info.Name;
        member.Extra = new uint[ExtraTagCount];
        member.CachedField = info;
        member.HasCachedField = true;

        member.LoadAttributes(info, writer);

        if (type.IsArray && member.SerializationKind != SerializationKind.None)
        {
            if (member.ArraySize == 0) throw new InvalidOperationException("Fixed size arrays map elements declarations require set sizing rules.");
            var elemType = type.GetElementType();
            if (elemType != null) type = elemType;
        }

        if (member.Type == MemberType.Invalid)
        {
            if (type == typeof(sbyte)) member.Type = MemberType.Int8;
            else if (type == typeof(byte)) member.Type = MemberType.UInt8;
            else if (type == typeof(short)) member.Type = MemberType.Int16;
            else if (type == typeof(ushort)) member.Type = MemberType.UInt16;
            else if (type == typeof(int)) member.Type = MemberType.Int32;
            else if (type == typeof(uint)) member.Type = MemberType.UInt32;
            else if (type == typeof(OpenTK.Mathematics.Half)) member.Type = MemberType.Real16;
            else if (type == typeof(float)) member.Type = MemberType.Real32;
            else if (type == typeof(string)) member.Type = MemberType.String;
            else if (type == typeof(Transform)) member.Type = MemberType.Transform;
            else if (type == typeof(object) || type.IsAbstract || type.IsInterface) member.Type = MemberType.VariantReference;
            else if (type.GetInterfaces().Contains(typeof(IList<object>))) member.Type = MemberType.ReferenceToVariantArray;
            else if (type.GetInterfaces().Contains(typeof(System.Collections.IList))) member.Type = MemberType.ReferenceToArray;
            else member.Type = MemberType.Reference;
        }

        if (member.SerializationKind != SerializationKind.None && member.WriteDefinition == null && writer != null)
        {
            if (member.Type == MemberType.Inline || member.Type == MemberType.Reference)
            {
                member.WriteDefinition = writer.LookupStructDefinition(type, null);
            }
            else if (member.Type == MemberType.ReferenceToArray || member.Type == MemberType.ArrayOfReferences)
            {
                var genericArgs = type.GetGenericArguments();
                if (genericArgs.Length > 0) member.WriteDefinition = writer.LookupStructDefinition(genericArgs[0], null);
            }
        }
        return member;
    }
}

#endregion

#region Struct Reflection Record Matrices

public sealed partial class StructDefinition
{
    public Type? Type { get; set; }
    public List<MemberDefinition> Members { get; set; } = [];
    public bool MixedMarshal { get; set; } = false;

    public uint Size(GR2Reader gr2)
    {
        uint totalSize = 0;
        foreach (var member in Members) totalSize += member.TotalSize(gr2);
        return totalSize;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075:RequiresUnreferencedCode",
        Justification = "The fields of the instantiated object layout are preserved via explicit serialization schema targets.")]
    public void MapType(object instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (Type == null)
        {
            Type = instance.GetType();
            var fields = Type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            foreach (var field in fields)
            {
                var name = field.Name;
                var serialization = field.GetCustomAttribute<SerializationAttribute>(true);
                if (serialization != null && !string.IsNullOrEmpty(serialization.Name)) name = serialization.Name;

                foreach (var member in Members)
                {
                    if (string.Equals(member.Name, name, StringComparison.Ordinal)) member.AssignFieldInfo(field);
                }
            }
        }
        Debug.Assert(Type == instance.GetType());
    }

    public void LoadFromType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] Type type, GR2Writer writer)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(writer);
        Type = type;

        var serialization = type.GetCustomAttribute<StructSerializationAttribute>(true);
        if (serialization != null) MixedMarshal = serialization.MixedMarshal;

        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
        foreach (var field in fields)
        {
            var member = MemberDefinition.CreateFromFieldInfo(field, writer);
            if (member.SerializationKind != SerializationKind.None) Members.Add(member);
        }
    }
}

#endregion

#region Data Trimming Struct Activators

internal static class CustomActivationAdapter
{
    [UnconditionalSuppressMessage("Trimming", "IL2067:RequiresUnreferencedCode", Justification = "Selectors are explicitly preserved by mapping attributes.")]
    public static IVariantTypeSelector? CreateTypeSelector(Type t) => Activator.CreateInstance(t) as IVariantTypeSelector;

    [UnconditionalSuppressMessage("Trimming", "IL2067:RequiresUnreferencedCode", Justification = "Selectors are explicitly preserved by mapping attributes.")]
    public static ISectionSelector? CreateSectionSelector(Type t) => Activator.CreateInstance(t) as ISectionSelector;

    [UnconditionalSuppressMessage("Trimming", "IL2067:RequiresUnreferencedCode", Justification = "Serializers are explicitly preserved by mapping attributes.")]
    public static INodeSerializer? CreateSerializer(Type t) => Activator.CreateInstance(t) as INodeSerializer;
}

[AttributeUsage(AttributeTargets.Field)]
public sealed class SerializationAttribute : Attribute
{
    public SectionType Section { get; set; } = SectionType.Invalid;
    public bool DataArea { get; set; }
    public MemberType Type { get; set; } = MemberType.Invalid;
    public Type? TypeSelector { get; set; }
    public Type? SectionSelector { get; set; }
    public Type? Serializer { get; set; }
    public Type? Prototype { get; set; }
    public string? Name { get; set; }
    public SerializationKind Kind { get; set; } = SerializationKind.Builtin;
    public uint ArraySize { get; set; }
    public uint MinVersion { get; set; } = 0;
    public uint MaxVersion { get; set; } = 0;
    public bool MixedMarshal { get; set; } = false;
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class StructSerializationAttribute : Attribute
{
    public bool MixedMarshal { get; set; } = false;
    public Type? TypeSelector { get; set; }
}

#endregion
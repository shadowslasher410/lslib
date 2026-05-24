using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace LSLib.Granny.GR2;

public interface INodeSerializer
{
    object Read(GR2Reader gr2, StructDefinition definition, MemberDefinition member, uint arraySize, object parent);
    void Write(GR2Writer writer, object section, MemberDefinition member, object obj);
}

public interface IVariantTypeSelector
{
    Type? SelectType(MemberDefinition member, object node);
    Type SelectType(MemberDefinition member, StructDefinition defn, object parent);
}

public interface ISectionSelector
{
    SectionType SelectSection(MemberDefinition member, Type type, object obj);
}

public sealed class WritableSection
{
    public BinaryWriter Writer { get; set; } = null!;
}

public sealed class Transform
{
    public uint Flags { get; set; } = 0;
    public OpenTK.Mathematics.Vector3 Translation { get; set; } = OpenTK.Mathematics.Vector3.Zero;
    public OpenTK.Mathematics.Quaternion Rotation { get; set; } = OpenTK.Mathematics.Quaternion.Identity;
    public OpenTK.Mathematics.Matrix3 ScaleShear { get; set; } = OpenTK.Mathematics.Matrix3.Identity;
}

public static class Helpers
{
    private static readonly Dictionary<Type, ObjectCtor> CachedConstructors = [];
    private static readonly Dictionary<Type, ArrayCtor> CachedArrayConstructors = [];

    public delegate object ObjectCtor();
    public delegate object ArrayCtor(int size);

    public static ObjectCtor GetConstructor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        ref var ctor = ref CollectionsMarshal.GetValueRefOrAddDefault(CachedConstructors, type, out var exists);
        if (!exists)
        {
            ctor = () => RuntimeHelpers.GetUninitializedObject(type);
        }

        return ctor!;
    }

    public static object CreateInstance(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] Type type)
    {
        ObjectCtor ctor = GetConstructor(type);
        return ctor();
    }

    public static object CreateArrayInstance([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type, int size)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentOutOfRangeException.ThrowIfNegative(size);

        ref var ctor = ref CollectionsMarshal.GetValueRefOrAddDefault(CachedArrayConstructors, type, out var exists);
        if (!exists)
        {
            ctor = (s) => Array.CreateInstance(type.IsArray ? type.GetElementType() ?? typeof(object) : type, s);
        }

        return ctor!(size);
    }
}

public sealed class UInt8ListSerializer : INodeSerializer
{
    private static List<byte> ReadStatic(GR2Reader gr2, uint arraySize)
    {
        var controls = new List<byte>((int)arraySize);
        for (int i = 0; i < arraySize; i++)
        {
            controls.Add(gr2.Reader.ReadByte());
        }
        return controls;
    }

    public object Read(GR2Reader gr2, StructDefinition definition, MemberDefinition member, uint arraySize, object parent)
    {
        ArgumentNullException.ThrowIfNull(gr2);
        _ = definition; _ = member; _ = parent;
        return ReadStatic(gr2, arraySize);
    }

    public void Write(GR2Writer writer, object section, MemberDefinition member, object obj)
    {
        _ = writer; _ = member;
        if (obj is not List<byte> items)
        {
            throw new ArgumentException("Provided input type reference violates serializer unboxing schemas matching List<Byte>.", nameof(obj));
        }

        if (section is WritableSection writableSection)
        {
            for (int i = 0; i < items.Count; i++)
            {
                writableSection.Writer.Write(items[i]);
            }
        }
    }
}

public sealed class UInt16ListSerializer : INodeSerializer
{
    private static List<ushort> ReadStatic(GR2Reader gr2, uint arraySize)
    {
        var controls = new List<ushort>((int)arraySize);
        for (int i = 0; i < arraySize; i++)
        {
            controls.Add(gr2.Reader.ReadUInt16());
        }
        return controls;
    }

    public object Read(GR2Reader gr2, StructDefinition definition, MemberDefinition member, uint arraySize, object parent)
    {
        ArgumentNullException.ThrowIfNull(gr2);
        _ = definition; _ = member; _ = parent;
        return ReadStatic(gr2, arraySize);
    }

    public void Write(GR2Writer writer, object section, MemberDefinition member, object obj)
    {
        _ = writer; _ = member;
        if (obj is not List<ushort> items)
        {
            throw new ArgumentException("Provided input type reference violates serializer unboxing schemas matching List<UInt16>.", nameof(obj));
        }

        if (section is WritableSection writableSection)
        {
            for (int i = 0; i < items.Count; i++)
            {
                writableSection.Writer.Write(items[i]);
            }
        }
    }
}

public sealed class Int16ListSerializer : INodeSerializer
{
    private static List<short> ReadStatic(GR2Reader gr2, uint arraySize)
    {
        var controls = new List<short>((int)arraySize);
        for (int i = 0; i < arraySize; i++)
        {
            controls.Add(gr2.Reader.ReadInt16());
        }
        return controls;
    }

    public object Read(GR2Reader gr2, StructDefinition definition, MemberDefinition member, uint arraySize, object parent)
    {
        ArgumentNullException.ThrowIfNull(gr2);
        _ = definition; _ = member; _ = parent;
        return ReadStatic(gr2, arraySize);
    }

    public void Write(GR2Writer writer, object section, MemberDefinition member, object obj)
    {
        _ = writer; _ = member;
        if (obj is not List<short> items)
        {
            throw new ArgumentException("Provided input type reference violates serializer unboxing schemas matching List<Int16>.", nameof(obj));
        }

        if (section is WritableSection writableSection)
        {
            for (int i = 0; i < items.Count; i++)
            {
                writableSection.Writer.Write(items[i]);
            }
        }
    }
}

public sealed class UInt32ListSerializer : INodeSerializer
{
    private static List<uint> ReadStatic(GR2Reader gr2, uint arraySize)
    {
        var controls = new List<uint>((int)arraySize);
        for (int i = 0; i < arraySize; i++)
        {
            controls.Add(gr2.Reader.ReadUInt32());
        }
        return controls;
    }

    public object Read(GR2Reader gr2, StructDefinition definition, MemberDefinition member, uint arraySize, object parent)
    {
        ArgumentNullException.ThrowIfNull(gr2);
        _ = definition; _ = member; _ = parent;
        return ReadStatic(gr2, arraySize);
    }

    public void Write(GR2Writer writer, object section, MemberDefinition member, object obj)
    {
        _ = writer; _ = member;
        if (obj is not List<uint> items)
        {
            throw new ArgumentException("Provided input type reference violates serializer unboxing schemas matching List<UInt32>.", nameof(obj));
        }

        if (section is WritableSection writableSection)
        {
            for (int i = 0; i < items.Count; i++)
            {
                writableSection.Writer.Write(items[i]);
            }
        }
    }
}

public sealed class Int32ListSerializer : INodeSerializer
{
    private static List<int> ReadStatic(GR2Reader gr2, uint arraySize)
    {
        var controls = new List<int>((int)arraySize);
        for (int i = 0; i < arraySize; i++)
        {
            controls.Add(gr2.Reader.ReadInt32());
        }
        return controls;
    }

    public object Read(GR2Reader gr2, StructDefinition definition, MemberDefinition member, uint arraySize, object parent)
    {
        ArgumentNullException.ThrowIfNull(gr2);
        _ = definition; _ = member; _ = parent;
        return ReadStatic(gr2, arraySize);
    }

    public void Write(GR2Writer writer, object section, MemberDefinition member, object obj)
    {
        _ = writer; _ = member;
        if (obj is not List<int> items)
        {
            throw new ArgumentException("Provided input type reference violates serializer unboxing schemas matching List<Int32>.", nameof(obj));
        }

        if (section is WritableSection writableSection)
        {
            for (int i = 0; i < items.Count; i++)
            {
                writableSection.Writer.Write(items[i]);
            }
        }
    }
}

public sealed class SingleListSerializer : INodeSerializer
{
    private static List<float> ReadStatic(GR2Reader gr2, uint arraySize)
    {
        var controls = new List<float>((int)arraySize);
        for (int i = 0; i < arraySize; i++)
        {
            controls.Add(gr2.Reader.ReadSingle());
        }
        return controls;
    }

    public object Read(GR2Reader gr2, StructDefinition definition, MemberDefinition member, uint arraySize, object parent)
    {
        ArgumentNullException.ThrowIfNull(gr2);
        _ = definition; _ = member; _ = parent;
        return ReadStatic(gr2, arraySize);
    }

    public void Write(GR2Writer writer, object section, MemberDefinition member, object obj)
    {
        _ = writer; _ = member;
        if (obj is not List<float> items)
        {
            throw new ArgumentException("Provided input type reference violates serializer unboxing schemas matching List<Single>.", nameof(obj));
        }

        if (section is WritableSection writableSection)
        {
            for (int i = 0; i < items.Count; i++)
            {
                writableSection.Writer.Write(items[i]);
            }
        }
    }
}
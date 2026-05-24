using System.Security.Cryptography;

namespace LSLib.Granny;

public static class ColladaUtils
{
    public static sourceTechnique_common MakeAccessor(string type, string[] components, int stride, int elements, string arrayId)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(arrayId);

        var sourceTechnique = new sourceTechnique_common();
        var accessor = new accessor();
        var accessorParams = new List<param>(components.Length);
        ReadOnlySpan<string> componentsSpan = components.AsSpan();

        for (int i = 0; i < componentsSpan.Length; i++)
        {
            var component = componentsSpan[i];
            var p = new param();
            if (!string.IsNullOrEmpty(component))
            {
                p.name = component;
            }
            p.type = type;
            accessorParams.Add(p);
        }

        accessor.param = [.. accessorParams];
        accessor.source = $"#{arrayId}";
        accessor.stride = (ulong)(components.Length * stride);
        accessor.offset = 0;
        accessor.count = (ulong)(elements / stride);
        sourceTechnique.accessor = accessor;
        return sourceTechnique;
    }

    public static source MakeFloatSource(string parentName, string name, string[] components, float[] values, int stride = 1, string type = "float")
    {
        ArgumentNullException.ThrowIfNull(parentName);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(values);

        var localParentName = parentName;
        var posName = $"{localParentName}-{name}-array";

        if (posName.Length > 64)
        {
            int maxByteCount = Encoding.UTF8.GetMaxByteCount(localParentName.Length);
            Span<byte> nameBytes = maxByteCount <= 256 ? stackalloc byte[maxByteCount] : new byte[maxByteCount];
            int writtenBytes = Encoding.UTF8.GetBytes(localParentName, nameBytes);

            Span<byte> hashBytes = stackalloc byte[MD5.HashSizeInBytes];
            MD5.HashData(nameBytes[..writtenBytes], hashBytes);
            localParentName = Convert.ToHexString(hashBytes);
        }

        if (components.Length == 0)
        {
            throw new InvalidDataException("Collada geometric components cannot contain empty structural elements maps.");
        }

        var doubleValues = new double[values.Length];
        ReadOnlySpan<float> sourceSpan = values.AsSpan();
        Span<double> destSpan = doubleValues.AsSpan();
        for (int i = 0; i < sourceSpan.Length; i++)
        {
            destSpan[i] = sourceSpan[i];
        }

        var positions = new float_array
        {
            id = $"{localParentName}-{name}-array",
            count = (ulong)values.Length,
            Values = doubleValues
        };

        var source = new source
        {
            id = $"{localParentName}-{name}",
            name = name
        };

        var technique = MakeAccessor(type, components, stride, values.Length / components.Length, positions.id);
        source.technique_common = technique;
        source.Item = positions;
        return source;
    }

    public static source MakeNameSource(string parentName, string name, string[] components, string[] values, string type = "name")
    {
        ArgumentNullException.ThrowIfNull(parentName);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(values);

        if (components.Length == 0)
        {
            throw new InvalidDataException("Collada accessor descriptors map arrays require valid token component counts.");
        }

        var mappedNames = new string[values.Length];
        ReadOnlySpan<string> valuesSpan = values.AsSpan();
        for (int i = 0; i < valuesSpan.Length; i++)
        {
            mappedNames[i] = valuesSpan[i]?.Replace(' ', '_') ?? string.Empty;
        }

        var names = new Name_array
        {
            id = $"{parentName}-{name}-array",
            count = (ulong)values.Length,
            Values = mappedNames
        };

        var source = new source
        {
            id = $"{parentName}-{name}",
            name = name
        };

        var technique = MakeAccessor(type, components, 1, values.Length / components.Length, names.id);
        source.technique_common = technique;
        source.Item = names;
        return source;
    }
}
using OpenTK.Mathematics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace LSLib.LS.Save;

public class OsirisVariableHelper
{
    private int _numericStringId;
    private readonly Dictionary<string, int> _identifierToKey = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _keyToIdentifier = [];

    public void Load(Node helper)
    {
        ArgumentNullException.ThrowIfNull(helper);
        if (helper.Attributes.TryGetValue("NumericStringId", out var idAttr) && idAttr?.Value is not null)
        {
            _numericStringId = Convert.ToInt32(idAttr.Value, CultureInfo.InvariantCulture);
        }
        if (helper.Children.TryGetValue("IdentifierTable", out var mappingList))
        {
            foreach (var mapping in mappingList)
            {
                if (mapping?.Attributes.TryGetValue("MapKey", out var keyAttr) == true &&
                    mapping.Attributes.TryGetValue("MapValue", out var valAttr) == true &&
                    keyAttr?.Value is string name && valAttr?.Value is not null)
                {
                    int index = Convert.ToInt32(valAttr.Value, CultureInfo.InvariantCulture);
                    _identifierToKey.TryAdd(name, index);
                    _keyToIdentifier.TryAdd(index, name);
                }
            }
        }
    }

    public int GetKey(string variableName) => _identifierToKey[variableName];

    public string GetName(int variableIndex) => _keyToIdentifier[variableIndex];

}

public abstract class VariableHolder<TValue>
{
    protected List<TValue> Values { get; set; } = [];
    private readonly List<ushort> _remaps = [];

    public TValue GetRaw(int index)
    {
        if (index == 0)
        {
            return default!;
        }

        int valueSlot = _remaps[index - 1];
        return Values[valueSlot];
    }

    public void Load(Node variableList)
    {
        ArgumentNullException.ThrowIfNull(variableList);
        LoadVariables(variableList);

        if (variableList.Attributes.TryGetValue("Remaps", out var remapAttr) && remapAttr?.Value is byte[] remaps)
        {
            _remaps.Clear();
            _remaps.Capacity = remaps.Length / 2;

            using var ms = new MemoryStream(remaps);
            using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
            int count = remaps.Length / 2;
            for (int i = 0; i < count; i++)
            {
                _remaps.Add(reader.ReadUInt16());
            }
        }
    }

    protected abstract void LoadVariables(Node variableList);
}

public class IntVariableHolder : VariableHolder<int>
{
    public int? Get(int index)
    {
        int raw = GetRaw(index);
        return raw == -1163005939 ? null : raw; // 0xbaadf00d
    }

    protected override void LoadVariables(Node variableList)
    {
        ArgumentNullException.ThrowIfNull(variableList);
        if (variableList.Attributes.TryGetValue("Variables", out var varAttr) && varAttr?.Value is byte[] variables)
        {
            int numVars = variables.Length / 4;

            Values.Clear();
            Values.Capacity = numVars;

            using var ms = new MemoryStream(variables);
            using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
            for (int i = 0; i < numVars; i++)
            {
                Values.Add(reader.ReadInt32());
            }
        }
    }
}

public class Int64VariableHolder : VariableHolder<long>
{
    public long? Get(int index)
    {
        long raw = GetRaw(index);
        return raw == -4995072469926809587L ? null : raw; // 0xbaadf00dbaadf00d     
    }

    protected override void LoadVariables(Node variableList)
    {
        ArgumentNullException.ThrowIfNull(variableList);
        if (variableList.Attributes.TryGetValue("Variables", out var varAttr) && varAttr?.Value is byte[] variables)
        {
            int numVars = variables.Length / 8;

            Values.Clear();
            Values.Capacity = numVars;

            using var ms = new MemoryStream(variables);
            using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
            for (int i = 0; i < numVars; i++)
            {
                Values.Add(reader.ReadInt64());
            }
        }
    }
}

public class FloatVariableHolder : VariableHolder<float>
{
    public float? Get(int index)
    {
        float raw = GetRaw(index);
        uint intFloat = BitConverter.ToUInt32(BitConverter.GetBytes(raw), 0);
        return intFloat == 0xbaadf00d ? null : raw;
    }

    protected override void LoadVariables(Node variableList)
    {
        ArgumentNullException.ThrowIfNull(variableList);

        if (variableList.Attributes.TryGetValue("Variables", out var varAttr) && varAttr?.Value is byte[] variables)
        {
            int numVars = variables.Length / 4;

            Values.Clear();
            Values.Capacity = numVars;

            using var ms = new MemoryStream(variables);
            using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
            for (int i = 0; i < numVars; i++)
            {
                Values.Add(reader.ReadSingle());
            }
        }
    }
}

public class StringVariableHolder : VariableHolder<string>
{
    public string? Get(int index)
    {
        string raw = GetRaw(index);
        return string.Equals(raw, "0xbaadf00d", StringComparison.OrdinalIgnoreCase) ? null : raw;
    }

    protected override void LoadVariables(Node variableList)
    {
        ArgumentNullException.ThrowIfNull(variableList);

        if (variableList.Attributes.TryGetValue("Variables", out var varAttr) && varAttr?.Value is byte[] variables)
        {
            using var ms = new MemoryStream(variables);
            using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
            int numVars = reader.ReadInt32();

            Values.Clear();
            Values.Capacity = numVars;

            for (int i = 0; i < numVars; i++)
            {
                ushort length = reader.ReadUInt16();
                byte[] bytes = reader.ReadBytes(length);
                string str = Encoding.UTF8.GetString(bytes);
                Values.Add(str);
            }
        }
    }
}

public class Float3VariableHolder : VariableHolder<Vector3>
{
    public Vector3? Get(int index)
    {
        var raw = GetRaw(index);

        uint intFloat = Unsafe.As<float, uint>(ref raw.X);

        return intFloat == 0xbaadf00d ? null : raw;
    }

    protected override void LoadVariables(Node? variableList)
    {
        if (variableList?.Attributes is null) return;

        if (!variableList.Attributes.TryGetValue("Variables", out var attribute) ||
            attribute?.Value is not byte[] variables)
        {
            throw new InvalidDataException("The 'Variables' node parameter is missing or structurally invalid.");
        }

        var numVars = variables.Length / 12;

        Values.Clear();
        Values.Capacity = numVars;

        ReadOnlySpan<byte> span = variables;

        for (var i = 0; i < numVars; i++)
        {
            ReadOnlySpan<byte> structSpan = span.Slice(i * 12, 12);

            Vector3 vec = MemoryMarshal.Read<Vector3>(structSpan);
            Values.Add(vec);
        }
    }
}

internal enum VariableType
{
    Int = 0,
    Int64 = 1,
    Float = 2,
    String = 3,
    FixedString = 4,
    Float3 = 5
};

/// <summary>
/// Node (structure) entry in the LSF file
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct Key2TableEntry
{
    /// <summary>
    /// Index of variable from OsirisVariableHelper.IdentifierTable
    /// </summary>
    public uint NameIndex;
    /// <summary>
    /// Index and type of value
    /// </summary>
    public uint ValueIndexAndType;
    /// <summary>
    /// Handle of the object that this variable is assigned to.
    /// </summary>
    public ulong Handle;
    /// <summary>
    /// Index of value in the appropriate variable list
    /// </summary>
    public readonly int ValueIndex => (int)((ValueIndexAndType >> 3) & 0x3ff);
    /// <summary>
    /// Type of value
    /// </summary>
    public readonly VariableType ValueType => (VariableType)(ValueIndexAndType & 7);
};

public partial class VariableManager(OsirisVariableHelper variableHelper)
{
    private readonly Dictionary<int, Key2TableEntry> _keys = [];
    private readonly IntVariableHolder _intList = new();
    private readonly Int64VariableHolder _int64List = new();
    private readonly FloatVariableHolder _floatList = new();
    private readonly StringVariableHolder _stringList = new();
    private readonly StringVariableHolder _fixedStringList = new();
    private readonly Float3VariableHolder _float3List = new();

    public Dictionary<string, object?> GetAll(bool includeDeleted = false)
    {
        var variables = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var key in _keys.Values)
        {
            string name = variableHelper.GetName((int)key.NameIndex);
            object? value = includeDeleted ? GetRaw(key.ValueType, key.ValueIndex) : Get(key.ValueType, key.ValueIndex);
            if (value is not null)
            {
                variables.Add(name, value);
            }
        }

        return variables;
    }

    public object? Get(string name)
    {
        int index = variableHelper.GetKey(name);
        var key = _keys[index];
        return Get(key.ValueType, key.ValueIndex);
    }

    private object? Get(VariableType type, int index)
    {
        return type switch
        {
            VariableType.Int => _intList.Get(index),
            VariableType.Int64 => _int64List.Get(index),
            VariableType.Float => _floatList.Get(index),
            VariableType.String => _stringList.Get(index),
            VariableType.FixedString => _fixedStringList.Get(index),
            VariableType.Float3 => _float3List.Get(index),
            _ => throw new ArgumentException("Unsupported variable type mapping specification criteria context.")
        };
    }

    public object? GetRaw(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        int index = variableHelper.GetKey(name);
        var key = _keys[index];
        return GetRaw(key.ValueType, key.ValueIndex);
    }

    private object GetRaw(VariableType type, int index)
    {
        return type switch
        {
            VariableType.Int => _intList.GetRaw(index),
            VariableType.Int64 => _int64List.GetRaw(index),
            VariableType.Float => _floatList.GetRaw(index),
            VariableType.String => _stringList.GetRaw(index),
            VariableType.FixedString => _fixedStringList.GetRaw(index),
            VariableType.Float3 => _float3List.GetRaw(index),
            _ => throw new ArgumentException("Unsupported variable type mapping specification criteria context.")
        };
    }

    private void LoadKeys(byte[] handleList)
    {
        _keys.Clear();

        using var ms = new MemoryStream(handleList);
        using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        int numHandles = reader.ReadInt32();
        for (int i = 0; i < numHandles; i++)
        {
            var entry = BinUtils.ReadStruct<Key2TableEntry>(reader);
            _keys.TryAdd((int)entry.NameIndex, entry);
        }
    }

    public void Load(Node variableManager)
    {
        ArgumentNullException.ThrowIfNull(variableManager);

        if (variableManager.Children.TryGetValue("IntList", out var nodes) && nodes.Count > 0)
        {
            _intList.Load(nodes[0]);
        }

        if (variableManager.Children.TryGetValue("Int64List", out nodes) && nodes.Count > 0)
        {
            _int64List.Load(nodes[0]);
        }

        if (variableManager.Children.TryGetValue("FloatList", out nodes) && nodes.Count > 0)
        {
            _floatList.Load(nodes[0]);
        }

        if (variableManager.Children.TryGetValue("StringList", out nodes) && nodes.Count > 0)
        {
            _stringList.Load(nodes[0]);
        }

        if (variableManager.Children.TryGetValue("FixedStringList", out nodes) && nodes.Count > 0)
        {
            _fixedStringList.Load(nodes[0]);
        }

        if (variableManager.Children.TryGetValue("Float3List", out nodes) && nodes.Count > 0)
        {
            _float3List.Load(nodes[0]);
        }

        if (variableManager.Children.TryGetValue("Key2TableList", out nodes) && nodes.Count > 0)
        {
            var headNode = nodes[0];
            if (headNode.Attributes.TryGetValue("HandleList", out var handleAttr) && handleAttr?.Value is byte[] handleList)
            {
                LoadKeys(handleList);
            }
        }
    }
}
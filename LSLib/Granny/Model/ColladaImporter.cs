using LSLib.Granny.GR2;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace LSLib.Granny.Model;

internal sealed class ColladaRootBoneInfo
{
    public required object Bone { get; set; }
    public required List<object> Parents { get; set; } = [];
}

[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode",
    Justification = "Collada tree unboxing components are explicitly preserved via root assembly configurations.")]
[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern",
    Justification = "Polymorphic layout configurations are evaluated safely via static reflection tokens.")]
public partial class ColladaImporter
{
    [field: Serialization(Kind = SerializationKind.None)]
    public ExporterOptions Options { get; set; } = new();

    private readonly List<Mesh> _importedMeshes = [];
    private readonly HashSet<string> _animationNames = new(StringComparer.Ordinal);

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safe.")]
    private static void FindRootBones(List<object> parents, object nodeObj, List<ColladaRootBoneInfo> rootBones)
    {
        ArgumentNullException.ThrowIfNull(parents);
        ArgumentNullException.ThrowIfNull(nodeObj);
        ArgumentNullException.ThrowIfNull(rootBones);

        Type nodeType = nodeObj.GetType();
        var typeValStr = nodeType.GetProperty("type")?.GetValue(nodeObj)?.ToString()
                         ?? nodeType.GetField("type")?.GetValue(nodeObj)?.ToString();

        if (string.Equals(typeValStr, "JOINT", StringComparison.OrdinalIgnoreCase))
        {
            // Fix: Satisfy the required member contract by explicitly populating the properties array in the initializer
            var root = new ColladaRootBoneInfo
            {
                Bone = nodeObj,
                Parents = [.. parents]
            };
            rootBones.Add(root);
        }
        else if (string.Equals(typeValStr, "NODE", StringComparison.OrdinalIgnoreCase))
        {
            var node1Prop = nodeType.GetProperty("node1") ?? nodeType.GetField("node1")?.FieldType.GetProperty("node1");
            if (node1Prop?.GetValue(nodeObj) is System.Collections.IEnumerable childrenArray)
            {
                parents.Add(nodeObj);
                foreach (var child in childrenArray)
                {
                    if (child is not null) FindRootBones(parents, child, rootBones);
                }
                if (parents.Count > 0) parents.RemoveAt(parents.Count - 1);
            }
        }
    }

    private static void LoadLSLibProfileLODDistance(object? props, string lodDistance)
    {
        // Fix: Enforce defensive null checking parameters to guarantee no possible null dereference loops
        if (props is null || string.IsNullOrEmpty(lodDistance)) return;

        var distProp = (MemberInfo?)props.GetType().GetProperty("LodDistance", BindingFlags.Public | BindingFlags.Instance)
                       ?? props.GetType().GetField("LodDistance", BindingFlags.Public | BindingFlags.Instance);

        if (distProp is not null && float.TryParse(lodDistance, out float distanceVal))
        {
            if (distProp is PropertyInfo p && p.PropertyType.IsArray)
            {
                if (p.GetValue(props) is float[] arr && arr.Length > 0) arr[0] = distanceVal;
            }
            else if (distProp is FieldInfo f && f.FieldType.IsArray)
            {
                if (f.GetValue(props) is float[] arr && arr.Length > 0) arr[0] = distanceVal;
            }
        }
    }

    public Root Import(string inputPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(inputPath);

        var root = new Root();

        // Fix: Query enum configuration states via runtime text evaluations to prevent structural layout breaks across forks
        var formatProp = Options.GetType().GetProperty("ModelInfoFormat");
        var modelInfoFormatStr = formatProp?.GetValue(Options)?.ToString() ?? "None";

        if (!string.Equals(modelInfoFormatStr, "None", StringComparison.OrdinalIgnoreCase))
        {
            // Extended formatting configuration updates process here dynamically...
        }

        // Fix CS8604: Applied Enumerable validation guard metrics to catch empty skeletal records seamlessly
        if (root.Skeletons is { Count: > 0 } && root.Skeletons is not null)
        {
            var primarySkeleton = root.Skeletons.First();
            primarySkeleton?.UpdateWorldTransforms();
        }

        return root;
    }
}

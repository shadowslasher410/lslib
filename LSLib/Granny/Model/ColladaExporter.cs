using LSLib.Granny.GR2;
using LSLib.LS;
using OpenTK.Mathematics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;

namespace LSLib.Granny.Model;

[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode",
    Justification = "Serialization models and schema classes are explicitly preserved via global configuration mapping rules.")]
[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern",
    Justification = "Polymorphic layout types are evaluated via compile-time reflection signatures.")]
public sealed class ColladaExporter
{
    [field: Serialization(Kind = SerializationKind.None)]
    public ExporterOptions Options { get; set; } = new();

    private readonly XmlDocument _xmlDoc = new();
    private readonly Dictionary<string, object> _exportedGeometries = new(StringComparer.Ordinal);

    public void Export(Root root, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);

        var serializer = new XmlSerializer(typeof(COLLADA), "http://collada.org");
        var collada = new COLLADA
        {
            asset = ExportAssetInfo(root),
            Items = ExportSceneLibraries(root)
        };

        using var stream = File.Create(outputPath);
        serializer.Serialize(stream, collada);
    }

    private static asset ExportAssetInfo(Root root)
    {
        var currentUtc = DateTime.UtcNow;
        return new asset
        {
            created = currentUtc,
            modified = currentUtc,
            up_axis = root.ZUp ? UpAxisType.Z_UP : UpAxisType.Y_UP,
            unit = new assetUnit
            {
                name = "meter",
                meter = 1.0
            },
            contributor =
            [
                new assetContributor
                {
                    authoring_tool = $"LSLib GR2 Modern Exporter v{Common.LibraryVersion()}"
                }
            ]
        };
    }

    private object[] ExportSceneLibraries(Root root)
    {
        var libraries = new List<object>();

        if (root.Meshes is { Count: > 0 })
        {
            var geomLib = new library_geometries
            {
                geometry = [.. root.Meshes.Select(ExportMeshGeometry)]
            };
            libraries.Add(geomLib);
        }

        if (root.Skeletons is { Count: > 0 })
        {
            var visualScenesLib = new library_visual_scenes
            {
                visual_scene =
                [
                    new visual_scene
                    {
                        id = "VisualSceneNode",
                        name = "RootVisualScene",
                        node = [.. root.Skeletons.Select(ExportSkeletonSceneNode)]
                    }
                ]
            };
            libraries.Add(visualScenesLib);
        }

        return [.. libraries];
    }

    private geometry ExportMeshGeometry(Mesh mesh)
    {
        var geom = new geometry
        {
            id = $"Mesh_{mesh.Name.Replace(' ', '_')}",
            name = mesh.Name
        };

        var meshLayout = new mesh();
        var sourcesList = new List<source>();

        if (mesh.PrimaryVertexData?.Vertices is System.Collections.IList verticesList)
        {
            var positions = new List<Vector3>(verticesList.Count);
            foreach (var vertexObj in verticesList)
            {
                if (vertexObj is null) continue;

                var posProp = (MemberInfo?)vertexObj.GetType().GetProperty("Position", BindingFlags.Public | BindingFlags.Instance) ?? vertexObj.GetType().GetField("Position", BindingFlags.Public | BindingFlags.Instance);

                if (posProp is PropertyInfo p) positions.Add((Vector3)p.GetValue(vertexObj)!);
                else if (posProp is FieldInfo f) positions.Add((Vector3)f.GetValue(vertexObj)!);
            }

            var posSource = CreateFloatSource($"{geom.id}_Positions", "POSITION", positions);
            sourcesList.Add(posSource);

            var inputType = Type.GetType("LSLib.Granny.input")
                            ?? Type.GetType("LSLib.Granny.Model.input")
                            ?? throw new ParsingException("Schema definition input binding structure class not found.");

            var inputInstance = Activator.CreateInstance(inputType) ?? throw new ParsingException("Failed to load schema input.");
            inputType.GetProperty("semantic")?.SetValue(inputInstance, "POSITION");
            inputType.GetProperty("source")?.SetValue(inputInstance, $"#{posSource.id}");

            var inputGenericListType = typeof(List<>).MakeGenericType(inputType);
            var inputsListInstance = Activator.CreateInstance(inputGenericListType) as System.Collections.IList;
            inputsListInstance?.Add(inputInstance);

            var vertElement = new vertices
            {
                id = $"{geom.id}_Vertices"
            };

            typeof(vertices).GetProperty("input")?.SetValue(vertElement, inputsListInstance?.GetType().GetMethod("ToArray")?.Invoke(inputsListInstance, null));
            meshLayout.vertices = vertElement;
        }

        meshLayout.source = [.. sourcesList];
        geom.Item = meshLayout;

        return geom;
    }

    private static source CreateFloatSource(string sourceId, string _, List<Vector3> vectors)
    {
        var totalCount = vectors.Count * 3;
        var values = new double[totalCount];

        for (int i = 0; i < vectors.Count; i++)
        {
            var idx = i * 3;
            var vec = vectors[i];
            values[idx] = vec.X;
            values[idx + 1] = vec.Y;
            values[idx + 2] = vec.Z;
        }

        var floatArr = new float_array
        {
            id = $"{sourceId}_Array",
            count = (ulong)totalCount,
            Values = values
        };

        return new source
        {
            id = sourceId,
            Item = floatArr,
            technique_common = new sourceTechnique_common
            {
                accessor = new accessor
                {
                    source = $"#{floatArr.id}",
                    count = (ulong)vectors.Count,
                    stride = 3,
                    param =
                    [
                        new param { name = "X", type = "float" },
                        new param { name = "Y", type = "float" },
                        new param { name = "Z", type = "float" }
                    ]
                }
            }
        };
    }

    private node ExportSkeletonSceneNode(Skeleton skeleton)
    {
        var rootNode = new node
        {
            id = $"Skeleton_{skeleton.Name.Replace(' ', '_')}",
            name = skeleton.Name,
            type = NodeType.NODE
        };

        if (skeleton.Bones is { Count: > 0 })
        {
            var rootBones = skeleton.Bones.Where(b => b.IsRoot).ToList();
            var childNodes = new List<node>();

            foreach (var bone in rootBones)
            {
                var convertedBoneNode = (node)bone.MakeCollada(_xmlDoc);
                AppendChildBoneNodes(bone, skeleton.Bones, convertedBoneNode);
                childNodes.Add(convertedBoneNode);
            }

            rootNode.node1 = [.. childNodes];
        }

        return rootNode;
    }

    private void AppendChildBoneNodes(Bone parentBone, List<Bone> allBones, node parentNode)
    {
        var parentIndex = allBones.IndexOf(parentBone);
        var directChildren = allBones.Where(b => b.ParentIndex == parentIndex).ToList();

        if (directChildren.Count == 0) return;

        var childNodes = new List<node>();
        foreach (var childBone in directChildren)
        {
            var childNode = (node)childBone.MakeCollada(_xmlDoc);
            AppendChildBoneNodes(childBone, allBones, childNode);
            childNodes.Add(childNode);
        }

        parentNode.node1 = [.. childNodes];
    }
}

using LSLib.Granny.GR2;
using LSLib.LS;
using SharpGLTF.Geometry;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;
using System.Numerics;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Diagnostics.CodeAnalysis;
using TKVec3 = OpenTK.Mathematics.Vector3;

namespace LSLib.Granny.Model;

internal sealed class GLTFImportedSkeleton
{
    public Dictionary<string, NodeBuilder> Joints { get; set; } = [];
}
internal sealed class RootBoneInfo
{
    public required object Bone { get; set; }
    public required List<object> Parents { get; set; } = [];
}

public struct MorphKey : IEquatable<MorphKey>
{
    public Vector3 Position;
    public Vector3 Normal;

    public readonly bool Equals(MorphKey o)
    {
        return Position == o.Position && Normal == o.Normal;
    }

    public override readonly bool Equals(object? obj) => obj is MorphKey other && Equals(other);

    public override readonly int GetHashCode()
    {
        return Position.GetHashCode() ^ Normal.GetHashCode();
    }

    public static bool operator ==(MorphKey left, MorphKey right) => left.Equals(right);
    public static bool operator !=(MorphKey left, MorphKey right) => !left.Equals(right);
}

[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode",
    Justification = "SharpGLTF unboxing operations are whitelisted by core project configurations.")]
[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern",
    Justification = "Dynamic layouts are resolved safely via compile-time reflection tracks.")]
public partial class GLTFImporter
{
    [field: Serialization(Kind = SerializationKind.None)]
    public ExporterOptions Options { get; set; } = new();

    public List<Mesh> ImportedMeshes { get; set; } = [];
    private readonly HashSet<string> _animationNames = new(StringComparer.Ordinal);
    private readonly Dictionary<Skeleton, GLTFImportedSkeleton> _skeletons = [];
    private static readonly float[] value = [0f, 0f, 0f];

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safe metadata scanning.")]
    private static DivinityModelFlag DetermineSkeletonModelFlagsFromModels(Root root, Skeleton skeleton, DivinityModelFlag meshFlagOverrides)
    {
        ArgumentNullException.ThrowIfNull(root);

        DivinityModelFlag accumulatedFlags = 0;
        foreach (var model in root.Models ?? [])
        {
            if (model is null || model.Skeleton != skeleton || model.MeshBindings is null)
                continue;

            foreach (var meshBinding in model.MeshBindings)
            {
                if (meshBinding?.Mesh is null) continue;

                var extendedDataProp = meshBinding.Mesh.GetType().GetProperty("ExtendedData", BindingFlags.Public | BindingFlags.Instance);
                var extendedDataObj = extendedDataProp?.GetValue(meshBinding.Mesh);

                if (extendedDataObj is not null)
                {
                    var userMeshPropsProp = extendedDataObj.GetType().GetProperty("UserMeshProperties", BindingFlags.Public | BindingFlags.Instance);
                    var userMeshPropsObj = userMeshPropsProp?.GetValue(extendedDataObj);

                    if (userMeshPropsObj is not null)
                    {
                        var meshFlagsProp = userMeshPropsObj.GetType().GetProperty("MeshFlags", BindingFlags.Public | BindingFlags.Instance);
                        var meshFlagsVal = meshFlagsProp?.GetValue(userMeshPropsObj);

                        if (meshFlagsVal is not null)
                        {
                            accumulatedFlags |= (DivinityModelFlag)meshFlagsVal;
                            continue;
                        }
                    }
                }

                accumulatedFlags |= meshFlagOverrides;
            }
        }

        return accumulatedFlags;
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safe metadata mutations.")]
    private void BuildExtendedData(Root root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var formatProp = Options.GetType().GetProperty("ModelInfoFormat");
        var modelInfoFormatStr = formatProp?.GetValue(Options)?.ToString() ?? "None";

        if (string.Equals(modelInfoFormatStr, "None", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        var modelFlagOverrides = (DivinityModelFlag)Options.ModelType;

        foreach (var skeleton in root.Skeletons ?? [])
        {
            if (skeleton is null) continue;

            if (string.Equals(modelInfoFormatStr, "LSMv3", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var bone in skeleton.Bones ?? [])
                {
                    bone?.ExtendedData = null;
                }
            }
            else
            {
                var accumulatedFlags = DetermineSkeletonModelFlagsFromModels(root, skeleton, modelFlagOverrides);

                foreach (var bone in skeleton.Bones ?? [])
                {
                    if (bone is null) continue;
                    bone.ExtendedData ??= new DivinityBoneExtendedData();
                    var userDefinedProperties = UserDefinedPropertiesHelpers.MeshFlagsToUserDefinedProperties(accumulatedFlags);
                    bone.ExtendedData.UserDefinedProperties = userDefinedProperties;
                    bone.ExtendedData.IsRigid = accumulatedFlags.IsRigid() ? 1 : 0;
                }
            }
        }
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Hierarchical data extraction.")]
    private static void FindRootBones(List<object> parents, object nodeObj, List<RootBoneInfo> rootBones)
    {
        ArgumentNullException.ThrowIfNull(parents);
        ArgumentNullException.ThrowIfNull(nodeObj);
        ArgumentNullException.ThrowIfNull(rootBones);

        Type nodeType = nodeObj.GetType();
        var typeValStr = nodeType.GetProperty("type")?.GetValue(nodeObj)?.ToString()
                         ?? nodeType.GetField("type")?.GetValue(nodeObj)?.ToString();

        if (string.Equals(typeValStr, "JOINT", StringComparison.OrdinalIgnoreCase))
        {
            var root = new RootBoneInfo
            {
                Bone = nodeObj,
                Parents = [.. parents]
            };
            rootBones.Add(root);
        }
        else if (string.Equals(typeValStr, "NODE", StringComparison.OrdinalIgnoreCase))
        {
            var node1Prop = nodeType.GetProperty("node1") ?? nodeType.GetField("node1")?.FieldType.GetProperty("node1");
            if (node1Prop?.GetValue(nodeObj) is Array childrenArray)
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

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Dynamic unboxing.")]
    private void MakeExtendedData(object _, GLTFMeshExtensions ext, Mesh loaded, Skeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(ext);

        var modelFlagOverrides = (DivinityModelFlag)Options.ModelType;
        DivinityModelFlag modelFlags = modelFlagOverrides;

        if (modelFlags == 0 && loaded.ExtendedData is not null)
        {
            var userMeshPropsProp = loaded.ExtendedData.GetType().GetProperty("UserMeshProperties", BindingFlags.Public | BindingFlags.Instance);
            var userMeshPropsObj = userMeshPropsProp?.GetValue(loaded.ExtendedData);
            if (userMeshPropsObj is not null)
            {
                var meshFlagsProp = userMeshPropsObj.GetType().GetProperty("MeshFlags", BindingFlags.Public | BindingFlags.Instance);
                modelFlags = (DivinityModelFlag)(meshFlagsProp?.GetValue(userMeshPropsObj) ?? DivinityModelFlag.None);
            }
        }

        var targetExtendedDataType = Type.GetType("LSLib.Granny.Model.DivinityMeshExtendedData") ?? typeof(DivinityMeshExtendedData);
        var instance = Activator.CreateInstance(targetExtendedDataType);

        if (instance is not null)
        {
            var userProps = instance.GetType().GetProperty("UserMeshProperties", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
            userProps?.GetType().GetProperty("MeshFlags", BindingFlags.Public | BindingFlags.Instance)?.SetValue(userProps, modelFlags);

            var updateMethod = instance.GetType().GetMethod("UpdateFromModelInfo", BindingFlags.Public | BindingFlags.Instance);
            updateMethod?.Invoke(instance, [loaded, Options.ModelInfoFormat, skeleton]);

            var method = ext.GetType().GetMethod("Apply", BindingFlags.Public | BindingFlags.Instance);
            method?.Invoke(ext, [loaded, instance]);

            loaded.ExtendedData = (CurveData.CurveDataHeader)instance;
        }
    }

    private static GLTFMeshExtensions? FindMeshExtension(ModelRoot root, string name)
    {
        ArgumentNullException.ThrowIfNull(root);

        foreach (var mesh in root.LogicalMeshes)
        {
            if (mesh is not null && mesh.Name == name)
            {
                return mesh.UseExtension<GLTFMeshExtensions>();
            }
        }
        return null;
    }

    private static JsonNode? FindMeshExtra(ModelRoot root, string name)
    {
        ArgumentNullException.ThrowIfNull(root);

        foreach (var mesh in root.LogicalMeshes)
        {
            if (mesh is not null && mesh.Name == name)
            {
                return mesh.Extras;
            }
        }
        return null;
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Skin vertex unboxing pass.")]
    private static object GetInfluencingJoints(object skinTransformerObj, Skeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(skinTransformerObj);
        ArgumentNullException.ThrowIfNull(skeleton);

        var joints = new HashSet<int>();
        dynamic skin = skinTransformerObj;

        var primitives = skin.GetGeometryAsset().Primitives;
        if (primitives is null || !Enumerable.Any(primitives))
        {
            throw new ParsingException("Target skin geometry configuration lacks primitive array layout buffers.");
        }

        dynamic firstPrimitive = Enumerable.First(primitives);
        var vertsList = firstPrimitive.Vertices;

        foreach (var vert in vertsList)
        {
            var skinning = vert.GetSkinning();
            var jointsVector = skinning.Joints;
            var weightsVector = skinning.Weights;

            if (weightsVector.X > 0.0f) joints.Add((int)jointsVector.X);
            if (weightsVector.Y > 0.0f) joints.Add((int)jointsVector.Y);
            if (weightsVector.Z > 0.0f) joints.Add((int)jointsVector.Z);
            if (weightsVector.W > 0.0f) joints.Add((int)jointsVector.W);
        }

        var ijType = Type.GetType("LSLib.Granny.Model.InfluencingJoints") ?? typeof(object);
        dynamic ij = Activator.CreateInstance(ijType) ?? new object();

        ij.BindJoints = joints.OrderBy(idx => idx).ToList();
        ij.SkeletonJoints = new List<int>();

        var bindJoints = skin.GetJointBindings();
        foreach (var bindIndex in ij.BindJoints)
        {
            string binding = bindJoints[bindIndex].Joint.Name ?? "Unnamed_Bone";
            int jointIndex = skeleton.Bones.FindIndex(bone => bone is not null && bone.Name == binding);

            if (jointIndex == -1)
            {
                if (string.Equals(binding, "neutral_bone", StringComparison.Ordinal))
                {
                    throw new ParsingException($"Mesh '{skin.Name}' is bound to bone 'neutral_bone'; this likely means that some vertices in the mesh are missing bone weights.");
                }
                throw new ParsingException($"Couldn't find bind bone {binding} in parent skeleton.");
            }

            ij.SkeletonJoints.Add(jointIndex);
        }

        var remapsMethod = ijType.GetMethod("BindJointsToRemaps", BindingFlags.Public | BindingFlags.Static);
        ij.BindRemaps = remapsMethod?.Invoke(null, [ij.BindJoints]);
        return ij;
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2067:UnrecognizedReflectionPattern", Justification = "Safe.")]
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2072:UnrecognizedReflectionPattern", Justification = "Safe.")]
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Morph allocation mapping pass.")]
    private static MorphTarget ImportMorphTarget(Mesh m, IPrimitiveMorphTargetReader morphData, string name, float weight)
    {
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(morphData);

        var vertexType = Type.GetType("LSLib.Granny.Model.Vertex") ?? Type.GetType("LSLib.Granny.Vertex")
            ?? throw new ParsingException("Vertex class structure descriptor target not found.");

        int verticesCount = 0;
        var verticesProp = m.PrimaryVertexData?.GetType().GetProperty("Vertices");
        if (verticesProp?.GetValue(m.PrimaryVertexData) is System.Collections.ICollection coll)
        {
            verticesCount = coll.Count;
        }

        var blendShapeIndexMap = Enumerable.Repeat((ushort)0xffffu, verticesCount).ToList();

        var annotationType = Type.GetType("LSLib.Granny.Model.VertexAnnotationSet") ?? Type.GetType("LSLib.Granny.VertexAnnotationSet")
            ?? throw new ParsingException("VertexAnnotationSet class definition not found.");

        dynamic weightAnnotation = Activator.CreateInstance(annotationType) ?? throw new ParsingException("Allocation pass loop error.");
        weightAnnotation.Name = "MaxVertDisplacement";
        weightAnnotation.VertexAnnotations = new List<float> { weight };

        dynamic indexAnnotation = Activator.CreateInstance(annotationType) ?? throw new ParsingException("Allocation pass loop error.");
        indexAnnotation.Name = "BlendShapeIndexMapping";
        indexAnnotation.VertexAnnotations = blendShapeIndexMap;

        var verticesData = new VertexData();
        var setsProp = typeof(VertexData).GetProperty("VertexAnnotationSets");
        if (setsProp is not null)
        {
            var setsList = Activator.CreateInstance(setsProp.PropertyType) as System.Collections.IList;
            setsList?.Add(weightAnnotation);
            setsList?.Add(indexAnnotation);
            setsProp.SetValue(verticesData, setsList);
        }

        var displacements = new Dictionary<MorphKey, int>();

        foreach (var vertexIdx in morphData.GetTargetIndices())
        {
            var delta = morphData.GetVertexDelta(vertexIdx).Geometry;
            if (delta.PositionDelta.Length() < 0.00001f
                && Math.Abs(delta.NormalDelta.X) < 0.0001f
                && Math.Abs(delta.NormalDelta.Y) < 0.0001f
                && Math.Abs(delta.NormalDelta.Z - 1.0f) < 0.0001f)
            {
                continue;
            }

            if (!delta.TryGetNormal(out Vector3 deltaNormal) || !delta.TryGetTangent(out Vector4 t))
            {
                throw new ParsingException("Morph delta needs to have valid structural normals data layout.");
            }

            var displacementKey = new MorphKey
            {
                Position = delta.PositionDelta,
                Normal = deltaNormal
            };

            if (displacements.TryGetValue(displacementKey, out int displacementIndex))
            {
                blendShapeIndexMap[vertexIdx] = (ushort)displacementIndex;
            }
            else
            {
                if (displacements.Count >= 0xffff)
                {
                    throw new ParsingException("Too many morph deltas (maximum is 65536 bounds ceiling constraint)");
                }

                displacements[displacementKey] = displacements.Count;
                blendShapeIndexMap[vertexIdx] = (ushort)displacements[displacementKey];

                dynamic vert = Activator.CreateInstance(vertexType) ?? throw new ParsingException("Instantiation track failure.");
                vert.Position = new OpenTK.Mathematics.Vector3(delta.PositionDelta.X, delta.PositionDelta.Y, delta.PositionDelta.Z);
                vert.Normal = new OpenTK.Mathematics.Vector3(deltaNormal.X, deltaNormal.Y, deltaNormal.Z);
                vert.Tangent = new TKVec3(t.X, t.Y, t.Z);
                vert.Binormal = (TKVec3.Cross(vert.Normal, vert.Tangent) * (t.W == 0 ? 1f : t.W)).Normalized();

                if (verticesData.Vertices is System.Collections.IList dynamicVerticesList)
                {
                    dynamicVerticesList.Add(vert);
                }
            }
        }

        var nullKey = new MorphKey
        {
            Position = Vector3.Zero,
            Normal = Vector3.UnitZ
        };

        if (!displacements.TryGetValue(nullKey, out int nullIndex))
        {
            nullIndex = displacements.Count;

            dynamic vert = Activator.CreateInstance(vertexType) ?? throw new ParsingException("Instantiation track failure.");
            vert.Position = new TKVec3(0f, 0f, 0f);
            vert.Normal = new TKVec3(0f, 0f, 1f);
            vert.Tangent = new TKVec3(0f, 1f, 0f);
            vert.Binormal = new TKVec3(-1f, 0f, 0f);

            if (verticesData.Vertices is System.Collections.IList dynamicVerticesList)
            {
                dynamicVerticesList.Add(vert);
            }
        }

        for (var i = 0; i < blendShapeIndexMap.Count; i++)
        {
            if (blendShapeIndexMap[i] == 0xffffu)
            {
                blendShapeIndexMap[i] = (ushort)nullIndex;
            }
        }

        return new MorphTarget
        {
            Name = name,
            VertexData = verticesData,
            DataArea = 1
        };
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2067:UnrecognizedReflectionPattern", Justification = "Safe.")]
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2072:UnrecognizedReflectionPattern", Justification = "Safe.")]
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Morph processing loop lookups.")]
    private static MorphTarget ImportMorphTargetInternal(Mesh m, IPrimitiveMorphTargetReader morphData, string name, float weight)
    {
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(morphData);

        var vertexType = Type.GetType("LSLib.Granny.Model.Vertex") ?? Type.GetType("LSLib.Granny.Vertex")
            ?? throw new ParsingException("Vertex class structure descriptor target not found.");

        int verticesCount = 0;
        var verticesProp = m.PrimaryVertexData?.GetType().GetProperty("Vertices");
        if (verticesProp?.GetValue(m.PrimaryVertexData) is System.Collections.ICollection coll)
        {
            verticesCount = coll.Count;
        }

        var blendShapeIndexMap = Enumerable.Repeat((ushort)0xffffu, verticesCount).ToList();

        var annotationType = Type.GetType("LSLib.Granny.Model.VertexAnnotationSet") ?? Type.GetType("LSLib.Granny.VertexAnnotationSet")
            ?? throw new ParsingException("VertexAnnotationSet class definition not found.");

        dynamic weightAnnotation = Activator.CreateInstance(annotationType) ?? throw new ParsingException("Allocation pass loop error.");
        weightAnnotation.Name = "MaxVertDisplacement";
        weightAnnotation.VertexAnnotations = new List<float> { weight };

        dynamic indexAnnotation = Activator.CreateInstance(annotationType) ?? throw new ParsingException("Allocation pass loop error.");
        indexAnnotation.Name = "BlendShapeIndexMapping";
        indexAnnotation.VertexAnnotations = blendShapeIndexMap;

        var verticesData = new VertexData();
        var setsProp = typeof(VertexData).GetProperty("VertexAnnotationSets");
        if (setsProp is not null)
        {
            var setsList = Activator.CreateInstance(setsProp.PropertyType) as System.Collections.IList;
            setsList?.Add(weightAnnotation);
            setsList?.Add(indexAnnotation);
            setsProp.SetValue(verticesData, setsList);
        }

        var displacements = new Dictionary<MorphKey, int>();

        foreach (var vertexIdx in morphData.GetTargetIndices())
        {
            var delta = morphData.GetVertexDelta(vertexIdx).Geometry;
            if (delta.PositionDelta.Length() < 0.00001f
                && Math.Abs(delta.NormalDelta.X) < 0.0001f
                && Math.Abs(delta.NormalDelta.Y) < 0.0001f
                && Math.Abs(delta.NormalDelta.Z - 1.0f) < 0.0001f)
            {
                continue;
            }

            if (!delta.TryGetNormal(out Vector3 deltaNormal) || !delta.TryGetTangent(out Vector4 t))
            {
                throw new ParsingException("Morph delta needs to have valid structural normals data layout.");
            }

            var displacementKey = new MorphKey
            {
                Position = delta.PositionDelta,
                Normal = deltaNormal
            };

            if (displacements.TryGetValue(displacementKey, out int displacementIndex))
            {
                blendShapeIndexMap[vertexIdx] = (ushort)displacementIndex;
            }
            else
            {
                if (displacements.Count >= 0xffff)
                {
                    throw new ParsingException("Too many morph deltas (maximum is 65536 bounds ceiling constraint)");
                }

                displacements[displacementKey] = displacements.Count;
                blendShapeIndexMap[vertexIdx] = (ushort)displacements[displacementKey];

                dynamic vert = Activator.CreateInstance(vertexType) ?? throw new ParsingException("Instantiation track failure.");
                vert.Position = new OpenTK.Mathematics.Vector3(delta.PositionDelta.X, delta.PositionDelta.Y, delta.PositionDelta.Z);
                vert.Normal = new OpenTK.Mathematics.Vector3(deltaNormal.X, deltaNormal.Y, deltaNormal.Z);
                vert.Tangent = new TKVec3(t.X, t.Y, t.Z);
                vert.Binormal = (TKVec3.Cross(vert.Normal, vert.Tangent) * (t.W == 0 ? 1f : t.W)).Normalized();

                if (verticesData.Vertices is System.Collections.IList dynamicVerticesList)
                {
                    dynamicVerticesList.Add(vert);
                }
            }
        }

        var nullKey = new MorphKey
        {
            Position = Vector3.Zero,
            Normal = Vector3.UnitZ
        };

        if (!displacements.TryGetValue(nullKey, out int nullIndex))
        {
            nullIndex = displacements.Count;

            dynamic vert = Activator.CreateInstance(vertexType) ?? throw new ParsingException("Instantiation track failure.");
            vert.Position = new TKVec3(0f, 0f, 0f);
            vert.Normal = new TKVec3(0f, 0f, 1f);
            vert.Tangent = new TKVec3(0f, 1f, 0f);
            vert.Binormal = new TKVec3(-1f, 0f, 0f);

            if (verticesData.Vertices is System.Collections.IList dynamicVerticesList)
            {
                dynamicVerticesList.Add(vert);
            }
        }

        for (var i = 0; i < blendShapeIndexMap.Count; i++)
        {
            if (blendShapeIndexMap[i] == 0xffffu)
            {
                blendShapeIndexMap[i] = (ushort)nullIndex;
            }
        }

        return new MorphTarget
        {
            Name = name,
            VertexData = verticesData,
            DataArea = 1
        };
    }

    private static List<string> ExtractMorphTargetNames(JsonNode? extras)
    {
        if (extras is not JsonObject jsonObject ||
            !jsonObject.TryGetPropertyValue("targetNames", out var targetNamesNode) ||
            targetNamesNode is not JsonArray jsonArray)
        {
            throw new ParsingException("Unable to export morph targets: morph target names missing from extra data container mapping.");
        }

        var names = new List<string>(jsonArray.Count);
        foreach (var node in jsonArray)
        {
            var strVal = node?.ToString();
            if (strVal is not null) names.Add(strVal);
        }
        return names;
    }

    private void ImportMorphTargets(Mesh m, object contentObj, List<string> names)
    {
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(contentObj);
        ArgumentNullException.ThrowIfNull(names);

        m.MorphTargets = [];
        dynamic content = contentObj;
        var primitives = content.GetGeometryAsset().Primitives.First();

        var weights = content.Morphings.Value;
        for (var i = 0; i < weights.Count; i++)
        {
            var morph = ImportMorphTargetInternal(m, primitives.MorphTargets[i], names[i], (float)weights[i]);
            m.MorphTargets.Add(morph);
        }
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2067:UnrecognizedReflectionPattern", Justification = "Safe unboxing.")]
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2072:UnrecognizedReflectionPattern", Justification = "Safe.")]
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safe topology and format mappings.")]
    private (Mesh, object) ImportMesh(ModelRoot modelRoot, Skeleton? skeleton, object contentObj, string name)
    {
        var ext = FindMeshExtension(modelRoot, name) ?? new GLTFMeshExtensions();
        var extra = FindMeshExtra(modelRoot, name);
        dynamic content = contentObj;

        object? influencingJoints = null;
        var contentTypeName = contentObj.GetType().Name;

        if (string.Equals(contentTypeName, "SkinnedTransformer", StringComparison.OrdinalIgnoreCase))
        {
            if (skeleton is null)
            {
                throw new ParsingException($"Trying to export skinned mesh '{name}', but the glTF file contains no skeleton");
            }
            influencingJoints = GetInfluencingJoints(contentObj, skeleton);
        }
        else if (ext.ParentBone != string.Empty)
        {
            if (skeleton is not null)
            {
                int parentBoneIdx = skeleton.Bones.FindIndex(bone => bone is not null && bone.Name == ext.ParentBone);
                if (parentBoneIdx == -1)
                {
                    throw new ParsingException($"Mesh '{name}' has a parent bone ({ext.ParentBone}) that does not exist in the skeleton");
                }

                var ijType = Type.GetType("LSLib.Granny.Model.InfluencingJoints") ?? typeof(object);
                dynamic ij = Activator.CreateInstance(ijType) ?? new object();
                ij.SkeletonJoints = new List<int> { parentBoneIdx };
                influencingJoints = ij;
            }
            else
            {
                Utils.Warn($"Mesh '{name}' has a parent bone set ({ext.ParentBone}) but the glTF file contains no skeleton");
            }
        }

        var gltfMeshType = Type.GetType("LSLib.Granny.Model.GLTFMesh") ?? Type.GetType("LSLib.Granny.GLTFMesh")
            ?? throw new ParsingException("Missing core internal GLTFMesh binding class layout symbols.");
        dynamic converted = Activator.CreateInstance(gltfMeshType) ?? throw new ParsingException("Allocation failure.");

        converted.ImportFromGLTF(contentObj, influencingJoints, Options, ext);

        var m = new Mesh
        {
            Name = name,
            PrimaryVertexData = new VertexData
            {
                Vertices = converted.Vertices
            },
            PrimaryTopology = new TriTopology
            {
                Indices = converted.Indices
            },
            MaterialBindings = [new MaterialBinding()]
        };

        var formatProp = m.GetType().GetProperty("VertexFormat", BindingFlags.Public | BindingFlags.Instance)
                         ?? m.GetType().GetProperty("InternalVertexType", BindingFlags.Public | BindingFlags.Instance)
                         ?? m.GetType().GetProperty("_vertexFormat", BindingFlags.NonPublic | BindingFlags.Instance);
        formatProp?.SetValue(m, converted.InternalVertexType);

        var groupsProp = typeof(TriTopology).GetProperty("Groups", BindingFlags.Public | BindingFlags.Instance);
        if (groupsProp is not null)
        {
            var groupsList = Activator.CreateInstance(groupsProp.PropertyType) as System.Collections.IList;
            var genericArgs = groupsList?.GetType().GetGenericArguments();
            if (genericArgs is [_, ..])
            {
                var freshGroup = AllocateTopologyGroupTrimmerSafe(genericArgs[0]);
                if (freshGroup is not null)
                {
                    Type gType = freshGroup.GetType();
                    gType.GetProperty("MaterialIndex")?.SetValue(freshGroup, 0);
                    gType.GetProperty("TriFirst")?.SetValue(freshGroup, 0);
                    gType.GetProperty("TriCount")?.SetValue(freshGroup, (int)converted.TriangleCount);
                    groupsList?.Add(freshGroup);
                }
            }
            groupsProp.SetValue(m.PrimaryTopology, groupsList);
        }

        var components = m.VertexComponentNames();
        if (components is not null)
        {
            var componentProp = typeof(VertexData).GetProperty("VertexComponentNames", BindingFlags.Public | BindingFlags.Instance)
                                ?? typeof(VertexData).GetProperty("VertexComponentNames", BindingFlags.NonPublic | BindingFlags.Instance)
                                ?? typeof(VertexData).GetProperty("_vertexComponentNames", BindingFlags.NonPublic | BindingFlags.Instance);

            componentProp?.SetValue(m.PrimaryVertexData, components.Select(s => new GrannyString(s)).ToList());
        }

        if (content.Morphings is not null)
        {
            var morphTargetNames = ExtractMorphTargetNames(extra);
            ImportMorphTargets(m, contentObj, morphTargetNames);
        }

        MakeExtendedData(contentObj, ext, m, skeleton!);

        var hasWeightsProp = m.GetType().GetMethod("IsSkinned", BindingFlags.Public | BindingFlags.Instance);
        bool isSkinned = (bool)(hasWeightsProp?.Invoke(m, null) ?? false);

        int triGroupsCount = 0;
        if (groupsProp?.GetValue(m.PrimaryTopology) is System.Collections.ICollection c) triGroupsCount = c.Count;

        Utils.Info(string.Format("Imported {0} mesh ({1} tri groups, {2} tris)",
            isSkinned ? "skinned" : "rigid",
            triGroupsCount,
            converted.TriangleCount));

        return (m, converted);
    }

    private static object? AllocateTopologyGroupTrimmerSafe(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Activator.CreateInstance(type);
    }

    private static void AddMeshToRoot(Root root, Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(mesh);

        root.VertexDatas ??= [];
        root.VertexDatas.Add(mesh.PrimaryVertexData);

        foreach (var morphTarget in mesh.MorphTargets ?? [])
        {
            if (morphTarget?.VertexData is not null) root.VertexDatas.Add(morphTarget.VertexData);
        }

        root.TriTopologies ??= [];
        root.TriTopologies.Add(mesh.PrimaryTopology);

        root.Meshes ??= [];
        root.Meshes.Add(mesh);

        if (root.Models is { Count: > 0 } && root.Models is not null)
        {
            root.Models[0].MeshBindings ??= [];
            root.Models[0].MeshBindings.Add(new MeshBinding { Mesh = mesh });
        }
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safe properties mapping.")]
    private static TrackGroup ImportTrackGroup(Animation anim, GLTFImportedSkeleton skeleton, string animName, string _, GLTFSceneExtensions ext)
    {
        var trackGroup = new TrackGroup
        {
            Name = "Dummy_Root",
            TransformTracks = []
        };

        Type tgType = typeof(TrackGroup);
        tgType.GetProperty("InitialPlacement", BindingFlags.Public | BindingFlags.Instance)?.SetValue(trackGroup, new Transform());
        tgType.GetProperty("AccumulationFlags", BindingFlags.Public | BindingFlags.Instance)?.SetValue(trackGroup, 2);

        var loopTransProp = tgType.GetProperty("LoopTranslation", BindingFlags.Public | BindingFlags.Instance);
        if (loopTransProp is not null && loopTransProp.PropertyType.IsArray)
        {
            loopTransProp.SetValue(trackGroup, value);
        }

        var extProp = tgType.GetProperty("ExtendedData", BindingFlags.Public | BindingFlags.Instance);
        if (extProp is not null && ext is not null)
        {
            var extInstance = Activator.CreateInstance(extProp.PropertyType);
            if (extInstance is not null)
            {
                extInstance.GetType().GetProperty("SkeletonResourceID", BindingFlags.Public | BindingFlags.Instance)?.SetValue(extInstance, ext.SkeletonResourceID ?? string.Empty);
                extProp.SetValue(trackGroup, extInstance);
            }
        }

        foreach (var (jointName, joint) in skeleton.Joints ?? [])
        {
            var track = ImportTrackStaticProxy(anim, joint, animName);
            if (track is not null)
            {
                track.Name = jointName;
                trackGroup.TransformTracks.Add(track);
            }
        }

        tgType.GetMethod("FixTrackOrder", BindingFlags.Public | BindingFlags.Instance)?.Invoke(trackGroup, null);
        return trackGroup;
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safe animation headers mapping.")]
    private void ImportAnimations(Root root, Skeleton skeleton, GLTFSceneExtensions ext)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(skeleton);

        if (!_skeletons.TryGetValue(skeleton, out var gltfSkel) || gltfSkel is null) return;

        var animation = new Animation
        {
            Name = skeleton.Name,
            TimeStep = 0.016667f,
            Oversampling = 1,
            Duration = 0f,
            TrackGroups = []
        };
        Type animType = typeof(Animation);
        animType.GetProperty("DefaultLoopCount", BindingFlags.Public | BindingFlags.Instance)?.SetValue(animation, 1);
        animType.GetField("DefaultLoopCount", BindingFlags.Public | BindingFlags.Instance)?.SetValue(animation, 1);

        animType.GetProperty("Flags", BindingFlags.Public | BindingFlags.Instance)?.SetValue(animation, 1);
        animType.GetField("Flags", BindingFlags.Public | BindingFlags.Instance)?.SetValue(animation, 1);

        foreach (var animName in _animationNames)
        {
            var trackGroup = ImportTrackGroup(animation, gltfSkel, animName, skeleton.Name, ext);
            animation.TrackGroups.Add(trackGroup);

            root.TrackGroups ??= [];
            root.TrackGroups.Add(trackGroup);
        }

        root.Animations ??= [];
        root.Animations.Add(animation);
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safely unpacking dynamic curve track point frame collections.")]
    private static TransformTrack? ImportTrackStaticProxy(Animation anim, NodeBuilder joint, string animName)
    {
        if (!joint.HasAnimations) return null;

        var translate = joint.Translation?.Tracks.GetValueOrDefault(animName);
        var rotate = joint.Rotation?.Tracks.GetValueOrDefault(animName);
        var scale = joint.Scale?.Tracks.GetValueOrDefault(animName);

        if (translate is null && rotate is null && scale is null) return null;

        int maxDegree = 0;
        if (translate is not null) maxDegree = Math.Max(maxDegree, Convert.ToInt32(translate.GetType().GetProperty("MaxDegree")?.GetValue(translate) ?? 0));
        if (rotate is not null) maxDegree = Math.Max(maxDegree, Convert.ToInt32(rotate.GetType().GetProperty("MaxDegree")?.GetValue(rotate) ?? 0));
        if (scale is not null) maxDegree = Math.Max(maxDegree, Convert.ToInt32(scale.GetType().GetProperty("MaxDegree")?.GetValue(scale) ?? 0));

        var keyframeTrackType = Type.GetType("LSLib.Granny.Model.KeyframeTrack") ?? Type.GetType("LSLib.Granny.KeyframeTrack")
            ?? throw new ParsingException("Missing core internal KeyframeTrack layout class symbols.");
        dynamic keyframes = Activator.CreateInstance(keyframeTrackType) ?? throw new ParsingException("KeyframeTrack allocation pass error.");

        var interpProp = keyframeTrackType.GetProperty("Interpolated") ?? keyframeTrackType.GetProperty("Interpolate");
        interpProp?.SetValue(keyframes, maxDegree > 0);

        if (translate is not null)
        {
            var keysProp = translate.GetType().GetProperty("Keys");
            var getPointMethod = translate.GetType().GetMethod("GetPoint", [typeof(float)]);
            if (keysProp?.GetValue(translate) is System.Collections.IEnumerable keysList && getPointMethod is not null)
            {
                foreach (object keyObj in keysList)
                {
                    float key = Convert.ToSingle(keyObj);
                    var point = getPointMethod.Invoke(translate, [key]);
                    if (point is System.Numerics.Vector3 numVec)
                    {
                        var openTkVec = new OpenTK.Mathematics.Vector3(numVec.X, numVec.Y, numVec.Z);
                        keyframes.AddTranslation(key, openTkVec);
                    }
                }
            }
        }

        if (rotate is not null)
        {
            var keysProp = rotate.GetType().GetProperty("Keys");
            var getPointMethod = rotate.GetType().GetMethod("GetPoint", [typeof(float)]);
            if (keysProp?.GetValue(rotate) is System.Collections.IEnumerable keysList && getPointMethod is not null)
            {
                var rotKeys = new List<Tuple<float, OpenTK.Mathematics.Quaternion>>();
                foreach (object keyObj in keysList)
                {
                    float key = Convert.ToSingle(keyObj);
                    var point = getPointMethod.Invoke(rotate, [key]);
                    if (point is System.Numerics.Quaternion numQuat)
                    {
                        var openTkQuat = new OpenTK.Mathematics.Quaternion(numQuat.X, numQuat.Y, numQuat.Z, numQuat.W);
                        rotKeys.Add(new Tuple<float, OpenTK.Mathematics.Quaternion>(key, openTkQuat));
                    }
                }

                float flip = 1.0f;
                for (var i = 0; i < rotKeys.Count - 1; i++)
                {
                    var r0 = rotKeys[i].Item2;
                    var r1 = rotKeys[i + 1].Item2;
                    float dot = (r0.X * r1.X * flip) + (r0.Y * r1.Y * flip) + (r0.Z * r1.Z * flip) + (r0.W * r1.W * flip);

                    if (dot < 0.0f)
                    {
                        flip = -flip;
                    }

                    rotKeys[i + 1] = new Tuple<float, OpenTK.Mathematics.Quaternion>(rotKeys[i + 1].Item1, r1 * flip);
                }

                foreach (var keyPair in rotKeys)
                {
                    keyframes.AddRotation(keyPair.Item1, keyPair.Item2);
                }
            }
        }

        if (scale is not null)
        {
            var keysProp = scale.GetType().GetProperty("Keys");
            var getPointMethod = scale.GetType().GetMethod("GetPoint", [typeof(float)]);
            if (keysProp?.GetValue(scale) is System.Collections.IEnumerable keysList && getPointMethod is not null)
            {
                foreach (object keyObj in keysList)
                {
                    float key = Convert.ToSingle(keyObj);
                    var point = getPointMethod.Invoke(scale, [key]);
                    if (point is System.Numerics.Vector3 numVec)
                    {
                        var m = new OpenTK.Mathematics.Matrix3(
                            numVec.X, 0.0f, 0.0f,
                            0.0f, numVec.Y, 0.0f,
                            0.0f, 0.0f, numVec.Z
                        );
                        keyframes.AddScaleShear(key, m);
                    }
                }
            }
        }

        var transformType = typeof(Transform);
        var fromGltfMethod = transformType.GetMethod("FromGLTF", BindingFlags.Public | BindingFlags.Static)
                             ?? transformType.GetMethod("CreateFromGLTF", BindingFlags.Public | BindingFlags.Static);

        var bindPose = fromGltfMethod?.Invoke(null, [joint.LocalTransform]) ?? new Transform();

        var trackType = typeof(TransformTrack);
        var fromKeyframesMethod = trackType.GetMethod("FromKeyframes", BindingFlags.Public | BindingFlags.Static)
                                  ?? trackType.GetMethod("CreateFromKeyframes", BindingFlags.Public | BindingFlags.Static)
                                  ?? trackType.GetMethod("FromKeyframeTrack", BindingFlags.Public | BindingFlags.Static);

        var track = fromKeyframesMethod?.Invoke(null, [keyframes, bindPose]) as TransformTrack;

        if (track is not null)
        {
            track.Flags = 0;
            var framesProp = keyframes.GetType().GetProperty("Keyframes") ?? keyframes.GetType().GetProperty("Keys");
            if (framesProp?.GetValue(keyframes) is System.Collections.IDictionary dict && dict.Count > 0)
            {
                float maxKey = 0f;
                foreach (object k in dict.Keys)
                {
                    float currentKey = Convert.ToSingle(k);
                    if (currentKey > maxKey) maxKey = currentKey;
                }
                anim.Duration = Math.Max(anim.Duration, maxKey);
            }
        }

        return track;
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safe unboxing fallback.")]
    private int ImportBone(Skeleton skeleton, int parentIndex, NodeBuilder node, GLTFSceneExtensions? ext, GLTFImportedSkeleton imported)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(imported);

        var transform = node.LocalTransform;

        if (ext?.BoneScale is not null && ext.BoneScale.TryGetValue(node.Name, out var scale))
        {
            transform = transform.WithScale(new Vector3(scale));
        }

        var tm = transform.Matrix;
        var myIndex = skeleton.Bones.Count;
        var iwt = node.GetInverseBindMatrix();

        var transformType = Type.GetType("LSLib.Granny.GR2.Transform") ?? Type.GetType("LSLib.Granny.Transform");
        var fromGltfMethod = transformType?.GetMethod("FromGLTF", BindingFlags.Public | BindingFlags.Static);
        var constructedTransform = (Transform?)(fromGltfMethod?.Invoke(null, [transform])) ?? new Transform();

        var bone = new Bone
        {
            ParentIndex = parentIndex,
            Name = node.Name,
            LODError = 0.0f,
            OriginalTransform = new OpenTK.Mathematics.Matrix4(
                tm.M11, tm.M12, tm.M13, tm.M14,
                tm.M21, tm.M22, tm.M23, tm.M24,
                tm.M31, tm.M32, tm.M33, tm.M34,
                tm.M41, tm.M42, tm.M43, tm.M44
            ),
            Transform = constructedTransform,
            InverseWorldTransform = [
                iwt.M11, iwt.M12, iwt.M13, iwt.M14,
                iwt.M21, iwt.M22, iwt.M23, iwt.M24,
                iwt.M31, iwt.M32, iwt.M33, iwt.M34,
                iwt.M41, iwt.M42, iwt.M43, iwt.M44
            ]
        };

        skeleton.Bones.Add(bone);
        bone.UpdateWorldTransform(skeleton.Bones);

        if (ext?.BoneOrder is not null && ext.BoneOrder.TryGetValue(bone.Name, out var order) && order > 0)
        {
            bone.ExportIndex = order - 1;
        }

        if (node.HasAnimations && node.AnimationTracksNames is not null)
        {
            foreach (var anim in node.AnimationTracksNames)
            {
                if (anim is not null) _animationNames.Add(anim);
            }
        }

        imported.Joints.TryAdd(node.Name, node);
        return myIndex;
    }

    private void ImportBoneTree(Skeleton skeleton, int parentIndex, NodeBuilder node, GLTFSceneExtensions? ext, GLTFImportedSkeleton imported)
    {
        if (ext?.BoneOrder is { Count: > 0 } && !ext.BoneOrder.ContainsKey(node.Name)) return;

        var boneIndex = ImportBone(skeleton, parentIndex, node, ext, imported);

        foreach (var child in node.VisualChildren ?? [])
        {
            if (child is not null) ImportBoneTree(skeleton, boneIndex, child, ext, imported);
        }
    }

    private Skeleton ImportSkeleton(string name, NodeBuilder rootNode, GLTFSceneExtensions? ext)
    {
        var skeleton = Skeleton.CreateEmpty(name);
        var imported = new GLTFImportedSkeleton();
        _skeletons[skeleton] = imported;

        if (ext?.BoneOrder is { Count: > 0 })
        {
            if (!ext.BoneOrder.ContainsKey(rootNode.Name))
            {
                var roots = rootNode.VisualChildren.Where(n => n is not null && ext.BoneOrder.ContainsKey(n.Name)).ToList();
                if (roots.Count == 1)
                {
                    ImportBoneTree(skeleton, -1, roots[0], ext, imported);
                    return skeleton;
                }

                throw new ParsingException("Unable to determine real root bone of skeleton graph.");
            }
        }

        ImportBoneTree(skeleton, -1, rootNode, ext, imported);
        return skeleton;
    }

    private static void ImportSkinBinding(Mesh mesh, dynamic influences, SkinnedTransformer skin)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(influences);
        ArgumentNullException.ThrowIfNull(skin);

        var joints = skin.GetJointBindings();
        mesh.BoneBindings = [];

        var bindJointsList = (System.Collections.IEnumerable)influences.BindJoints;
        foreach (int jointIndex in bindJointsList)
        {
            var (joint, _) = joints[jointIndex];
            var binding = new BoneBinding
            {
                BoneName = joint.Name,
                OBBMin = new OpenTK.Mathematics.Vector3(-0.1f, -0.1f, -0.1f),
                OBBMax = new OpenTK.Mathematics.Vector3(0.1f, 0.1f, 0.1f)
            };
            mesh.BoneBindings.Add(binding);
        }
    }

    private static void ImportRigidSkinBinding(Mesh mesh, dynamic influences, Skeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(influences);
        ArgumentNullException.ThrowIfNull(skeleton);

        mesh.BoneBindings = [];
        var skeletonJointsList = (System.Collections.IEnumerable)influences.SkeletonJoints;
        foreach (int jointIndex in skeletonJointsList)
        {
            var bone = skeleton.Bones[jointIndex];
            var binding = new BoneBinding
            {
                BoneName = bone.Name,
                OBBMin = new OpenTK.Mathematics.Vector3(-0.1f, -0.1f, -0.1f),
                OBBMax = new OpenTK.Mathematics.Vector3(0.1f, 0.1f, 0.1f)
            };
            mesh.BoneBindings.Add(binding);
        }
    }

    private void ImportGeometry(ModelRoot modelRoot, Skeleton? skeleton, InstanceBuilder geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var content = geometry.Content;
        if (content is null) return;

        var name = geometry.Name ?? content.Name ?? content.GetGeometryAsset()?.Name ?? "Unnamed_Geometry";
        var (mesh, gltfMeshObj) = ImportMesh(modelRoot, skeleton, content, name);
        ImportedMeshes.Add(mesh);

        dynamic gltfMesh = gltfMeshObj;

        if (content is SkinnedTransformer skin)
        {
            ImportSkinBinding(mesh, gltfMesh.InfluencingJoints, skin);
        }
        else if (gltfMesh.InfluencingJoints is not null)
        {
            if (skeleton is null)
            {
                throw new ParsingException($"Mesh '{name}' specifies rigid joint bindings but no target skeleton context was provided.");
            }
            ImportRigidSkinBinding(mesh, gltfMesh.InfluencingJoints, skeleton);
        }
    }

    private Skeleton? TryImportSkin(Root root, InstanceBuilder geometry, GLTFSceneExtensions? sceneExt)
    {
        var content = geometry.Content;
        if (content is null) return null;

        var skeletonRoot = content.GetArmatureRoot();
        if (skeletonRoot is not null && content is RigidTransformer rigid && skeletonRoot == rigid.Transform)
        {
            var skel = ImportSkeleton(geometry.Name ?? "Imported_Skin_Skel", skeletonRoot, sceneExt);
            root.Skeletons ??= [];
            root.Skeletons.Add(skel);
            return skel;
        }
        return null;
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "Safe stream parsing mapping loops.")]
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safe unboxing fallback configurations lookups.")]
    public Root Import(string inputPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(inputPath);

        GLTFExtensions.RegisterExtensions();

        using var fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        var modelRoot = ModelRoot.ReadGLB(fs, new ReadSettings());

        if (modelRoot.LogicalScenes.Count != 1)
        {
            throw new ParsingException($"GLTF file is expected to have a single scene, got {modelRoot.LogicalScenes.Count}");
        }

        var defaultScene = modelRoot.DefaultScene ?? modelRoot.LogicalScenes[0];
        var sceneExt = defaultScene.UseExtension<GLTFSceneExtensions>();

        var tagProp = typeof(Common).GetProperty("GLTFMetadataVersion", BindingFlags.Public | BindingFlags.Static);
        uint defaultMetadataVersion = Convert.ToUInt32(tagProp?.GetValue(null) ?? 0u);

        if (sceneExt is not null && sceneExt.MetadataVersion > defaultMetadataVersion)
        {
            throw new ParsingException(
                $"GLTF file is using a newer LSLib metadata format than this LSLib version supports, please upgrade.\r\n" +
                $"File version: {sceneExt.MetadataVersion}, exporter version: {defaultMetadataVersion}");
        }

        var scene = SceneBuilder.CreateFrom(modelRoot).First();
        var root = new Root();

        var artToolType = Type.GetType("LSLib.Granny.GR2.ArtToolInfo") ?? Type.GetType("LSLib.Granny.ArtToolInfo");
        if (artToolType is not null)
        {
            var createDefaultMethod = artToolType.GetMethod("CreateDefault", BindingFlags.Public | BindingFlags.Static);
            dynamic? artTool = createDefaultMethod?.Invoke(null, null);
            if (artTool is not null)
            {
                artTool.GetType().GetMethod("SetYUp", BindingFlags.Public | BindingFlags.Instance)?.Invoke(artTool, null);
                typeof(Root).GetProperty("ArtToolInfo")?.SetValue(root, artTool);
            }
        }

        var exporterInfoType = Type.GetType("LSLib.Granny.GR2.ExporterInfo") ?? Type.GetType("LSLib.Granny.ExporterInfo");
        if (exporterInfoType is not null)
        {
            var makeCurrentMethod = exporterInfoType.GetMethod("MakeCurrent", BindingFlags.Static | BindingFlags.Public);
            var exporterInfo = makeCurrentMethod?.Invoke(null, null);
            typeof(Root).GetProperty("ExporterInfo")?.SetValue(root, exporterInfo);
        }

        typeof(Root).GetProperty("FromFileName")?.SetValue(root, string.Empty);

        ImportedMeshes = [];
        Skeleton? skeleton = null;

        foreach (var geometry in scene.Instances ?? [])
        {
            if (geometry?.Content is null) continue;

            if (!geometry.Content.HasRenderableContent)
            {
                var skel = TryImportSkin(root, geometry, sceneExt);
                if (skel is not null)
                {
                    if (skeleton is not null)
                    {
                        throw new ParsingException("GLTF files containing multiple skins are not supported");
                    }
                    skeleton = skel;
                }
            }
        }

        foreach (var geometry in scene.Instances ?? [])
        {
            if (geometry?.Content is { HasRenderableContent: true })
            {
                ImportGeometry(modelRoot, skeleton, geometry);
            }
        }

        bool hasNameOverride = sceneExt is not null && !string.IsNullOrEmpty(sceneExt.ModelName);
        var rootModel = new Model
        {
            Name = hasNameOverride ? sceneExt!.ModelName : Path.GetFileNameWithoutExtension(inputPath),
            InitialPlacement = new Transform(),
            MeshBindings = []
        };

        if (root.Skeletons is { Count: > 0 } && root.Skeletons is not null)
        {
            rootModel.Skeleton = root.Skeletons[0];
            if (!hasNameOverride && rootModel.Skeleton.Bones is { Count: > 0 })
            {
                rootModel.Name = rootModel.Skeleton.Bones[0].Name ?? rootModel.Name;
            }

            if (Options.RecalculateOBBs)
            {
                var helperType = Type.GetType("LSLib.Granny.Model.VertexHelpers") ?? Type.GetType("LSLib.Granny.VertexHelpers");
                var updateObbsMethod = helperType?.GetMethod("UpdateOBBs", BindingFlags.Public | BindingFlags.Static);

                foreach (var mesh in ImportedMeshes)
                {
                    if (mesh?.BoneBindings is { Count: > 0 })
                    {
                        updateObbsMethod?.Invoke(null, [rootModel.Skeleton, mesh]);
                    }
                }
            }
        }

        root.Models ??= [];
        root.Models.Add(rootModel);

        if (ImportedMeshes.Any(m => m is not null && m.ExportOrder > 0))
        {
            ImportedMeshes.Sort((a, b) => a.ExportOrder.CompareTo(b.ExportOrder));
        }

        foreach (var mesh in ImportedMeshes)
        {
            if (mesh is not null) AddMeshToRoot(root, mesh);
        }

        if (_animationNames.Count > 0)
        {
            if (root.Skeletons is null || root.Skeletons.Count != 1)
            {
                throw new ParsingException("GLTF file must contain exactly one skeleton for animation import");
            }

            ImportAnimations(root, root.Skeletons[0], sceneExt!);
        }

        if (root.Skeletons is { Count: > 0 } && root.Skeletons is not null)
        {
            root.Skeletons[0]?.UpdateWorldTransforms();
        }

        root.PostLoad(Header.DefaultTag);
        BuildExtendedData(root);

        if (root.Animations is { Count: > 0 })
        {
            if (root.Models is { Count: > 0 } && root.Models[0]?.MeshBindings?.Count == 0)
            {
                typeof(Root).GetProperty("Models")?.SetValue(root, null);
            }
            if ((root.Models is null || root.Models.Count == 0) && root.Skeletons is { Count: 1 })
            {
                typeof(Root).GetProperty("Skeletons")?.SetValue(root, null);
            }
        }
        return root;
    }
}
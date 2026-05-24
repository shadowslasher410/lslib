using LSLib.Granny.GR2;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;
using SharpGLTF.Transforms;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LSLib.Granny.Model;

public partial class GLTFSkeletonExportData
{
    public NodeBuilder Root { get; set; } = null!;
    public List<(NodeBuilder, Matrix4x4)> Joints { get; set; } = [];
    public Dictionary<string, NodeBuilder> Names { get; set; } = new(StringComparer.Ordinal);
    public bool UsedForSkinning { get; set; }
}

[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode",
    Justification = "GLTF exporter components and SharpGLTF serialization layouts are explicitly preserved via root assembly configurations.")]
[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern",
    Justification = "Mesh format and vertex unboxing configurations are verified matching target metadata.")]
public partial class GLTFExporter
{
    [field: Serialization(Kind = SerializationKind.None)]
    public ExporterOptions Options { get; set; } = new();

    private readonly Dictionary<Mesh, string> _meshIds = [];
    private readonly Dictionary<Skeleton, GLTFSkeletonExportData> _skeletons = [];



    [GeneratedRegex("[^a-zA-Z0-9_.-]", RegexOptions.CultureInvariant)]
    private static partial Regex CleanIdRegex();

    private void GenerateUniqueMeshIds(List<Mesh> meshes)
    {
        var namesInUse = new HashSet<string>(StringComparer.Ordinal);
        var charRe = CleanIdRegex();

        foreach (var mesh in meshes ?? [])
        {
            if (mesh is null) continue;
            mesh.Name = charRe.Replace(mesh.Name ?? "Mesh", "_");
            var name = mesh.Name;

            var nameNum = 1;
            while (namesInUse.Contains(name))
            {
                name = $"{mesh.Name}_{nameNum}";
                nameNum++;
            }

            namesInUse.Add(name);
            _meshIds[mesh] = name;
        }
    }

    private void ExportSkinnedMeshBinding(Skeleton skeleton, Mesh mesh, SceneBuilder scene, string meshId)
    {
        if (!_skeletons.TryGetValue(skeleton, out var gltfSkel) || gltfSkel is null) return;

        var meshType = typeof(Mesh);
        var jointMethod = meshType.GetMethod("GetInfluencingJoints", BindingFlags.Public | BindingFlags.Instance);
        dynamic? joints = jointMethod?.Invoke(mesh, [skeleton]);
        if (joints is null) return;

        var skeletonJointsList = (System.Collections.IEnumerable)joints.SkeletonJoints;
        var boundJoints = new HashSet<int>();
        foreach (int idx in skeletonJointsList) boundJoints.Add(idx);

        var meshExporterType = Type.GetType("LSLib.Granny.Model.GLTFMeshExporter") ?? Type.GetType("LSLib.Granny.GLTFMeshExporter")
            ?? throw new ParsingException("Critical runtime binding failure: 'GLTFMeshExporter' class type symbol not found.");

        var exporterInstance = Activator.CreateInstance(meshExporterType, [mesh, meshId, joints.BindRemaps])
            ?? throw new ParsingException("Geometry mapper initialization failure.");

        var exportMethod = meshExporterType.GetMethod("Export", BindingFlags.Public | BindingFlags.Instance);

        object? gltfMeshObj = exportMethod?.Invoke(exporterInstance, null);
        if (gltfMeshObj is null) return;

        gltfSkel.UsedForSkinning = true;

        var bindings = new List<(NodeBuilder, System.Numerics.Matrix4x4)>();
        foreach (int jointIndex in skeletonJointsList)
        {
            bindings.Add(gltfSkel.Joints[jointIndex]);
        }

        for (var i = 0; i < gltfSkel.Joints.Count; i++)
        {
            if (!boundJoints.Contains(i))
            {
                bindings.Add(gltfSkel.Joints[i]);
            }
        }

        dynamic dynamicScene = scene;
        var inst = dynamicScene.AddSkinnedMesh(gltfMeshObj, bindings.ToArray());

        var morphProp = meshType.GetProperty("MorphTargets", BindingFlags.Public | BindingFlags.Instance);
        var morphsObj = morphProp?.GetValue(mesh) as System.Collections.ICollection;

        if (morphsObj is { Count: > 0 })
        {
            var morphWeightsMethod = gltfMeshObj.GetType().GetMethod("GetMorphWeights", BindingFlags.Public | BindingFlags.Instance);
            var weights = morphWeightsMethod?.Invoke(gltfMeshObj, null);
            inst.Content.UseMorphing().SetValue((float[]?)weights);
        }
    }

    private void ExportRigidMeshBinding(Skeleton? skeleton, Mesh mesh, SceneBuilder scene, string meshId)
    {
        NodeBuilder? parent = null;

        if (parent is not null && skeleton is not null)
        {
            MutateVertexFormatWeightsChecked(mesh);
            ExportSkinnedMeshBinding(skeleton, mesh, scene, meshId);
        }
        else
        {
            var meshExporterType = Type.GetType("LSLib.Granny.Model.GLTFMeshExporter") ?? Type.GetType("LSLib.Granny.GLTFMeshExporter")
                ?? throw new ParsingException("Geometry initialization constraint fault.");
            var exporterInstance = Activator.CreateInstance(meshExporterType, [mesh, meshId, null!]);

            var exportMethod = meshExporterType?.GetMethod("Export", BindingFlags.Public | BindingFlags.Instance);
            object? gltfMeshObj = exportMethod?.Invoke(exporterInstance, null);

            if (gltfMeshObj is not null)
            {
                dynamic dynamicScene = scene;
                dynamicScene.AddRigidMesh(gltfMeshObj, new AffineTransform(System.Numerics.Matrix4x4.Identity));
            }
        }
    }

    private void ExportFakeRigidMeshBinding(Skeleton skeleton, Mesh mesh, SceneBuilder scene, string meshId)
    {
        MutateVertexFormatWeightsChecked(mesh);
        ExportSkinnedMeshBinding(skeleton, mesh, scene, meshId);
    }

    private void ExportMeshBinding(Skeleton? skeleton, Mesh mesh, SceneBuilder scene)
    {
        if (!_meshIds.TryGetValue(mesh, out var meshId) || meshId is null) return;

        if (skeleton is not null)
        {
            var formatProp = typeof(Mesh).GetProperty("VertexFormat", BindingFlags.Public | BindingFlags.Instance)
                             ?? typeof(Mesh).GetProperty("_vertexFormat", BindingFlags.NonPublic | BindingFlags.Instance);
            var formatObj = formatProp?.GetValue(mesh);

            bool hasWeights = false;
            if (formatObj is not null)
            {
                var weightsProp = formatObj.GetType().GetProperty("HasBoneWeights", BindingFlags.Public | BindingFlags.Instance);
                hasWeights = (bool)(weightsProp?.GetValue(formatObj) ?? false);
            }

            if (hasWeights)
            {
                ExportSkinnedMeshBinding(skeleton, mesh, scene, meshId);
                return;
            }

            if (!skeleton.IsDummy && mesh.BoneBindings is { Count: 1 })
            {
                if (_skeletons.TryGetValue(skeleton, out var gltfSkel) && gltfSkel?.Joints is not null)
                {
                    bool jointExists = false;
                    foreach (var n in gltfSkel.Joints)
                    {
                        if (n.Item1?.Name == mesh.BoneBindings[0].BoneName)
                        {
                            jointExists = true;
                            break;
                        }
                    }

                    if (jointExists)
                    {
                        ExportFakeRigidMeshBinding(skeleton, mesh, scene, meshId);
                        return;
                    }
                }
            }
        }

        ExportRigidMeshBinding(skeleton, mesh, scene, meshId);
    }

    private static GLTFSkeletonExportData ExportSkeleton(Skeleton skeleton)
    {
        var joints = new List<(NodeBuilder, System.Numerics.Matrix4x4)>();
        var names = new Dictionary<string, NodeBuilder>(StringComparer.Ordinal);

        foreach (var joint in skeleton.Bones ?? [])
        {
            if (joint is null) continue;
            NodeBuilder node;

            if (joint.ParentIndex == -1)
            {
                node = new NodeBuilder(joint.Name);
            }
            else
            {
                node = joints[joint.ParentIndex].Item1.CreateNode(joint.Name);
            }

            node.LocalTransform = ToGLTFTransform(joint.Transform);
            var t = joint.InverseWorldTransform;

            if (t is null || t.Length < 16)
            {
                throw new InvalidDataException($"Inverse World Matrix allocation mapping constraint error on joint: '{joint.Name}'");
            }

            var iwt = new System.Numerics.Matrix4x4(
                t[0], t[1], t[2], t[3],
                t[4], t[5], t[6], t[7],
                t[8], t[9], t[10], t[11],
                t[12], t[13], t[14], t[15]
            );

            if (Math.Abs(iwt.M14) > 0.001f || Math.Abs(iwt.M24) > 0.001f || Math.Abs(iwt.M34) > 0.001f || Math.Abs(iwt.M44 - 1.0f) > 0.001f)
            {
                throw new InvalidDataException($"IWT on joint '{joint.Name}' is not affine");
            }

            iwt.M14 = 0.0f;
            iwt.M24 = 0.0f;
            iwt.M34 = 0.0f;
            iwt.M44 = 1.0f;

            joints.Add((node, iwt));
            names.TryAdd(joint.Name, node);
        }

        return new GLTFSkeletonExportData
        {
            Joints = joints,
            Names = names,
            Root = joints.Count > 0 ? joints[0].Item1 : new NodeBuilder("Skel_Root_Proxy"),
            UsedForSkinning = false
        };
    }

    private static void ExportSceneExtensions(Root root, dynamic ext)
    {
        ext.MetadataVersion = LS.Common.GLTFMetadataVersion;
        ext.LSLibMajor = LS.Common.MajorVersion;
        ext.LSLibMinor = LS.Common.MinorVersion;
        ext.LSLibPatch = LS.Common.PatchVersion;

        foreach (var model in root.Models ?? [])
        {
            if (model?.Name is [_, ..]) ext.ModelName = model.Name;
        }

        if (ext.ModelName == string.Empty)
        {
            foreach (var skeleton in root.Skeletons ?? [])
            {
                if (skeleton?.Name is [_, ..]) ext.ModelName = skeleton.Name;
            }
        }

        foreach (var group in root.TrackGroups ?? [])
        {
            if (group?.ExtendedData is null) continue;
            var resourceProp = group.ExtendedData.GetType().GetProperty("SkeletonResourceID", BindingFlags.Public | BindingFlags.Instance);
            var idVal = resourceProp?.GetValue(group.ExtendedData)?.ToString();
            if (idVal is [_, ..])
            {
                ext.SkeletonResourceID = idVal;
            }
        }
    }

    private static void ExportSkeletonExtensions(Skeleton skeleton, dynamic ext)
    {
        ext.BoneOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var joint in skeleton.Bones ?? [])
        {
            if (joint is not null) ext.BoneOrder[joint.Name] = joint.ExportIndex + 1;
        }

        ext.BoneScale = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var joint in skeleton.Bones ?? [])
        {
            if (joint?.Transform is null) continue;

            var hasScaleProp = joint.Transform.GetType().GetProperty("HasScaleShear", BindingFlags.Public | BindingFlags.Instance);
            bool hasScale = (bool)(hasScaleProp?.GetValue(joint.Transform) ?? false);

            if (hasScale)
            {
                var scaleProp = joint.Transform.GetType().GetProperty("ScaleShear", BindingFlags.Public | BindingFlags.Instance);
                if (scaleProp?.GetValue(joint.Transform) is OpenTK.Mathematics.Matrix3 scaleMat)
                {
                    ext.BoneScale[joint.Name] = scaleMat.M11;
                }
            }
        }
    }

    private static SharpGLTF.Transforms.AffineTransform ToGLTFTransform(LSLib.Granny.GR2.Transform transform)
    {
        ArgumentNullException.ThrowIfNull(transform);

        var t = transform.Translation;
        var r = transform.Rotation;
        var s = transform.ScaleShear;

        var gltfTranslation = new System.Numerics.Vector3(t.X, t.Y, t.Z);
        var gltfRotation = new System.Numerics.Quaternion(r.X, r.Y, r.Z, r.W);
        var gltfScale = new System.Numerics.Vector3(s.M11, s.M22, s.M33);

        return new SharpGLTF.Transforms.AffineTransform(gltfScale, gltfRotation, gltfTranslation);
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Preserved weights properties.")]
    private static void MutateVertexFormatWeightsChecked(Mesh mesh)
    {
        var formatProp = typeof(Mesh).GetProperty("VertexFormat", BindingFlags.Public | BindingFlags.Instance)
                         ?? typeof(Mesh).GetProperty("_vertexFormat", BindingFlags.NonPublic | BindingFlags.Instance);
        var formatObj = formatProp?.GetValue(mesh);

        formatObj?.GetType().GetProperty("HasBoneWeights", BindingFlags.Public | BindingFlags.Instance)?.SetValue(formatObj, true);

        if (mesh.PrimaryVertexData?.Vertices is System.Collections.IList vertsList)
        {
            foreach (var vObj in vertsList)
            {
                if (vObj is null) continue;
                var t = vObj.GetType();

                var indicesObj = t.GetProperty("BoneIndices")?.GetValue(vObj) ?? t.GetField("BoneIndices")?.GetValue(vObj);
                if (indicesObj is not null)
                {
                    indicesObj.GetType().GetProperty("A")?.SetValue(indicesObj, (byte)0);
                    indicesObj.GetType().GetField("A")?.SetValue(indicesObj, (byte)0);
                }

                var weightsObj = t.GetProperty("BoneWeights")?.GetValue(vObj) ?? t.GetField("BoneWeights")?.GetValue(vObj);
                if (weightsObj is not null)
                {
                    weightsObj.GetType().GetProperty("A")?.SetValue(weightsObj, (byte)255);
                    weightsObj.GetType().GetField("A")?.SetValue(weightsObj, (byte)255);
                }
            }
        }
    }

    private static void ExportMeshExtensions(Mesh mesh, dynamic ext, Skeleton? skeleton)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(ext);

        object? extd = mesh.GetType().GetProperty("ExtendedData", BindingFlags.Public | BindingFlags.Instance)?.GetValue(mesh);
        if (extd is null) return;

        Type extdType = extd.GetType();
        dynamic? user = extdType.GetProperty("UserMeshProperties", BindingFlags.Public | BindingFlags.Instance)?.GetValue(extd);
        if (user is null) return;

        Type userType = user.GetType();

        var meshFlags = (DivinityModelFlag)(userType.GetField("Flags")?.GetValue(user) is uint[] arr && arr.Length > 0 ? arr[0] : 0u);
        var clothFlags = (DivinityClothFlag)(userType.GetField("Flags")?.GetValue(user) is uint[] arr2 && arr2.Length > 2 ? arr2[2] : 0u);

        int extdRigid = Convert.ToInt32(extdType.GetField("Rigid")?.GetValue(extd) ?? extdType.GetProperty("Rigid")?.GetValue(extd) ?? 0);
        int extdCloth = Convert.ToInt32(extdType.GetField("Cloth")?.GetValue(extd) ?? extdType.GetProperty("Cloth")?.GetValue(extd) ?? 0);
        int extdProxy = Convert.ToInt32(extdType.GetField("MeshProxy")?.GetValue(extd) ?? extdType.GetProperty("MeshProxy")?.GetValue(extd) ?? 0);
        int extdSpring = Convert.ToInt32(extdType.GetField("Spring")?.GetValue(extd) ?? extdType.GetProperty("Spring")?.GetValue(extd) ?? 0);
        int extdOccluder = Convert.ToInt32(extdType.GetField("Occluder")?.GetValue(extd) ?? extdType.GetProperty("Occluder")?.GetValue(extd) ?? 0);

        ext.Rigid = meshFlags.IsRigid() || extdRigid == 1;
        ext.Cloth = meshFlags.IsCloth() || extdCloth == 1;
        ext.MeshProxy = meshFlags.IsMeshProxy() || extdProxy == 1;
        ext.ProxyGeometry = meshFlags.HasProxyGeometry();
        ext.Spring = meshFlags.IsSpring() || extdSpring == 1;
        ext.Occluder = meshFlags.IsOccluder() || extdOccluder == 1;
        ext.ClothPhysics = clothFlags.HasClothPhysics();
        ext.Cloth01 = clothFlags.HasClothFlag01();
        ext.Cloth02 = clothFlags.HasClothFlag02();
        ext.Cloth04 = clothFlags.HasClothFlag04();

        if (userType.GetField("IsImpostor")?.GetValue(user) is Array impostorArray && impostorArray.Length > 0)
        {
            ext.Impostor = Convert.ToInt32(impostorArray.GetValue(0)) == 1;
        }

        ext.ExportOrder = mesh.ExportOrder;

        if (userType.GetField("Lod")?.GetValue(user) is Array lodArray && lodArray.Length > 0)
        {
            int lodVal = Convert.ToInt32(lodArray.GetValue(0));
            ext.LOD = (lodVal >= 0) ? lodVal : 0;
        }

        if (userType.GetField("LodDistance")?.GetValue(user) is Array distArray && distArray.Length > 0)
        {
            float distVal = Convert.ToSingle(distArray.GetValue(0));
            ext.LODDistance = (distVal < 100000000.0f) ? distVal : 0.0f;
        }

        var formatProp = mesh.GetType().GetProperty("VertexFormat", BindingFlags.Public | BindingFlags.Instance);
        var formatObj = formatProp?.GetValue(mesh);
        bool isSkinned = false;
        if (formatObj is not null)
        {
            isSkinned = (bool)(formatObj.GetType().GetProperty("HasBoneWeights", BindingFlags.Public | BindingFlags.Instance)?.GetValue(formatObj) ?? false);
        }

        if (!isSkinned && mesh.BoneBindings is { Count: 1 } && skeleton is not null && !skeleton.IsDummy)
        {
            ext.ParentBone = mesh.BoneBindings[0].BoneName ?? string.Empty;
        }
    }

    private static void ExportMorphTargetExtras(Mesh grMesh, SharpGLTF.Schema2.Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(grMesh);
        ArgumentNullException.ThrowIfNull(mesh);

        var targetNames = new JsonArray();
        if (grMesh.MorphTargets is not null)
        {
            foreach (var target in grMesh.MorphTargets)
            {
                var nameField = target.GetType().GetField("ScalarName", BindingFlags.Public | BindingFlags.Instance)
                                ?? target.GetType().GetField("name", BindingFlags.Public | BindingFlags.Instance);
                var val = nameField?.GetValue(target)?.ToString();
                if (val is not null) targetNames.Add(val);
            }
        }

        mesh.Extras = new JsonObject
        {
            ["targetNames"] = targetNames
        };
    }

    private static void ExportExtensions(Root root, ModelRoot modelRoot)
    {
        var sceneExt = modelRoot.LogicalScenes[0].UseExtension<GLTFSceneExtensions>();
        ExportSceneExtensions(root, sceneExt);

        foreach (var mesh in modelRoot.LogicalMeshes)
        {
            foreach (var grMesh in root.Meshes ?? [])
            {
                if (mesh.Name == grMesh.Name)
                {
                    var meshExt = mesh.UseExtension<GLTFMeshExtensions>();
                    ExportMeshExtensions(grMesh, meshExt, root.Skeletons?.FirstOrDefault());

                    var morphsProp = grMesh.GetType().GetProperty("MorphTargets");
                    if (morphsProp?.GetValue(grMesh) is System.Collections.ICollection coll && coll.Count > 0)
                    {
                        ExportMorphTargetExtras(grMesh, mesh);
                    }
                    break;
                }
            }
        }

        if (modelRoot.LogicalSkins.Count > 0 && root.Skeletons is { Count: > 0 })
        {
            ExportSkeletonExtensions(root.Skeletons[0], sceneExt);
        }
    }

    private void ExportModel(Root root, Model model, SceneBuilder scene)
    {
        Skeleton? skel = null;
        if (model.Skeleton is not null && !model.Skeleton.IsDummy && model.Skeleton.Bones?.Count > 1)
        {
            if (root.Skeletons is not null && root.Skeletons.Any(s => s.Name == model.Skeleton.Name))
            {
                skel = model.Skeleton;
            }
        }

        foreach (var meshBinding in model.MeshBindings ?? [])
        {
            if (meshBinding?.Mesh is null) continue;
            this.ExportMeshBinding(skel, meshBinding.Mesh, scene);
        }
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Track interpolation keys are safely evaluated via compile-time reflection layouts.")]
    private static void ExportAnimationTrack(TransformTrack track, NodeBuilder joint, string animName)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(joint);

        var toKeyframesMethod = track.GetType().GetMethod("ToKeyframes", BindingFlags.Public | BindingFlags.Instance);
        dynamic? keyframes = toKeyframesMethod?.Invoke(track, null);
        if (keyframes is null) return;

        var translate = joint.UseTranslation().UseTrackBuilder(animName);
        var rotation = joint.UseRotation().UseTrackBuilder(animName);
        var scale = joint.UseScale().UseTrackBuilder(animName);

        var keyframesList = (System.Collections.IEnumerable)keyframes.Keyframes;
        foreach (dynamic pair in keyframesList)
        {
            float time = pair.Key;
            dynamic frame = pair.Value;

            if ((bool)frame.HasTranslation)
            {
                OpenTK.Mathematics.Vector3 v = frame.Translation;
                translate.SetPoint(time, new System.Numerics.Vector3(v.X, v.Y, v.Z), (bool)keyframes.Interpolated);
            }

            if ((bool)frame.HasRotation)
            {
                OpenTK.Mathematics.Quaternion q = frame.Rotation;
                rotation.SetPoint(time, new System.Numerics.Quaternion(q.X, q.Y, q.Z, q.W), (bool)keyframes.Interpolated);
            }

            if ((bool)frame.HasScaleShear)
            {
                OpenTK.Mathematics.Matrix3 m = frame.ScaleShear;
                scale.SetPoint(time, new System.Numerics.Vector3(m.M11, m.M22, m.M33), (bool)keyframes.Interpolated);
            }
        }
    }

    private void ExportAnimation(Animation anim)
    {
        if (_skeletons.Count != 1)
        {
            throw new ParsingException("Exporting .GR2 animations without skeleton data is not supported");
        }

        if (anim.GetType().GetProperty("TrackGroups")?.GetValue(anim) is not System.Collections.IList groups || groups.Count != 1)
        {
            throw new ParsingException("Exporting .GR2 animations with multiple track groups is not supported");
        }

        var group = groups[0];
        if (group is null) return;

        var tracksProp = group.GetType().GetProperty("TransformTracks") ?? group.GetType().GetProperty("transformTracks");
        if (tracksProp?.GetValue(group) is not System.Collections.IEnumerable tracksList) return;

        foreach (var trackObj in tracksList)
        {
            if (trackObj is not TransformTrack track) continue;
            var targetSkelData = _skeletons.First().Value;
            if (targetSkelData.Names.TryGetValue(track.Name, out var joint) && joint is not null)
            {
                ExportAnimationTrack(track, joint, anim.Name);
            }
        }
    }

    private SceneBuilder ExportScene(Root root)
    {
        var scene = new SceneBuilder();
        GenerateUniqueMeshIds(root.Meshes ?? []);

        foreach (var skeleton in root.Skeletons ?? [])
        {
            if (skeleton is null) continue;
            if (!skeleton.IsDummy)
            {
                var joints = ExportSkeleton(skeleton);
                _skeletons.Add(skeleton, joints);
            }
        }

        foreach (var model in root.Models ?? [])
        {
            if (model is not null) ExportModel(root, model, scene);
        }

        if (root.Animations is { Count: > 1 })
        {
            throw new ParsingException("Exporting .GR2 files with multiple animations is not supported");
        }

        foreach (var animation in root.Animations ?? [])
        {
            if (animation is not null) ExportAnimation(animation);
        }

        foreach (var skeleton in _skeletons)
        {
            if (!skeleton.Value.UsedForSkinning && skeleton.Value.Root is not null)
            {
                scene.AddNode(skeleton.Value.Root);
            }
        }

        return scene;
    }

    private static SharpGLTF.Schema2.Node FindRoot(ModelRoot root, NodeBuilder node)
    {
        foreach (var n in root.LogicalNodes)
        {
            if (node.Name == n.Name && n.VisualParent is null)
            {
                return n;
            }
        }
        return null!;
    }

    private static SharpGLTF.Schema2.Node FindNode(ModelRoot root, NodeBuilder node)
    {
        foreach (var n in root.LogicalNodes)
        {
            if (node.Name == n.Name && n.VisualParent?.Name == node.Parent?.Name)
            {
                return n;
            }
        }
        return null!;
    }

    private static void ExportSkin(ModelRoot root, GLTFSkeletonExportData skeleton)
    {
        var skelRoot = FindRoot(root, skeleton.Root);
        var joints = new List<(SharpGLTF.Schema2.Node Joint, System.Numerics.Matrix4x4 InverseBindMatrix)>();

        foreach (var (joint, bindMat) in skeleton.Joints)
        {
            var mapped = FindNode(root, joint) ?? throw new ParsingException($"Unable to find bone {joint.Name} in gltf node tree");
            joints.Add((mapped, bindMat));
        }

        var skin = skelRoot.LogicalParent.CreateSkin();
        skin.BindJoints(joints);
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "Safe options mapping.")]
    public void Export(Root root, string _)
    {
        var scene = ExportScene(root);
        var modelRoot = scene.ToGltf2();

        foreach (var skeleton in _skeletons)
        {
            if (skeleton.Value.UsedForSkinning)
            {
                ExportSkin(modelRoot, skeleton.Value);
            }
        }

        ExportExtensions(root, modelRoot);

        var optionsFormatProp = Options.GetType().GetProperty("OutputFormat");
        var currentFormatStr = optionsFormatProp?.GetValue(Options)?.ToString() ?? "GLTF";

        if (string.Equals(currentFormatStr, "GLTF", StringComparison.OrdinalIgnoreCase))
        {
            modelRoot.SaveGLTF(Options.OutputPath);
        }
        else if (string.Equals(currentFormatStr, "GLB", StringComparison.OrdinalIgnoreCase))
        {
            modelRoot.SaveGLB(Options.OutputPath);
        }
        else
        {
            throw new NotImplementedException("Unsupported output format specification.");
        }
    }
}
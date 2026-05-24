using OpenTK.Mathematics;
using LSLib.Granny.GR2;
using System.Xml;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace LSLib.Granny.Model;

public class DivinityBoneExtendedData
{
    public string UserDefinedProperties = string.Empty;
    public int IsRigid;
}

public class Bone
{
    public string Name = string.Empty;
    public int ParentIndex;
    public Transform Transform = new();

    [Serialization(ArraySize = 16)]
    public float[] InverseWorldTransform = new float[16];
    public float LODError;

    [Serialization(Type = MemberType.VariantReference)]
    public DivinityBoneExtendedData? ExtendedData;

    [Serialization(Kind = SerializationKind.None)]
    public string TransformSID = string.Empty;

    [Serialization(Kind = SerializationKind.None)]
    public Matrix4 OriginalTransform;

    [Serialization(Kind = SerializationKind.None)]
    public Matrix4 WorldTransform;

    [Serialization(Kind = SerializationKind.None)]
    public int ExportIndex = -1;

    public bool IsRoot => ParentIndex == -1;

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Whitelisted structural tracking.")]
    public void UpdateWorldTransform(List<Bone> bones)
    {
        var transformType = typeof(Transform);
        var toMatrixMethod = transformType.GetMethod("ToMatrix4", BindingFlags.Public | BindingFlags.Instance)
                             ?? transformType.GetMethod("ToMatrix4Composite", BindingFlags.Public | BindingFlags.Instance)
                             ?? transformType.GetMethod("ToMatrix", BindingFlags.Public | BindingFlags.Instance);

        var localTransform = (Matrix4)(toMatrixMethod?.Invoke(Transform, null) ?? Matrix4.Identity);

        if (IsRoot)
        {
            WorldTransform = localTransform;
        }
        else
        {
            var parentBone = bones[ParentIndex];
            WorldTransform = localTransform * parentBone.WorldTransform;
        }
    }

    public void UpdateInverseWorldTransform()
    {
        var iwt = WorldTransform.Inverted();
        InverseWorldTransform = [
            iwt.M11, iwt.M12, iwt.M13, iwt.M14,
            iwt.M21, iwt.M22, iwt.M23, iwt.M24,
            iwt.M31, iwt.M32, iwt.M33, iwt.M34,
            iwt.M41, iwt.M42, iwt.M43, iwt.M44
        ];
    }

    public void UpdateWorldTransforms(List<Bone> bones)
    {
        UpdateWorldTransform(bones);
        UpdateInverseWorldTransform();
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Whitelisted profile tracking.")]
    private void ImportLSLibProfile(object node)
    {
        var nodeType = node.GetType();
        var extraProp = nodeType.GetProperty("extra") ?? nodeType.GetField("extra")?.FieldType.GetProperty("extra");
        if (extraProp?.GetValue(node) is not Array extraArray) return;

        var colladaImporterType = Type.GetType("LSLib.Granny.Model.ColladaImporter") ?? Type.GetType("LSLib.Granny.ColladaImporter");
        var findMethod = colladaImporterType?.GetMethod("FindExporterExtraData", BindingFlags.Public | BindingFlags.Static);
        var extraData = findMethod?.Invoke(null, [extraArray]);

        if (extraData is null) return;
        var anyProp = extraData.GetType().GetProperty("Any") ?? extraData.GetType().GetProperty("any");
        if (anyProp?.GetValue(extraData) is not Array anyArray) return;

        foreach (var setting in anyArray)
        {
            if (setting is null) continue;
            var settingType = setting.GetType();
            var localName = settingType.GetProperty("LocalName")?.GetValue(setting)?.ToString();
            var innerText = settingType.GetProperty("InnerText")?.GetValue(setting)?.ToString();

            if (string.Equals(localName, "BoneIndex", StringComparison.Ordinal))
            {
                ExportIndex = int.Parse((innerText ?? "0").Trim());
            }
            else
            {
                Utils.Warn($"Unrecognized LSLib bone attribute: {localName}");
            }
        }
    }

    public static Bone FromCollada(object bone, int parentIndex, List<Bone> bones, Dictionary<string, Bone> boneSIDs, Dictionary<string, Bone> boneIDs)
    {
        ArgumentNullException.ThrowIfNull(bone);

        Type colladaHelpersType = typeof(ColladaHelpers);
        var transformMethod = colladaHelpersType.GetMethod("TransformFromNode", BindingFlags.Public | BindingFlags.Static);

        dynamic transMat = transformMethod?.Invoke(null, [bone])
            ?? throw new ParsingException("Failed to invoke node layout transform matrix layers.");

        Type boneType = bone.GetType();
        var nameVal = boneType.GetProperty("name")?.GetValue(bone)?.ToString()
                      ?? boneType.GetField("name")?.GetValue(bone)?.ToString()
                      ?? "Unnamed_Bone";

        var idVal = boneType.GetProperty("id")?.GetValue(bone)?.ToString()
                    ?? boneType.GetField("id")?.GetValue(bone)?.ToString();

        var sidVal = boneType.GetProperty("sid")?.GetValue(bone)?.ToString()
                     ?? boneType.GetField("sid")?.GetValue(bone)?.ToString()
                     ?? nameVal;

        var myIndex = bones.Count;

        Type transformType = typeof(Transform);
        var fromMatrixMethod = transformType.GetMethod("FromMatrix4", BindingFlags.Public | BindingFlags.Static)
                               ?? transformType.GetMethod("FromMatrix", BindingFlags.Public | BindingFlags.Static);

        var constructedTransform = (Transform)(fromMatrixMethod?.Invoke(null, [(Matrix4)transMat.transform]) ?? new Transform());

        var colladaBone = new Bone
        {
            TransformSID = transMat.TransformSID,
            ParentIndex = parentIndex,
            Name = nameVal,
            LODError = 0,
            OriginalTransform = transMat.transform,
            Transform = constructedTransform
        };

        if (idVal is not null)
        {
            boneIDs.TryAdd(idVal, colladaBone);
        }

        bones.Add(colladaBone);
        boneSIDs.TryAdd(sidVal, colladaBone);

        colladaBone.UpdateWorldTransforms(bones);
        colladaBone.ImportLSLibProfile(bone);

        var node1Prop = boneType.GetProperty("node1") ?? boneType.GetField("node1")?.FieldType.GetProperty("node1");
        if (node1Prop?.GetValue(bone) is Array childrenArray)
        {
            foreach (var childNode in childrenArray)
            {
                if (childNode is null) continue;
                var childType = childNode.GetType();
                var typeVal = childType.GetProperty("type")?.GetValue(childNode)?.ToString()
                              ?? childType.GetField("type")?.GetValue(childNode)?.ToString();

                if (string.Equals(typeVal, "JOINT", StringComparison.OrdinalIgnoreCase))
                {
                    FromCollada(childNode, myIndex, bones, boneSIDs, boneIDs);
                }
            }
        }

        return colladaBone;
    }

    private object ExportLSLibProfile(XmlDocument xml)
    {
        var techniqueType = Type.GetType("LSLib.Granny.Model.technique") ?? Type.GetType("LSLib.Granny.technique")
            ?? throw new ParsingException("Critical layout error: schema technique definition not found.");

        var profileInstance = Activator.CreateInstance(techniqueType) ?? throw new ParsingException("Instantiation track failure.");
        techniqueType.GetProperty("profile")?.SetValue(profileInstance, "LSTools");

        var props = new List<XmlElement>();
        var prop = xml.CreateElement("BoneIndex");
        prop.InnerText = ExportIndex.ToString();
        props.Add(prop);

        techniqueType.GetProperty("Any")?.SetValue(profileInstance, props.ToArray());
        return profileInstance;
    }

    public object MakeCollada(XmlDocument xml)
    {
        var transformType = typeof(Transform);
        var toMatrixMethod = transformType.GetMethod("ToMatrix4", BindingFlags.Public | BindingFlags.Instance)
                             ?? transformType.GetMethod("ToMatrix", BindingFlags.Public | BindingFlags.Instance);

        var mat = (Matrix4)(toMatrixMethod?.Invoke(Transform, null) ?? Matrix4.Identity);
        mat.Transpose();

        var nodeType = Type.GetType("LSLib.Granny.Model.node") ?? Type.GetType("LSLib.Granny.node")
            ?? throw new ParsingException("Schema core node definition layout is missing.");
        var nodeInstance = Activator.CreateInstance(nodeType) ?? throw new ParsingException("Failed to load node layout.");

        nodeType.GetProperty("id")?.SetValue(nodeInstance, "Bone_" + Name.Replace(' ', '_'));
        nodeType.GetProperty("name")?.SetValue(nodeInstance, Name);
        nodeType.GetProperty("sid")?.SetValue(nodeInstance, Name.Replace(' ', '_'));

        var enumType = Type.GetType("LSLib.Granny.Model.NodeType") ?? Type.GetType("LSLib.Granny.NodeType");
        if (enumType is not null)
        {
            var jointValue = Enum.Parse(enumType, "JOINT");
            nodeType.GetProperty("type")?.SetValue(nodeInstance, jointValue);
        }

        var matrixType = Type.GetType("LSLib.Granny.Model.matrix") ?? Type.GetType("LSLib.Granny.matrix");
        if (matrixType is not null)
        {
            var matrixInstance = Activator.CreateInstance(matrixType);
            matrixType.GetProperty("sid")?.SetValue(matrixInstance, "Transform");
            matrixType.GetProperty("Values")?.SetValue(matrixInstance, new double[] {
                mat.M11, mat.M12, mat.M13, mat.M14,
                mat.M21, mat.M22, mat.M23, mat.M24,
                mat.M31, mat.M32, mat.M33, mat.M34,
                mat.M41, mat.M42, mat.M43, mat.M44
            });

            var itemsChoiceType = Type.GetType("LSLib.Granny.Model.ItemsChoiceType2") ?? Type.GetType("LSLib.Granny.ItemsChoiceType2");
            if (itemsChoiceType is not null)
            {
                var choiceValue = Enum.Parse(itemsChoiceType, "matrix");
                nodeType.GetProperty("Items")?.SetValue(nodeInstance, new object[] { matrixInstance! });
                nodeType.GetProperty("ItemsElementName")?.SetValue(nodeInstance, new object[] { choiceValue });
            }
        }

        var extraType = Type.GetType("LSLib.Granny.Model.extra") ?? Type.GetType("LSLib.Granny.extra");
        if (extraType is not null)
        {
            var extraInstance = Activator.CreateInstance(extraType);
            var techniqueArrayType = extraType.GetProperty("technique")?.PropertyType.GetElementType();
            if (techniqueArrayType is not null)
            {
                var techArray = Array.CreateInstance(techniqueArrayType, 1);
                techArray.SetValue(ExportLSLibProfile(xml), 0);
                extraType.GetProperty("technique")?.SetValue(extraInstance, techArray);
            }

            var extraArray = Array.CreateInstance(extraType, 1);
            extraArray.SetValue(extraInstance, 0);
            nodeType.GetProperty("extra")?.SetValue(nodeInstance, extraArray);
        }

        return nodeInstance;
    }

    private static bool MirrorBoneName(ref string name, string from, string to)
    {
        var pos = 0;
        while (true)
        {
            pos = name.IndexOf(from, pos, StringComparison.Ordinal);
            if (pos == -1) return false;

            if (pos + 2 == name.Length || name[pos + 2] == '_')
            {
                name = name[..pos] + to + name[(pos + 2)..];
                return true;
            }

            pos += 2;
        }
    }

    public static string MirrorBoneName(string name)
    {
        var mirrored = name;
        if (MirrorBoneName(ref mirrored, "_l", "_r")
            || MirrorBoneName(ref mirrored, "_L", "_R")
            || MirrorBoneName(ref mirrored, "_r", "_l")
            || MirrorBoneName(ref mirrored, "_R", "_L"))
        {
            return mirrored;
        }

        return name;
    }

    public void Mirror() => Name = MirrorBoneName(Name);
}

public class Skeleton
{
    public string Name = string.Empty;
    public List<Bone> Bones = [];
    public int LODType;

    [Serialization(Type = MemberType.VariantReference, MinVersion = 0x80000027)]
    public object? ExtendedData;

    [Serialization(Kind = SerializationKind.None)]
    public Dictionary<string, Bone> BonesBySID = new(StringComparer.Ordinal);

    [Serialization(Kind = SerializationKind.None)]
    public Dictionary<string, Bone> BonesByID = new(StringComparer.Ordinal);

    [Serialization(Kind = SerializationKind.None)]
    public bool IsDummy;

    public static Skeleton CreateEmpty(string name) =>
        new()
        {
            Bones = [],
            LODType = 1,
            Name = name,
            BonesBySID = new Dictionary<string, Bone>(StringComparer.Ordinal),
            BonesByID = new Dictionary<string, Bone>(StringComparer.Ordinal)
        };

    public static Skeleton FromCollada(object root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var nameVal = root.GetType().GetProperty("name")?.GetValue(root)?.ToString() ?? "Unnamed_Skeleton";
        var skeleton = new Skeleton
        {
            Bones = [],
            LODType = 1,
            Name = nameVal,
            BonesBySID = new Dictionary<string, Bone>(StringComparer.Ordinal),
            BonesByID = new Dictionary<string, Bone>(StringComparer.Ordinal)
        };

        Bone.FromCollada(root, -1, skeleton.Bones, skeleton.BonesBySID, skeleton.BonesByID);
        return skeleton;
    }

    public Bone? GetBoneByName(string name) => Bones.FirstOrDefault(b => b.Name == name);

    public void TransformRoots(Matrix4 transform)
    {
        var transformType = typeof(Transform);
        var toMatrixMethod = transformType.GetMethod("ToMatrix4", BindingFlags.Public | BindingFlags.Instance)
                             ?? transformType.GetMethod("ToMatrix", BindingFlags.Public | BindingFlags.Instance);
        var fromMatrixMethod = transformType.GetMethod("FromMatrix4", BindingFlags.Public | BindingFlags.Static)
                               ?? transformType.GetMethod("FromMatrix", BindingFlags.Public | BindingFlags.Static);

        foreach (var bone in Bones)
        {
            if (bone.IsRoot)
            {
                var existingMatrix = (Matrix4)(toMatrixMethod?.Invoke(bone.Transform, null) ?? Matrix4.Identity);
                var boneTransform = existingMatrix * transform;
                bone.Transform = (Transform)(fromMatrixMethod?.Invoke(null, [boneTransform]) ?? new Transform());
            }
        }

        UpdateWorldTransforms();
    }

    public void Mirror()
    {
        foreach (var bone in Bones)
        {
            bone.Mirror();
        }
    }

    public void UpdateWorldTransforms()
    {
        foreach (var bone in Bones)
        {
            bone.UpdateWorldTransforms(Bones);
        }
    }

    public void ReorderBones()
    {
        if (Bones.Any(m => m.ExportIndex > -1))
        {
            var newBones = Bones.ToList();
            newBones.Sort((a, b) => a.ExportIndex.CompareTo(b.ExportIndex));

            foreach (var bone in newBones)
            {
                if (!bone.IsRoot)
                {
                    var parent = Bones[bone.ParentIndex];
                    bone.ParentIndex = newBones.IndexOf(parent);
                }
            }

            Bones = newBones;
        }
    }

    private bool CheckIsDummy(Root root)
    {
        var hasSkinnedMeshes = root.Models is not null
            && root.Models.Any(model => model.Skeleton == this)
            && root.Meshes is not null
            && root.Meshes.Any(mesh =>
            {
                object? rawVertexFormat = mesh.GetType().GetProperty("VertexFormat", BindingFlags.Public | BindingFlags.Instance)?.GetValue(mesh);
                if (rawVertexFormat is null) return false;

                var hasWeightsProp = rawVertexFormat.GetType().GetProperty("HasBoneWeights", BindingFlags.Public | BindingFlags.Instance);
                return (bool)(hasWeightsProp?.GetValue(rawVertexFormat) ?? false);
            });

        if (hasSkinnedMeshes) return false;
        if (root.Animations is { Count: > 0 }) return false;
        if (root.Meshes is null or { Count: 0 }) return false;
        if (Bones.Count == 1) return true;

        if (Bones.Count == 1 + root.Meshes.Count)
        {
            foreach (var bone in Bones)
            {
                if (!bone.IsRoot && bone.ParentIndex != 0) return false;
            }

            HashSet<string> marked = new(StringComparer.Ordinal);
            foreach (var mesh in root.Meshes)
            {
                if (mesh.BoneBindings is null || mesh.BoneBindings.Count != 1) return false;

                var firstBindingName = mesh.BoneBindings[0].BoneName;
                if (firstBindingName is null || !marked.Add(firstBindingName)) return false;
            }

            return true;
        }

        return false;
    }

    public void PostLoad(Root root)
    {
        if (CheckIsDummy(root))
        {
            IsDummy = true;
        }

        for (var i = 0; i < Bones.Count; i++)
        {
            Bones[i].ExportIndex = i;
        }
    }
}
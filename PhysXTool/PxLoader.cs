using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace PhysXTool;

public sealed class PhysXLoader
{
    private readonly Dictionary<uint, PhysicsMaterial> _materials = [];
    private readonly PhysicsCollection _collection = new();

    public PhysicsCollection Load(XElement rootElement)
    {
        ArgumentNullException.ThrowIfNull(rootElement);

        foreach (XElement child in rootElement.Elements())
        {
            LoadTopLevel(child);
        }
        return _collection;
    }

    private void LoadTopLevel(XElement ele)
    {
        switch (ele.Name.LocalName)
        {
            case "Material":
                LoadMaterial(ele);
                break;
            case "RigidStatic":
                LoadRigidStatic(ele);
                break;
            case "RigidDynamic":
                LoadRigidDynamic(ele);
                break;
        }
    }

    private PhysicsMaterial LoadMaterial(XElement ele)
    {
        var mat = new PhysicsMaterial
        {
            Index = LoadProperty(ele, "Index", 0u),
            StaticFriction = LoadProperty(ele, "StaticFriction", 1.0f),
            DynamicFriction = LoadProperty(ele, "DynamicFriction", 1.0f),
            Restitution = LoadProperty(ele, "Restitution", 0.0f)
        };

        _materials[mat.Index] = mat;
        _collection.Materials.Add(mat);
        return mat;
    }

    private RigidStatic LoadRigidStatic(XElement ele)
    {
        var actor = new RigidStatic
        {
            GlobalPose = LoadTransformProperty(ele, "GlobalPose")
        };

        LoadRigidActor(ele, actor);
        _collection.Actors.Add(actor);
        return actor;
    }

    private RigidDynamic LoadRigidDynamic(XElement ele)
    {
        var actor = new RigidDynamic
        {
            GlobalPose = LoadTransformProperty(ele, "GlobalPose")
        };

        LoadRigidBody(ele, actor);
        _collection.Actors.Add(actor);
        return actor;
    }

    private void LoadRigidActor(XElement ele, RigidActor o)
    {
        o.Name = LoadProperty(ele, "Name", string.Empty);
        o.DisableGravity = LoadProperty(ele, "DisableGravity", false);
        o.DominanceGroup = LoadProperty(ele, "DominanceGroup", 0u);

        XElement? shapesEle = ele.Element("Shapes");
        if (shapesEle is not null)
        {
            foreach (XElement sEle in shapesEle.Elements("Shape"))
            {
                if (LoadShape(sEle) is { } shape)
                {
                    o.Shapes.Add(shape);
                }
            }
        }
    }

    private void LoadRigidBody(XElement ele, RigidDynamic o)
    {
        LoadRigidActor(ele, o);

        o.CMassLocalPose = LoadTransformProperty(ele, "CMassLocalPose");
        o.Mass = LoadProperty(ele, "Mass", 1.0f);
        o.MassSpaceInertiaTensor = LoadVector3Property(ele, "MassSpaceInertiaTensor", Vector3.One);
        o.LinearDamping = LoadProperty(ele, "LinearDamping", 0.0f);
        o.AngularDamping = LoadProperty(ele, "AngularDamping", 0.05f);

        o.MaxLinearVelocity = LoadBoundedProperty(ele, "MaxLinearVelocity", 1e+15f);
        o.MaxAngularVelocity = LoadProperty(ele, "MaxAngularVelocity", 100.0f);

        o.Kinematic = LoadProperty(ele, "Kinematic", false);
        o.EnableCCD = LoadProperty(ele, "EnableCCD", false);
        o.EnableCCDFriction = LoadProperty(ele, "EnableCCDFriction", false);
        o.EnableSpeculativeCCD = LoadProperty(ele, "EnableSpeculativeCCD", false);
        o.EnableCCDMaxContactImpulse = LoadProperty(ele, "EnableCCDMaxContactImpulse", false);
        o.RetainAccelerations = LoadProperty(ele, "RetainAccelerations", false);

        o.MinCCDAdvanceCoefficient = LoadProperty(ele, "MinCCDAdvanceCoefficient", 0.15f);
        o.MaxDepenetrationVelocity = LoadBoundedProperty(ele, "MaxDepenetrationVelocity", 1e+31f);
        o.MaxContactImpulse = LoadBoundedProperty(ele, "MaxContactImpulse", 1e+31f);

        o.MinPositionIters = LoadProperty(ele, "MinPositionIters", 4u);
        o.MinVelocityIters = LoadProperty(ele, "MinVelocityIters", 1u);
        o.SleepThreshold = LoadProperty(ele, "SleepThreshold", 0.005f);
        o.StabilizationThreshold = LoadProperty(ele, "StabilizationThreshold", 0.0025f);

        o.LockLinearX = LoadProperty(ele, "LockLinearX", false);
        o.LockLinearY = LoadProperty(ele, "LockLinearY", false);
        o.LockLinearZ = LoadProperty(ele, "LockLinearZ", false);
        o.LockAngularX = LoadProperty(ele, "LockAngularX", false);
        o.LockAngularY = LoadProperty(ele, "LockAngularY", false);
        o.LockAngularZ = LoadProperty(ele, "LockAngularZ", false);
        o.WakeCounter = LoadProperty(ele, "WakeCounter", 0u);
        o.ContactReportThreshold = LoadBoundedProperty(ele, "ContactReportThreshold", 3.40282e+37f);
    }

    private PhysicsShape? LoadShape(XElement ele)
    {
        uint matIdx = LoadProperty(ele, "MaterialIndex", 0u);
        if (!_materials.ContainsKey(matIdx))
        {
            throw new KeyNotFoundException($"Shape configuration refers to an undefined material index: {matIdx}");
        }

        XElement? geomEle = ele.Element("Geometry");
        if (geomEle is null) return null;

        PhysicsGeometry? geom = LoadGeometry(geomEle);
        if (geom is null) return null;

        return new PhysicsShape
        {
            Name = LoadProperty(ele, "Name", string.Empty),
            MaterialIndex = matIdx,
            Geometry = geom,
            LocalPose = LoadTransformProperty(ele, "LocalPose"),
            ContactOffset = LoadProperty(ele, "ContactOffset", 0.02f),
            RestOffset = LoadProperty(ele, "RestOffset", 0.0f),
            TorsionalPatchRadius = LoadProperty(ele, "TorsionalPatchRadius", 0.0f),
            MinTorsionalPatchRadius = LoadProperty(ele, "MinTorsionalPatchRadius", 0.0f)
        };
    }

    private static PhysicsGeometry? LoadGeometry(XElement ele)
    {
        string type = LoadProperty(ele, "Type", string.Empty);
        return type switch
        {
            "Box" => (PhysicsGeometry)new BoxGeometry(LoadVector3Property(ele, "HalfExtents", Vector3.Zero)),
            "Sphere" => (PhysicsGeometry)new SphereGeometry(LoadProperty(ele, "Radius", 1.0f)),
            "Capsule" => (PhysicsGeometry)new CapsuleGeometry(LoadProperty(ele, "Radius", 1.0f), LoadProperty(ele, "HalfHeight", 1.0f)),
            "ConvexMesh" => (PhysicsGeometry)new ConvexMeshGeometry(LoadMeshScaleProperty(ele, "Scale"), new ConvexMesh(LoadProperty(ele, "MeshName", string.Empty))),
            "TriangleMesh" => (PhysicsGeometry)new TriangleMeshGeometry(LoadMeshScaleProperty(ele, "Scale"), new TriangleMesh(LoadProperty(ele, "MeshName", string.Empty))),
            _ => null
        };
    }

    private static float LoadProperty(XElement ele, string name, float defaultVal)
    {
        XElement? attr = ele.Element(name);
        return attr is not null && float.TryParse(attr.Value, CultureInvariant, CultureInfo.InvariantCulture, out float result) ? result : defaultVal;
    }

    private static string LoadProperty(XElement ele, string name, string defaultVal)
    {
        XElement? attr = ele.Element(name);
        return attr?.Value ?? defaultVal;
    }

    private static uint LoadProperty(XElement ele, string name, uint defaultVal)
    {
        XElement? attr = ele.Element(name);
        return attr is not null && uint.TryParse(attr.Value, CultureInvariant, CultureInfo.InvariantCulture, out uint result) ? result : defaultVal;
    }

    private static bool LoadProperty(XElement ele, string name, bool defaultVal)
    {
        XElement? attr = ele.Element(name);
        return attr is not null && bool.TryParse(attr.Value, out bool result) ? result : defaultVal;
    }

    private static float LoadBoundedProperty(XElement ele, string name, float bound)
    {
        XElement? attr = ele.Element(name);
        if (attr is null || string.IsNullOrWhiteSpace(attr.Value)) return bound;
        if (string.Equals(attr.Value, "Unbounded", StringComparison.OrdinalIgnoreCase)) return bound;
        return float.TryParse(attr.Value, CultureInvariant, CultureInfo.InvariantCulture, out float result) ? result : bound;
    }

    private static Vector3 LoadVector3Property(XElement ele, string name, Vector3 defaultVal)
    {
        XElement? attr = ele.Element(name);
        if (attr is null) return defaultVal;

        return new Vector3(
            LoadProperty(attr, "X", defaultVal.X),
            LoadProperty(attr, "Y", defaultVal.Y),
            LoadProperty(attr, "Z", defaultVal.Z)
        );
    }

    private static Quaternion LoadQuaternionProperty(XElement ele, string name)
    {
        XElement? attr = ele.Element(name);
        if (attr is null) return Quaternion.Identity;

        return new Quaternion(
            LoadProperty(attr, "X", 0.0f),
            LoadProperty(attr, "Y", 0.0f),
            LoadProperty(attr, "Z", 0.0f),
            LoadProperty(attr, "W", 1.0f)
        );
    }

    private static PhysicsTransform LoadTransformProperty(XElement ele, string name)
    {
        XElement? attr = ele.Element(name);
        if (attr is null) return PhysicsTransform.Identity;

        return new PhysicsTransform(
            LoadVector3Property(attr, "Position", Vector3.Zero),
            LoadQuaternionProperty(attr, "Rotation")
        );
    }

    private static MeshScale LoadMeshScaleProperty(XElement ele, string name)
    {
        XElement? attr = ele.Element(name);
        if (attr is null) return new MeshScale(Vector3.One, Quaternion.Identity);

        return new MeshScale(
            LoadVector3Property(attr, "Scale", Vector3.One),
            LoadQuaternionProperty(attr, "Rotation")
        );
    }

    private static NumberStyles CultureInvariant => NumberStyles.Float | NumberStyles.AllowThousands;
}
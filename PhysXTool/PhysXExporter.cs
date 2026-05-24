using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace PhysXTool;

public sealed partial class PhysXExporter
{
    private const float PxMaxF32 = 3.4e+37f;

    public bool ExportAllProperties { get; set; } = true;

    private readonly Dictionary<PhysicsMaterial, uint> _materials = [];

    public XDocument Export(PhysicsCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var root = new XElement("BG3Physics");
        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), root);

        foreach (var mat in collection.Materials)
        {
            ExportMaterial(root, mat);
        }

        foreach (var actor in collection.Actors)
        {
            ExportTopLevelActor(root, actor);
        }

        return doc;
    }

    private void ExportTopLevelActor(XElement root, RigidActor actor)
    {
        var name = actor switch
        {
            RigidStatic => "RigidStatic",
            RigidDynamic => "RigidDynamic",
            _ => throw new NotSupportedException($"Actor type hierarchy unmapped: {actor.GetType().Name}")
        };

        var actorEle = new XElement(name);
        ExportProperty(actorEle, "Name", actor.Name);
        ExportProperty(actorEle, "GlobalPose", actor.GlobalPose);

        if (actor.Shapes.Count > 0)
        {
            var shapesEle = new XElement("Shapes");
            foreach (var shape in actor.Shapes)
            {
                ExportShape(shapesEle, shape);
            }
            actorEle.Add(shapesEle);
        }

        if (actor is RigidDynamic dynamicActor)
        {
            ExportRigidBodyProperties(actorEle, dynamicActor);
        }

        root.Add(actorEle);
    }

    private void ExportMaterial(XElement parent, PhysicsMaterial obj)
    {
        if (_materials.ContainsKey(obj)) return;

        var index = (uint)_materials.Count;
        _materials.Add(obj, index);

        var ele = new XElement("Material");
        ExportProperty(ele, "Index", index);
        ExportProperty(ele, "StaticFriction", obj.StaticFriction, 1.0f);
        ExportProperty(ele, "DynamicFriction", obj.DynamicFriction, 1.0f);
        ExportProperty(ele, "Restitution", obj.Restitution, 0.0f);

        parent.Add(ele);
    }

    private void ExportShape(XElement parent, PhysicsShape shape)
    {
        var ele = new XElement("Shape");
        ExportProperty(ele, "Name", shape.Name);
        ExportProperty(ele, "MaterialIndex", shape.MaterialIndex);
        ExportProperty(ele, "LocalPose", shape.LocalPose, PhysicsTransform.Identity);
        ExportProperty(ele, "ContactOffset", shape.ContactOffset, 0.02f);
        ExportProperty(ele, "RestOffset", shape.RestOffset, 0.0f);
        ExportProperty(ele, "TorsionalPatchRadius", shape.TorsionalPatchRadius, 0.0f);
        ExportProperty(ele, "MinTorsionalPatchRadius", shape.MinTorsionalPatchRadius, 0.0f);

        if (shape.Geometry is not null)
        {
            var geomEle = new XElement("Geometry");
            ExportGeometryProperties(geomEle, shape.Geometry);
            ele.Add(geomEle);
        }

        parent.Add(ele);
    }

    private void ExportGeometryProperties(XElement ele, PhysicsGeometry geometry)
    {
        switch (geometry)
        {
            case SphereGeometry sphere:
                ExportProperty(ele, "Type", "Sphere");
                ExportProperty(ele, "Radius", sphere.Radius);
                break;

            case CapsuleGeometry capsule:
                ExportProperty(ele, "Type", "Capsule");
                ExportProperty(ele, "Radius", capsule.Radius);
                ExportProperty(ele, "HalfHeight", capsule.HalfHeight);
                break;

            case BoxGeometry box:
                ExportProperty(ele, "Type", "Box");
                ExportProperty(ele, "HalfExtents", box.HalfExtents);
                break;

            case ConvexMeshGeometry convex:
                ExportProperty(ele, "Type", "ConvexMesh");
                ExportProperty(ele, "Scale", convex.Scale);
                ExportProperty(ele, "MeshName", convex.Mesh.Name);
                break;

            case TriangleMeshGeometry triangle:
                ExportProperty(ele, "Type", "TriangleMesh");
                ExportProperty(ele, "Scale", triangle.Scale);
                ExportProperty(ele, "MeshName", triangle.Mesh.Name);
                break;
        }
    }

    private void ExportRigidBodyProperties(XElement ele, RigidDynamic o)
    {
        ExportProperty(ele, "CMassLocalPose", o.CMassLocalPose, PhysicsTransform.Identity);
        ExportProperty(ele, "Mass", o.Mass, 1.0f);
        ExportProperty(ele, "MassSpaceInertiaTensor", o.MassSpaceInertiaTensor, Vector3.One);
        ExportProperty(ele, "LinearDamping", o.LinearDamping, 0.0f);
        ExportProperty(ele, "AngularDamping", o.AngularDamping, 0.05f);

        ExportBoundedProperty(ele, "MaxLinearVelocity", o.MaxLinearVelocity, 1e+15f);
        ExportProperty(ele, "MaxAngularVelocity", o.MaxAngularVelocity, 100.0f);

        ExportFlagProperty(ele, "Kinematic", o.Kinematic);
        ExportFlagProperty(ele, "EnableCCD", o.EnableCCD);
        ExportFlagProperty(ele, "EnableCCDFriction", o.EnableCCDFriction);
        ExportFlagProperty(ele, "EnableSpeculativeCCD", o.EnableSpeculativeCCD);
        ExportFlagProperty(ele, "EnableCCDMaxContactImpulse", o.EnableCCDMaxContactImpulse);
        ExportFlagProperty(ele, "RetainAccelerations", o.RetainAccelerations);

        ExportProperty(ele, "MinCCDAdvanceCoefficient", o.MinCCDAdvanceCoefficient, 0.15f);
        ExportBoundedProperty(ele, "MaxDepenetrationVelocity", o.MaxDepenetrationVelocity, 1e+31f);
        ExportBoundedProperty(ele, "MaxContactImpulse", o.MaxContactImpulse, 1e+31f);
    }

    private static void ExportProperty(XElement parent, string name, float value, float def)
    {
        if (Math.Abs(value - def) > float.Epsilon)
        {
            parent.Add(new XElement(name, value.ToString("F8", CultureInfo.InvariantCulture)));
        }
    }

    private static void ExportProperty(XElement parent, string name, string value) => parent.Add(new XElement(name, value));
    private static void ExportProperty(XElement parent, string name, uint value) => parent.Add(new XElement(name, value.ToString(CultureInfo.InvariantCulture)));
    private static void ExportProperty(XElement parent, string name, float value) => parent.Add(new XElement(name, value.ToString("F8", CultureInfo.InvariantCulture)));

    private static void ExportProperty(XElement parent, string name, Vector3 value)
    {
        var ele = new XElement(name);
        ExportProperty(ele, "X", value.X, 0.0f);
        ExportProperty(ele, "Y", value.Y, 0.0f);
        ExportProperty(ele, "Z", value.Z, 0.0f);
        parent.Add(ele);
    }

    private void ExportProperty(XElement parent, string name, Vector3 value, Vector3 def)
    {
        if (ExportAllProperties || value != def) ExportProperty(parent, name, value);
    }

    private static void ExportProperty(XElement parent, string name, Quaternion value)
    {
        var ele = new XElement(name);
        ExportProperty(ele, "X", value.X, 0.0f);
        ExportProperty(ele, "Y", value.Y, 0.0f);
        ExportProperty(ele, "Z", value.Z, 0.0f);
        ExportProperty(ele, "W", value.W, 1.0f);
        parent.Add(ele);
    }

    private void ExportProperty(XElement parent, string name, PhysicsTransform value)
    {
        var ele = new XElement(name);
        ExportProperty(ele, name: "Position", value: value.Position, def: Vector3.Zero);
        ExportProperty(ele, name: "Rotation", value: value.Rotation);
        parent.Add(ele);
    }
    private void ExportProperty(XElement parent, string name, PhysicsTransform value, PhysicsTransform def)
    {
        if (ExportAllProperties || !value.Equals(def))
        {
            var ele = new XElement(name);
            ExportProperty(ele, name: "Position", value: value.Position, def: Vector3.Zero);
            ExportProperty(ele, name: "Rotation", value: value.Rotation);
            parent.Add(ele);
        }
    }

    private void ExportProperty(XElement parent, string name, MeshScale value)
    {
        var ele = new XElement(name);
        ExportProperty(ele, name: "Scale", value: value.Scale, def: Vector3.One);
        ExportProperty(ele, name: "Rotation", value: value.Rotation);
        parent.Add(ele);
    }

    private static void ExportBoundedProperty(XElement parent, string name, float value, float bound)
    {
        string output = value < bound ? value.ToString("F8", CultureInfo.InvariantCulture) : "Unbounded";
        parent.Add(new XElement(name, output));
    }

    private void ExportFlagProperty(XElement parent, string name, bool flagValue)
    {
        if (ExportAllProperties || flagValue) parent.Add(new XElement(name, flagValue ? "true" : "false"));
    }
}

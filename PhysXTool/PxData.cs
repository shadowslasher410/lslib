using System.Numerics;

namespace PhysXTool;

public enum D6Motion : uint
{
    Locked = 0,
    Limited = 1,
    Free = 2
}

[Flags]
public enum D6JointDriveFlag : uint
{
    Acceleration = 1
}

public record struct PhysicsMaterial(uint Index, float StaticFriction, float DynamicFriction, float Restitution);

public record struct PhysicsTransform(Vector3 Position, Quaternion Rotation)
{
    public static PhysicsTransform Identity => new(Vector3.Zero, Quaternion.Identity);
}

public record struct MeshScale(Vector3 Scale, Quaternion Rotation);

public abstract record PhysicsGeometry;
public record BoxGeometry(Vector3 HalfExtents) : PhysicsGeometry;
public record SphereGeometry(float Radius) : PhysicsGeometry;
public record ConvexMesh(string Name) : PhysicsGeometry;
public record TriangleMesh(string Name) : PhysicsGeometry;
public record CapsuleGeometry(float Radius, float HalfHeight) : PhysicsGeometry;
public record ConvexMeshGeometry(MeshScale Scale, ConvexMesh Mesh) : PhysicsGeometry;
public record TriangleMeshGeometry(MeshScale Scale, TriangleMesh Mesh) : PhysicsGeometry;
public record ManagedConvexMeshGeometry(MeshScale Scale, ConvexMeshAsset Mesh) : PhysicsGeometry;
public record ManagedTriangleMeshGeometry(MeshScale Scale, TriangleMeshAsset Mesh) : PhysicsGeometry;

public sealed class ConvexMeshAsset
{
    public string Name { get; set; } = string.Empty;
    public List<List<Vector3>> Polygons { get; set; } = [];
}

public sealed class TriangleMeshAsset
{
    public string Name { get; set; } = string.Empty;
    public List<Vector3> Triangles { get; set; } = [];
}

public sealed partial class PhysicsShape
{
    public string Name { get; set; } = string.Empty;
    public uint MaterialIndex { get; set; }
    public PhysicsGeometry? Geometry { get; set; }
    public PhysicsTransform LocalPose { get; set; } = PhysicsTransform.Identity;
    public float ContactOffset { get; set; } = 0.02f;
    public float RestOffset { get; set; } = 0.0f;
    public float TorsionalPatchRadius { get; set; } = 0.0f;
    public float MinTorsionalPatchRadius { get; set; } = 0.0f;
}

public abstract partial class RigidActor
{
    public string Name { get; set; } = string.Empty;
    public PhysicsTransform GlobalPose { get; set; } = PhysicsTransform.Identity;
    public List<PhysicsShape> Shapes { get; set; } = [];
    public bool DisableGravity { get; set; }
    public uint DominanceGroup { get; set; } = 0;
}

public sealed class RigidStatic : RigidActor;

public sealed partial class RigidDynamic : RigidActor
{
    public PhysicsTransform CMassLocalPose { get; set; } = PhysicsTransform.Identity;
    public float Mass { get; set; } = 1.0f;
    public Vector3 MassSpaceInertiaTensor { get; set; } = Vector3.One;
    public float LinearDamping { get; set; } = 0.0f;
    public float AngularDamping { get; set; } = 0.05f;
    public float MaxLinearVelocity { get; set; } = 1e+15f;
    public float MaxAngularVelocity { get; set; } = 100.0f;
    public bool Kinematic { get; set; }
    public bool EnableCCD { get; set; }
    public bool EnableCCDFriction { get; set; }
    public bool EnableSpeculativeCCD { get; set; }
    public bool EnableCCDMaxContactImpulse { get; set; }
    public bool RetainAccelerations { get; set; }
    public float MinCCDAdvanceCoefficient { get; set; } = 0.15f;
    public float MaxDepenetrationVelocity { get; set; } = 1e+31f;
    public float MaxContactImpulse { get; set; } = 1e+31f;
    public uint MinPositionIters { get; set; } = 4;
    public uint MinVelocityIters { get; set; } = 1;
    public float SleepThreshold { get; set; } = 0.005f;
    public float StabilizationThreshold { get; set; } = 0.0025f;
    public bool LockLinearX { get; set; }
    public bool LockLinearY { get; set; }
    public bool LockLinearZ { get; set; }
    public bool LockLockAngularX { get; set; }
    public bool LockAngularX { get; set; }
    public bool LockAngularY { get; set; }
    public bool LockAngularZ { get; set; }
    public uint WakeCounter { get; set; } = 0;
    public float ContactReportThreshold { get; set; } = 3.40282e+37f;
}

public sealed class PhysicsCollection
{
    public List<PhysicsMaterial> Materials { get; set; } = [];
    public List<RigidActor> Actors { get; set; } = [];
}
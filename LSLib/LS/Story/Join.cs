namespace LSLib.LS.Story;

public abstract class JoinNode : TreeNode
{
    public NodeReference LeftParentRef { get; set; } = new();
    public NodeReference RightParentRef { get; set; } = new();
    public AdapterReference LeftAdapterRef { get; set; } = new();
    public AdapterReference RightAdapterRef { get; set; } = new();
    public NodeReference LeftDatabaseNodeRef { get; set; } = new();
    public byte LeftDatabaseIndirection { get; set; }
    public NodeEntryItem LeftDatabaseJoin { get; set; } = new();
    public NodeReference RightDatabaseNodeRef { get; set; } = new();
    public byte RightDatabaseIndirection { get; set; }
    public NodeEntryItem RightDatabaseJoin { get; set; } = new();

    public override void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        base.Read(reader);
        LeftParentRef = reader.ReadNodeRef();
        RightParentRef = reader.ReadNodeRef();
        LeftAdapterRef = reader.ReadAdapterRef();
        RightAdapterRef = reader.ReadAdapterRef();

        LeftDatabaseNodeRef = reader.ReadNodeRef();
        LeftDatabaseJoin = new NodeEntryItem();
        LeftDatabaseJoin.Read(reader);
        LeftDatabaseIndirection = reader.ReadByte();

        RightDatabaseNodeRef = reader.ReadNodeRef();
        RightDatabaseJoin = new NodeEntryItem();
        RightDatabaseJoin.Read(reader);
        RightDatabaseIndirection = reader.ReadByte();
    }

    public override void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        base.Write(writer);
        LeftParentRef.Write(writer);
        RightParentRef.Write(writer);
        LeftAdapterRef.Write(writer);
        RightAdapterRef.Write(writer);

        LeftDatabaseNodeRef.Write(writer);
        LeftDatabaseJoin.Write(writer);
        writer.Write(LeftDatabaseIndirection);

        RightDatabaseNodeRef.Write(writer);
        RightDatabaseJoin.Write(writer);
        writer.Write(RightDatabaseIndirection);
    }

    public override void PostLoad(Story story)
    {
        ArgumentNullException.ThrowIfNull(story);

        base.PostLoad(story);

        ValidateAndBindAdapter(LeftAdapterRef);
        ValidateAndBindAdapter(RightAdapterRef);

        void ValidateAndBindAdapter(AdapterReference adapterRef)
        {
            if (adapterRef is { IsValid: true } && adapterRef.Resolve() is Adapter adapter)
            {
                if (adapter.OwnerNode is not null)
                {
                    throw new InvalidDataException("An adapter cannot be assigned to multiple join/rel nodes!");
                }

                adapter.OwnerNode = this;
            }
        }
    }

    public override void DebugDump(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);

        base.DebugDump(writer, story);

        writer.Write("    Left:");
        if (LeftParentRef.IsValid)
        {
            writer.Write(" Parent ");
            LeftParentRef.DebugDump(writer, story);
        }

        if (LeftAdapterRef.IsValid)
        {
            writer.Write(" Adapter ");
            LeftAdapterRef.DebugDump(writer, story);
        }

        if (LeftDatabaseNodeRef.IsValid)
        {
            writer.Write(" DbNode ");
            LeftDatabaseNodeRef.DebugDump(writer, story);
            writer.Write(" Indirection {0}", LeftDatabaseIndirection);
            writer.Write(" Join ");
            LeftDatabaseJoin.DebugDump(writer, story);
        }

        writer.WriteLine();

        writer.Write("    Right:");
        if (RightParentRef.IsValid)
        {
            writer.Write(" Parent ");
            RightParentRef.DebugDump(writer, story);
        }

        if (RightAdapterRef.IsValid)
        {
            writer.Write(" Adapter ");
            RightAdapterRef.DebugDump(writer, story);
        }

        if (RightDatabaseNodeRef.IsValid)
        {
            writer.Write(" DbNode ");
            RightDatabaseNodeRef.DebugDump(writer, story);
            writer.Write(" Indirection {0}", RightDatabaseIndirection);
            writer.Write(" Join ");
            RightDatabaseJoin.DebugDump(writer, story);
        }

        writer.WriteLine();
    }
}

public class AndNode : JoinNode
{
    public override Node.Type NodeType()
    {
        return Node.Type.And;
    }

    public override string TypeName()
    {
        return "And";
    }

    public override void MakeScript(TextWriter writer, Story story, Tuple tuple, bool printTypes)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(tuple);

        if (LeftAdapterRef.Resolve() is not Adapter leftAdapter ||
            LeftParentRef.Resolve() is not Node leftParent)
        {
            throw new InvalidDataException($"JoinNode {Index}: Failed to resolve Left-hand adapter or parent node dependencies.");
        }

        if (RightAdapterRef.Resolve() is not Adapter rightAdapter ||
            RightParentRef.Resolve() is not Node rightParent)
        {
            throw new InvalidDataException($"JoinNode {Index}: Failed to resolve Right-hand adapter or parent node dependencies.");
        }

        Tuple leftTuple = leftAdapter.Adapt(tuple);
        leftParent.MakeScript(writer, story, leftTuple, printTypes);

        writer.WriteLine("AND");

        Tuple rightTuple = rightAdapter.Adapt(tuple);
        rightParent.MakeScript(writer, story, rightTuple, false);
    }
}

public class NotAndNode : JoinNode
{
    public override Node.Type NodeType()
    {
        return Node.Type.NotAnd;
    }

    public override string TypeName()
    {
        return "Not And";
    }

    public override void MakeScript(TextWriter writer, Story story, Tuple tuple, bool printTypes)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(tuple);

        if (LeftAdapterRef.Resolve() is not Adapter leftAdapter ||
            LeftParentRef.Resolve() is not Node leftParent)
        {
            throw new InvalidDataException($"NotJoinNode {Index}: Failed to resolve Left-hand adapter or parent node references.");
        }

        if (RightAdapterRef.Resolve() is not Adapter rightAdapter ||
            RightParentRef.Resolve() is not Node rightParent)
        {
            throw new InvalidDataException($"NotJoinNode {Index}: Failed to resolve Right-hand adapter or parent node references.");
        }

        Tuple leftTuple = leftAdapter.Adapt(tuple);
        leftParent.MakeScript(writer, story, leftTuple, printTypes);

        writer.WriteLine("AND NOT");

        Tuple rightTuple = rightAdapter.Adapt(tuple);
        rightParent.MakeScript(writer, story, rightTuple, false);
    }
}
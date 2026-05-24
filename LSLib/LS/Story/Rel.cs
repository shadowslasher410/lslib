using System;
using System.IO;

namespace LSLib.LS.Story;

public abstract class RelNode : TreeNode
{
    public NodeReference ParentRef { get; set; } = new();
    public AdapterReference AdapterRef { get; set; } = new();
    public NodeReference RelDatabaseNodeRef { get; set; } = new();
    public NodeEntryItem RelJoin { get; set; } = new();
    public byte RelDatabaseIndirection { get; set; }

    public override void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        base.Read(reader);
        ParentRef = reader.ReadNodeRef();
        AdapterRef = reader.ReadAdapterRef();

        RelDatabaseNodeRef = reader.ReadNodeRef();
        RelJoin = new NodeEntryItem();
        RelJoin.Read(reader);
        RelDatabaseIndirection = reader.ReadByte();
    }

    public override void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        base.Write(writer);
        ParentRef.Write(writer);
        AdapterRef.Write(writer);

        RelDatabaseNodeRef.Write(writer);
        RelJoin.Write(writer);
        writer.Write(RelDatabaseIndirection);
    }

    public override void PostLoad(Story story)
    {
        ArgumentNullException.ThrowIfNull(story);

        base.PostLoad(story);

        if (AdapterRef.IsValid)
        {
            Adapter? adapter = AdapterRef.Resolve();

            if (adapter is not null)
            {
                if (adapter.OwnerNode is not null)
                {
                    throw new InvalidDataException("An adapter cannot be assigned to multiple join/rel nodes!");
                }

                adapter.OwnerNode = this;
            }
            else
            {
                throw new InvalidDataException($"Failed to resolve required Adapter reference layout with Index: {AdapterRef.Index}");
            }
        }
    }


    public override void DebugDump(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);

        base.DebugDump(writer, story);

        writer.Write("   ");
        if (ParentRef.IsValid)
        {
            writer.Write(" Parent ");
            ParentRef.DebugDump(writer, story);
        }

        if (AdapterRef.IsValid)
        {
            writer.Write(" Adapter ");
            AdapterRef.DebugDump(writer, story);
        }

        if (RelDatabaseNodeRef.IsValid)
        {
            writer.Write(" DbNode ");
            RelDatabaseNodeRef.DebugDump(writer, story);
            writer.Write(" Indirection {0}", RelDatabaseIndirection);
            writer.Write(" Join ");
            RelJoin.DebugDump(writer, story);
        }

        writer.WriteLine();
    }
}

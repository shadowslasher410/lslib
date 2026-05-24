using System;
using System.IO;

namespace LSLib.LS.Story;

public abstract class OsiReference<T> : IOsirisSerializable where T : class
{
    public const uint NullReference = 0;

    public uint Index { get; private set; } = NullReference;

    protected Story? Story;

    public bool IsNull => Index == NullReference;

    public bool IsValid => Index != NullReference;

    protected OsiReference()
    {
    }

    protected OsiReference(Story story, uint reference)
    {
        Story = story ?? throw new ArgumentNullException(nameof(story));
        Index = reference;
    }

    public void BindStory(Story story)
    {
        ArgumentNullException.ThrowIfNull(story);

        if (Story is null)
        {
            Story = story;
        }
        else if (Story != story)
        {
            throw new InvalidOperationException("Reference already bound to a different story configuration structure!");
        }
    }

    public void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Index = reader.ReadUInt32();
    }

    public void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(Index);
    }

    public abstract T? Resolve();

    public abstract void DebugDump(TextWriter writer, Story story);
}

public class NodeReference : OsiReference<Node>
{
    public NodeReference() : base() { }

    public NodeReference(Story story, uint reference) : base(story, reference) { }

    public NodeReference(Story story, Node? reference)
        : base(story, reference is null ? NullReference : reference.Index) { }

    public static NodeReference Create(Story story, Node? node) =>
        node is null ? new NodeReference() : new NodeReference(story, node);

    public override Node? Resolve()
    {
        if (Index == NullReference || Story is null) return null;
        return Story.Nodes.TryGetValue(Index, out var node) ? node : null;
    }

    public override void DebugDump(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);

        if (!IsValid)
        {
            writer.Write("(None)");
            return;
        }

        Node? node = Resolve();
        if (node is null)
        {
            writer.Write("#{0} <Unresolved Node>", Index);
            return;
        }

        if (!string.IsNullOrEmpty(node.Name))
        {
            writer.Write("#{0} <{1}({2}) {3}>", Index, node.Name, node.NumParams, node.TypeName() ?? string.Empty);
        }
        else
        {
            writer.Write("#{0} <{1}>", Index, node.TypeName() ?? string.Empty);
        }
    }
}

public class AdapterReference : OsiReference<Adapter>
{
    public AdapterReference() : base() { }

    public AdapterReference(Story story, uint reference) : base(story, reference) { }

    public AdapterReference(Story story, Adapter reference)
        : base(story, (reference ?? throw new ArgumentNullException(nameof(reference))).Index) { }

    public static AdapterReference Create(Story story, Adapter? adapter) =>
        adapter is null ? new AdapterReference() : new AdapterReference(story, adapter);

    public override Adapter? Resolve()
    {
        if (Index == NullReference || Story is null) return null;
        return Story.Adapters.TryGetValue(Index, out var adapter) ? adapter : null;
    }

    public override void DebugDump(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (!IsValid)
        {
            writer.Write("(None)");
        }
        else
        {
            writer.Write("#{0}", Index);
        }
    }
}

public class DatabaseReference : OsiReference<Database>
{
    public DatabaseReference() : base() { }

    public DatabaseReference(Story story, uint reference) : base(story, reference) { }

    public DatabaseReference(Story story, Database reference)
        : base(story, (reference ?? throw new ArgumentNullException(nameof(reference))).Index) { }

    public static DatabaseReference Create(Story story, Database? database) =>
        database is null ? new DatabaseReference() : new DatabaseReference(story, database);

    public override Database? Resolve()
    {
        if (Index == NullReference || Story is null) return null;
        return Story.Databases.TryGetValue(Index, out var database) ? database : null;
    }

    public override void DebugDump(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (!IsValid)
        {
            writer.Write("(None)");
        }
        else
        {
            writer.Write("#{0}", Index);
        }
    }
}

public class GoalReference : OsiReference<Goal>
{
    public GoalReference() : base() { }

    public GoalReference(Story story, uint reference) : base(story, reference) { }

    public GoalReference(Story story, Goal reference)
        : base(story, (reference ?? throw new ArgumentNullException(nameof(reference))).Index) { }

    public static GoalReference Create(Story story, Goal? goal) =>
        goal is null ? new GoalReference() : new GoalReference(story, goal);

    public override Goal? Resolve()
    {
        if (Index == NullReference || Story is null) return null;
        return Story.Goals.TryGetValue(Index, out var goal) ? goal : null;
    }

    public override void DebugDump(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (!IsValid)
        {
            writer.Write("(None)");
            return;
        }

        Goal? goal = Resolve();
        if (goal is null)
        {
            writer.Write("#{0} <Unresolved Goal>", Index);
        }
        else
        {
            writer.Write("#{0} <{1}>", Index, goal.Name ?? string.Empty);
        }
    }
}
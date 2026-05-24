namespace LSLib.LS.Story;

public abstract class DataNode : Node
{
    public List<NodeEntryItem> ReferencedBy { get; set; } = [];

    public override void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        base.Read(reader);
        ReferencedBy = reader.ReadList<NodeEntryItem>();
    }

    public override void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        base.Write(writer);
        writer.WriteList(ReferencedBy);
    }

    public override void PostLoad(Story story)
    {
        ArgumentNullException.ThrowIfNull(story);

        base.PostLoad(story);

        foreach (NodeEntryItem reference in ReferencedBy)
        {
            if (reference?.NodeRef is { IsValid: true })
            {
                if (reference.NodeRef.Resolve() is RuleNode ruleNodeInstance &&
                    reference.GoalRef is { IsNull: false })
                {
                    ruleNodeInstance.DerivedGoalRef = new GoalReference(story, reference.GoalRef.Index);
                }
            }
        }
    }


    public override void DebugDump(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);

        base.DebugDump(writer, story);

        if (ReferencedBy.Count > 0)
        {
            writer.WriteLine("    Referenced By:");
            foreach (NodeEntryItem entry in ReferencedBy)
            {
                if (entry is null) continue;

                writer.Write("        ");
                entry.DebugDump(writer, story);
                writer.WriteLine();
            }
        }
    }
}

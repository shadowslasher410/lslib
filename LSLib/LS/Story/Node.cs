namespace LSLib.LS.Story;

public abstract class Node : IOsirisSerializable
{
    public enum Type : byte
    {
        Database = 1,
        Proc = 2,
        DivQuery = 3,
        And = 4,
        NotAnd = 5,
        RelOp = 6,
        Rule = 7,
        InternalQuery = 8,
        UserQuery = 9
    }

    public uint Index { get; set; }
    public DatabaseReference DatabaseRef { get; set; } = new();
    public string Name { get; set; } = string.Empty;
    public byte NumParams { get; set; } 

    public virtual void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        DatabaseRef = reader.ReadDatabaseRef();
        Name = reader.ReadString() ?? string.Empty;
        if (Name.Length > 0)
        {
            NumParams = reader.ReadByte();
        }
    }

    public virtual void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        DatabaseRef.Write(writer);
        writer.Write(Name);
        if (Name.Length > 0)
        {
            writer.Write(NumParams);
        }
    }

    public abstract Type NodeType();

    public abstract string TypeName();

    public abstract void MakeScript(TextWriter writer, Story story, Tuple tuple, bool printTypes = false);

    public virtual void PostLoad(Story story)
    {
        ArgumentNullException.ThrowIfNull(story);

        if (DatabaseRef.IsValid && DatabaseRef.Resolve() is Database database)
        {
            if (database.OwnerNode is not null)
            {
                throw new InvalidDataException("A database cannot be assigned to multiple database nodes!");
            }

            database.OwnerNode = this;
        }
    }

    public virtual void PreSave(Story story)
    {
    }

    public virtual void PostSave(Story story)
    {
    }

    public virtual void DebugDump(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);

        if (Name.Length > 0)
        {
            writer.Write("{0}({1}): ", Name, NumParams);
        }

        writer.Write("<{0}>", TypeName());
        if (DatabaseRef.IsValid)
        {
            writer.Write(", Database ");
            DatabaseRef.DebugDump(writer, story);
        }

        writer.WriteLine();
    }
}

public abstract class TreeNode : Node
{
    public NodeEntryItem NextNode { get; set; } = new();

    public override void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        base.Read(reader);
        NextNode = new NodeEntryItem();
        NextNode.Read(reader);
    }

    public override void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        base.Write(writer);
        NextNode.Write(writer);
    }

    public override void PostLoad(Story story)
    {
        ArgumentNullException.ThrowIfNull(story);

        base.PostLoad(story);

        if (NextNode.NodeRef is { IsValid: true } &&
            NextNode.NodeRef.Resolve() is RuleNode ruleNode &&
            NextNode.GoalRef is not null)
        {
            ruleNode.DerivedGoalRef = new(story, NextNode.GoalRef.Index);
        }
    }

    public override void DebugDump(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);

        base.DebugDump(writer, story);

        writer.Write("    Next: ");
        NextNode.DebugDump(writer, story);
        writer.WriteLine();
    }
}
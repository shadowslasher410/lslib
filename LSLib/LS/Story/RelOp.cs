namespace LSLib.LS.Story;

public enum RelOpType : byte
{
    Less = 0,
    LessOrEqual = 1,
    Greater = 2,
    GreaterOrEqual = 3,
    Equal = 4,
    NotEqual = 5
}

public class RelOpNode : RelNode
{
    public sbyte LeftValueIndex { get; set; }
    public sbyte RightValueIndex { get; set; }
    public Value LeftValue { get; set; } = new();
    public Value RightValue { get; set; } = new();
    public RelOpType RelOp { get; set; }

    public override void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        base.Read(reader);
        LeftValueIndex = reader.ReadSByte();
        RightValueIndex = reader.ReadSByte();

        LeftValue = new Value();
        LeftValue.Read(reader);

        RightValue = new Value();
        RightValue.Read(reader);

        RelOp = (RelOpType)reader.ReadInt32();
    }

    public override void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        base.Write(writer);
        writer.Write(LeftValueIndex);
        writer.Write(RightValueIndex);

        LeftValue.Write(writer);
        RightValue.Write(writer);
        writer.Write((uint)RelOp);
    }

    public override Node.Type NodeType()
    {
        return Node.Type.RelOp;
    }

    public override string TypeName()
    {
        return $"RelOp {RelOp}";
    }

    public override void DebugDump(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);

        base.DebugDump(writer, story);

        writer.Write("    Left Value: ");
        if (LeftValueIndex != -1)
            writer.Write("[Source Column {0}]", LeftValueIndex);
        else
            LeftValue.DebugDump(writer, story);
        writer.WriteLine();

        writer.Write("    Right Value: ");
        if (RightValueIndex != -1)
            writer.Write("[Source Column {0}]", RightValueIndex);
        else
            RightValue.DebugDump(writer, story);
        writer.WriteLine();
    }

    public override void MakeScript(TextWriter writer, Story story, Tuple tuple, bool printTypes)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(tuple);

        Adapter? adapter = AdapterRef.Resolve() ?? throw new InvalidDataException($"Failed to resolve required Adapter reference inside RelOpNode with index: {AdapterRef.Index}");
        Tuple adaptedTuple = adapter.Adapt(tuple);

        Node? parentNode = ParentRef.Resolve() ?? throw new InvalidDataException($"Failed to resolve required Parent reference inside RelOpNode with index: {ParentRef.Index}");
        parentNode.MakeScript(writer, story, adaptedTuple, printTypes);
        writer.WriteLine("AND");

        if (LeftValueIndex != -1)
        {
            if (adaptedTuple.Logical.TryGetValue(LeftValueIndex, out Value? leftVal) && leftVal is not null)
            {
                leftVal.MakeScript(writer, story, tuple);
            }
            else
            {
                throw new InvalidDataException($"Left source column index {LeftValueIndex} could not be found inside the adapted logical tuple mapping.");
            }
        }
        else
        {
            LeftValue.MakeScript(writer, story, tuple);
        }

        switch (RelOp)
        {
            case RelOpType.Less: writer.Write(" < "); break;
            case RelOpType.LessOrEqual: writer.Write(" <= "); break;
            case RelOpType.Greater: writer.Write(" > "); break;
            case RelOpType.GreaterOrEqual: writer.Write(" >= "); break;
            case RelOpType.Equal: writer.Write(" == "); break;
            case RelOpType.NotEqual: writer.Write(" != "); break;
        }

        if (RightValueIndex != -1)
        {
            if (adaptedTuple.Logical.TryGetValue(RightValueIndex, out Value? rightVal) && rightVal is not null)
            {
                rightVal.MakeScript(writer, story, tuple);
            }
            else
            {
                throw new InvalidDataException($"Right source column index {RightValueIndex} could not be found inside the adapted logical tuple mapping.");
            }
        }
        else
        {
            RightValue.MakeScript(writer, story, tuple);
        }
        writer.WriteLine();
    }
}
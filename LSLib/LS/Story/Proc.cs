namespace LSLib.LS.Story;

public class ProcNode : DataNode
{
    public override Node.Type NodeType()
    {
        return Node.Type.Proc;
    }

    public override string TypeName()
    {
        return "Proc";
    }

    public override void MakeScript(TextWriter writer, Story story, Tuple tuple, bool printTypes)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(tuple);

        writer.Write("{0}(", Name ?? string.Empty);
        tuple.MakeScript(writer, story, true);
        writer.WriteLine(")");
    }
}

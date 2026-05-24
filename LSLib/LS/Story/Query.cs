namespace LSLib.LS.Story;

public abstract class QueryNode : Node
{
    public override void MakeScript(TextWriter writer, Story story, Tuple tuple, bool printTypes)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(tuple);

        writer.Write("{0}(", Name ?? string.Empty);
        tuple.MakeScript(writer, story, printTypes);
        writer.WriteLine(")");
    }
}

public class DivQueryNode : QueryNode
{
    public override Node.Type NodeType()
    {
        return Node.Type.DivQuery;
    }

    public override string TypeName()
    {
        return "Div Query";
    }
}

public class InternalQueryNode : QueryNode
{
    public override Node.Type NodeType()
    {
        return Node.Type.InternalQuery;
    }

    public override string TypeName()
    {
        return "Internal Query";
    }
}

public class UserQueryNode : QueryNode
{
    public override Node.Type NodeType()
    {
        return Node.Type.UserQuery;
    }

    public override string TypeName()
    {
        return "User Query";
    }
}

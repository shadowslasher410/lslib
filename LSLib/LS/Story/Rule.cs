namespace LSLib.LS.Story;

public enum RuleType
{
    Rule,
    Proc,
    Query
}

public class RuleNode : RelNode
{
    public List<Call> Calls { get; set; } = [];
    public List<Variable> Variables { get; set; } = [];
    public uint Line { get; set; }
    public GoalReference DerivedGoalRef { get; set; } = null!;
    public bool IsQuery { get; set; }

    public override void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        base.Read(reader);
        Calls = reader.ReadList<Call>();

        byte variables = reader.ReadByte();
        Variables = new List<Variable>(variables);
        while (variables-- > 0)
        {
            if (reader.Ver < OsiVersion.VerValueFlags)
            {
                byte type = reader.ReadByte();
                if (type != 1) throw new InvalidDataException("Illegal value type in rule variable list");
            }
            var variable = new Variable();
            variable.Read(reader);
            if (variable.Adapted)
            {
                variable.VariableName = $"_Var{Variables.Count + 1}";
            }

            Variables.Add(variable);
        }

        Line = reader.ReadUInt32();

        if (reader.Ver >= OsiVersion.VerAddQuery)
            IsQuery = reader.ReadBoolean();
        else
            IsQuery = false;
    }

    public override void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        base.Write(writer);
        writer.WriteList(Calls);

        writer.Write((byte)Variables.Count);
        foreach (Variable variable in Variables)
        {
            if (writer.Ver < OsiVersion.VerValueFlags)
            {
                writer.Write((byte)1);
            }
            variable.Write(writer);
        }

        writer.Write(Line);
        if (writer.Ver >= OsiVersion.VerAddQuery)
            writer.Write(IsQuery);
    }

    public override Node.Type NodeType()
    {
        return Node.Type.Rule;
    }

    public override string TypeName()
    {
        return IsQuery ? "Query Rule" : "Rule";
    }

    public override void DebugDump(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);

        base.DebugDump(writer, story);

        writer.WriteLine("    Variables: ");
        foreach (Variable v in Variables)
        {
            writer.Write("        ");
            v.DebugDump(writer, story);
            writer.WriteLine();
        }

        writer.WriteLine("    Calls: ");
        foreach (Call call in Calls)
        {
            writer.Write("        ");
            call.DebugDump(writer, story);
            writer.WriteLine();
        }
    }

    public Node GetRoot(Story _)
    {
        Node parent = this;
        while (parent is not null)
        {
            if (parent is RelNode rel)
            {
                if (rel.ParentRef is null) break;
                Node? resolved = rel.ParentRef.Resolve();
                if (resolved is null) break;
                parent = resolved;
            }
            else if (parent is JoinNode join)
            {
                if (join.LeftParentRef is null) break;
                Node? resolved = join.LeftParentRef.Resolve();
                if (resolved is null) break;
                parent = resolved;
            }
            else
            {
                return parent;
            }
        }
        return parent!;
    }

    public RuleType? GetRuleType(Story story)
    {
        ArgumentNullException.ThrowIfNull(story);

        Node root = GetRoot(story);
        if (root is DatabaseNode)
        {
            return RuleType.Rule;
        }
        else if (root is ProcNode)
        {
            string rootName = root.Name ?? string.Empty;
            string querySig = $"{rootName}__DEF__/{root.NumParams}";
            string sig = $"{rootName}/{root.NumParams}";

            if (!story.FunctionSignatureMap.TryGetValue(querySig, out Function? func)
                && !story.FunctionSignatureMap.TryGetValue(sig, out func))
            {
                return null;
            }

            return func.Type switch
            {
                FunctionType.Event => RuleType.Rule,
                FunctionType.Proc => RuleType.Proc,
                FunctionType.UserQuery => RuleType.Query,
                _ => throw new InvalidDataException($"Unsupported root function type context parameters: {func.Type}")
            };
        }
        else
        {
            throw new InvalidDataException("Cannot export rules with this specific target root node type layout configuration.");
        }
    }

    public Tuple MakeInitialTuple()
    {
        var tuple = new Tuple();
        for (int i = 0; i < Variables.Count; i++)
        {
            tuple.Physical.Add(Variables[i]);
            tuple.Logical.Add(i, Variables[i]);
        }

        return tuple;
    }

    public override void MakeScript(TextWriter writer, Story story, Tuple tuple, bool printTypes)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);

        RuleType? ruleType = GetRuleType(story);
        if (ruleType is null) return;

        switch (ruleType)
        {
            case RuleType.Proc: writer.WriteLine("PROC"); break;
            case RuleType.Query: writer.WriteLine("QRY"); break;
            case RuleType.Rule: writer.WriteLine("IF"); break;
        }

        Tuple initialTuple = MakeInitialTuple();
        if (AdapterRef is not null && AdapterRef.IsValid)
        {
            Adapter? adapter = AdapterRef.Resolve();
            if (adapter is not null)
            {
                initialTuple = adapter.Adapt(initialTuple);
            }
        }

        printTypes = printTypes || ruleType == RuleType.Proc || ruleType == RuleType.Query;

        if (ParentRef is not null && ParentRef.IsValid)
        {
            Node? parentNode = ParentRef.Resolve();
            parentNode?.MakeScript(writer, story, initialTuple, printTypes);
        }

        writer.WriteLine("THEN");
        foreach (Call call in Calls)
        {
            if (call is not null)
            {
                call.MakeScript(writer, story, initialTuple, false);
                writer.WriteLine(";");
            }
        }
    }

    private void RemoveQueryPostfix(Story story)
    {
        if (IsQuery)
        {
            Node ruleRoot = GetRoot(story);
            if (ruleRoot?.Name is not null && ruleRoot.Name.Length > 7 && ruleRoot.Name.EndsWith("__DEF__", StringComparison.Ordinal))
            {
                ruleRoot.Name = ruleRoot.Name[..^7];
            }
        }
    }

    public override void PostLoad(Story story)
    {
        base.PostLoad(story);
        RemoveQueryPostfix(story);
    }

    public override void PreSave(Story story)
    {
        base.PreSave(story);

        if (IsQuery)
        {
            Node ruleRoot = GetRoot(story);
            if (ruleRoot is not null)
            {
                string name = ruleRoot.Name ?? string.Empty;
                if (name.Length < 7 || !name.EndsWith("__DEF__", StringComparison.Ordinal))
                {
                    ruleRoot.Name = $"{name}__DEF__";
                }
            }
        }
    }

    public override void PostSave(Story story)
    {
        base.PostSave(story);
        RemoveQueryPostfix(story);
    }
}
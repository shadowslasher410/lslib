namespace LSLib.LS.Story;

public class Goal(Story story) : IOsirisSerializable
{
    public uint Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte SubGoalCombination { get; set; }
    public List<GoalReference> ParentGoals { get; set; } = [];
    public List<GoalReference> SubGoals { get; set; } = [];
    public byte Flags { get; set; } // 0x02 = Child goal
    public List<Call> InitCalls { get; set; } = [];
    public List<Call> ExitCalls { get; set; } = [];
    public Story Story { get; set; } = story ?? throw new ArgumentNullException(nameof(story));

    public void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        Index = reader.ReadUInt32();
        Name = reader.ReadString() ?? string.Empty;
        SubGoalCombination = reader.ReadByte();

        ParentGoals = reader.ReadRefList<GoalReference, Goal>();
        SubGoals = reader.ReadRefList<GoalReference, Goal>();

        Flags = reader.ReadByte();

        if (reader.Ver >= OsiVersion.VerAddInitExitCalls)
        {
            InitCalls = reader.ReadList<Call>();
            ExitCalls = reader.ReadList<Call>();
        }
        else
        {
            InitCalls = [];
            ExitCalls = [];
        }
    }

    public void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write(Index);
        writer.Write(Name);
        writer.Write(SubGoalCombination);

        writer.WriteList(ParentGoals);
        writer.WriteList(SubGoals);

        writer.Write(Flags);

        if (writer.Ver >= OsiVersion.VerAddInitExitCalls)
        {
            writer.WriteList(InitCalls);
            writer.WriteList(ExitCalls);
        }
    }

    public void DebugDump(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);

        writer.WriteLine($"{Name}: SubGoalCombiner {SubGoalCombination}, Flags {Flags}");

        if (ParentGoals.Count > 0)
        {
            writer.Write("    Parent goals: ");
            foreach (GoalReference goalRef in ParentGoals)
            {
                if (goalRef?.Resolve() is Goal goal)
                {
                    writer.Write($"#{goal.Index} {goal.Name}, ");
                }
            }
            writer.WriteLine();
        }

        if (SubGoals.Count > 0)
        {
            writer.Write("    Subgoals: ");
            foreach (GoalReference goalRef in SubGoals)
            {
                if (goalRef?.Resolve() is Goal goal)
                {
                    writer.Write($"#{goal.Index} {goal.Name}, ");
                }
            }
            writer.WriteLine();
        }

        if (InitCalls.Count > 0)
        {
            writer.WriteLine("    Init Calls: ");
            foreach (Call call in InitCalls)
            {
                if (call is null) continue;
                writer.Write("        ");
                call.DebugDump(writer, story);
                writer.WriteLine();
            }
        }

        if (ExitCalls.Count > 0)
        {
            writer.WriteLine("    Exit Calls: ");
            foreach (Call call in ExitCalls)
            {
                if (call is null) continue;
                writer.Write("        ");
                call.DebugDump(writer, story);
                writer.WriteLine();
            }
        }
    }

    public void MakeScript(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);

        writer.WriteLine("Version 1");
        writer.WriteLine("SubGoalCombiner SubGoalCombinerAnd");
        writer.WriteLine();
        writer.WriteLine("InitSection");

        Tuple nullTuple = new();

        foreach (Call call in InitCalls)
        {
            if (call is null) continue;
            call.MakeScript(writer, story, nullTuple, false);
            writer.WriteLine(";");
        }

        writer.WriteLine();
        writer.WriteLine("KbSection");

        foreach (var (_, nodeValue) in story.Nodes)
        {
            if (nodeValue is RuleNode rule &&
                rule.DerivedGoalRef is { Index: var ruleIndex } &&
                ruleIndex == Index)
            {
                nodeValue.MakeScript(writer, story, nullTuple, false);
                writer.WriteLine();
            }
        }

        writer.WriteLine();
        writer.WriteLine("ExitSection");

        foreach (Call call in ExitCalls)
        {
            if (call is null) continue;
            call.MakeScript(writer, story, nullTuple, false);
            writer.WriteLine(";");
        }

        writer.WriteLine("EndExitSection");
        writer.WriteLine();

        foreach (GoalReference goalRef in ParentGoals)
        {
            if (goalRef?.Resolve() is Goal goal)
            {
                writer.WriteLine($"ParentTargetEdge \"{goal.Name}\"");
            }
        }
    }
}
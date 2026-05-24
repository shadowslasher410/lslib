using System;
using System.Collections.Generic;
using System.IO;

namespace LSLib.LS.Story;

public class Call : IOsirisSerializable
{
    public string Name { get; set; } = string.Empty;
    public List<TypedValue> Parameters { get; set; } = [];
    public bool Negate { get; set; }
    public int GoalIdOrDebugHook { get; set; }

    public void Read(OsiReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        Name = reader.ReadString() ?? string.Empty;
        if (Name.Length > 0)
        {
            byte hasParams = reader.ReadByte();
            if (hasParams > 0)
            {
                byte numParams = reader.ReadByte();
                Parameters = new List<TypedValue>(numParams);

                for (int i = 0; i < numParams; i++)
                {
                    TypedValue param;
                    if (reader.Ver >= OsiVersion.VerValueFlags)
                    {
                        param = new Variable();
                    }
                    else
                    {
                        byte type = reader.ReadByte();
                        if (type == 1)
                            param = new Variable();
                        else
                            param = new TypedValue();
                    }
                    param.Read(reader);
                    Parameters.Add(param);
                }
            }
            else
            {
                Parameters = [];
            }

            Negate = reader.ReadBoolean();
        }
        else
        {
            Parameters = [];
        }

        GoalIdOrDebugHook = reader.ReadInt32();
    }

    public void Write(OsiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write(Name);
        if (Name.Length > 0)
        {
            writer.Write(Parameters is { Count: > 0 });
            if (Parameters is { Count: > 0 })
            {
                writer.Write((byte)Parameters.Count);
                foreach (TypedValue param in Parameters)
                {
                    if (writer.Ver < OsiVersion.VerValueFlags)
                    {
                        writer.Write(param is Variable);
                    }
                    param.Write(writer);
                }
            }

            writer.Write(Negate);
        }

        writer.Write(GoalIdOrDebugHook);
    }

    public void DebugDump(TextWriter writer, Story story)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);

        if (Name.Length > 0)
        {
            if (Negate) writer.Write("!");
            writer.Write("{0}(", Name);

            for (int i = 0; i < Parameters.Count; i++)
            {
                Parameters[i].DebugDump(writer, story);
                if (i < Parameters.Count - 1) writer.Write(", ");
            }

            writer.Write(") ");
        }

        if (GoalIdOrDebugHook != 0)
        {
            if (GoalIdOrDebugHook < 0)
            {
                writer.Write("<Debug hook #{0}>", -GoalIdOrDebugHook);
            }
            else
            {
                if (story.Goals.TryGetValue((uint)GoalIdOrDebugHook, out Goal? goal))
                {
                    writer.Write("<Complete goal #{0} {1}>", GoalIdOrDebugHook, goal.Name);
                }
                else
                {
                    writer.Write("<Complete goal #{0} (Unresolved Goal Reference)>", GoalIdOrDebugHook);
                }
            }
        }
    }

    public void MakeScript(TextWriter writer, Story story, Tuple tuple, bool printTypes)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(tuple);

        if (Name.Length > 0)
        {
            if (Negate) writer.Write("NOT ");
            writer.Write("{0}(", Name);

            for (int i = 0; i < Parameters.Count; i++)
            {
                TypedValue param = Parameters[i];
                param.MakeScript(writer, story, tuple, printTypes);
                if (i < Parameters.Count - 1)
                    writer.Write(", ");
            }

            writer.Write(")");
        }

        if (GoalIdOrDebugHook > 0)
        {
            writer.Write("GoalCompleted");
        }
    }
}
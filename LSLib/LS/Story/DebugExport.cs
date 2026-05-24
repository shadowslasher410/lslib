using System.Globalization;
using System.Text.Json;

namespace LSLib.LS.Story;

public sealed class StoryDebugExportVisitor(Stream outputStream)
{
    private readonly Stream _stream = outputStream ?? throw new ArgumentNullException(nameof(outputStream));

    public void Visit(Story story)
    {
            var options = new JsonWriterOptions
            {
                Indented = true,
                IndentCharacter = '\t',
                IndentSize = 1,
                SkipValidation = false
            };
            using var writer = new Utf8JsonWriter(_stream, options);
            writer.WriteStartObject();

            writer.WritePropertyName("types");
            writer.WriteStartObject();
        foreach (KeyValuePair<uint, OsirisType> type in story.Types)
        {
            writer.WritePropertyName(type.Key.ToString(CultureInfo.InvariantCulture));
            Visit(writer, type.Value);
        }
        writer.WriteEndObject();

        writer.WritePropertyName("objects");
        writer.WriteStartObject();
        foreach (OsirisDivObject obj in story.DivObjects)
        {
            if (obj?.Name is not null)
            {
                writer.WritePropertyName(obj.Name);
                Visit(writer, obj);
            }
        }
        writer.WriteEndObject();

        writer.WritePropertyName("functions");
        writer.WriteStartObject();
        int funcId = 1;
        foreach (Function fun in story.Functions)
        {
            writer.WritePropertyName(funcId.ToString(CultureInfo.InvariantCulture));
            funcId++;
            Visit(writer, fun);
        }
        writer.WriteEndObject();

        writer.WritePropertyName("nodes");
        writer.WriteStartObject();
        foreach (KeyValuePair<uint, Node> node in story.Nodes)
        {
            writer.WritePropertyName(node.Key.ToString(CultureInfo.InvariantCulture));
            writer.WriteStartObject();
            VisitNode(writer, node.Value);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();

        writer.WritePropertyName("adapters");
        writer.WriteStartObject();
        foreach (KeyValuePair<uint, Adapter> adapter in story.Adapters)
        {
            writer.WritePropertyName(adapter.Key.ToString(CultureInfo.InvariantCulture));
            writer.WriteStartObject();
            writer.WriteEndObject();
        }
        writer.WriteEndObject();

        writer.WritePropertyName("databases");
        writer.WriteStartObject();
        foreach (KeyValuePair<uint, Database> database in story.Databases)
        {
            writer.WritePropertyName(database.Key.ToString(CultureInfo.InvariantCulture));
            writer.WriteStartObject();
            writer.WriteEndObject();
        }
        writer.WriteEndObject();

        writer.WritePropertyName("goals");
        writer.WriteStartObject();
        foreach (KeyValuePair<uint, Goal> goal in story.Goals)
        {
            writer.WritePropertyName(goal.Key.ToString(CultureInfo.InvariantCulture));
            writer.WriteStartObject();
            writer.WriteEndObject();
        }
        writer.WriteEndObject();

        writer.WriteEndObject();
        writer.Flush();
    }

    public static void Visit(Utf8JsonWriter writer, OsirisType type)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(type);

        writer.WriteStartObject();
        writer.WriteString("name", type.Name ?? string.Empty);
        writer.WriteEndObject();
    }

    public static void Visit(Utf8JsonWriter writer, OsirisDivObject obj)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(obj);

        writer.WriteStartObject();
        writer.WriteString("name", obj.Name ?? string.Empty);
        writer.WriteNumber("type", obj.Type);
        writer.WriteEndObject();
    }

    public static void Visit(Utf8JsonWriter writer, NodeReference r)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(r);

        if (r.IsNull)
            writer.WriteNullValue();
        else
            writer.WriteNumberValue(r.Index);
    }

    public static void Visit(Utf8JsonWriter writer, FunctionSignature fun)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(fun);

        writer.WriteStartObject();
        writer.WriteString("name", fun.Name ?? string.Empty);

        if (fun.OutParamMask is { Count: > 0 })
        {
            writer.WriteNumber("out", fun.OutParamMask[0]);
        }

        writer.WritePropertyName("params");
        writer.WriteStartObject();
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    public static void Visit(Utf8JsonWriter writer, Function fun)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(fun);

        writer.WriteStartObject();
        writer.WritePropertyName("signature");
        if (fun.Name is not null) Visit(writer, fun.Name);
        writer.WriteString("type", fun.Type.ToString());
        writer.WritePropertyName("ref");
        if (fun.NodeRef is not null) Visit(writer, fun.NodeRef);
        writer.WriteEndObject();
    }

    public static void VisitNode(Utf8JsonWriter writer, Node node)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(node);

        if (node is RelOpNode relOpNode)
        {
            Visit(writer, relOpNode);
        }
        else if (node is RuleNode)
        {
            writer.WriteString("nodeType", "RuleNode");
        }
        else if (node is UserQueryNode or InternalQueryNode or DivQueryNode or QueryNode)
        {
            writer.WriteString("nodeType", "QueryNode");
        }
        else if (node is AndNode or NotAndNode or JoinNode)
        {
            writer.WriteString("nodeType", "JoinNode");
        }
        else if (node is ProcNode or DatabaseNode or DataNode)
        {
            writer.WriteString("nodeType", "DataNode");
        }
        else
        {
            throw new NotSupportedException($"Unsupported AST narrative node format pattern encountered: {node.GetType().Name}");
        }
    }

    public static void Visit(Utf8JsonWriter writer, Value val)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(val);

        writer.WriteNumber("type", val.TypeId);
        writer.WriteString("value", val.ToString() ?? string.Empty);
    }

    public static void Visit(Utf8JsonWriter writer, TypedValue val)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(val);

        Visit(writer, (Value)val);
        writer.WriteBoolean("valid", val.IsValid);
        writer.WriteBoolean("out", val.OutParam);
        writer.WriteBoolean("isType", val.IsAType);
    }

    public static void Visit(Utf8JsonWriter writer, Variable var)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(var);

        Visit(writer, (TypedValue)var);
        writer.WriteNumber("index", var.Index);
        writer.WriteBoolean("unused", var.Unused);
        writer.WriteBoolean("adapted", var.Adapted);
        writer.WriteString("name", var.VariableName ?? string.Empty);
    }

    public static void VisitVar(Utf8JsonWriter writer, Value val)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(val);

        writer.WriteStartObject();
        if (val is Variable variable)
            Visit(writer, variable);
        else if (val is TypedValue typedValue)
            Visit(writer, typedValue);
        else
            Visit(writer, val);
        writer.WriteEndObject();
    }

    public static void Visit(Utf8JsonWriter writer, AdapterReference r)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(r);

        if (r.IsNull)
            writer.WriteNullValue();
        else
            writer.WriteNumberValue(r.Index);
    }

    public static void Visit(Utf8JsonWriter writer, DatabaseReference r)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(r);

        if (r.IsNull)
            writer.WriteNullValue();
        else
            writer.WriteNumberValue(r.Index);
    }

    public static void Visit(Utf8JsonWriter writer, GoalReference r)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(r);

        if (r.IsNull)
            writer.WriteNullValue();
        else
            writer.WriteNumberValue(r.Index);
    }

    public static void Visit(Utf8JsonWriter writer, NodeEntryItem entry)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(entry);

        writer.WriteStartObject();
        writer.WritePropertyName("node");
        if (entry.NodeRef is not null) Visit(writer, entry.NodeRef);
        writer.WriteString("entry", entry.EntryPoint.ToString());
        writer.WritePropertyName("goal");
        if (entry.GoalRef is not null) Visit(writer, entry.GoalRef);
        writer.WriteEndObject();
    }

    public static void Visit(Utf8JsonWriter writer, RelOpNode node)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(node);

        writer.WriteString("op", node.RelOp.ToString());
        writer.WritePropertyName("left");
        if (node.LeftValue is not null) VisitVar(writer, node.LeftValue);
        writer.WriteNumber("leftIndex", node.LeftValueIndex);
        writer.WritePropertyName("right");
        if (node.RightValue is not null) VisitVar(writer, node.RightValue);
        writer.WriteNumber("rightIndex", node.RightValueIndex);
    }

    public static void Visit(Utf8JsonWriter writer, RuleNode node)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(node);

        Visit(writer, (RelNode)node);

        writer.WritePropertyName("calls");
        writer.WriteStartArray();
        foreach (Call call in node.Calls)
        {
            if (call is not null) Visit(writer, call);
        }
        writer.WriteEndArray();

        writer.WritePropertyName("variables");
        writer.WriteStartArray();
        foreach (Variable v in node.Variables)
        {
            if (v is not null) VisitVar(writer, v);
        }
        writer.WriteEndArray();

        writer.WriteNumber("line", node.Line);
        writer.WriteBoolean("query", node.IsQuery);
    }

    public static void Visit(Utf8JsonWriter writer, RelNode node)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(node);

        Visit(writer, (TreeNode)node);

        writer.WritePropertyName("parent");
        if (node.ParentRef is not null) Visit(writer, node.ParentRef);

        writer.WritePropertyName("adapter");
        if (node.AdapterRef is not null) Visit(writer, node.AdapterRef);

        writer.WritePropertyName("databaseNode");
        if (node.RelDatabaseNodeRef is not null) Visit(writer, node.RelDatabaseNodeRef);

        writer.WritePropertyName("databaseJoin");
        if (node.RelJoin is not null) Visit(writer, node.RelJoin);

        writer.WriteNumber("databaseIndirection", node.RelDatabaseIndirection);
    }

    public static void Visit(Utf8JsonWriter writer, TreeNode node)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(node);

        Visit(writer, (Node)node);

        writer.WritePropertyName("next");
        if (node.NextNode is not null) Visit(writer, node.NextNode);
    }

    public static void Visit(Utf8JsonWriter writer, Node node)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(node);

        writer.WriteString("type", node.TypeName() ?? string.Empty);
        writer.WriteString("name", node.Name ?? string.Empty);
        writer.WriteNumber("numParams", node.NumParams);

        writer.WritePropertyName("nodeDb");
        if (node.DatabaseRef is not null) Visit(writer, node.DatabaseRef);
    }

    public static void Visit(Utf8JsonWriter writer, QueryNode node)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(node);

        Visit(writer, (Node)node);
    }

    public static void Visit(Utf8JsonWriter writer, JoinNode node)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(node);

        Visit(writer, (Node)node);

        writer.WritePropertyName("left");
        writer.WriteStartObject();
        writer.WritePropertyName("parent");
        if (node.LeftParentRef is not null) Visit(writer, node.LeftParentRef);
        writer.WritePropertyName("adapter");
        if (node.LeftAdapterRef is not null) Visit(writer, node.LeftAdapterRef);
        writer.WritePropertyName("databaseNode");
        if (node.LeftDatabaseNodeRef is not null) Visit(writer, node.LeftDatabaseNodeRef);
        writer.WritePropertyName("databaseJoin");
        if (node.LeftDatabaseJoin is not null) Visit(writer, node.LeftDatabaseJoin);
        writer.WriteNumber("databaseIndirection", node.LeftDatabaseIndirection);
        writer.WriteEndObject();

        writer.WritePropertyName("right");
        writer.WriteStartObject();
        writer.WritePropertyName("parent");
        if (node.RightParentRef is not null) Visit(writer, node.RightParentRef);
        writer.WritePropertyName("adapter");
        if (node.RightAdapterRef is not null) Visit(writer, node.RightAdapterRef);
        writer.WritePropertyName("databaseNode");
        if (node.RightDatabaseNodeRef is not null) Visit(writer, node.RightDatabaseNodeRef);
        writer.WritePropertyName("databaseJoin");
        if (node.RightDatabaseJoin is not null) Visit(writer, node.RightDatabaseJoin);
        writer.WriteNumber("databaseIndirection", node.RightDatabaseIndirection);
        writer.WriteEndObject();
    }

    public static void Visit(Utf8JsonWriter writer, DataNode node)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(node);

        Visit(writer, (Node)node);

        writer.WritePropertyName("references");
        writer.WriteStartArray();
        foreach (NodeEntryItem r in node.ReferencedBy)
        {
            if (r is not null) Visit(writer, r);
        }
        writer.WriteEndArray();
    }

    public static void Visit(Utf8JsonWriter writer, Tuple tuple)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(tuple);

        writer.WriteStartObject();
        int count = tuple.Logical.Count;
        if (count > 0)
        {
            int i = 0;
            foreach (KeyValuePair<int, Value> pair in tuple.Logical)
            {
                writer.WritePropertyName(pair.Key.ToString(CultureInfo.InvariantCulture));
                if (pair.Value is not null) VisitVar(writer, pair.Value);
                i++;
            }
        }
        writer.WriteEndObject();
    }

    public static void Visit(Utf8JsonWriter writer, Adapter adapter)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(adapter);

        writer.WriteStartObject();

        Tuple? constants = adapter.Constants;
        writer.WritePropertyName("constants");
        if (constants is not null)
        {
            Visit(writer, constants);
        }

        writer.WritePropertyName("logical");
        writer.WriteStartArray();
        foreach (sbyte index in adapter.LogicalIndices)
        {
            writer.WriteNumberValue(index);
        }
        writer.WriteEndArray();

        writer.WritePropertyName("mappings");
        writer.WriteStartObject();
        foreach (KeyValuePair<byte, byte> pair in adapter.LogicalToPhysicalMap)
        {
            writer.WritePropertyName(pair.Key.ToString(CultureInfo.InvariantCulture));
            writer.WriteNumberValue(pair.Value);
        }
        writer.WriteEndObject();

        writer.WritePropertyName("output");
        writer.WriteStartArray();
        for (int i = 0; i < adapter.LogicalIndices.Count; i++)
        {
            sbyte index = adapter.LogicalIndices[i];
            if (index != -1)
            {
                writer.WriteStringValue($"input[{index}]");
            }
            else if (constants is not null && constants.Logical.TryGetValue(i, out Value? value))
            {
                if (value is not null) VisitVar(writer, value);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
    }

    public static void Visit(Utf8JsonWriter writer, ParameterList args)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(args);

        writer.WriteStartArray();
        foreach (uint arg in args.Types)
        {
            writer.WriteNumberValue(arg);
        }
        writer.WriteEndArray();
    }

    public static void Visit(Utf8JsonWriter writer, Fact fact)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(fact);

        writer.WriteStartArray();
        foreach (Value val in fact.Columns)
        {
            if (val is not null) VisitVar(writer, val);
        }
        writer.WriteEndArray();
    }

    public static void Visit(Utf8JsonWriter writer, FactCollection facts)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(facts);

        writer.WriteStartArray();
        foreach (Fact fact in facts)
        {
            if (fact is not null) Visit(writer, fact);
        }
        writer.WriteEndArray();
    }

    public static void Visit(Utf8JsonWriter writer, Database db)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(db);

        writer.WriteStartObject();
        writer.WritePropertyName("columns");
        if (db.Parameters is not null) Visit(writer, db.Parameters);
        writer.WritePropertyName("facts");
        if (db.Facts is not null) Visit(writer, db.Facts);
        writer.WriteEndObject();
    }

    public static void Visit(Utf8JsonWriter writer, Call call)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(call);

        writer.WriteStartObject();
        writer.WriteBoolean("negate", call.Negate);
        writer.WriteString("name", call.Name ?? string.Empty);

        if (call.Parameters is { Count: > 0 })
        {
            writer.WritePropertyName("params");
            writer.WriteStartArray();
            foreach (TypedValue arg in call.Parameters)
            {
                if (arg is not null) VisitVar(writer, arg);
            }
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }

    public static void Visit(Utf8JsonWriter writer, List<Call> calls)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(calls);

        writer.WriteStartArray();
        foreach (Call call in calls)
        {
            if (call is not null) Visit(writer, call);
        }
        writer.WriteEndArray();
    }

    public static void Visit(Utf8JsonWriter writer, Goal goal)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(goal);

        writer.WriteStartObject();
        writer.WriteString("name", goal.Name ?? string.Empty);
        writer.WriteNumber("sgc", goal.SubGoalCombination);

        writer.WritePropertyName("init");
        if (goal.InitCalls is not null) Visit(writer, goal.InitCalls);

        writer.WritePropertyName("exit");
        if (goal.ExitCalls is not null) Visit(writer, goal.ExitCalls);

        writer.WriteEndObject();
    }
}
using LSLib.DebuggerFrontend.ExpressionParser;
using LSLib.LS.Story;
using LSLib.LS.Story.Compiler;
using System.Runtime.InteropServices;

namespace LSTools.DebuggerFrontend;

public class PendingExpressionEvaluation
{
    public required DAPRequest Request { get; init; }
    public required EvaluationResults Results { get; init; }
    public required NodeDebugInfo Node { get; init; }
    public required FunctionDebugInfo Function { get; init; }
}

class ExpressionEvaluator(
    StoryDebugInfo debugInfo,
    DAPStream dap,
    DebuggerClient dbgClient,
    EvaluationResultManager results)
{
    private Dictionary<FunctionNameAndArity, NodeDebugInfo> _nameToNodeMap = [];
    private readonly Dictionary<uint, PendingExpressionEvaluation> _pendingEvaluations = [];
    public DatabaseEnumerator DatabaseDumper { get; init; } = new(dbgClient, dap, debugInfo, results);


    public void Initialize()
    {
        dbgClient.OnEvaluateRow = OnEvaluateRow;
        dbgClient.OnEvaluateFinished = OnEvaluateFinished;
        MakeFunctionNameMap();
    }

    private void MakeFunctionNameMap()
    {
        _nameToNodeMap = [];
        foreach (var node in debugInfo.Nodes.Values)
        {
            if (node.FunctionName is null) continue;

            ref var existingNode = ref CollectionsMarshal.GetValueRefOrAddDefault(_nameToNodeMap, node.FunctionName, out bool exists);

            if (!exists || existingNode?.Type != Node.Type.UserQuery)
            {
                existingNode = node;
            }
        }
    }

    private static MsgTypedValue ConstantToTypedValue(ConstantValue c)
    {
        var typeId = c.Type switch
        {
            IRConstantType.Integer => (uint)Value.Type.Integer,
            IRConstantType.Float => (uint)Value.Type.Float,
            IRConstantType.String => (uint)Value.Type.String,
            IRConstantType.Name => (uint)Value.Type.GuidString,
            _ => throw new ArgumentException($"Constant has unknown or unmappable type: {c.Type}")
        };

        var tv = new MsgTypedValue { TypeId = typeId };

        switch (c.Type)
        {
            case IRConstantType.Integer: tv.Intval = c.IntegerValue; break;
            case IRConstantType.Float: tv.Floatval = c.FloatValue; break;
            case IRConstantType.String:
            case IRConstantType.Name: tv.Stringval = c.StringValue; break;
        }

        return tv;
    }

    private static MsgTypedValue VariableToTypedValue(LocalVar lvar, CoalescedFrame frame)
    {
        if (lvar.Name == "_")
        {
            return new MsgTypedValue { TypeId = (uint)Value.Type.None };
        }

        var frameVar = frame.Variables.FirstOrDefault(v => v.Name == lvar.Name)
            ?? throw new RequestFailedException($"Variable does not exist within the current evaluation context frame: \"{lvar.Name}\"");

        return frameVar.TypedValue;
    }

    private static MsgTuple ParamsToTuple(IEnumerable<RValue> args, CoalescedFrame? frame)
    {
        var tuple = new MsgTuple();
        foreach (var arg in args)
        {
            switch (arg)
            {
                case ConstantValue constVal:
                    tuple.Column.Add(ConstantToTypedValue(constVal));
                    break;
                case LocalVar lvar when frame is not null:
                    tuple.Column.Add(VariableToTypedValue(lvar, frame));
                    break;
                case LocalVar:
                    throw new RequestFailedException("Local variables cannot be safely evaluated without an active context stack frame reference.");
            }
        }
        return tuple;
    }

    public void EvaluateCall(DAPRequest request, Statement stmt, CoalescedFrame? frame, bool allowMutation)
    {
        var func = new FunctionNameAndArity(stmt.Name, stmt.Params.Count);
        if (!_nameToNodeMap.TryGetValue(func, out var node))
        {
            dap.SendReply(request, $"Name signature not found: {func}");
            return;
        }

        if (node.FunctionName is { } funcName)
        {
            var function = debugInfo.Functions[funcName];
            var args = ParamsToTuple(stmt.Params, frame);

            EvalType evalType = node.Type switch
            {
                Node.Type.Database => stmt.Not ? EvalType.Insert : EvalType.Delete,
                Node.Type.Proc when stmt.Not => throw new RequestFailedException("\"NOT\" statements are not supported for PROC types."),
                Node.Type.Proc => EvalType.Insert,

                Node.Type.DivQuery or
                Node.Type.InternalQuery or
                Node.Type.UserQuery when stmt.Not => throw new RequestFailedException("\"NOT\" statements are not supported for QRY types."),
                Node.Type.DivQuery or
                Node.Type.InternalQuery or
                Node.Type.UserQuery => EvalType.IsValid,

                _ => throw new RequestFailedException($"Evaluation target node type execution layout profile is not supported: {node.Type}")
            };

            if ((evalType != EvalType.IsValid || node.Type == Node.Type.UserQuery) && !allowMutation)
            {
                throw new RequestFailedException("Evaluation was rejected because it could mutate active game memory state fields.");
            }

            uint seq = dbgClient.SendEvaluate(evalType, node.Id, args);

            List<string> argNames = [.. function.Params.Select(static arg => arg.Name)];

            _pendingEvaluations.Add(seq, new PendingExpressionEvaluation
            {
                Request = request,
                Results = results.MakeResults(function.Params.Count, argNames),
                Node = node,
                Function = function
            });
        }
        else
        {
            throw new InvalidOperationException($"The evaluated node {node.Id} does not possess a valid FunctionName signature.");
        }
    }


    public void EvaluateName(DAPRequest request, string name, bool allowMutation)
    {
        if (name == "help")
        {
            SendUsage();
            return;
        }

        var db = debugInfo.Databases.Values.FirstOrDefault(r => r.Name == name)
            ?? throw new RequestFailedException($"Target database signature context record does not exist: \"{name}\"");

        if (!allowMutation)
        {
            throw new RequestFailedException($"Evaluation of database '{name}' rejected: inspecting database states requires side-effect mutation permissions.");
        }

        DatabaseDumper.RequestDatabaseEvaluation(request, db.Id);
    }

    private void SendUsage()
    {
        const string usageText = """
            Basic Usage:
                Dump the contents of a database: DB_Database
                Insert a row into a database (EXPERIMENTAL!): DB_Database(1, 2, 3)
                Delete a row from a database (EXPERIMENTAL!): NOT DB_Database(4, 5, 6)
                Evaluate a query: QRY_Query("test")
                Evaluate a built-in query: IntegerSum(100, 200, _)
                Call a PROC: PROC_Proc(111.0, TEST_12345678-1234-1234-1234-123456789abc)
                Call a built-in call (NOT YET COMPLETE!): SetStoryEvent(...)
                Trigger an event: GameStarted("FTJ_FortJoy", 1)

            Notes:
                - Built-in queries will return their output if they succeed.
                - You can use local variables from the active rule (_Char, etc.) in the expressions.
            """;
        dap.SendEvent("output", new DAPOutputMessage
        {
            Category = "console",
            Output = usageText
        });
    }

    private static Statement? Parse(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return null;
        var parserHarness = new ExpressionParser.ExpressionParser(expression);
        return parserHarness.Parse() ? parserHarness.GetStatement() : null;
    }

    public void Evaluate(DAPRequest request, string expression, CoalescedFrame frame, bool allowMutation)
    {
        var stmt = Parse(expression);
        if (stmt is null)
        {
            dap.SendReply(request, "Syntax error. Type \"help\" for usage.");
            return;
        }

        if (stmt.Params is null)
        {
            EvaluateName(request, stmt.Name, allowMutation);
        }
        else
        {
            EvaluateCall(request, stmt, frame, allowMutation);
        }
    }

    private void OnEvaluateRow(uint seq, BkEvaluateRow msg)
    {
        ref var pendingEval = ref CollectionsMarshal.GetValueRefOrAddDefault(_pendingEvaluations, seq, out bool exists);
        if (!exists || pendingEval is null) return;

        var resultsObj = pendingEval.Results;
        foreach (var row in msg.Row)
        {
            resultsObj.Add(row);
        }
    }

    private void OnEvaluateFinished(uint seq, BkEvaluateFinished msg)
    {
        if (!_pendingEvaluations.Remove(seq, out var eval)) return;

        if (msg.ResultCode != StatusCode.Success)
        {
            dap.SendReply(eval.Request, $"Evaluation failed: DBG server sent error code: {msg.ResultCode}");
            return;
        }

        var funcType = eval.Node.Type == Node.Type.UserQuery
            ? LSLib.LS.Story.FunctionType.UserQuery
            : (LSLib.LS.Story.FunctionType)eval.Function.TypeId;

        var (consoleText, resultText, returnResults) = funcType switch
        {
            LSLib.LS.Story.FunctionType.Event =>
                ($"Event {eval.Node.FunctionName} triggered", "", false),

            LSLib.LS.Story.FunctionType.Query or
            LSLib.LS.Story.FunctionType.SysQuery or
            LSLib.LS.Story.FunctionType.UserQuery =>
                (msg.QuerySucceeded ? $"Query {eval.Node.FunctionName} SUCCEEDED" : $"Query {eval.Node.FunctionName} FAILED",
                 "Query results",
                 funcType != LSLib.LS.Story.FunctionType.UserQuery),

            LSLib.LS.Story.FunctionType.Proc =>
                ($"PROC {eval.Node.FunctionName} called", "", false),

            LSLib.LS.Story.FunctionType.SysCall or
            LSLib.LS.Story.FunctionType.Call =>
                ($"Built-in function {eval.Node.FunctionName} called", "", false),

            LSLib.LS.Story.FunctionType.Database =>
                ($"Inserted row into {eval.Node.FunctionName}", "", false),

            _ => throw new InvalidOperationException($"Unknown function type: {eval.Function.TypeId}")
        };

        if (consoleText is { Length: > 0 })
        {
            dap.SendEvent("output", new DAPOutputMessage
            {
                Category = "console",
                Output = $"{consoleText}\r\n"
            });
        }

        if (funcType == LSLib.LS.Story.FunctionType.Database)
        {
            DatabaseDumper.RequestDatabaseEvaluation(eval.Request, eval.Node.DatabaseId);
            return;
        }

        dap.SendReply(eval.Request, new DAPEvaluateResponse
        {
            Result = resultText,
            NamedVariables = 0,
            IndexedVariables = returnResults ? eval.Results.Count : 0,
            VariablesReference = returnResults ? eval.Results.VariablesReference : 0
        });
    }
}
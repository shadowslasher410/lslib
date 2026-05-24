using LSLib.Granny;
using LSLib.LS.Story;
using LSLib.LS.Story.Compiler;
using LSLib.Parser;
using System.Diagnostics;

namespace LSLib.LS.Story.Compiler;

/// <summary>
/// Type of reference being made to a named function.
/// </summary>
public enum NameRefType
{
    // Function is not referenced, only emitted
    None,
    // Function is referenced in the IF part of a rule
    Condition,
    // Function referenced in the THEN part of a rule, or init/exit section of a goal
    Action
}

public class StoryEmitter(CompilationContext context)
{
    private readonly CompilationContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private Story _story = new();
    private readonly Dictionary<IRGoal, Goal> _goals = [];
    private readonly Dictionary<FunctionNameAndArity, Database> _databases = [];
    private readonly Dictionary<FunctionNameAndArity, Node> _funcs = [];
    private readonly Dictionary<FunctionNameAndArity, Function> _funcEntries = [];
    private readonly Dictionary<IRRule, RuleNode> _rules = [];
    public StoryDebugInfo? DebugInfo { get; set; }

    public void EnableDebugInfo()
    {
        DebugInfo = new StoryDebugInfo
        {
            Version = StoryDebugInfo.CurrentVersion
        };
    }
    private void AddStoryTypes()
    {
        foreach (var type in _context.TypesById)
        {
            var osiType = new OsirisType
            {
                Index = (byte)type.Value.TypeId,
                Alias = (type.Value.TypeId == (uint)type.Value.IntrinsicTypeId) ? (byte)0 : (byte)type.Value.IntrinsicTypeId,
                IsBuiltin = type.Value.TypeId == (uint)type.Value.IntrinsicTypeId,
                Name = type.Value.Name ?? string.Empty
            };
            _story.Types.Add(osiType.Index, osiType);
        }
    }

    private static TypedValue EmitTypedValue(IRConstant constant)
    {
        ArgumentNullException.ThrowIfNull(constant);

        return new TypedValue
        {
            TypeId = constant.Type.TypeId,
            IntValue = (int)constant.IntegerValue,
            Int64Value = constant.IntegerValue,
            FloatValue = constant.FloatValue,
            StringValue = constant.StringValue ?? string.Empty,
            IsValid = true,
            OutParam = false,
            IsAType = false
        };
    }

    private static TypedValue EmitTypedValue(IRValue val)
    {
        ArgumentNullException.ThrowIfNull(val);
        if (val is IRVariable variable)
        {
            return new Variable
            {
                TypeId = val.Type.TypeId,
                IsValid = false,
                OutParam = false,
                IsAType = true,
                Index = (sbyte)variable.Index,
                Unused = false,
                Adapted = true
            };
        }

        return EmitTypedValue((IRConstant)val);
    }

    private static Value EmitValue(IRConstant constant)
    {
        ArgumentNullException.ThrowIfNull(constant);

        return new Value
        {
            TypeId = constant.Type.TypeId,
            IntValue = (int)constant.IntegerValue,
            Int64Value = constant.IntegerValue,
            FloatValue = constant.FloatValue,
            StringValue = constant.StringValue ?? string.Empty
        };
    }
    private static LS.Story.FunctionSignature EmitFunctionSignature(FunctionSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);

        var osiSignature = new LS.Story.FunctionSignature
        {
            Name = signature.Name ?? string.Empty,
            OutParamMask = new List<byte>(signature.Params.Count / 8 + 1),
            Parameters = new ParameterList
            {
                Types = new List<uint>(signature.Params.Count)
            }
        };

        int outParamBytes = ((signature.Params.Count + 7) & ~7) >> 3;
        for (int outByte = 0; outByte < outParamBytes; outByte++)
        {
            byte outParamByte = 0;
            int limit = Math.Min((outByte + 1) * 8, signature.Params.Count);
            for (int i = outByte * 8; i < limit; i++)
            {
                if (signature.Params[i].Direction == ParamDirection.Out)
                {
                    outParamByte |= (byte)(0x80 >> (i & 7));
                }
            }
            osiSignature.OutParamMask.Add(outParamByte);
        }

        foreach (var param in signature.Params)
        {
            osiSignature.Parameters.Types.Add(param.Type.TypeId);
        }

        return osiSignature;
    }

    private void AddNodeDebugInfo(Node node, CodeLocation? location, int numColumns, IRRule? rule)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (DebugInfo is not null)
        {
            var nodeDebug = new NodeDebugInfo
            {
                Id = node.Index,
                RuleId = 0,
                Line = location?.StartLine ?? 0,
                ColumnToVariableMaps = [],
                DatabaseId = node.DatabaseRef.Index,
                Name = node.Name ?? string.Empty,
                Type = node.NodeType(),
                ParentNodeId = 0
            };

            if (node is JoinNode joinNode)
            {
                nodeDebug.ParentNodeId = joinNode.LeftParentRef.Index;
            }
            else if (node is RelNode relNode)
            {
                nodeDebug.ParentNodeId = relNode.ParentRef.Index;
            }

            if (!string.IsNullOrEmpty(node.Name))
            {
                nodeDebug.FunctionName = new FunctionNameAndArity(node.Name, node.NumParams);
            }

            if (location is not null && rule is not null)
            {
                int columnIndex = 0;
                int variableIndex = 0;
                while (columnIndex < numColumns && variableIndex < rule.Variables.Count)
                {
                    if (!rule.Variables[variableIndex].IsUnused())
                    {
                        nodeDebug.ColumnToVariableMaps.Add(columnIndex, variableIndex);
                        columnIndex++;
                    }
                    variableIndex++;
                }
            }

            DebugInfo.Nodes.Add(nodeDebug.Id, nodeDebug);
        }
    }

    private void AddNodeWithoutDebugInfo(Node node)
    {
        node.Index = (uint)_story.Nodes.Count + 1;
        _story.Nodes.Add(node.Index, node);
    }

    private void AddNode(Node node)
    {
        AddNodeWithoutDebugInfo(node);
        AddNodeDebugInfo(node, null, 0, null);
    }

    private Function EmitFunction(LS.Story.FunctionType type, FunctionSignature signature, NodeReference nodeRef)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(nodeRef);

        var osiFunc = new Function
        {
            Line = 0,
            ConditionReferences = 0,
            ActionReferences = 0,
            NodeRef = nodeRef,
            Type = type,
            Meta1 = 0,
            Meta2 = 0,
            Meta3 = 0,
            Meta4 = 0,
            Name = EmitFunctionSignature(signature)
        };

        var sig = signature.GetNameAndArity();
        _funcEntries.Add(sig, osiFunc);
        _story.Functions.Add(osiFunc);
        _story.FunctionSignatureMap.Add($"{sig.Name}/{sig.Arity}", osiFunc);

        if (DebugInfo is not null)
        {
            var funcDebug = new FunctionDebugInfo
            {
                Name = osiFunc.Name.Name ?? string.Empty,
                Params = [],
                TypeId = (uint)osiFunc.Type
            };

            foreach (var param in signature.Params)
            {
                funcDebug.Params.Add(new FunctionParamDebugInfo
                {
                    TypeId = (uint)param.Type.IntrinsicTypeId,
                    Name = param.Name ?? string.Empty,
                    Out = param.Direction == ParamDirection.Out
                });
            }

            DebugInfo.Functions.Add(sig, funcDebug);
        }

        return osiFunc;
    }

    private Function EmitFunction(LS.Story.FunctionType type, FunctionSignature signature, NodeReference nodeRef, BuiltinFunction builtin)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(nodeRef);
        ArgumentNullException.ThrowIfNull(builtin);

        var osiFunc = EmitFunction(type, signature, nodeRef);
        osiFunc.Meta1 = builtin.Meta1;
        osiFunc.Meta2 = builtin.Meta2;
        osiFunc.Meta3 = builtin.Meta3;
        osiFunc.Meta4 = builtin.Meta4;
        return osiFunc;
    }

    private InternalQueryNode? EmitSysQuery(FunctionSignature signature, NameRefType refType)
    {
        ArgumentNullException.ThrowIfNull(signature);

        var builtin = _context.LookupName(signature.GetNameAndArity()) as BuiltinFunction;
        InternalQueryNode? osiQuery = null;

        if (refType == NameRefType.Condition)
        {
            osiQuery = new InternalQueryNode
            {
                DatabaseRef = new DatabaseReference(),
                Name = signature.Name ?? string.Empty,
                NumParams = (byte)signature.Params.Count
            };
            AddNode(osiQuery);
        }

        if (builtin is not null)
        {
            EmitFunction(LS.Story.FunctionType.SysQuery, signature, NodeReference.Create(_story, osiQuery), builtin);
        }
        return osiQuery;
    }

    private void EmitSysCall(FunctionSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);

        var builtin = _context.LookupName(signature.GetNameAndArity()) as BuiltinFunction;
        if (builtin is not null)
        {
            EmitFunction(LS.Story.FunctionType.SysCall, signature, new NodeReference(), builtin);
        }
    }

    private ProcNode? EmitEvent(FunctionSignature signature, NameRefType refType)
    {
        ArgumentNullException.ThrowIfNull(signature);

        var builtin = _context.LookupName(signature.GetNameAndArity()) as BuiltinFunction;
        ProcNode? osiProc = null;

        if (refType == NameRefType.Condition)
        {
            osiProc = new ProcNode
            {
                DatabaseRef = new DatabaseReference(),
                Name = signature.Name ?? string.Empty,
                NumParams = (byte)signature.Params.Count,
                ReferencedBy = []
            };
            AddNode(osiProc);
        }

        if (builtin is not null)
        {
            EmitFunction(LS.Story.FunctionType.Event, signature, NodeReference.Create(_story, osiProc), builtin);
        }
        return osiProc;
    }

    private ProcNode? EmitCall(FunctionSignature signature, NameRefType refType)
    {
        ArgumentNullException.ThrowIfNull(signature);

        var builtin = _context.LookupName(signature.GetNameAndArity()) as BuiltinFunction;
        ProcNode? osiProc = null;

        if (refType == NameRefType.Condition)
        {
            osiProc = new ProcNode
            {
                DatabaseRef = new DatabaseReference(),
                Name = signature.Name ?? string.Empty,
                NumParams = (byte)signature.Params.Count,
                ReferencedBy = []
            };
            AddNode(osiProc);
        }

        if (builtin is not null)
        {
            EmitFunction(LS.Story.FunctionType.Call, signature, NodeReference.Create(_story, osiProc), builtin);
        }
        return osiProc;
    }

    private DivQueryNode? EmitQuery(FunctionSignature signature, NameRefType refType)
    {
        ArgumentNullException.ThrowIfNull(signature);

        var builtin = _context.LookupName(signature.GetNameAndArity()) as BuiltinFunction;
        DivQueryNode? osiQuery = null;

        if (refType == NameRefType.Condition)
        {
            osiQuery = new DivQueryNode
            {
                DatabaseRef = new DatabaseReference(),
                Name = signature.Name ?? string.Empty,
                NumParams = (byte)signature.Params.Count
            };
            AddNode(osiQuery);
        }

        if (builtin is not null)
        {
            EmitFunction(LS.Story.FunctionType.Query, signature, NodeReference.Create(_story, osiQuery), builtin);
        }
        return osiQuery;
    }

    private ProcNode EmitProc(FunctionSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);

        var osiProc = new ProcNode
        {
            DatabaseRef = new DatabaseReference(),
            Name = signature.Name ?? string.Empty,
            NumParams = (byte)signature.Params.Count,
            ReferencedBy = []
        };
        AddNode(osiProc);

        EmitFunction(LS.Story.FunctionType.Proc, signature, NodeReference.Create(_story, osiProc));
        return osiProc;
    }

    private UserQueryNode EmitUserQuery(FunctionSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);

        var osiQuery = new UserQueryNode
        {
            DatabaseRef = new DatabaseReference(),
            Name = signature.Name ?? string.Empty,
            NumParams = (byte)signature.Params.Count
        };
        AddNode(osiQuery);

        EmitFunction(LS.Story.FunctionType.Database, signature, NodeReference.Create(_story, osiQuery));
        return osiQuery;
    }

    private DatabaseNode EmitDatabase(FunctionSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);

        var osiDb = new Database
        {
            Index = (uint)_story.Databases.Count + 1,
            Parameters = new ParameterList
            {
                Types = new List<uint>(signature.Params.Count)
            },
            OwnerNode = null!
        };

        foreach (var param in signature.Params)
        {
            osiDb.Parameters.Types.Add(param.Type.TypeId);
        }

        osiDb.Facts = new FactCollection(osiDb, _story);
        _story.Databases.Add(osiDb.Index, osiDb);

        var osiDbNode = new DatabaseNode
        {
            DatabaseRef = DatabaseReference.Create(_story, osiDb),
            Name = signature.Name ?? string.Empty,
            NumParams = (byte)signature.Params.Count,
            ReferencedBy = []
        };
        AddNode(osiDbNode);

        osiDb.OwnerNode = osiDbNode;

        EmitFunction(LS.Story.FunctionType.Database, signature, NodeReference.Create(_story, osiDbNode));

        if (DebugInfo is not null)
        {
            var dbDebug = new DatabaseDebugInfo
            {
                Id = osiDb.Index,
                Name = signature.Name ?? string.Empty,
                ParamTypes = []
            };
            foreach (var param in signature.Params)
            {
                dbDebug.ParamTypes.Add(param.Type.TypeId);
            }

            DebugInfo.Databases.Add(dbDebug.Id, dbDebug);
        }

        return osiDbNode;
    }

    private Database? EmitIntermediateDatabase(IRRule rule, int tupleSize, Node ownerNode)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var paramTypes = new List<uint>(tupleSize);
        for (int i = 0; i < tupleSize; i++)
        {
            if (i >= rule.Variables.Count) break;
            var param = rule.Variables[i];
            if (!param.IsUnused())
            {
                paramTypes.Add(param.Type.TypeId);
            }
        }

        if (paramTypes.Count == 0)
        {
            return null;
        }

        var osiDb = new Database
        {
            Index = (uint)_story.Databases.Count + 1,
            Parameters = new ParameterList
            {
                Types = paramTypes
            },
            OwnerNode = ownerNode,
            Facts = null!
        };

        osiDb.Facts = new FactCollection(osiDb, _story);
        _story.Databases.Add(osiDb.Index, osiDb);

        if (DebugInfo is not null)
        {
            var dbDebug = new DatabaseDebugInfo
            {
                Id = osiDb.Index,
                Name = string.Empty,
                ParamTypes = []
            };
            foreach (uint paramType in paramTypes)
            {
                dbDebug.ParamTypes.Add(paramType);
            }

            DebugInfo.Databases.Add(dbDebug.Id, dbDebug);
        }

        return osiDb;
    }

    private Node? EmitName(FunctionNameAndArity name, NameRefType refType)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!_funcs.TryGetValue(name, out var node))
        {
            var signature = _context.LookupSignature(name) ?? throw new InvalidDataException($"Cannot locate required compiler function signature for symbol: {name}");
            switch (signature.Type)
            {
                case FunctionType.SysQuery:
                    node = EmitSysQuery(signature, refType);
                    break;
                case FunctionType.SysCall:
                    EmitSysCall(signature);
                    node = null;
                    break;
                case FunctionType.Event:
                    node = EmitEvent(signature, refType);
                    break;
                case FunctionType.Query:
                    node = EmitQuery(signature, refType);
                    break;
                case FunctionType.Call:
                    node = EmitCall(signature, refType);
                    break;
                case FunctionType.Database:
                    node = EmitDatabase(signature);
                    break;
                case FunctionType.Proc:
                    node = EmitProc(signature);
                    break;
                case FunctionType.UserQuery:
                    node = EmitUserQuery(signature);
                    break;
                default:
                    throw new ArgumentException("Invalid function type mapping context specification value parameter.");
            }

            _funcs.Add(name, node!);
        }

        if (_funcEntries.TryGetValue(name, out var func))
        {
            switch (refType)
            {
                case NameRefType.None:
                    break;
                case NameRefType.Condition:
                    func.ConditionReferences++;
                    if (node is null)
                    {
                        throw new InvalidOperationException("Tried to emit a condition reference after a node was already generated inside compilation pipeline loops.");
                    }
                    break;
                case NameRefType.Action:
                    func.ActionReferences++;
                    break;
            }
        }

        if (node is UserQueryNode)
        {
            var defnName = new FunctionNameAndArity($"{name.Name}__DEF__", name.Arity);
            if (_funcEntries.TryGetValue(defnName, out var defn))
            {
                switch (refType)
                {
                    case NameRefType.Condition:
                        defn.ConditionReferences++;
                        break;
                    case NameRefType.Action:
                        defn.ActionReferences++;
                        break;
                }
            }
        }

        return node;
    }

    /// <summary>
    /// Emits a runtime Call instance structure warning-free from an intermediate representation fact.
    /// </summary>
    private Call EmitCall(IRFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        if (fact.Database is not null && fact.Database.Name is not null)
        {
            EmitName(fact.Database.Name, NameRefType.Action);

            const int InvalidGoalId = 0;

            var osiCall = new Call
            {
                Name = fact.Database.Name.Name ?? string.Empty,
                Parameters = new List<TypedValue>(fact.Elements.Count),
                Negate = fact.Not,
                GoalIdOrDebugHook = InvalidGoalId
            };

            foreach (var param in fact.Elements)
            {
                if (param is not null)
                {
                    var osiParam = EmitTypedValue(param);
                    osiCall.Parameters.Add(osiParam);
                }
            }

            return osiCall;
        }

        int targetGoalId = 0;
        if (fact.Goal is not null && _goals.TryGetValue(fact.Goal, out Goal? cachedGoal) && cachedGoal is not null)
        {
            targetGoalId = (int)cachedGoal.Index;
        }

        return new Call
        {
            Name = string.Empty,
            Parameters = [],
            Negate = false,
            GoalIdOrDebugHook = targetGoalId
        };
    }

    /// <summary>
    /// Emits a runtime Call instance structure warning-free from an intermediate representation rule statement.
    /// </summary>
    private Call EmitCall(IRStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);

        if (statement.Goal is not null)
        {
            int targetGoalId = 0;
            if (_goals.TryGetValue(statement.Goal, out var cachedGoal))
            {
                targetGoalId = (int)(cachedGoal.GetType().GetProperty("Index")?.GetValue(cachedGoal) ?? 0);
            }

            return new Call
            {
                Name = string.Empty,
                Parameters = new List<TypedValue>(statement.Params.Count),
                Negate = false,
                GoalIdOrDebugHook = targetGoalId
            };
        }

        if (statement.Func is not null && statement.Func.Name is not null)
        {
            _ = _context.LookupSignature(statement.Func.Name);
            EmitName(statement.Func.Name, NameRefType.Action);

            var osiCall = new Call
            {
                Name = statement.Func.Name.Name ?? string.Empty,
                Parameters = new List<TypedValue>(statement.Params.Count),
                Negate = statement.Not,
                GoalIdOrDebugHook = 0
            };

            foreach (var param in statement.Params)
            {
                if (param is not null)
                {
                    var osiParam = EmitTypedValue(param);
                    osiCall.Parameters.Add(osiParam);
                }
            }

            return osiCall;
        }

        throw new InvalidOperationException("Malformed compiler statement layout structure encountered.");
    }

    private void AddJoinTarget(Node node, Node target, EntryPoint entryPoint, Goal goal)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(goal);

        var targetRef = new NodeEntryItem
        {
            NodeRef = NodeReference.Create(_story, target),
            EntryPoint = entryPoint,
            GoalRef = GoalReference.Create(_story, goal)
        };

        if (node is TreeNode treeNode)
        {
            Debug.Assert(treeNode.NextNode is null);
            treeNode.NextNode = targetRef;
        }
        else if (node is DataNode dataNode)
        {
            dataNode.ReferencedBy.Add(targetRef);
        }

        if (target is RelNode relNode)
        {
            Debug.Assert(entryPoint == EntryPoint.None);
            relNode.ParentRef = NodeReference.Create(_story, node);
        }
        else if (target is JoinNode joinNode)
        {
            if (entryPoint == EntryPoint.Left)
            {
                joinNode.LeftParentRef = NodeReference.Create(_story, node);
            }
            else
            {
                Debug.Assert(entryPoint == EntryPoint.Right);
                joinNode.RightParentRef = NodeReference.Create(_story, node);
            }
        }
    }

    private Adapter EmitAdapter()
    {
        var adapter = new Adapter
        {
            Index = (uint)_story.Adapters.Count + 1,
            Constants = new Tuple(),
            LogicalIndices = [],
            LogicalToPhysicalMap = []
        };
        _story.Adapters.Add(adapter.Index, adapter);
        return adapter;
    }

    private Adapter EmitIdentityMappingAdapter(IRRule rule, int tupleSize, bool allowPartialPhysicalRow)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var adapter = EmitAdapter();

        if (tupleSize > rule.Variables.Count)
        {
            tupleSize = rule.Variables.Count;
        }

        for (int i = 0; i < tupleSize; i++)
        {
            if (rule.Variables[i].IsUnused())
            {
                if (!allowPartialPhysicalRow)
                {
                    adapter.LogicalIndices.Add(-1);
                }
            }
            else
            {
                adapter.LogicalIndices.Add((sbyte)i);
                adapter.LogicalToPhysicalMap.Add((byte)i, (byte)(adapter.LogicalIndices.Count - 1));
            }
        }

        return adapter;
    }

    private Adapter EmitJoinAdapter(IRFuncCondition condition, IRRule rule)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(rule);

        var adapter = EmitAdapter();

        for (int i = 0; i < condition.Params.Count; i++)
        {
            var param = condition.Params[i];
            if (param is IRConstant constant)
            {
                var osiConst = EmitValue(constant);
                adapter.Constants.Physical.Add(osiConst);
                adapter.Constants.Logical.Add(i, osiConst);
                adapter.LogicalIndices.Add(-1);
            }
            else if (param is IRVariable variable)
            {
                if (variable.Index < rule.Variables.Count && rule.Variables[variable.Index].IsUnused())
                {
                    adapter.LogicalIndices.Add(-1);
                }
                else
                {
                    adapter.LogicalIndices.Add((sbyte)variable.Index);
                    byte keyByte = (byte)variable.Index;
                    if (!adapter.LogicalToPhysicalMap.ContainsKey(keyByte))
                    {
                        adapter.LogicalToPhysicalMap.Add(keyByte, (byte)(adapter.LogicalIndices.Count - 1));
                    }
                }
            }
        }

        var sortedMap = new Dictionary<byte, byte>(adapter.LogicalToPhysicalMap.Count);
        foreach (var mapping in adapter.LogicalToPhysicalMap.OrderBy(v => v.Key))
        {
            sortedMap.Add(mapping.Key, mapping.Value);
        }
        adapter.LogicalToPhysicalMap = sortedMap;

        return adapter;
    }

    private Adapter EmitNodeAdapter(IRRule rule, IRCondition condition, Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node is DataNode || node is QueryNode)
        {
            if (condition is not IRFuncCondition funcCond) throw new InvalidOperationException("Mismatched node function condition layout elements context mappings.");
            return EmitJoinAdapter(funcCond, rule);
        }

        if (node is RelOpNode || node is JoinNode)
        {
            return EmitIdentityMappingAdapter(rule, condition.TupleSize, allowPartialPhysicalRow: true);
        }

        throw new ArgumentException("Unable to emit an adapter for this node type.");
    }

    private JoinNode EmitJoin(Node left, IRCondition leftCondition, IRFuncCondition rightCondition, IRRule rule, Goal goal, ReferencedDatabaseInfo referencedDb)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(rightCondition);
        ArgumentNullException.ThrowIfNull(referencedDb);

        if (referencedDb.DbNodeRef.IsValid)
        {
            referencedDb.Indirection++;
        }
        if (rightCondition.Func?.Name is null)
        {
            throw new InvalidDataException("Right-hand function symbol definition identity reference was missing or null during compilation join network emissions.");
        }
        var right = EmitName(rightCondition.Func.Name, NameRefType.Condition) ?? throw new InvalidDataException("Right-hand function node reference could not be resolved during network node join emissions.");
        JoinNode osiCall = rightCondition.Not ? new NotAndNode() : new AndNode();

        var leftAdapter = EmitNodeAdapter(rule, leftCondition, left);
        var rightAdapter = EmitNodeAdapter(rule, rightCondition, right);

        DatabaseReference database;
        Database? db = null;

        if (left.DatabaseRef.IsValid && right.DatabaseRef.IsValid)
        {
            db = EmitIntermediateDatabase(rule, rightCondition.TupleSize, null!);
            database = db is not null ? DatabaseReference.Create(_story, db) : new DatabaseReference();
        }
        else
        {
            database = new DatabaseReference();
        }

        osiCall.DatabaseRef = database;
        osiCall.Name = string.Empty;
        osiCall.NumParams = 0;
        osiCall.LeftParentRef = new NodeReference();
        osiCall.RightParentRef = new NodeReference();
        osiCall.LeftAdapterRef = AdapterReference.Create(_story, leftAdapter);
        osiCall.RightAdapterRef = AdapterReference.Create(_story, rightAdapter);

        if (db is null)
        {
            osiCall.LeftDatabaseNodeRef = referencedDb.DbNodeRef;
            osiCall.LeftDatabaseIndirection = referencedDb.Indirection;
            osiCall.LeftDatabaseJoin = referencedDb.JoinRef;
        }
        else
        {
            osiCall.LeftDatabaseNodeRef = new NodeReference();
            osiCall.LeftDatabaseIndirection = 0;
            osiCall.LeftDatabaseJoin = new NodeEntryItem
            {
                NodeRef = new NodeReference(),
                EntryPoint = EntryPoint.None,
                GoalRef = new GoalReference()
            };
        }

        var uniqueLogicalIndices = new SortedSet<byte>();
        foreach (byte columnIndex in leftAdapter.LogicalToPhysicalMap.Keys)
        {
            uniqueLogicalIndices.Add(columnIndex);
        }

        foreach (byte columnIndex in rightAdapter.LogicalToPhysicalMap.Keys)
        {
            uniqueLogicalIndices.Add(columnIndex);
        }

        AddNodeWithoutDebugInfo(osiCall);

        if (db is not null)
        {
            referencedDb.DbNodeRef = NodeReference.Create(_story, osiCall);
            referencedDb.Indirection = 0;
            referencedDb.JoinRef = new NodeEntryItem
            {
                NodeRef = NodeReference.Create(_story, osiCall),
                GoalRef = GoalReference.Create(_story, goal),
                EntryPoint = EntryPoint.None
            };
        }
        else if (referencedDb.DbNodeRef.IsValid && left.DatabaseRef.IsValid)
        {
            referencedDb.JoinRef = new NodeEntryItem
            {
                NodeRef = NodeReference.Create(_story, osiCall),
                GoalRef = GoalReference.Create(_story, goal),
                EntryPoint = EntryPoint.Left
            };
            osiCall.LeftDatabaseJoin = referencedDb.JoinRef;
        }

        if (right is DatabaseNode && db is null)
        {
            osiCall.RightDatabaseNodeRef = NodeReference.Create(_story, right);
            osiCall.RightDatabaseIndirection = 1;
            osiCall.RightDatabaseJoin = new NodeEntryItem
            {
                NodeRef = NodeReference.Create(_story, osiCall),
                EntryPoint = EntryPoint.Right,
                GoalRef = GoalReference.Create(_story, goal)
            };
        }
        else
        {
            osiCall.RightDatabaseNodeRef = new NodeReference();
            osiCall.RightDatabaseIndirection = 0;
            osiCall.RightDatabaseJoin = new NodeEntryItem
            {
                NodeRef = new NodeReference(),
                EntryPoint = EntryPoint.None,
                GoalRef = new GoalReference()
            };
        }

        AddJoinTarget(left, osiCall, EntryPoint.Left, goal);
        AddJoinTarget(right, osiCall, EntryPoint.Right, goal);

        AddNodeDebugInfo(osiCall, rightCondition.Location, uniqueLogicalIndices.Count, rule);

        if (osiCall.RightDatabaseIndirection != 0
            && osiCall.LeftDatabaseIndirection != 0
            && osiCall.RightDatabaseIndirection < osiCall.LeftDatabaseIndirection)
        {
            referencedDb.DbNodeRef = osiCall.RightDatabaseNodeRef;
            referencedDb.Indirection = osiCall.RightDatabaseIndirection;
            referencedDb.JoinRef = osiCall.RightDatabaseJoin;
        }

        return osiCall;
    }

    private RelOpNode EmitRelOp(IRRule rule, IRBinaryCondition condition, ReferencedDatabaseInfo referencedDb,
       IRCondition previousCondition, Node previousNode)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(referencedDb);
        ArgumentNullException.ThrowIfNull(previousNode);

        if (referencedDb.DbNodeRef.IsValid)
        {
            referencedDb.Indirection++;
        }

        DatabaseReference database;
        Database? db = null;
        if (previousNode.DatabaseRef.IsValid)
        {
            db = EmitIntermediateDatabase(rule, condition.TupleSize, null!);
            database = db is not null ? DatabaseReference.Create(_story, db) : new DatabaseReference();
        }
        else
        {
            database = new DatabaseReference();
        }

        var adapter = EmitNodeAdapter(rule, previousCondition, previousNode);
        var osiRelOp = new RelOpNode
        {
            DatabaseRef = database,
            Name = string.Empty,
            NumParams = 0,

            ParentRef = null!,
            AdapterRef = AdapterReference.Create(_story, adapter),

            RelOp = (LS.Story.RelOpType)condition.Op,
            LeftValue = null!,
            RightValue = null!
        };

        if (condition.LValue is IRConstant leftConst)
        {
            osiRelOp.LeftValue = EmitValue(leftConst);
            osiRelOp.LeftValueIndex = -1;
        }
        else if (condition.LValue is IRVariable leftVar)
        {
            osiRelOp.LeftValue = new Value
            {
                TypeId = (uint)Value.Type.None,
                StringValue = string.Empty
            };
            osiRelOp.LeftValueIndex = (sbyte)leftVar.Index;
        }

        if (condition.RValue is IRConstant rightConst)
        {
            osiRelOp.RightValue = EmitValue(rightConst);
            osiRelOp.RightValueIndex = -1;
        }
        else if (condition.RValue is IRVariable rightVar)
        {
            osiRelOp.RightValue = new Value
            {
                TypeId = (uint)Value.Type.None,
                StringValue = string.Empty
            };
            osiRelOp.RightValueIndex = (sbyte)rightVar.Index;
        }

        if (db is not null)
        {
            db.OwnerNode = osiRelOp;

            osiRelOp.RelDatabaseNodeRef = new NodeReference();
            osiRelOp.RelJoin = new NodeEntryItem
            {
                NodeRef = new NodeReference(),
                GoalRef = new GoalReference(),
                EntryPoint = EntryPoint.None
            };
            osiRelOp.RelDatabaseIndirection = 0;
        }
        else
        {
            osiRelOp.RelDatabaseNodeRef = referencedDb.DbNodeRef;
            osiRelOp.RelJoin = referencedDb.JoinRef;
            osiRelOp.RelDatabaseIndirection = referencedDb.Indirection;
        }

        AddNodeWithoutDebugInfo(osiRelOp);

        if (db is not null)
        {
            referencedDb.DbNodeRef = NodeReference.Create(_story, osiRelOp);
            referencedDb.Indirection = 0;
            referencedDb.JoinRef = new NodeEntryItem
            {
                NodeRef = new NodeReference(),
                EntryPoint = EntryPoint.None,
                GoalRef = new GoalReference()
            };
        }

        return osiRelOp;
    }

    private static Variable EmitVariable(IRRuleVariable variable)
    {
        ArgumentNullException.ThrowIfNull(variable);

        return new Variable
        {
            TypeId = variable.Type.TypeId,
            IsValid = false,
            OutParam = false,
            IsAType = true,
            Index = (sbyte)variable.Index,
            Unused = variable.IsUnused(),
            Adapted = !variable.IsUnused(),
            VariableName = variable.Name ?? string.Empty
        };
    }

    private RuleNode EmitRuleNode(IRRule rule, Goal goal, ReferencedDatabaseInfo referencedDb, IRCondition lastCondition, Node previousNode)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(referencedDb);
        ArgumentNullException.ThrowIfNull(previousNode);

        if (referencedDb.DbNodeRef.IsValid)
        {
            referencedDb.Indirection++;
        }

        DatabaseReference database;
        Database? db = null;
        if (previousNode.DatabaseRef.IsValid)
        {
            db = EmitIntermediateDatabase(rule, rule.Variables.Count, null!);
            if (db is not null)
            {
                database = DatabaseReference.Create(_story, db);
                referencedDb = new ReferencedDatabaseInfo
                {
                    DbNodeRef = new NodeReference(),
                    Indirection = 0,
                    JoinRef = new NodeEntryItem
                    {
                        NodeRef = new NodeReference(),
                        GoalRef = new GoalReference(),
                        EntryPoint = EntryPoint.None
                    }
                };
            }
            else
            {
                database = new DatabaseReference();
            }
        }
        else
        {
            database = new DatabaseReference();
        }

        Adapter adapter = EmitNodeAdapter(rule, lastCondition, previousNode);
        var osiRule = new RuleNode
        {
            DatabaseRef = database,
            Name = string.Empty,
            NumParams = 0,

            NextNode = new NodeEntryItem
            {
                NodeRef = new NodeReference(),
                EntryPoint = EntryPoint.None,
                GoalRef = new GoalReference()
            },
            ParentRef = null!,
            AdapterRef = AdapterReference.Create(_story, adapter),
            RelDatabaseNodeRef = referencedDb.DbNodeRef,
            RelJoin = referencedDb.JoinRef,
            RelDatabaseIndirection = referencedDb.Indirection,

            Calls = new List<Call>(rule.Actions.Count),
            Variables = new List<Variable>(rule.Variables.Count),
            Line = 0,
            DerivedGoalRef = GoalReference.Create(_story, goal),
            IsQuery = rule.Type == RuleType.Query
        };

        foreach (var variable in rule.Variables)
        {
            if (variable is not null)
            {
                osiRule.Variables.Add(EmitVariable(variable));
            }
        }

        db?.OwnerNode = osiRule;

        AddNodeWithoutDebugInfo(osiRule);

        if (referencedDb.DbNodeRef.IsValid && referencedDb.Indirection == 1)
        {
            osiRule.RelJoin = new NodeEntryItem
            {
                NodeRef = NodeReference.Create(_story, osiRule),
                GoalRef = GoalReference.Create(_story, goal),
                EntryPoint = EntryPoint.None
            };
        }

        return osiRule;
    }

    private void EmitRuleActions(IRRule rule, RuleNode osiRule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(osiRule);

        foreach (var action in rule.Actions)
        {
            if (action is not null)
            {
                osiRule.Calls.Add(EmitCall(action));
            }
        }
    }

    private ProcNode EmitUserQueryDefinition(FunctionSignature signature, Function? queryFunc)
    {
        ArgumentNullException.ThrowIfNull(signature);

        var osiProc = new ProcNode
        {
            DatabaseRef = new DatabaseReference(),
            Name = signature.Name ?? string.Empty,
            NumParams = (byte)signature.Params.Count,
            ReferencedBy = []
        };
        AddNode(osiProc);

        var aliasedSignature = new FunctionSignature
        {
            FullyTyped = signature.FullyTyped,
            Name = $"{signature.Name}__DEF__",
            Params = signature.Params,
            Type = signature.Type,
            Inserted = signature.Inserted,
            Deleted = signature.Deleted,
            Read = signature.Read
        };

        var osiFunc = EmitFunction(LS.Story.FunctionType.UserQuery, aliasedSignature, NodeReference.Create(_story, osiProc));
        if (queryFunc is not null)
        {
            osiFunc.ConditionReferences = queryFunc.ConditionReferences;
            osiFunc.ActionReferences = queryFunc.ActionReferences;
        }
        return osiProc;
    }

    private Node EmitUserQueryInitialFunc(IRFuncCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);

        if (condition.Func?.Name is null)
        {
            throw new InvalidDataException("Initial condition function definition identity reference was missing or null during user query compilation emissions.");
        }

        var signature = _context.LookupSignature(condition.Func.Name) ?? throw new InvalidDataException($"Missing required compilation parameters context signature definition for function: {condition.Func.Name}");
        var name = new FunctionNameAndArity($"{signature.Name}__DEF__", signature.Params.Count);
        if (!_funcs.TryGetValue(name, out var initialFunc))
        {
            _funcEntries.TryGetValue(signature.GetNameAndArity(), out var osiUserQuery);
            initialFunc = EmitUserQueryDefinition(signature, osiUserQuery);
            _funcs.Add(name, initialFunc);
        }

        return initialFunc;
    }

    private sealed class ReferencedDatabaseInfo
    {
        public NodeReference DbNodeRef { get; set; } = new();
        public byte Indirection { get; set; }
        public NodeEntryItem JoinRef { get; set; } = new()
        {
            NodeRef = new NodeReference(),
            EntryPoint = EntryPoint.None,
            GoalRef = new GoalReference()
        };
    }

    private RuleNode EmitRule(IRRule rule, Goal goal)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(goal);

        if (rule.Conditions.Count == 0)
        {
            throw new InvalidDataException("Cannot emit a production rule with zero criteria conditions maps.");
        }

        var referencedDb = new ReferencedDatabaseInfo();

        if (rule.Conditions[0] is not IRFuncCondition initialCall)
        {
            throw new InvalidOperationException("Initial production rule condition constraint must resolve to a valid non-null IRFuncCondition.");
        }

        Node? initialFunc;
        if (rule.Type == RuleType.Query)
        {
            initialFunc = EmitUserQueryInitialFunc(initialCall);
        }
        else
        {
            if (initialCall.Func?.Name is null) throw new InvalidDataException("Missing required initial condition function token identity.");
            initialFunc = EmitName(initialCall.Func.Name, NameRefType.Condition);

            if (initialFunc is DatabaseNode)
            {
                referencedDb.Indirection = 0;
                referencedDb.DbNodeRef = NodeReference.Create(_story, initialFunc);
            }
        }

        if (initialFunc is null)
        {
            throw new InvalidDataException("Initial condition function tracking pointer completely failed to resolve a backend functional node.");
        }

        var lastConditionNode = initialFunc;
        IRCondition lastCondition = initialCall;

        for (int i = 1; i < rule.Conditions.Count; i++)
        {
            var condition = rule.Conditions[i];
            if (condition is null) continue;

            if (condition is IRBinaryCondition binCond)
            {
                var relOp = EmitRelOp(rule, binCond, referencedDb, lastCondition, lastConditionNode);
                AddJoinTarget(lastConditionNode, relOp, EntryPoint.None, goal);

                var adapterProp = relOp.GetType().GetProperty("AdapterRef")?.GetValue(relOp);
                var resolvedMap = adapterProp?.GetType().GetMethod("Resolve")?.Invoke(adapterProp, null);
                int mapCount = (int)(resolvedMap?.GetType().GetProperty("LogicalToPhysicalMap")?.GetValue(resolvedMap)
                                     ?? resolvedMap?.GetType().GetProperty("LogicalToPhysicalMapCount")?.GetValue(resolvedMap) ?? 0);

                AddNodeDebugInfo(relOp, condition.Location, mapCount, rule);
                lastConditionNode = relOp;
            }
            else if (condition is IRFuncCondition funcCond)
            {
                var join = EmitJoin(lastConditionNode, lastCondition, funcCond, rule, goal, referencedDb);
                lastConditionNode = join;
            }
            lastCondition = condition;
        }

        var osiRule = EmitRuleNode(rule, goal, referencedDb, lastCondition, lastConditionNode);
        AddJoinTarget(lastConditionNode, osiRule, EntryPoint.None, goal);
        _rules.Add(rule, osiRule);

        int validVariables = 0;
        for (int i = 0; i < rule.Variables.Count; i++)
        {
            if (!rule.Variables[i].IsUnused()) validVariables++;
        }
        AddNodeDebugInfo(osiRule, rule.Location, validVariables, rule);

        if (DebugInfo is not null)
        {
            var firstCond = rule.Conditions.Count > 0 ? rule.Conditions[0] as IRFuncCondition : null;
            var lastCond = rule.Conditions.Count > 0 ? rule.Conditions[^1] : null;
            var firstAction = rule.Actions.Count > 0 ? rule.Actions[0] : null;

            var ruleDebug = new RuleDebugInfo
            {
                Id = osiRule.Index,
                GoalId = (uint)_story.Goals.Count,
                Name = firstCond?.Func?.Name?.ToString() ?? string.Empty,
                Variables = [],
                Actions = [],

                ConditionsStartLine = (uint)(rule.Location?.StartLine ?? 0),
                ConditionsEndLine = (uint)(lastCond?.Location?.EndLine ?? 0),
                ActionsStartLine = (uint)(firstAction?.Location?.StartLine ?? 0),
                ActionsEndLine = (uint)(rule.Location?.EndLine ?? 0)
            };

            foreach (var variable in rule.Variables)
            {
                if (variable is null) continue;

                var varDebug = new RuleVariableDebugInfo
                {
                    Index = (uint)variable.Index,
                    Name = variable.Name ?? string.Empty,
                    Type = (uint)(variable.Type?.IntrinsicTypeId ?? Value.Type.None),
                    Unused = variable.IsUnused()
                };
                ruleDebug.Variables.Add(varDebug);
            }

            foreach (var action in rule.Actions)
            {
                if (action?.Location is null) continue;

                ruleDebug.Actions.Add(new ActionDebugInfo
                {
                    Line = (uint)action.Location.StartLine
                });
            }

            DebugInfo.Rules.Add(ruleDebug.Id, ruleDebug);
        }

        return osiRule;
    }

    private void EmitGoalActions(IRGoal goal, Goal osiGoal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(osiGoal);

        foreach (var fact in goal.InitSection)
        {
            if (fact is null) continue;
            var call = EmitCall(fact);
            osiGoal.InitCalls.Add(call);
        }

        foreach (var fact in goal.ExitSection)
        {
            if (fact is null) continue;
            var call = EmitCall(fact);
            osiGoal.ExitCalls.Add(call);
        }
    }

    private Goal EmitGoal(IRGoal goal)
    {
        ArgumentNullException.ThrowIfNull(goal);

        var osiGoal = new Goal(_story)
        {
            Index = (uint)(_story.Goals.Count + 1),
            Name = goal.Name ?? string.Empty,
            InitCalls = new List<Call>(goal.InitSection.Count),
            ExitCalls = new List<Call>(goal.ExitSection.Count),
            ParentGoals = [],
            SubGoals = []
        };

        if (goal.ParentTargetEdges.Count > 0)
        {
            osiGoal.GetType().GetProperty("SubGoalCombination")?.SetValue(osiGoal, 1);
            osiGoal.GetType().GetProperty("Flags")?.SetValue(osiGoal, 2);
        }
        else
        {
            osiGoal.GetType().GetProperty("SubGoalCombination")?.SetValue(osiGoal, 0);
            osiGoal.GetType().GetProperty("Flags")?.SetValue(osiGoal, 0);
        }

        if (DebugInfo is not null)
        {
            string rawFile = goal.Location?.FileName ?? string.Empty;
            string canonicalizedPath = !string.IsNullOrEmpty(rawFile) && File.Exists(rawFile)
                ? Path.GetFullPath(rawFile)
                : rawFile;

            var goalDebug = new GoalDebugInfo
            {
                Id = osiGoal.Index,
                Name = goal.Name ?? string.Empty,
                Path = canonicalizedPath,
                InitActions = [],
                ExitActions = []
            };

            foreach (var action in goal.InitSection)
            {
                if (action?.Location is null) continue;
                goalDebug.InitActions.Add(new ActionDebugInfo
                {
                    Line = (uint)action.Location.StartLine
                });
            }

            foreach (var action in goal.ExitSection)
            {
                if (action?.Location is null) continue;
                goalDebug.ExitActions.Add(new ActionDebugInfo
                {
                    Line = (uint)action.Location.StartLine
                });
            }

            DebugInfo.Goals.Add(goalDebug.Id, goalDebug);
        }

        return osiGoal;
    }

    public void EmitGoals()
    {
        foreach (var goal in _context.GoalsByName)
        {
            if (goal.Value is null) continue;

            var osiGoal = EmitGoal(goal.Value);
            osiGoal.Index = (uint)_story.Goals.Count + 1;

            _goals.Add(goal.Value, osiGoal);

            _story.Goals.Add(osiGoal.Index, osiGoal);

            foreach (var rule in goal.Value.KBSection)
            {
                if (rule is null) continue;

                uint firstNodeIndex = (uint)_story.Nodes.Count + 1;
                var osiRule = EmitRule(rule, osiGoal);

                if (DebugInfo is not null)
                {
                    uint lastNodeIndex = (uint)_story.Nodes.Count;
                    for (uint i = firstNodeIndex; i <= lastNodeIndex; i++)
                    {
                        int idx = (int)(i - 1);
                        if (idx < 0) continue;

                        if (_story.Nodes.TryGetValue((uint)idx, out var osiNode))
                        {
                            if (osiNode is TreeNode || osiNode is RelNode || i == lastNodeIndex)
                            {
                                if (DebugInfo.Nodes.TryGetValue(i, out var nodeDebug))
                                {
                                    nodeDebug.RuleId = osiRule.Index;
                                }
                            }
                        }
                    }
                }

                foreach (var goalPair in _goals)
                {
                    EmitGoalActions(goalPair.Key, goalPair.Value);
                }

                foreach (var rulePair in _rules)
                {
                    EmitRuleActions(rulePair.Key, rulePair.Value);
                }
            }
        }
    }

    /// <summary>
    /// Add parent goal/subgoal mapping to the story.
    /// This needs to be done after all goals were generated, as we need the Osiris goal
    /// object ID-s to make goal references.
    /// </summary>
    private void EmitParentGoals()
    {
        foreach (var goalEntry in _context.GoalsByName)
        {
            if (goalEntry.Value is null) continue;

            if (_goals.TryGetValue(goalEntry.Value, out var osiGoal))
            {
                foreach (var parent in goalEntry.Value.ParentTargetEdges)
                {
                    if (parent?.Goal?.Name is null) continue;

                    var parentGoal = _context.LookupGoal(parent.Goal.Name);
                    if (parentGoal is not null && _goals.TryGetValue(parentGoal, out var osiParentGoal))
                    {
                        osiGoal.ParentGoals.Add(GoalReference.Create(_story, osiParentGoal));
                        osiParentGoal.SubGoals.Add(GoalReference.Create(_story, osiGoal));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Generates a function entry for each function in the story header that was not referenced
    /// from the story scripts. The Osiris runtime crashes if some functions from the story
    /// header are not included in the final story file.
    /// </summary>
    private void EmitHeaderFunctions()
    {
        foreach (var signature in _context.Signatures)
        {
            if (signature.Value is null) continue;

            if (signature.Value.Type is not FunctionType.SysCall
                and not FunctionType.SysQuery
                and not FunctionType.Call
                and not FunctionType.Query
                and not FunctionType.Event)
            {
                continue;
            }
            if (!_funcs.TryGetValue(signature.Key, out _))
            {
                EmitName(signature.Value.GetNameAndArity(), NameRefType.None);
            }
        }
    }
    /// <summary>
    /// Orchestrates and emits the fully compiled Story memory graph target structure warning-free.
    /// </summary>

    public Story EmitStory()
    {
        _story = new Story
        {
            MajorVersion = (byte)(OsiVersion.VerLastSupported >> 8),
            MinorVersion = (byte)(OsiVersion.VerLastSupported & 0xff),
            Types = [],
            DivObjects = [],
            Functions = [],
            Nodes = [],
            Adapters = [],
            Databases = [],
            Goals = [],
            GlobalActions = [],
            ExternalStringTable = [],
            FunctionSignatureMap = new Dictionary<string, Function>(StringComparer.Ordinal)
        };
        _story.GetType().GetProperty("Header")?.SetValue(_story, new SaveFileHeader
        {
            Version = "Osiris save file dd. 03/30/17 07:28:20. Version 1.8.",
            BigEndian = false,
            DebugFlags = 0x000C10A0,
            MajorVersion = (byte)(OsiVersion.VerLastSupported >> 8),
            MinorVersion = (byte)(OsiVersion.VerLastSupported & 0xff),
            Unused = 0
        });

        AddStoryTypes();
        EmitGoals();
        EmitHeaderFunctions();
        EmitParentGoals();

        return _story;
    }
}
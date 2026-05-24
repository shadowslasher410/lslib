namespace LSTools.DebuggerFrontend;

using LightProto;
using System;
using System.Collections.Generic;

#region Global Enums

public enum StatusCode : uint
{
    Success = 0,
    UnsupportedBreakpointType = 1,
    InvalidNodeId = 2,
    NotInPause = 3,
    NoDebuggee = 4,
    InvalidContinueAction = 5,
    InPause = 6,
    InvalidGoalId = 7,
    UnsupportedContinueFlags = 8,
    InvalidDatabaseId = 9,
    NotCallable = 10,
    InvalidParameters = 11,
    NoAdapter = 12,
    InvalidEvalType = 13,
    EvalEngineNotReady = 14,
    InvalidParamTupleArity = 15,
    InvalidParamType = 16,
    MissingRequiredParam = 17
}

public enum FrameType : uint
{
    IsValid = 0,
    Pushdown = 1,
    Insert = 2,
    Delete = 3,
    PushdownDelete = 4,
    RuleAction = 5,
    GoalInitAction = 6,
    GoalExitAction = 7
}

[Flags]
public enum GlobalBreakpointType : uint
{
    None = 0,
    StoryLoaded = 1,
    Valid = 2,
    Pushdown = 4,
    Insert = 8,
    RuleAction = 16,
    InitCall = 32,
    ExitCall = 64,
    GameInit = 128,
    GameExit = 256,
    Delete = 512,
    FailedQuery = 1024
}

[Flags]
public enum BreakpointType : uint
{
    None = 0,
    Valid = 1,
    Pushdown = 2,
    Insert = 4,
    RuleAction = 8,
    InitCall = 16,
    ExitCall = 32,
    Delete = 64,
    FailedQuery = 128
}

public enum QueryStatus : uint
{
    NotAQuery = 0,
    Succeeded = 1,
    Failed = 2
}

public enum ContinueAction : uint
{
    Continue = 0,
    StepOver = 1,
    StepInto = 2,
    StepOut = 3,
    Pause = 4
}

[Flags]
public enum ContinueFlags : uint
{
    None = 0,
    SkipRulePushdown = 1,
    SkipDbPropagation = 2
}

public enum GlobalBreakpointReason : uint
{
    StoryLoaded = 0,
    GameInit = 1,
    GameExit = 2
}

public enum EvalType : uint
{
    IsValid = 0,
    Pushdown = 1,
    Insert = 2,
    Delete = 3
}

#endregion

#region Protocol Messages

// 1. Every contract MUST be partial for LightProto's generator to append wire serialization logic
[ProtoContract(SkipConstructor = true)]
public sealed partial class MsgTypedValue
{
    public enum ValueOneofCase
    {
        None = 0,
        Intval = 2,
        Floatval = 3,
        Stringval = 4
    }

    [ProtoMember(1)]
    public uint TypeId { get; set; }

    [ProtoMember(2)]
    public ValueOneofCase ValueCase { get; private set; } = ValueOneofCase.None;

    [ProtoMember(3)]
    public long Intval
    {
        get => ValueCase == ValueOneofCase.Intval ? field : 0L;
        set { ValueCase = ValueOneofCase.Intval; field = value; }
    }

    [ProtoMember(4)]
    public float Floatval
    {
        get => ValueCase == ValueOneofCase.Floatval ? field : 0.0f;
        set { ValueCase = ValueOneofCase.Floatval; field = value; }
    }

    [ProtoMember(5)]
    public string? Stringval
    {
        get => ValueCase == ValueOneofCase.Stringval ? field : string.Empty;
        set { ValueCase = ValueOneofCase.Stringval; field = value ?? string.Empty; }
    }

    public void Clear()
    {
        TypeId = 0;
        ValueCase = ValueOneofCase.None;
        Intval = 0;
        Floatval = 0.0f;
        Stringval = string.Empty;
    }
}

[ProtoContract]
public sealed partial class MsgTuple
{
    [ProtoMember(1)]
    public List<MsgTypedValue> Column { get; } = [];

    public void Clear() => Column.Clear();
}

[ProtoContract]
public sealed partial class MsgFrame
{
    [ProtoMember(1)] public uint NodeId { get; set; }
    [ProtoMember(2)] public FrameType Type { get; set; }
    [ProtoMember(3)] public MsgTuple Tuple { get; } = new();
    [ProtoMember(4)] public uint GoalId { get; set; }
    [ProtoMember(5)] public uint ActionIndex { get; set; }

    public void Clear()
    {
        NodeId = 0;
        Type = FrameType.IsValid;
        Tuple.Clear();
        GoalId = 0;
        ActionIndex = 0;
    }
}

[ProtoContract]
public sealed partial class DbgIdentifyRequest
{
    [ProtoMember(1)] public uint ProtocolVersion { get; set; }

    public void Clear() => ProtocolVersion = 0;
}

[ProtoContract]
public sealed partial class BkVersionInfoResponse
{
    [ProtoMember(1)] public uint ProtocolVersion { get; set; }
    [ProtoMember(2)] public bool StoryLoaded { get; set; }
    [ProtoMember(3)] public bool StoryInitialized { get; set; }

    public void Clear()
    {
        ProtocolVersion = 0;
        StoryLoaded = false;
        StoryInitialized = false;
    }
}

[ProtoContract]
public sealed partial class DbgSetGlobalBreakpoints
{
    [ProtoMember(1)] public uint BreakpointMask { get; set; }

    public void Clear() => BreakpointMask = 0;
}

[ProtoContract]
public sealed partial class MsgBreakpoint
{
    [ProtoMember(1)] public uint NodeId { get; set; }
    [ProtoMember(2)] public uint BreakpointMask { get; set; }
    [ProtoMember(3)] public uint GoalId { get; set; }
    [ProtoMember(4)] public bool IsInitAction { get; set; }
    [ProtoMember(5)] public int ActionIndex { get; set; }

    public void Clear()
    {
        NodeId = 0;
        BreakpointMask = 0;
        GoalId = 0;
        IsInitAction = false;
        ActionIndex = 0;
    }
}

[ProtoContract]
public sealed partial class DbgSetBreakpoints
{
    [ProtoMember(1)] public List<MsgBreakpoint> Breakpoint { get; } = [];

    public void Clear() => Breakpoint.Clear();
}

[ProtoContract]
public sealed partial class BkBreakpointTriggered
{
    [ProtoMember(1)] public List<MsgFrame> CallStack { get; } = [];
    [ProtoMember(2)] public QueryStatus QuerySucceeded { get; set; }
    [ProtoMember(3)] public MsgTuple QueryResults { get; } = new();
    [ProtoMember(4)] public uint QueryNodeId { get; set; }

    public void Clear()
    {
        CallStack.Clear();
        QuerySucceeded = QueryStatus.NotAQuery;
        QueryResults.Clear();
        QueryNodeId = 0;
    }
}

[ProtoContract]
public sealed partial class BkGlobalBreakpointTriggered
{
    [ProtoMember(1)] public GlobalBreakpointReason Reason { get; set; }

    public void Clear() => Reason = GlobalBreakpointReason.StoryLoaded;
}

[ProtoContract]
public sealed partial class DbgContinue
{
    [ProtoMember(1)] public ContinueAction Action { get; set; }
    [ProtoMember(2)] public uint BreakpointMask { get; set; }
    [ProtoMember(3)] public uint Flags { get; set; }

    public void Clear()
    {
        Action = ContinueAction.Continue;
        BreakpointMask = 0;
        Flags = 0;
    }
}

[ProtoContract]
public sealed partial class DbgGetDatabaseContents
{
    [ProtoMember(1)] public uint DatabaseId { get; set; }

    public void Clear() => DatabaseId = 0;
}

[ProtoContract]
public sealed partial class DbgSyncStory
{
    public static void Clear() { }
}

[ProtoContract]
public sealed partial class DbgEvaluate
{
    [ProtoMember(1)] public EvalType Type { get; set; }
    [ProtoMember(2)] public uint NodeId { get; set; }
    [ProtoMember(3)] public MsgTuple Params { get; } = new();

    public void Clear()
    {
        Type = EvalType.IsValid;
        NodeId = 0;
        Params.Clear();
    }
}

[ProtoContract]
public sealed partial class BkResult
{
    [ProtoMember(1)] public StatusCode StatusCode { get; set; }

    public void Clear() => StatusCode = StatusCode.Success;
}

[ProtoContract]
public sealed partial class BkStoryLoaded
{
    public static void Clear() { }
}

[ProtoContract]
public sealed partial class BkDebugSessionEnded
{
    public static void Clear() { }
}

[ProtoContract]
public sealed partial class MsgActionInfo
{
    [ProtoMember(1)] public string Function { get; set; } = string.Empty;
    [ProtoMember(2)] public uint Arity { get; set; }
    [ProtoMember(3)] public int GoalId { get; set; }

    public void Clear()
    {
        Function = string.Empty;
        Arity = 0;
        GoalId = 0;
    }
}

[ProtoContract]
public sealed partial class MsgGoalInfo
{
    [ProtoMember(1)] public uint Id { get; set; }
    [ProtoMember(2)] public string Name { get; set; } = string.Empty;
    [ProtoMember(3)] public List<MsgActionInfo> InitActions { get; } = [];
    [ProtoMember(4)] public List<MsgActionInfo> ExitActions { get; } = [];

    public void Clear()
    {
        Id = 0;
        Name = string.Empty;
        InitActions.Clear();
        ExitActions.Clear();
    }
}

[ProtoContract]
public sealed partial class MsgDatabaseInfo
{
    [ProtoMember(1)] public uint Id { get; set; }
    [ProtoMember(2)] public List<uint> ArgumentType { get; } = [];

    public void Clear()
    {
        Id = 0;
        ArgumentType.Clear();
    }
}

[ProtoContract]
public sealed partial class MsgNodeInfo
{
    [ProtoMember(1)] public uint Id { get; set; }
    [ProtoMember(2)] public uint Type { get; set; }
    [ProtoMember(3)] public string Name { get; set; } = string.Empty;

    public void Clear()
    {
        Id = 0;
        Type = 0;
        Name = string.Empty;
    }
}

[ProtoContract]
public sealed partial class MsgRuleInfo
{
    [ProtoMember(1)] public uint NodeId { get; set; }
    [ProtoMember(2)] public List<MsgActionInfo> Actions { get; } = [];

    public void Clear()
    {
        NodeId = 0;
        Actions.Clear();
    }
}

[ProtoContract]
public sealed partial class BkSyncStoryData
{
    [ProtoMember(1)] public List<MsgGoalInfo> Goal { get; } = [];
    [ProtoMember(2)] public List<MsgDatabaseInfo> Database { get; } = [];
    [ProtoMember(3)] public List<MsgNodeInfo> Node { get; } = [];
    [ProtoMember(4)] public List<MsgRuleInfo> Rule { get; } = [];
    public void Clear()
    {
        Goal.Clear();
        Database.Clear();
        Node.Clear();
        Rule.Clear();
    }
}
[ProtoContract]
public sealed partial class BkSyncStoryFinished
{
    public static void Clear() { }
}
[ProtoContract]
public sealed partial class BkDebugOutput
{
    [ProtoMember(1)] public string Message { get; set; } = string.Empty;

    public void Clear() => Message = string.Empty;
}

[ProtoContract]
public sealed partial class BkBeginDatabaseContents
{
    [ProtoMember(1)] public uint DatabaseId { get; set; }

    public void Clear() => DatabaseId = 0;
}

[ProtoContract]
public sealed partial class BkDatabaseRow
{
    [ProtoMember(1)] public uint DatabaseId { get; set; }
    [ProtoMember(2)] public List<MsgTuple> Row { get; } = [];

    public void Clear()
    {
        DatabaseId = 0;
        Row.Clear();
    }
}

[ProtoContract]
public sealed partial class BkEndDatabaseContents
{
    [ProtoMember(1)] public uint DatabaseId { get; set; }

    public void Clear() => DatabaseId = 0;
}

[ProtoContract]
public sealed partial class BkEvaluateRow
{
    [ProtoMember(1)] public List<MsgTuple> Row { get; } = [];

    public void Clear() => Row.Clear();
}

[ProtoContract]
public sealed partial class BkEvaluateFinished
{
    [ProtoMember(1)] public StatusCode ResultCode { get; set; }
    [ProtoMember(2)] public bool QuerySucceeded { get; set; }

    public void Clear()
    {
        ResultCode = StatusCode.Success;
        QuerySucceeded = false;
    }
}

#endregion

#region Top-Level Envelopes

[ProtoContract(SkipConstructor = true)]
public sealed partial class DebuggerToBackend
{
    public enum MsgOneofCase
    {
        None = 0,
        Identify = 1,
        SetGlobalBreakpoints = 2,
        SetBreakpoints = 3,
        Continue = 4,
        GetDatabaseContents = 5,
        SyncStory = 8,
        Evaluate = 9
    }

    [ProtoMember(1)] public MsgOneofCase MsgCase { get; private set; } = MsgOneofCase.None;
    [ProtoMember(2)] public uint SeqNo { get; set; }
    [ProtoMember(3)] public uint ReplySeqNo { get; set; }

    [ProtoMember(4)]
    public DbgIdentifyRequest Identify => GetOrSetCase(MsgOneofCase.Identify, ref field!);

    [ProtoMember(5)]
    public DbgSetGlobalBreakpoints SetGlobalBreakpoints => GetOrSetCase(MsgOneofCase.SetGlobalBreakpoints, ref field!);

    [ProtoMember(6)]
    public DbgSetBreakpoints SetBreakpoints => GetOrSetCase(MsgOneofCase.SetBreakpoints, ref field!);

    [ProtoMember(7)]
    public DbgContinue Continue => GetOrSetCase(MsgOneofCase.Continue, ref field!);

    [ProtoMember(8)]
    public DbgGetDatabaseContents GetDatabaseContents => GetOrSetCase(MsgOneofCase.GetDatabaseContents, ref field!);

    [ProtoMember(9)]
    public DbgSyncStory SyncStory => GetOrSetCase(MsgOneofCase.SyncStory, ref field!);

    [ProtoMember(10)]
    public DbgEvaluate Evaluate => GetOrSetCase(MsgOneofCase.Evaluate, ref field!);

    private T GetOrSetCase<T>(MsgOneofCase targetCase, ref T backingField) where T : class, new()
    {
        if (MsgCase != targetCase)
        {
            ClearCases();
            MsgCase = targetCase;
            backingField = new T();
        }
        return backingField;
    }

    private void ClearCases()
    {
        Identify?.Clear();
        SetGlobalBreakpoints?.Clear();
        SetBreakpoints?.Clear();
        Continue?.Clear();
        GetDatabaseContents?.Clear();
        Evaluate?.Clear();
    }

    public void Clear()
    {
        MsgCase = MsgOneofCase.None;
        SeqNo = 0;
        ReplySeqNo = 0;
        ClearCases();
    }
}

[ProtoContract(SkipConstructor = true)]
public sealed partial class BackendToDebugger
{
    public enum MsgOneofCase
    {
        None = 0,
        VersionInfo = 1,
        BreakpointTriggered = 2,
        Results = 4,
        StoryLoaded = 5,
        DebugSessionEnded = 6,
        GlobalBreakpointTriggered = 7,
        SyncStoryData = 10,
        SyncStoryFinished = 11,
        DebugOutput = 12,
        BeginDatabaseContents = 13,
        DatabaseRow = 14,
        EndDatabaseContents = 15,
        EvaluateRow = 16,
        EvaluateFinished = 17
    }

    [ProtoMember(1)] public MsgOneofCase MsgCase { get; private set; } = MsgOneofCase.None;
    [ProtoMember(2)] public uint SeqNo { get; set; }
    [ProtoMember(3)] public uint ReplySeqNo { get; set; }
    [ProtoMember(4)] public BkVersionInfoResponse VersionInfo => GetOrSetCase(MsgOneofCase.VersionInfo, ref field!);
    [ProtoMember(5)] public BkBreakpointTriggered BreakpointTriggered => GetOrSetCase(MsgOneofCase.BreakpointTriggered, ref field!);
    [ProtoMember(6)] public BkResult Results => GetOrSetCase(MsgOneofCase.Results, ref field!);
    [ProtoMember(7)] public BkStoryLoaded StoryLoaded => GetOrSetCase(MsgOneofCase.StoryLoaded, ref field!);
    [ProtoMember(8)] public BkDebugSessionEnded DebugSessionEnded => GetOrSetCase(MsgOneofCase.DebugSessionEnded, ref field!);
    [ProtoMember(9)] public BkGlobalBreakpointTriggered GlobalBreakpointTriggered => GetOrSetCase(MsgOneofCase.GlobalBreakpointTriggered, ref field!);
    [ProtoMember(10)] public BkSyncStoryData SyncStoryData => GetOrSetCase(MsgOneofCase.SyncStoryData, ref field!);
    [ProtoMember(11)] public BkSyncStoryFinished SyncStoryFinished => GetOrSetCase(MsgOneofCase.SyncStoryFinished, ref field!);
    [ProtoMember(12)] public BkDebugOutput DebugOutput => GetOrSetCase(MsgOneofCase.DebugOutput, ref field!);
    [ProtoMember(13)] public BkBeginDatabaseContents BeginDatabaseContents => GetOrSetCase(MsgOneofCase.BeginDatabaseContents, ref field!);
    [ProtoMember(14)] public BkDatabaseRow DatabaseRow => GetOrSetCase(MsgOneofCase.DatabaseRow, ref field!);
    [ProtoMember(15)] public BkEndDatabaseContents EndDatabaseContents => GetOrSetCase(MsgOneofCase.EndDatabaseContents, ref field!);
    [ProtoMember(16)] public BkEvaluateRow EvaluateRow => GetOrSetCase(MsgOneofCase.EvaluateRow, ref field!);
    [ProtoMember(17)] public BkEvaluateFinished EvaluateFinished => GetOrSetCase(MsgOneofCase.EvaluateFinished, ref field!);

    private T GetOrSetCase<T>(MsgOneofCase targetCase, ref T backingField) where T : class, new()
    {
        if (MsgCase != targetCase)
        {
            ClearCases();
            MsgCase = targetCase;
            backingField = new T();
        }
        return backingField;
    }

    private void ClearCases()
    {
        VersionInfo?.Clear();
        BreakpointTriggered?.Clear();
        Results?.Clear();
        GlobalBreakpointTriggered?.Clear();
        DebugOutput?.Clear();
        BeginDatabaseContents?.Clear();
        DatabaseRow?.Clear();
        EndDatabaseContents?.Clear();
        EvaluateRow?.Clear();
        EvaluateFinished?.Clear();
    }

    public void Clear()
    {
        MsgCase = MsgOneofCase.None;
        SeqNo = 0;
        ReplySeqNo = 0;
        ClearCases();
    }
}

#endregion
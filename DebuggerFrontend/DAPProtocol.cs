using System.Text.Json.Serialization;

namespace LSTools.DebuggerFrontend;


[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(DAPRequest), typeDiscriminator: "request")]
[JsonDerivedType(typeof(DAPEvent), typeDiscriminator: "event")]
[JsonDerivedType(typeof(DAPResponse), typeDiscriminator: "response")]
public abstract record DAPMessage
{
    public required int Seq { get; init; }
    public required string Type { get; init; }
}

public sealed record DAPRequest : DAPMessage
{
    public required string Command { get; init; }
    public IDAPMessagePayload? Arguments { get; init; }
}

public sealed record DAPEvent : DAPMessage
{
    [JsonPropertyName("event")]
    public required string EventName { get; init; }
    public IDAPMessagePayload? Body { get; init; }
}

public sealed record DAPResponse : DAPMessage
{
    [JsonPropertyName("request_seq")]
    public required int RequestSeq { get; init; }
    public required bool Success { get; init; }
    public required string Command { get; init; }
    public string? Message { get; init; }
    public IDAPMessagePayload? Body { get; init; }
}

[JsonPolymorphic]
[JsonDerivedType(typeof(DAPInitializeRequest))]
[JsonDerivedType(typeof(DAPCapabilities))]
[JsonDerivedType(typeof(DAPLaunchRequest))]
[JsonDerivedType(typeof(DAPLaunchResponse))]
[JsonDerivedType(typeof(DAPInitializedEvent))]
[JsonDerivedType(typeof(DAPSetBreakpointsRequest))]
[JsonDerivedType(typeof(DAPSetBreakpointsResponse))]
[JsonDerivedType(typeof(DAPEmptyPayload))]
[JsonDerivedType(typeof(DAPThreadsResponse))]
[JsonDerivedType(typeof(DAPDisconnectRequest))]
[JsonDerivedType(typeof(DAPStoppedEvent))]
[JsonDerivedType(typeof(DAPStackFramesRequest))]
[JsonDerivedType(typeof(DAPStackFramesResponse))]
[JsonDerivedType(typeof(DAPScopesRequest))]
[JsonDerivedType(typeof(DAPScopesResponse))]
[JsonDerivedType(typeof(DAPVariablesRequest))]
[JsonDerivedType(typeof(DAPVariablesResponse))]
[JsonDerivedType(typeof(DAPContinueRequest))]
[JsonDerivedType(typeof(DAPContinueResponse))]
[JsonDerivedType(typeof(DAPOutputMessage))]
[JsonDerivedType(typeof(DAPTerminatedEvent))]
[JsonDerivedType(typeof(DAPBreakpointEvent))]
[JsonDerivedType(typeof(DAPEvaulateRequest))]
[JsonDerivedType(typeof(DAPEvaluateResponse))]
[JsonDerivedType(typeof(DAPCustomVersionInfoEvent))]
[JsonDerivedType(typeof(DAPCustomQueryResultEvent))]
public interface IDAPMessagePayload { }

public sealed record DAPInitializeRequest : IDAPMessagePayload
{
    public string ClientID { get; init; } = string.Empty;
    public string ClientName { get; init; } = string.Empty;
    public string AdapterID { get; init; } = string.Empty;
    public string Locale { get; init; } = string.Empty;
    public bool LinesStartAt1 { get; init; }
    public bool ColumnsStartAt1 { get; init; }
    public string PathFormat { get; init; } = string.Empty;
    public bool SupportsVariableType { get; init; }
    public bool SupportsVariablePaging { get; init; }
    public bool SupportsRunInTerminalRequest { get; init; }
}

public sealed record DAPCapabilities : IDAPMessagePayload
{
    public bool SupportsConfigurationDoneRequest { get; init; }
    public bool SupportsEvaluateForHovers { get; init; }
}

public sealed record DAPCustomConfiguration(
    bool RawFrames = false,
    bool StopOnAllFrames = false,
    bool StopOnDbPropagation = false,
    bool StopOnFailedQueries = false
);

public sealed record DAPLaunchRequest : IDAPMessagePayload
{
    public bool NoDebug { get; init; }
    public object? Restart { get; init; }
    public string DebugInfoPath { get; init; } = string.Empty;
    public string BackendHost { get; init; } = string.Empty;
    public int BackendPort { get; init; }
    public string ModUuid { get; init; } = string.Empty;
    public DAPCustomConfiguration DbgOptions { get; init; } = new();
}

public sealed record DAPLaunchResponse : IDAPMessagePayload;

public sealed record DAPInitializedEvent : IDAPMessagePayload;

public sealed record DAPSource(string Name = "", string Path = "");

public sealed record DAPSourceBreakpoint
{
    public int Line { get; init; }
    public int? Column { get; init; }
    public string Condition { get; init; } = string.Empty;
    public string HitCondition { get; init; } = string.Empty;
    public string LogMessage { get; init; } = string.Empty;
}

public sealed record DAPSetBreakpointsRequest : IDAPMessagePayload
{
    public required DAPSource Source { get; init; }
    public List<DAPSourceBreakpoint> Breakpoints { get; init; } = [];
    public bool SourceModified { get; init; }
}

public sealed record DAPBreakpoint
{
    public int? Id { get; init; }
    public bool Verified { get; init; }
    public string Message { get; init; } = string.Empty;
    public DAPSource? Source { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
    public int? EndLine { get; init; }
    public int? EndColumn { get; init; }
}

public sealed record DAPSetBreakpointsResponse : IDAPMessagePayload
{
    public List<DAPBreakpoint> Breakpoints { get; init; } = [];
}

public sealed record DAPEmptyPayload : IDAPMessagePayload;

public sealed record DAPThread(int Id, string Name = "");

public sealed record DAPThreadsResponse : IDAPMessagePayload
{
    public List<DAPThread> Threads { get; init; } = [];
}

public sealed record DAPDisconnectRequest : IDAPMessagePayload
{
    public bool? TerminateDebuggee { get; init; }
}

public sealed record DAPStoppedEvent : IDAPMessagePayload
{
    public required string Reason { get; init; }
    public int ThreadId { get; init; }
}

public sealed record DAPStackFrameFormat
{
    public bool? Parameters { get; init; }
    public bool? ParameterTypes { get; init; }
    public bool? ParameterNames { get; init; }
    public bool? ParameterValues { get; init; }
    public bool? Line { get; init; }
    public bool? Module { get; init; }
    public bool? IncludeAll { get; init; }
}

public sealed record DAPStackFramesRequest : IDAPMessagePayload
{
    public int ThreadId { get; init; }
    public int? StartFrame { get; init; }
    public int? Levels { get; init; }
    public DAPStackFrameFormat Format { get; init; } = new();
}

public sealed record DAPStackFrame(int Id, string Name, DAPSource Source, int Line, int Column);

public sealed record DAPStackFramesResponse : IDAPMessagePayload
{
    public List<DAPStackFrame> StackFrames { get; init; } = [];
    public int? TotalFrames { get; init; }
}

public sealed record DAPScopesRequest : IDAPMessagePayload
{
    public int FrameId { get; init; }
}

public sealed record DAPScope
{
    public required string Name { get; init; }
    public long VariablesReference { get; init; }
    public int? NamedVariables { get; init; }
    public int? IndexedVariables { get; init; }
    public bool Expensive { get; init; }
    public DAPSource? Source { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
    public int? EndLine { get; init; }
    public int? EndColumn { get; init; }
}

public sealed record DAPScopesResponse : IDAPMessagePayload
{
    public List<DAPScope> Scopes { get; init; } = [];
}

public sealed record DAPValueFormat(bool Hex = false);

public sealed record DAPVariablesRequest : IDAPMessagePayload
{
    public long VariablesReference { get; init; }
    public string Filter { get; init; } = string.Empty;
    public int? Start { get; init; }
    public int? Count { get; init; }
    public DAPValueFormat Format { get; init; } = new();
}

public sealed record DAPVariablePresentationHint
{
    public string Kind { get; init; } = string.Empty;
    public List<string> Attributes { get; init; } = [];
    public string Visibility { get; init; } = string.Empty;
}

public sealed record DAPVariable
{
    public required string Name { get; init; }
    public required string Value { get; init; }
    public string Type { get; init; } = string.Empty;
    public long VariablesReference { get; init; }
    public int? NamedVariables { get; init; }
    public int? IndexedVariables { get; init; }
}

public sealed record DAPVariablesResponse : IDAPMessagePayload
{
    public List<DAPVariable> Variables { get; init; } = [];
}

public sealed record DAPContinueRequest : IDAPMessagePayload
{
    public int ThreadId { get; init; }
}

public sealed record DAPContinueResponse : IDAPMessagePayload
{
    public bool AllThreadsContinued { get; init; }
}

public sealed record DAPOutputMessage : IDAPMessagePayload
{
    public string Category { get; init; } = string.Empty;
    public string Output { get; init; } = string.Empty;
}

public sealed record DAPTerminatedEvent : IDAPMessagePayload;

public sealed record DAPBreakpointEvent : IDAPMessagePayload
{
    public required string Reason { get; init; }
    public required DAPBreakpoint Breakpoint { get; init; }
}

public sealed record DAPEvaulateRequest : IDAPMessagePayload
{
    public required string Expression { get; init; }
    public int? FrameId { get; init; }
    public string Context { get; init; } = string.Empty;
    public DAPValueFormat Format { get; init; } = new();
}

public sealed record DAPEvaluateResponse : IDAPMessagePayload
{
    public required string Result { get; init; }
    public string Type { get; init; } = string.Empty;
    public DAPVariablePresentationHint PresentationHint { get; init; } = new();
    public long VariablesReference { get; init; }
    public int? NamedVariables { get; init; }
    public int? IndexedVariables { get; init; }
}

public sealed record DAPCustomVersionInfoEvent(int Version) : IDAPMessagePayload;

public sealed record DAPCustomQueryResultEvent(bool Succeeded) : IDAPMessagePayload;


[JsonSerializable(typeof(DAPMessage))]
[JsonSerializable(typeof(DAPRequest))]
[JsonSerializable(typeof(DAPEvent))]
[JsonSerializable(typeof(DAPResponse))]
[JsonSerializable(typeof(IDAPMessagePayload))]
[JsonSerializable(typeof(DAPInitializeRequest))]
[JsonSerializable(typeof(DAPCapabilities))]
[JsonSerializable(typeof(DAPLaunchRequest))]
[JsonSerializable(typeof(DAPLaunchResponse))]
[JsonSerializable(typeof(DAPInitializedEvent))]
[JsonSerializable(typeof(DAPSetBreakpointsRequest))]
[JsonSerializable(typeof(DAPSetBreakpointsResponse))]
[JsonSerializable(typeof(DAPEmptyPayload))]
[JsonSerializable(typeof(DAPThreadsResponse))]
[JsonSerializable(typeof(DAPDisconnectRequest))]
[JsonSerializable(typeof(DAPStoppedEvent))]
[JsonSerializable(typeof(DAPStackFramesRequest))]
[JsonSerializable(typeof(DAPStackFramesResponse))]
[JsonSerializable(typeof(DAPScopesRequest))]
[JsonSerializable(typeof(DAPScopesResponse))]
[JsonSerializable(typeof(DAPVariablesRequest))]
[JsonSerializable(typeof(DAPVariablesResponse))]
[JsonSerializable(typeof(DAPContinueRequest))]
[JsonSerializable(typeof(DAPContinueResponse))]
[JsonSerializable(typeof(DAPOutputMessage))]
[JsonSerializable(typeof(DAPTerminatedEvent))]
[JsonSerializable(typeof(DAPBreakpointEvent))]
[JsonSerializable(typeof(DAPEvaulateRequest))]
[JsonSerializable(typeof(DAPEvaluateResponse))]
[JsonSerializable(typeof(DAPCustomVersionInfoEvent))]
[JsonSerializable(typeof(DAPCustomQueryResultEvent))]
internal partial class DAPJsonContext : JsonSerializerContext;
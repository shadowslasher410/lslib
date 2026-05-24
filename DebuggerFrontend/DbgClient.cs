using LSLib.LS.Story.Compiler;
using System.Text;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Buffers;
using LightProto;

namespace LSTools.DebuggerFrontend;

public class AsyncProtoClient(Socket socket)
{
    private readonly Socket _socket = socket ?? throw new ArgumentNullException(nameof(socket));
    private readonly Pipe _pipe = new();

    public Action<BackendToDebugger>? MessageReceived { get; set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var fillTask = FillPipeAsync(_socket, _pipe.Writer, cancellationToken);
        var readTask = ReadPipeAsync(_pipe.Reader, cancellationToken);

        await Task.WhenAll(fillTask, readTask);
    }

    public void Send(DebuggerToBackend message)
    {
        ArgumentNullException.ThrowIfNull(message);

        byte[] payload = message.ToByteArray();
        Span<byte> lengthHeader = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(lengthHeader, payload.Length);

        _socket.Send(lengthHeader, SocketFlags.None);
        _socket.Send(payload, SocketFlags.None);
    }


    private static async Task FillPipeAsync(Socket socket, PipeWriter writer, CancellationToken cancellationToken)
    {
        const int minimumBufferSize = 512;

        while (!cancellationToken.IsCancellationRequested)
        {
            Memory<byte> memory = writer.GetMemory(minimumBufferSize);
            try
            {
                int bytesRead = await socket.ReceiveAsync(memory, SocketFlags.None, cancellationToken);
                if (bytesRead == 0) break; 

                writer.Advance(bytesRead);
            }
            catch
            {
                break;
            }

            FlushResult result = await writer.FlushAsync(cancellationToken);
            if (result.IsCompleted) break;
        }

        await writer.CompleteAsync();
    }

    private async Task ReadPipeAsync(PipeReader reader, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ReadResult result = await reader.ReadAsync(cancellationToken);
            ReadOnlySequence<byte> buffer = result.Buffer;

            while (TryReadMessage(ref buffer, out var message))
            {
                MessageReceived?.Invoke(message);
            }

            reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted) break;
        }

        await reader.CompleteAsync();
    }

    private static bool TryReadMessage(ref ReadOnlySequence<byte> buffer, out BackendToDebugger message)
    {
        message = null!;
        if (buffer.Length < 4) return false;

        Span<byte> lengthBytes = stackalloc byte[4];
        buffer.Slice(0, 4).CopyTo(lengthBytes);
        int messageLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(lengthBytes);

        if (buffer.Length < 4 + messageLength) return false;

        ReadOnlySequence<byte> payloadSequence = buffer.Slice(4, messageLength);
        byte[] localArray = payloadSequence.ToArray();

        message = Serializer.Deserialize<BackendToDebugger>(localArray);

        buffer = buffer.Slice(4 + messageLength);
        return true;
    }
}


public class DebuggerClient(AsyncProtoClient client, StoryDebugInfo debugInfo)
{
    private readonly AsyncProtoClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly StoryDebugInfo _debugInfo = debugInfo ?? throw new ArgumentNullException(nameof(debugInfo));
    private Stream? _logStream;

    private uint OutoutgoingSeq { get => field++; set; } = 1;
    private int IncomingSeq { get => field++; set; } = 1;

    public Action<BkVersionInfoResponse> OnBackendInfo { get; set; } = delegate { };
    public Action OnStoryLoaded { get; set; } = delegate { };
    public Action OnDebugSessionEnded { get; set; } = delegate { };
    public Action<BkBreakpointTriggered> OnBreakpointTriggered { get; set; } = delegate { };
    public Action<BkGlobalBreakpointTriggered> OnGlobalBreakpointTriggered { get; set; } = delegate { };
    public Action<BkSyncStoryData> OnStorySyncData { get; set; } = delegate { };
    public Action OnStorySyncFinished { get; set; } = delegate { };
    public Action<BkDebugOutput> OnDebugOutput { get; set; } = delegate { };
    public Action<BkBeginDatabaseContents> OnBeginDatabaseContents { get; set; } = delegate { };
    public Action<BkDatabaseRow> OnDatabaseRow { get; set; } = delegate { };
    public Action<BkEndDatabaseContents> OnEndDatabaseContents { get; set; } = delegate { };
    public Action<uint, BkEvaluateRow> OnEvaluateRow { get; set; } = delegate { };
    public Action<uint, BkEvaluateFinished> OnEvaluateFinished { get; set; } = delegate { };

    public void Initialize()
    {
        _client.MessageReceived = MessageReceived;
    }

    public void EnableLogging(Stream logStream) => _logStream = logStream;

    private void LogMessage(object message)
    {
        if (_logStream is not null)
        {
            using var writer = new StreamWriter(_logStream, Encoding.UTF8, 0x1000, leaveOpen: true);
            writer.WriteLine($" DBG >>> [{DateTime.UtcNow:O}] Dispatched command payload frame: {message.GetType().Name}");
        }
    }

    public uint Send(DebuggerToBackend message)
    {
        ArgumentNullException.ThrowIfNull(message);

        message.SeqNo = OutoutgoingSeq;
        LogMessage(message);
        _client.Send(message);
        return message.SeqNo;
    }

    public void SendIdentify(uint protocolVersion)
    {
        var msg = new DebuggerToBackend();
        msg.Identify.ProtocolVersion = protocolVersion;
        Send(msg);
    }

    private static MsgBreakpoint BreakpointToMsg(Breakpoint breakpoint)
    {
        var msgBp = new MsgBreakpoint();

        if (breakpoint.LineInfo?.Node is not null)
        {
            msgBp.NodeId = breakpoint.LineInfo.Node.Id;
        }
        else if (breakpoint.LineInfo is not null)
        {
            msgBp.GoalId = breakpoint.LineInfo.Goal.Id;
        }

        if (breakpoint.LineInfo is not null)
        {
            msgBp.IsInitAction = breakpoint.LineInfo.Type == LineType.GoalInitActionLine;

            msgBp.ActionIndex = breakpoint.LineInfo.Type is LineType.GoalInitActionLine
                or LineType.GoalExitActionLine
                or LineType.RuleActionLine
                ? (int)breakpoint.LineInfo.ActionIndex
                : -1;
        }

        msgBp.BreakpointMask = 0x3f;
        return msgBp;
    }

    public void SendSetBreakpoints(List<Breakpoint> breakpoints)
    {
        ArgumentNullException.ThrowIfNull(breakpoints);

        var msg = new DebuggerToBackend();
        var setBps = msg.SetBreakpoints;

        foreach (var breakpoint in breakpoints)
        {
            if (breakpoint.Verified)
            {
                setBps.Breakpoint.Add(BreakpointToMsg(breakpoint));
            }
        }

        Send(msg);
    }

    public void SendGetDatabaseContents(uint databaseId)
    {
        var msg = new DebuggerToBackend();
        msg.GetDatabaseContents.DatabaseId = databaseId;
        Send(msg);
    }

    public void SendSetGlobalBreakpoints(uint breakpointMask)
    {
        var msg = new DebuggerToBackend();
        msg.SetGlobalBreakpoints.BreakpointMask = breakpointMask;
        Send(msg);
    }

    public void SendContinue(ContinueAction action, uint breakpointMask, uint flags)
    {
        var msg = new DebuggerToBackend();
        var req = msg.Continue;
        req.Action = action;
        req.BreakpointMask = breakpointMask;
        req.Flags = flags;
        Send(msg);
    }

    public void SendSyncStory()
    {
        var msg = new DebuggerToBackend();
        _ = msg.SyncStory; 
        Send(msg);
    }

    public uint SendEvaluate(EvalType type, uint nodeId, MsgTuple args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var msg = new DebuggerToBackend();
        var req = msg.Evaluate;
        req.Type = type;
        req.NodeId = nodeId;

        req.Params.Clear();

        foreach (var column in args.Column)
        {
            req.Params.Column.Add(column);
        }

        return Send(msg);
    }

    private void BreakpointTriggered(BkBreakpointTriggered message)
    {
        ArgumentNullException.ThrowIfNull(message);
        OnBreakpointTriggered(message);
    }

    private void GlobalBreakpointTriggered(BkGlobalBreakpointTriggered message)
    {
        ArgumentNullException.ThrowIfNull(message);
        OnGlobalBreakpointTriggered(message);
    }

    private void OnResultsReceived(BkResult results, uint replySeqNo)
    {
        OnEvaluateFinished(replySeqNo, new BkEvaluateFinished
        {
            ResultCode = results.StatusCode,
            QuerySucceeded = false
        });
    }

    private void MessageReceived(BackendToDebugger message)
    {
        ArgumentNullException.ThrowIfNull(message);
        LogMessage(message);

        if ((int)message.SeqNo != IncomingSeq)
        {
            throw new InvalidDataException($"DBG sequence number mismatch; got {message.SeqNo} expected {IncomingSeq}");
        }

        _ = IncomingSeq;

        switch (message.MsgCase)
        {
            case BackendToDebugger.MsgOneofCase.VersionInfo:
                OnBackendInfo(message.VersionInfo);
                break;
            case BackendToDebugger.MsgOneofCase.BreakpointTriggered:
                BreakpointTriggered(message.BreakpointTriggered);
                break;
            case BackendToDebugger.MsgOneofCase.Results:
                OnResultsReceived(message.Results, message.ReplySeqNo);
                break;
            case BackendToDebugger.MsgOneofCase.StoryLoaded:
                OnStoryLoaded();
                break;
            case BackendToDebugger.MsgOneofCase.DebugSessionEnded:
                OnDebugSessionEnded();
                break;
            case BackendToDebugger.MsgOneofCase.GlobalBreakpointTriggered:
                GlobalBreakpointTriggered(message.GlobalBreakpointTriggered);
                break;
            case BackendToDebugger.MsgOneofCase.SyncStoryData:
                OnStorySyncData(message.SyncStoryData);
                break;
            case BackendToDebugger.MsgOneofCase.SyncStoryFinished:
                OnStorySyncFinished();
                break;
            case BackendToDebugger.MsgOneofCase.DebugOutput:
                OnDebugOutput(message.DebugOutput);
                break;
            case BackendToDebugger.MsgOneofCase.BeginDatabaseContents:
                OnBeginDatabaseContents(message.BeginDatabaseContents);
                break;
            case BackendToDebugger.MsgOneofCase.DatabaseRow:
                OnDatabaseRow(message.DatabaseRow);
                break;
            case BackendToDebugger.MsgOneofCase.EndDatabaseContents:
                OnEndDatabaseContents(message.EndDatabaseContents);
                break;
            case BackendToDebugger.MsgOneofCase.EvaluateRow:
                OnEvaluateRow(message.ReplySeqNo, message.EvaluateRow);
                break;
            case BackendToDebugger.MsgOneofCase.EvaluateFinished:
                OnEvaluateFinished(message.ReplySeqNo, message.EvaluateFinished);
                break;
            default:
                throw new InvalidOperationException($"Unknown message from DBG: {message.MsgCase}");
        }
    }
}
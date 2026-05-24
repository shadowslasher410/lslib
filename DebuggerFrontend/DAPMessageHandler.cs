using LSLib.LS.Story;
using LSLib.LS.Story.Compiler;
using System.Net.Sockets;
using System.Text;

namespace LSTools.DebuggerFrontend;

public partial class DAPMessageHandler
{
    private const uint DBGProtocolVersion = 8;
    private const int DAPProtocolVersion = 1;

    private DAPStream Stream { get; init; }
    private Stream? LogStream { get; set; }

    private StoryDebugInfo? _debugInfo;
    private string _debugInfoPath = string.Empty;
    private DebugInfoSync? _debugInfoSync;
    private Task? _dbgNetworkTask;
    private AsyncProtoClient? _dbgClient;
    private DebuggerClient? _dbgCli;
    private ValueFormatter? _formatter;
    private StackTracePrinter? _tracePrinter;
    private BreakpointManager _breakpoints;
    private EvaluationResultManager? _evalResults;
    private ExpressionEvaluator? _evaluator;
    private List<CoalescedFrame>? _stack;
    private DAPCustomConfiguration? _config;
    private bool _stopped;
    private bool _continueAfterSync;
    private bool _pauseRequested;
    private bool _debuggingStory;
    private FunctionDebugInfo? _lastQueryFunc;
    private List<DebugVariable>? _lastQueryResults;

    public required string ModUuid { get; set; } = string.Empty;

    public DAPMessageHandler(DAPStream stream)
    {
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        Stream.MessageReceived = MessageReceived; 
        _breakpoints = new BreakpointManager(null!);
    }

    public void EnableLogging(Stream logStream) => LogStream = logStream;

    private void SendBreakpoint(string eventType, Breakpoint bp)
    {
        Stream.SendEvent("breakpoint", new DAPBreakpointEvent
        {
            Reason = eventType,
            Breakpoint = bp.ToDAP()
        });
    }


    public void SendOutput(string category, string output)
    {
        Stream.SendEvent("output", new DAPOutputMessage
        {
            Category = category,
            Output = output
        });
    }

    private void LogError(string message)
    {
        SendOutput("stderr", $"{message}\r\n");

        if (LogStream is not null)
        {
            using var writer = new StreamWriter(LogStream, Encoding.UTF8, 0x1000, leaveOpen: true);
            writer.WriteLine(message);
            Console.WriteLine(message);
        }
    }

    private void MessageReceived(DAPMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        switch (message)
        {
            case DAPRequest request:
                try
                {
                    HandleRequest(request);
                }
                catch (RequestFailedException e)
                {
                    Stream.SendReply(request, e.Message);
                }
                catch (Exception e)
                {
                    LogError(e.ToString());
                    Stream.SendReply(request, e.ToString());
                }
                break;

            case DAPEvent dapEvent:
                HandleEvent(dapEvent);
                break;

            default:
                throw new InvalidDataException("DAP replies or unhandled abstract message schemas are not natively supported.");
        }
    }

    private void InitDebugger()
    {
        if (string.IsNullOrEmpty(_debugInfoPath) || !File.Exists(_debugInfoPath))
        {
            throw new FileNotFoundException("The specified story symbol debug layout path target is invalid or missing.", _debugInfoPath);
        }

        byte[] debugPayload = File.ReadAllBytes(_debugInfoPath);
        _ = new DebugInfoLoader();

        _debugInfo = DebugInfoLoader.Load(debugPayload);
        if (_debugInfo.Version != StoryDebugInfo.CurrentVersion)
        {
            throw new InvalidDataException($"Story debug info version too old (found {_debugInfo.Version}, required {StoryDebugInfo.CurrentVersion}). Please recompile the story project file.");
        }

        _formatter = new ValueFormatter(_debugInfo);
        _tracePrinter = new StackTracePrinter(_debugInfo, _formatter)
        {
            ModUuid = ModUuid,
            MergeFrames = _config is null || !_config.RawFrames
        };

        _evalResults = new EvaluationResultManager();
        _evaluator = new ExpressionEvaluator(_debugInfo, Stream, _dbgCli!, _evalResults);

        _stack = null;
        _stopped = false;
        _debuggingStory = false;
    }


    private void StartDebugSession()
    {
        if (_debugInfo is null) return;
        _debuggingStory = true;

        var changedBps = _breakpoints.DebugInfoLoaded(_debugInfo);

        foreach (var bp in changedBps)
        {
            SendBreakpoint("changed", bp);
        }

        SendOutput("console", "Debug session started\r\n");
    }

    private void OnDebugSessionEnded()
    {
        if (_debuggingStory)
        {
            SendOutput("console", "Story unloaded - debug session terminated\r\n");
        }

        _debuggingStory = false;
        _stopped = false;
        _debugInfo = null;
        _evaluator = null;
        _evalResults = null;
        _tracePrinter = null;
        _formatter = null;

        var changedBps = _breakpoints.DebugInfoUnloaded();
        foreach (var bp in changedBps)
        {
            SendBreakpoint("changed", bp);
        }
    }

    private void SynchronizeStoryWithBackend(bool continueAfterSync)
    {
        if (_debugInfo is null || _dbgCli is null) return;

        _debugInfoSync = new DebugInfoSync(_debugInfo);
        _continueAfterSync = continueAfterSync;
        _dbgCli.SendSyncStory();
    }

    private void OnBackendInfo(BkVersionInfoResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.ProtocolVersion != DBGProtocolVersion)
        {
            throw new InvalidDataException($"Backend sent unsupported protocol version; got {response.ProtocolVersion}, expected {DBGProtocolVersion}");
        }

        if (response.StoryLoaded)
        {
            InitDebugger();
        }

        if (response.StoryInitialized)
        {
            SynchronizeStoryWithBackend(false);
        }
    }

    private void OnStoryLoaded() => InitDebugger();

    private void OnBreakpointTriggered(BkBreakpointTriggered bp)
    {
        ArgumentNullException.ThrowIfNull(bp);
        if (_tracePrinter is null) return;

        _stack = _tracePrinter.BreakpointToStack(bp);
        _stopped = true;
        _pauseRequested = false;

        Stream.SendEvent("stopped", new DAPStoppedEvent
        {
            Reason = "breakpoint",
            ThreadId = 1
        });

        _lastQueryFunc = null;
        _lastQueryResults = null;

        if (bp.QueryResults is not null && _debugInfo is not null)
        {
            var node = _debugInfo.Nodes[bp.QueryNodeId];

            if (node.FunctionName is { } funcName)
            {
                var function = _debugInfo.Functions[funcName];
                _lastQueryFunc = function;

                _lastQueryResults = new List<DebugVariable>(bp.QueryResults.Column.Count);
                for (var i = 0; i < bp.QueryResults.Column.Count; i++)
                {
                    if (function.Params[i].Out)
                    {
                        var col = bp.QueryResults.Column[i];

                        string typeNameToken = (Value.Type)function.Params[i].TypeId switch
                        {
                            Value.Type.None => "None",
                            Value.Type.Integer => "Integer",
                            Value.Type.Integer64 => "Integer64",
                            Value.Type.Float => "Float",
                            Value.Type.String => "String",
                            Value.Type.GuidString => "GuidString",
                            _ => $"UnknownType({function.Params[i].TypeId})"
                        };

                        _lastQueryResults.Add(new DebugVariable
                        {
                            Name = $"@{function.Params[i].Name}",
                            Type = typeNameToken,
                            Value = ValueFormatter.ValueToString(col),
                            TypedValue = col
                        });
                    }
                }
            }
        }

        if (bp.QuerySucceeded != QueryStatus.NotAQuery)
        {
            Stream.SendEvent("osirisQueryResult", new DAPCustomQueryResultEvent(bp.QuerySucceeded == QueryStatus.Succeeded));
        }
    }

    private void OnGlobalBreakpointTriggered(BkGlobalBreakpointTriggered message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Reason == GlobalBreakpointReason.StoryLoaded)
        {
            uint failedQueryMask = (uint)GlobalBreakpointType.FailedQuery; 

            _dbgCli?.SendSetGlobalBreakpoints(failedQueryMask);
            SendContinue(ContinueAction.StepInto);
        }
        else if (message.Reason == GlobalBreakpointReason.GameInit)
        {
            SynchronizeStoryWithBackend(true);
        }
        else
        {
            throw new InvalidOperationException($"Global breakpoint type not supported: {message.Reason}");
        }
    }


    private void OnStorySyncData(BkSyncStoryData data)
    {
        _debugInfoSync?.AddData(data);
    }

    private void OnStorySyncFinished()
    {
        if (_debugInfoSync is null) return;
        _debugInfoSync.Finish();

        if (_debugInfoSync.Matches)
        {
            StartDebugSession();
        }
        else
        {
            OnDebugSessionEnded();
            SendOutput("stderr", "Could not start debugging session - debug info does not match loaded story.\r\n");

            var reasons = $"   {string.Join("\r\n   ", _debugInfoSync.Reasons)}";
            SendOutput("console", $"Mismatches:\r\n{reasons}\r\n");
        }

        _debugInfoSync = null;

        if (_continueAfterSync)
        {
            if (_pauseRequested && _debuggingStory)
            {
                SendContinue(ContinueAction.StepInto);
            }
            else
            {
                SendContinue(ContinueAction.Continue);
            }
        }
    }

    private void OnDebugOutput(BkDebugOutput msg)
    {
        ArgumentNullException.ThrowIfNull(msg);
        SendOutput("stdout", $"DebugBreak: {msg.Message}\r\n");
    }

    private void HandleInitializeRequest(DAPRequest request)
    {
        Stream.SendReply(request, new DAPCapabilities
        {
            SupportsConfigurationDoneRequest = true,
            SupportsEvaluateForHovers = true
        });

        Stream.SendEvent("osirisProtocolVersion", new DAPCustomVersionInfoEvent(DAPProtocolVersion));
    }


    private async Task StartNetworkProcessingLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_dbgClient is not null)
            {
                await _dbgClient.StartAsync(cancellationToken);
            }
        }
        catch (Exception e)
        {
            LogError(e.ToString());
            Environment.Exit(2);
        }
    }

    private void HandleLaunchRequest(DAPRequest request, DAPLaunchRequest launch)
    {
        ArgumentNullException.ThrowIfNull(launch);

        _config = launch.DbgOptions;
        ModUuid = launch.ModUuid;

        if (!File.Exists(launch.DebugInfoPath))
        {
            throw new RequestFailedException($"Story debug file does not exist: {launch.DebugInfoPath}");
        }

        _debugInfoPath = launch.DebugInfoPath;

        try
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(launch.BackendHost, launch.BackendPort);

            _dbgClient = new AsyncProtoClient(socket);
        }
        catch (SocketException e)
        {
            throw new RequestFailedException($"Could not connect to Osiris backend server: {e.Message}");
        }

        _dbgCli = new DebuggerClient(_dbgClient, _debugInfo!)
        {
            OnStoryLoaded = OnStoryLoaded,
            OnDebugSessionEnded = OnDebugSessionEnded,
            OnBackendInfo = OnBackendInfo,
            OnBreakpointTriggered = OnBreakpointTriggered,
            OnGlobalBreakpointTriggered = OnGlobalBreakpointTriggered,
            OnStorySyncData = OnStorySyncData,
            OnStorySyncFinished = OnStorySyncFinished,
            OnDebugOutput = OnDebugOutput
        };
        _dbgCli.Initialize();

        if (LogStream is not null)
        {
            _dbgCli.EnableLogging(LogStream);
        }

        _dbgCli.SendIdentify(DBGProtocolVersion);

        var cts = new CancellationTokenSource();
        _dbgNetworkTask = Task.Run(() => StartNetworkProcessingLoopAsync(cts.Token), cts.Token);

        _breakpoints = new BreakpointManager(_dbgCli);

        Stream.SendReply(request, new DAPLaunchResponse());
        Stream.SendEvent("initialized", new DAPInitializedEvent());
    }

    private void HandleSetBreakpointsRequest(DAPRequest request, DAPSetBreakpointsRequest breakpoints)
    {
        ArgumentNullException.ThrowIfNull(breakpoints);

        if (_breakpoints is not null)
        {
            var goalName = Path.GetFileNameWithoutExtension(breakpoints.Source.Name) ?? string.Empty;
            _breakpoints.ClearGoalBreakpoints(goalName);

            var reply = new DAPSetBreakpointsResponse
            {
                Breakpoints = []
            };

            foreach (var breakpoint in breakpoints.Breakpoints)
            {
                var bp = _breakpoints.AddBreakpoint(breakpoints.Source, breakpoint);
                reply.Breakpoints.Add(bp.ToDAP());
            }

            _breakpoints.UpdateBreakpointsOnBackend();
            Stream.SendReply(request, reply);
        }
        else
        {
            throw new RequestFailedException("Cannot add breakpoint - breakpoint manager not yet initialized");
        }
    }

    private void HandleConfigurationDoneRequest(DAPRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Stream.SendReply(request, new DAPEmptyPayload());
    }

    private void HandleThreadsRequest(DAPRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reply = new DAPThreadsResponse
        {
            Threads = [
                new DAPThread(1, "OsirisThread")
            ]
        };
        Stream.SendReply(request, reply);
    }

    private void HandleStackTraceRequest(DAPRequest request, DAPStackFramesRequest msg)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(msg);

        if (!_stopped || _stack is null)
        {
            throw new RequestFailedException("Cannot inspect stack frames when the story is running.");
        }

        if (msg.ThreadId != 1)
        {
            throw new RequestFailedException("Requested a stack trace for an unknown thread identifier.");
        }

        int startFrame = msg.StartFrame ?? 0;
        int levels = (msg.Levels is null or 0) ? _stack.Count : msg.Levels.Value;
        int lastFrame = Math.Min(startFrame + levels, _stack.Count);

        List<DAPStackFrame> frames = new(Math.Max(0, lastFrame - startFrame));
        for (var i = startFrame; i < lastFrame; i++)
        {
            var frame = _stack[i];

            var dapFrame = new DAPStackFrame(i, frame.Name, frame.File is not null ? new DAPSource
            {
                Name = Path.GetFileNameWithoutExtension(frame.File) ?? string.Empty,
                Path = frame.File
            } : null!, (int)frame.Line, 1);

            frames.Add(dapFrame);
        }

        Stream.SendReply(request, new DAPStackFramesResponse
        {
            StackFrames = frames,
            TotalFrames = _stack.Count
        });
    }


    private void HandleScopesRequest(DAPRequest request, DAPScopesRequest msg)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(msg);

        if (!_stopped || _stack is null)
        {
            throw new RequestFailedException("Cannot get scopes when story execution is active.");
        }

        if (msg.FrameId < 0 || msg.FrameId >= _stack.Count)
        {
            throw new RequestFailedException("Requested scopes for an unknown frame identifier.");
        }

        var frame = _stack[msg.FrameId];

        var stackScope = new DAPScope
        {
            Name = "Locals",
            VariablesReference = msg.FrameId + 1,
            NamedVariables = frame.Variables.Count,
            IndexedVariables = 0,
            Expensive = false,
            Source = frame.Rule is not null && frame.File is not null ? new DAPSource
            {
                Name = Path.GetFileNameWithoutExtension(frame.File) ?? string.Empty,
                Path = frame.File
            } : null!,
            Line = frame.Rule is not null ? (int)frame.Rule.ConditionsStartLine : 0,
            Column = frame.Rule is not null ? 1 : 0,
            EndLine = frame.Rule is not null ? (int)frame.Rule.ActionsEndLine + 1 : 0,
            EndColumn = frame.Rule is not null ? 1 : 0
        };

        List<DAPScope> scopes = [stackScope];

        if (msg.FrameId == 0 && _lastQueryResults is { Count: > 0 } && _lastQueryFunc is not null)
        {
            var queryScope = new DAPScope
            {
                Name = $"{_lastQueryFunc.Name} Returns",
                VariablesReference = (long)3 << 48,
                NamedVariables = _lastQueryResults.Count,
                IndexedVariables = 0,
                Expensive = false,

                Source = stackScope.Source,
                Line = stackScope.Line,
                Column = stackScope.Column,
                EndLine = stackScope.EndLine,
                EndColumn = stackScope.EndColumn
            };

            scopes.Add(queryScope);
        }

        Stream.SendReply(request, new DAPScopesResponse
        {
            Scopes = scopes
        });
    }

    private List<DAPVariable> GetStackVariables(DAPVariablesRequest msg, int frameIndex)
    {
        if (_stack is null || frameIndex < 0 || frameIndex >= _stack.Count)
        {
            throw new RequestFailedException($"Requested variables for unknown frame index: {frameIndex}");
        }

        var frame = _stack[frameIndex];
        int startIndex = msg.Start ?? 0;
        int numVars = (msg.Count is null or 0) ? frame.Variables.Count : msg.Count.Value;
        int lastIndex = Math.Min(startIndex + numVars, frame.Variables.Count);

        List<DAPVariable> variables = new(Math.Max(0, lastIndex - startIndex));
        for (var i = startIndex; i < lastIndex; i++)
        {
            var variable = frame.Variables[i];
            variables.Add(new DAPVariable
            {
                Name = variable.Name,
                Value = variable.Value,
                Type = variable.Type
            });
        }

        return variables;
    }

    private List<DAPVariable> GetQueryResultVariables(DAPVariablesRequest msg, int frameIndex)
    {
        if (frameIndex != 0 || _lastQueryResults is null)
        {
            throw new RequestFailedException($"Requested query results for an invalid frame index context: {frameIndex}");
        }

        int startIndex = msg.Start ?? 0;
        int numVars = (msg.Count is null or 0) ? _lastQueryResults.Count : msg.Count.Value;
        int lastIndex = Math.Min(startIndex + numVars, _lastQueryResults.Count);

        List<DAPVariable> variables = new(Math.Max(0, lastIndex - startIndex));
        for (var i = startIndex; i < lastIndex; i++)
        {
            var variable = _lastQueryResults[i];
            variables.Add(new DAPVariable
            {
                Name = variable.Name,
                Value = variable.Value,
                Type = variable.Type
            });
        }

        return variables;
    }

    private void HandleVariablesRequest(DAPRequest request, DAPVariablesRequest msg)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(msg);

        if (!_stopped)
        {
            throw new RequestFailedException("Cannot get execution variables when the story is actively running.");
        }

        long variableType = msg.VariablesReference >> 48;
        List<DAPVariable> variables;

        if (variableType == 0)
        {
            int frameIndex = (int)msg.VariablesReference - 1;
            variables = GetStackVariables(msg, frameIndex);
        }
        else if (variableType is 1 or 2)
        {
            if (_evalResults is null) throw new InvalidOperationException("Evaluation results layer has not been initialized.");
            variables = _evalResults.GetVariables(msg, msg.VariablesReference);
        }
        else if (variableType == 3)
        {
            int frameIndex = (int)(msg.VariablesReference & 0xFF_FFFF);
            variables = GetQueryResultVariables(msg, frameIndex);
        }
        else
        {
            throw new InvalidOperationException($"Unknown variables reference schema block identifier type: {variableType}");
        }

        Stream.SendReply(request, new DAPVariablesResponse
        {
            Variables = variables
        });
    }

    private uint GetContinueBreakpointMask()
    {
        uint breakpoints = 0;
        if (_config is null || _config.StopOnFailedQueries)
        {
            breakpoints |= (uint)BreakpointType.FailedQuery;
        }

        if (_config is not null && _config.StopOnAllFrames)
        {
            breakpoints |=
                (uint)BreakpointType.Valid

                | (uint)BreakpointType.Pushdown
                | (uint)BreakpointType.Insert
                | (uint)BreakpointType.RuleAction

                | (uint)BreakpointType.InitCall
                | (uint)BreakpointType.ExitCall
                | (uint)BreakpointType.Delete;
        }
        else
        {
            breakpoints |=
                (uint)BreakpointType.Pushdown

                | (uint)BreakpointType.RuleAction
                | (uint)BreakpointType.InitCall
                | (uint)BreakpointType.ExitCall;
        }

        return breakpoints;
    }

    private uint GetContinueFlags()
    {
        uint flags = 0;
        if (_config is null || !_config.StopOnAllFrames)
        {
            flags |= (uint)ContinueFlags.SkipRulePushdown;
        }

        if (_config is null || !_config.StopOnDbPropagation)
        {
            flags |= (uint)ContinueFlags.SkipDbPropagation;
        }

        return flags;
    }

    private void SendContinue(ContinueAction action)
    {
        _dbgCli?.SendContinue(action, GetContinueBreakpointMask(), GetContinueFlags());
    }

    private void HandleContinueRequest(DAPRequest request, DAPContinueRequest msg, ContinueAction action)
    {
        ArgumentNullException.ThrowIfNull(msg);

        if (msg.ThreadId != 1)
        {
            throw new RequestFailedException("Requested continue for unknown thread");
        }

        if (action == ContinueAction.Pause)
        {
            if (_stopped)
            {
                throw new RequestFailedException("Already stopped");
            }

            _pauseRequested = true;
        }
        else
        {
            if (!_stopped)
            {
                throw new RequestFailedException("Already running");
            }

            _stopped = false;
        }

        if (_debuggingStory)
        {
            SendContinue(action);
        }

        Stream.SendReply(request, new DAPContinueResponse
        {
            AllThreadsContinued = false
        });
    }

    private void HandleEvaluateRequest(DAPRequest request, DAPEvaulateRequest req)
    {
        ArgumentNullException.ThrowIfNull(req);

        if (!_stopped || _stack is null)
        {
            throw new RequestFailedException("Can only evaluate expressions when stopped");
        }

        var frameIndex = req.FrameId ?? 0;
        if (frameIndex < 0 || frameIndex >= _stack.Count)
        {
            throw new RequestFailedException($"Requested evaluate for unknown frame {frameIndex}");
        }

        var frame = _stack[frameIndex];
        bool allowMutation = req.Context == "repl";

        if (_evaluator is null) throw new InvalidOperationException("The expression evaluator has not been initialized.");
        _evaluator.Evaluate(request, req.Expression, frame, allowMutation);
    }

    private void HandleDisconnectRequest(DAPRequest request)
    {
        Stream.SendReply(request, new DAPEmptyPayload());

        try
        {
            OnDebugSessionEnded();
            _dbgClient = null;
            _dbgCli = null;
        }
        catch (Exception ex)
        {
            LogError($"Exception thrown while flushing active debug background session: {ex.Message}");
        }
    }

    private void HandleRequest(DAPRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (request.Command)
        {
            case "initialize":
                HandleInitializeRequest(request);
                break;

            case "launch":
                HandleLaunchRequest(request, (DAPLaunchRequest)request.Arguments!);
                break;

            case "setBreakpoints":
                HandleSetBreakpointsRequest(request, (DAPSetBreakpointsRequest)request.Arguments!);
                break;

            case "configurationDone":
                HandleConfigurationDoneRequest(request);
                break;

            case "threads":
                HandleThreadsRequest(request);
                break;

            case "stackTrace":
                HandleStackTraceRequest(request, (DAPStackFramesRequest)request.Arguments!);
                break;

            case "scopes":
                HandleScopesRequest(request, (DAPScopesRequest)request.Arguments!);
                break;

            case "variables":
                HandleVariablesRequest(request, (DAPVariablesRequest)request.Arguments!);
                break;

            case "continue":
                HandleContinueRequest(request, (DAPContinueRequest)request.Arguments!, ContinueAction.Continue);
                break;

            case "next":
                HandleContinueRequest(request, (DAPContinueRequest)request.Arguments!, ContinueAction.StepOver);
                break;

            case "stepIn":
                HandleContinueRequest(request, (DAPContinueRequest)request.Arguments!, ContinueAction.StepInto);
                break;

            case "stepOut":
                HandleContinueRequest(request, (DAPContinueRequest)request.Arguments!, ContinueAction.StepOut);
                break;

            case "pause":
                HandleContinueRequest(request, (DAPContinueRequest)request.Arguments!, ContinueAction.Pause);
                break;

            case "evaluate":
                HandleEvaluateRequest(request, (DAPEvaulateRequest)request.Arguments!);
                break;

            case "disconnect":
                HandleDisconnectRequest(request);
                break;

            default:
                throw new InvalidOperationException($"Unsupported DAP request command identifier target: {request.Command}");
        }
    }

    private static void HandleEvent(DAPEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        throw new InvalidOperationException($"Unsupported or unhandled incoming notification profile event action: {evt.EventName}");
    }
}
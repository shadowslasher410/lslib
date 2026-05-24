using LSLib.LS.Story.Compiler;
using System.Runtime.InteropServices;

namespace LSTools.DebuggerFrontend;

public class RequestFailedException(string message) : Exception(message);

class DatabaseEnumerator(
    DebuggerClient dbgClient,
    DAPStream dap,
    StoryDebugInfo debugInfo,
    EvaluationResultManager resultManager)
{
    private readonly Dictionary<uint, List<DAPRequest>> _pendingDatabaseRequests = [];
    private readonly Dictionary<uint, EvaluationResults> _databaseContents = [];

    public void InitializeCallbacks()
    {
        dbgClient.OnBeginDatabaseContents = OnBeginDatabaseContents;
        dbgClient.OnDatabaseRow = OnDatabaseRow;
        dbgClient.OnEndDatabaseContents = OnEndDatabaseContents;
    }

    public void RequestDatabaseEvaluation(DAPRequest request, uint databaseId)
    {
        ArgumentNullException.ThrowIfNull(request);

        ref var requests = ref CollectionsMarshal.GetValueRefOrAddDefault(_pendingDatabaseRequests, databaseId, out bool exists);
        if (!exists || requests is null)
        {
            requests = [];
        }

        if (requests.Count == 0)
        {
            var databaseDebugInfo = debugInfo.Databases[databaseId];
            _databaseContents[databaseId] = resultManager.MakeResults(databaseDebugInfo.ParamTypes.Count);
        }

        requests.Add(request);
        dbgClient.SendGetDatabaseContents(databaseId);
    }

    private void OnBeginDatabaseContents(BkBeginDatabaseContents msg)
    {
        // Reserved hooks context channel frame hook
    }

    private void OnDatabaseRow(BkDatabaseRow msg)
    {
        ArgumentNullException.ThrowIfNull(msg);

        if (_databaseContents.TryGetValue(msg.DatabaseId, out var db) && msg.Row is { Count: > 0 })
        {
            foreach (var row in msg.Row)
            {
                db.Add(row);
            }
        }
    }

    private void OnEndDatabaseContents(BkEndDatabaseContents msg)
    {
        ArgumentNullException.ThrowIfNull(msg);

        if (!_databaseContents.TryGetValue(msg.DatabaseId, out var rows)) return;
        var db = debugInfo.Databases[msg.DatabaseId];

        DAPEvaluateResponse evalResponse = new()
        {
            Result = $"Database {db.Name} ({rows.Count} rows)",
            NamedVariables = 0,
            IndexedVariables = rows.Count,
            VariablesReference = rows.VariablesReference
        };

        if (_pendingDatabaseRequests.Remove(msg.DatabaseId, out var requests))
        {
            foreach (var request in requests)
            {
                dap.SendReply(request, evalResponse);
            }
        }
    }
}
namespace LSTools.DebuggerFrontend;

public class EvaluationResults(long variableReference, long variableIndexPrefix, int numColumns)
{
    private readonly List<MsgTuple> _tuples = [];

    public List<string>? ColumnNames { get; init; }
    public int Count => _tuples.Count;
    public long VariablesReference => variableReference;

    public void Add(MsgTuple tuple) => _tuples.Add(tuple);

    public List<DAPVariable> GetRows(DAPVariablesRequest msg)
    {
        int startIndex = msg.Start ?? 0;
        int numVars = (msg.Count is null or 0) ? _tuples.Count : msg.Count.Value;
        int lastIndex = Math.Min(startIndex + numVars, _tuples.Count);

        int totalElements = Math.Max(0, lastIndex - startIndex);
        List<DAPVariable> variables = new(totalElements);

        for (var i = startIndex; i < lastIndex; i++)
        {
            var row = _tuples[i];

            variables.Add(new DAPVariable
            {
                Name = i.ToString(),
                Value = $"({ValueFormatter.TupleToString(row)})",
                VariablesReference = variableIndexPrefix | (uint)i,
                IndexedVariables = ColumnNames is null ? numColumns : 0,
                NamedVariables = ColumnNames is null ? 0 : numColumns
            });
        }

        return variables;
    }

    public List<DAPVariable> GetRow(DAPVariablesRequest msg, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _tuples.Count)
        {
            throw new RequestFailedException($"Requested nonexistent row {rowIndex}");
        }

        int startIndex = msg.Start ?? 0;
        int numVars = (msg.Count is null or 0) ? numColumns : msg.Count.Value;
        int lastIndex = Math.Min(startIndex + numVars, numColumns);

        var row = _tuples[rowIndex];

        int totalElements = Math.Max(0, lastIndex - startIndex);
        List<DAPVariable> variables = new(totalElements);

        for (var i = startIndex; i < lastIndex; i++)
        {
            variables.Add(new DAPVariable
            {
                Name = ColumnNames is null ? i.ToString() : ColumnNames[i],
                Value = ValueFormatter.ValueToString(row.Column[i])
            });
        }

        return variables;
    }
}

public class EvaluationResultManager
{
    private readonly List<EvaluationResults> _results = [];

    public EvaluationResults MakeResults(int numColumns) =>
        MakeResults(numColumns, null);

    public EvaluationResults MakeResults(int numColumns, List<string>? columnNames)
    {
        ulong variableRef = (1UL << 48) | ((ulong)_results.Count << 24);
        ulong variableIndexPrefix = (2UL << 48) | ((ulong)_results.Count << 24);

        var result = new EvaluationResults((long)variableRef, (long)variableIndexPrefix, numColumns)
        {
            ColumnNames = columnNames
        };

        _results.Add(result);
        return result;
    }

    public List<DAPVariable> GetVariables(DAPVariablesRequest msg, long variablesReference)
    {
        long variableType = variablesReference >> 48;
        int resultSetIdx = (int)((variablesReference >> 24) & 0xFF_FFFF);

        if (resultSetIdx < 0 || resultSetIdx >= _results.Count)
        {
            throw new InvalidOperationException($"Evaluation result set ID does not exist: {resultSetIdx}");
        }

        return variableType switch
        {
            1 => _results[resultSetIdx].GetRows(msg),
            2 => _results[resultSetIdx].GetRow(msg, (int)(msg.VariablesReference & 0xFF_FFFF)),
            _ => throw new InvalidOperationException($"EvaluationResultManager does not support this variable type token: {variableType}")
        };
    }
}

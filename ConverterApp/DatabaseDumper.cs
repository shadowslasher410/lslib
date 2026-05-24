using System.Text;
using LSLib.LS.Story;

namespace LSTools.DivineGUI;

public sealed class DatabaseDumper : IDisposable
{
    public bool DumpUnnamedDbs { get; set; } = false;

    private readonly StreamWriter _writer;

    public DatabaseDumper(Stream outputStream)
    {
        ArgumentNullException.ThrowIfNull(outputStream);
        _writer = new StreamWriter(outputStream, Encoding.UTF8);
    }

    public void Dispose() => _writer.Dispose();

    private void DumpFact(Story story, Fact fact)
    {
        _writer.Write("(");

        int columnCount = fact.Columns.Count;
        for (int i = 0; i < columnCount; i++)
        {
            fact.Columns[i].DebugDump(_writer, story);
            if (i + 1 < columnCount)
            {
                _writer.Write(", ");
            }
        }

        _writer.Write(")\n");
    }

    public void DumpDatabase(Story story, Database database)
    {
        if (database.OwnerNode is { } node)
        {
            if (!string.IsNullOrEmpty(node.Name))
            {
                _writer.Write($"Database '{node.Name}'");
            }
            else
            {
                _writer.Write($"Database #{database.Index} <{node.TypeName()}>");
            }
        }
        else
        {
            _writer.Write($"Database #{database.Index}");
        }

        var typeNamesList = new List<string>(database.Parameters.Types.Count);
        foreach (int typeId in database.Parameters.Types.Select(v => (int)v))
        {
            if (story.Types.TryGetValue((uint)typeId, out var storyType))
            {
                typeNamesList.Add(storyType.Name);
            }
        }
        string typesString = string.Join(", ", typeNamesList);
        _writer.Write($" ({typesString}):\n");

        if (database.Facts is IEnumerable<Fact> stronglyTypedFacts)
        {
            foreach (Fact fact in stronglyTypedFacts)
            {
                _writer.Write("\t");
                DumpFact(story, fact);
            }
        }
    }

    public void DumpAll(Story story)
    {
        ArgumentNullException.ThrowIfNull(story);
        _writer.Write(" === DUMP OF DATABASES === \n");

        foreach (KeyValuePair<uint, Database> entry in story.Databases)
        {
            Database db = entry.Value;
            if (DumpUnnamedDbs || (db.OwnerNode is { } node && !string.IsNullOrEmpty(node.Name)))
            {
                DumpDatabase(story, db);
                _writer.Write("\n");
            }
        }
    }

    public List<FactRowModel> GenerateGridRows(Story story)
    {
        ArgumentNullException.ThrowIfNull(story);
        var rowsCollection = new List<FactRowModel>();

        foreach (KeyValuePair<uint, Database> entry in story.Databases)
        {
            Database db = entry.Value;

            if (DumpUnnamedDbs || (db.OwnerNode is { } node && !string.IsNullOrEmpty(node.Name)))
            {
                if (db.Facts is IEnumerable<Fact> stronglyTypedFacts)
                {
                    foreach (Fact fact in stronglyTypedFacts)
                    {
                        var evaluatedDisplayValues = new string[fact.Columns.Count];
                        for (int i = 0; i < fact.Columns.Count; i++)
                        {
                            using var stringWriter = new StringWriter();
                            fact.Columns[i].DebugDump(stringWriter, story);
                            evaluatedDisplayValues[i] = stringWriter.ToString();
                        }
                        rowsCollection.Add(new FactRowModel(fact, evaluatedDisplayValues));
                    }
                }
            }
        }

        return rowsCollection;
    }
}
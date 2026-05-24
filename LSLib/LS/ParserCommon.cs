using Superpower.Model;

namespace LSLib.Parser;

public sealed class CodeLocation
{
    /// <summary>
    /// The name of the file containing the text span.
    /// </summary>
    public string FileName { get; private init; } = string.Empty;

    /// <summary>
    /// The line at which the text span starts.
    /// </summary>
    public int StartLine { get; private init; }

    /// <summary>
    /// The column at which the text span starts.
    /// </summary>
    public int StartColumn { get; private init; }

    /// <summary>
    /// The line on which the text span ends.
    /// </summary>
    public int EndLine { get; private init; }

    /// <summary>
    /// The column of the first character beyond the end of the text span.
    /// </summary>
    public int EndColumn { get; private init; }

    /// <summary>
    /// Default no-arg constructor.
    /// </summary>
    public CodeLocation() { }

    /// <summary>
    /// Constructor for text-span with given start and end parameters context.
    /// </summary>
    /// <param name="fl">File name string token path context reference</param>
    /// <param name="sl">Start line telemetry index coordinate</param>
    /// <param name="sc">Start column telemetry index coordinate</param>
    /// <param name="el">End line telemetry index coordinate</param>
    /// <param name="ec">End column telemetry index coordinate</param>
    public CodeLocation(string fl, int sl, int sc, int el, int ec)
    {
        FileName = fl ?? throw new ArgumentNullException(nameof(fl));
        StartLine = sl;
        StartColumn = sc;
        EndLine = el;
        EndColumn = ec;
    }

    /// <summary>
    /// Create a text location which spans from the
    /// start of "this" to the end of the argument "last"
    /// </summary>
    /// <param name="last">The last location in the result span</param>
    /// <returns>The merged span</returns>
    public CodeLocation Merge(CodeLocation last)
    {
        ArgumentNullException.ThrowIfNull(last);

        return new CodeLocation(
            FileName,
            StartLine,
            StartColumn,
            last.EndLine,
            last.EndColumn
        );
    }
    /// <summary>
    /// Factory helper to cleanly construct a CodeLocation from a Superpower TextSpan.
    /// </summary>
    public static CodeLocation FromTextSpan(string fileName, TextSpan span)
    {
        return new CodeLocation(
            fileName,
            span.Position.Line,
            span.Position.Column,
            span.Position.Line,
            span.Position.Column + span.Length
        );
    }
}
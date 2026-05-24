namespace LSLib.LS.Story.Compiler;

public sealed class Preprocessor
{
    public static bool Preprocess(string script, out string preprocessed)
    {
        ArgumentNullException.ThrowIfNull(script);

        if (!script.Contains("/* [OSITOOLS_ONLY]")
            && !script.Contains("// [BEGIN_NO_OSITOOLS]"))
        {
            preprocessed = script;
            return false;
        }

        ReadOnlySpan<char> sourceSpan = script.AsSpan();
        var builder = new StringBuilder(script.Length);

        int pos = 0;
        while (pos < sourceSpan.Length)
        {
            int next = script.IndexOf("/* [OSITOOLS_ONLY]", pos, StringComparison.Ordinal);
            if (next == -1)
            {
                builder.Append(sourceSpan[pos..]);
                break;
            }

            int end = script.IndexOf("*/", next, StringComparison.Ordinal);
            if (end == -1)
            {
                builder.Append(sourceSpan[pos..]);
                break;
            }

            builder.Append(sourceSpan[pos..next]);
            builder.Append(sourceSpan[(next + 19)..end]);
            pos = end + 2;
        }

        string ph1 = builder.ToString();
        ReadOnlySpan<char> ph1Span = ph1.AsSpan();
        var builderPh2 = new StringBuilder(ph1.Length);

        pos = 0;
        while (pos < ph1Span.Length)
        {
            int next = ph1.IndexOf("// [BEGIN_NO_OSITOOLS]", pos, StringComparison.Ordinal);
            if (next == -1)
            {
                builderPh2.Append(ph1Span[pos..]);
                break;
            }

            int end = ph1.IndexOf("// [END_NO_OSITOOLS]", next, StringComparison.Ordinal);
            if (end == -1)
            {
                builderPh2.Append(ph1Span[pos..]);
                break;
            }

            builderPh2.Append(ph1Span[pos..next]);
            pos = end + 21;
        }

        preprocessed = builderPh2.ToString();
        return true;
    }
}
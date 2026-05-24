using System.CommandLine;
using System.Text;
using LSTools.DebuggerFrontend;

var defaultLogPath = Path.Combine(AppContext.BaseDirectory, "DAP.log");

var logFileOption = new Option<FileInfo>("--log-file", "-l")
{
    Description = "The destination path where Debug Adapter Protocol session logs are written.",
    Required = false
};

var rootCommand = new RootCommand("LSTools Debugger Frontend - Debug Adapter Protocol Host")
{
    logFileOption
};

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var logFileInfo = parseResult.GetValue(logFileOption) ?? new FileInfo(defaultLogPath);

    try
    {
        logFileInfo.Directory?.Create();
        using var logFile = logFileInfo.Open(FileMode.Create, FileAccess.Write, FileShare.Read);

        var dap = new DAPStream();
        dap.EnableLogging(logFile);

        var dapHandler = new DAPMessageHandler(dap)
        {
            ModUuid = string.Empty
        };
        dapHandler.EnableLogging(logFile);

        await Task.Run(dap.RunLoop, cancellationToken);
        return 0;
    }
    catch (Exception e)
    {
        var exceptionString = e.ToString();

        try
        {
            if (logFileInfo is { Exists: true })
            {
                using var errorStream = logFileInfo.Open(FileMode.Append, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(errorStream, Encoding.UTF8);
                await writer.WriteAsync(exceptionString.AsMemory(), cancellationToken);
            }
        }
        catch
        {
            // Suppress
        }

        await Console.Error.WriteLineAsync(exceptionString.AsMemory(), cancellationToken);
        return 1;
    }
});

return await rootCommand.Parse(args).InvokeAsync();
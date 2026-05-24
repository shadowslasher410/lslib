using LSLib.LS.Enums;

namespace LSLib.Divine.CLI;

internal static class CommandLineLogger
{
    private static readonly LogLevel LogLevelOption = CommandLineActions.LogLevel;

    public static void LogFatal(string message, int errorCode) => Log(LogLevel.FATAL, message, errorCode);
    public static void LogError(string message) => Log(LogLevel.ERROR, message);
    public static void LogWarn(string message) => Log(LogLevel.WARN, message);
    public static void LogInfo(string message) => Log(LogLevel.INFO, message);
    public static void LogDebug(string message) => Log(LogLevel.DEBUG, message);
    public static void LogTrace(string message) => Log(LogLevel.TRACE, message);
    public static void LogAll(string message) => Log(LogLevel.ALL, message);

    private static void Log(LogLevel logLevel, string message, int errorCode = -1)
    {
        if (LogLevelOption == LogLevel.OFF && logLevel != LogLevel.FATAL)
        {
            return;
        }

        if (logLevel == LogLevel.FATAL)
        {
            if (LogLevelOption > LogLevel.OFF)
            {
                Console.Error.WriteLine($"[FATAL] {message}");
            }

            int exitCode = errorCode == -1 ? (int)LogLevel.FATAL : (int)LogLevel.FATAL + errorCode;
            Environment.Exit(exitCode);
            return;
        }

        if (LogLevelOption < logLevel)
        {
            return;
        }

        string prefix = logLevel switch
        {
            LogLevel.ERROR => "ERROR",
            LogLevel.WARN => "WARN",
            LogLevel.INFO => "INFO",
            LogLevel.DEBUG => "DEBUG",
            LogLevel.TRACE => "TRACE",
            _ => "LOG"
        };

        if (logLevel == LogLevel.ERROR)
        {
            Console.Error.WriteLine($"[{prefix}] {message}");
        }
        else
        {
            Console.WriteLine($"[{prefix}] {message}");
        }
    }
}
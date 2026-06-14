using BepInEx.Logging;

namespace MelonLoader.BepInExCompat;

internal static class CompatLog
{
    private static ManualLogSource? log;

    public static void Initialize(ManualLogSource source)
    {
        log = source;
    }

    public static void Info(string message) => Write(LogLevel.Info, message);

    public static void Message(string message) => Write(LogLevel.Message, message);

    public static void Warning(string message) => Write(LogLevel.Warning, message);

    public static void Error(string message) => Write(LogLevel.Error, message);

    public static void Error(Exception exception, string? message = null)
    {
        Write(LogLevel.Error, string.IsNullOrWhiteSpace(message) ? exception.ToString() : $"{message}{Environment.NewLine}{exception}");
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message);

    public static string Format(string message, params object?[] args)
    {
        if (args.Length == 0)
            return message;

        try
        {
            return string.Format(message, args);
        }
        catch (FormatException)
        {
            return $"{message} {string.Join(" ", args.Select(a => a?.ToString() ?? "null"))}";
        }
    }

    private static void Write(LogLevel level, string message)
    {
        if (log is not null)
        {
            log.Log(level, message);
            return;
        }

        Console.WriteLine($"[{level}] {message}");
    }
}

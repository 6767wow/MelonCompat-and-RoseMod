namespace MelonLoader.BepInExCompat;

internal static class CompatLog
{
    private static readonly object Gate = new();
    private static string? logFile;
    private const string Reset = "\u001b[0m";
    private static readonly string Gray = Color(211, 211, 211);
    private static readonly string Timestamp = Color(0, 255, 0);
    private static readonly string Theme = Color(80, 220, 140);
    private static readonly string InfoColor = Color(0, 255, 255);
    private static readonly string WarningColor = Color(255, 255, 0);
    private static readonly string ErrorColor = Color(205, 92, 92);
    private static readonly string DebugColor = Color(130, 130, 130);

    public static void Initialize(string path)
    {
        logFile = path;
    }

    public static void Info(string message) => Write("Info", message);
    public static void Message(string message) => Write("Message", message);
    public static void Warning(string message) => Write("Warning", message);
    public static void Error(string message) => Write("Error", message);
    public static void Debug(string message) => Write("Debug", message);

    public static void Error(Exception exception, string? message = null)
    {
        Write("Error", string.IsNullOrWhiteSpace(message) ? exception.ToString() : $"{message}{Environment.NewLine}{exception}");
    }

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
            return $"{message} {string.Join(" ", args.Select(arg => arg?.ToString() ?? "null"))}";
        }
    }

    private static void Write(string level, string message)
    {
        var color = ColorForLevel(level);
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}";
        lock (Gate)
        {
            foreach (var part in message.Replace("\r\n", "\n").Split('\n'))
                WriteConsoleLine(level, color, part);

            if (!string.IsNullOrWhiteSpace(logFile))
                File.AppendAllText(logFile!, line + Environment.NewLine);
        }
    }

    // Console format adapted from Simple Log Utility by Fibles.
    private static void WriteConsoleLine(string level, string color, string message)
    {
        var cleanText = message.Replace(Reset, color);
        Console.WriteLine($"{Gray}[{Timestamp}{DateTime.Now:HH:mm:ss.fff}{Gray}] [{color}{level}{Gray}]{color} {cleanText}{Reset}");
    }

    private static string ColorForLevel(string level)
    {
        return level.ToLowerInvariant() switch
        {
            "error" => ErrorColor,
            "warning" => WarningColor,
            "debug" => DebugColor,
            "info" => InfoColor,
            "message" => Theme,
            _ => Theme
        };
    }

    private static string Color(byte red, byte green, byte blue) => $"\u001b[38;2;{red};{green};{blue}m";
}

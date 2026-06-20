namespace RoseMod;

public static class RoseModLog
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
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
    }

    public static void Banner()
    {
        WriteRaw("============================================================", Gray);
        WriteRaw(" RoseMod 0.1.0 - standalone universal Unity mod framework", Theme);
        WriteRaw("============================================================", Gray);
    }

    public static void Message(string message) => Write("Message", message, Theme);
    public static void Info(string message) => Write("Info", message, InfoColor);
    public static void Warning(string message) => Write("Warning", message, WarningColor);
    public static void Error(string message) => Write("Error", message, ErrorColor);
    public static void Error(Exception exception, string? message = null) => Error(string.IsNullOrWhiteSpace(message) ? exception.ToString() : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message, string color)
    {
        lock (Gate)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}";
            foreach (var part in SplitLines(message))
                WriteConsoleLine(level, color, part);

            if (!string.IsNullOrWhiteSpace(logFile))
                File.AppendAllText(logFile!, line + Environment.NewLine);
        }
    }

    private static void WriteRaw(string message, string color, bool appendNewLine = true)
    {
        lock (Gate)
        {
            var colored = $"{color}{message}{Reset}";
            if (appendNewLine)
                Console.WriteLine(colored);
            else
                Console.Write(colored);

            if (appendNewLine && !string.IsNullOrWhiteSpace(logFile))
                File.AppendAllText(logFile!, message + Environment.NewLine);
        }
    }

    // Console format adapted from Simple Log Utility by Fibles.
    private static void WriteConsoleLine(string level, string color, string message)
    {
        var cleanText = message.Replace(Reset, color);
        Console.WriteLine($"{Gray}[{Timestamp}{DateTime.Now:HH:mm:ss.fff}{Gray}] [{color}{level}{Gray}]{color} {cleanText}{Reset}");
    }

    private static IEnumerable<string> SplitLines(string message)
    {
        return message.Replace("\r\n", "\n").Split('\n');
    }

    private static string Color(byte red, byte green, byte blue) => $"\u001b[38;2;{red};{green};{blue}m";
}

using System.Runtime.InteropServices;
using System.Text;

namespace RoseMod;

internal static class RoseModConsole
{
    private static bool initialized;

    public static void Initialize()
    {
        if (initialized)
            return;

        initialized = true;
        if (!IsWindows())
            return;

        try
        {
            var consoleWindow = GetConsoleWindow();
            if (consoleWindow == IntPtr.Zero)
            {
                if (!ShouldShowConsole() || !AllocConsole())
                    return;
            }

            SetConsoleTitle("RoseMod Console");
            TrySetEncoding();
            TryEnableVirtualTerminal();
            RedirectStandardStreams();
        }
        catch
        {
            // Logging still goes to RoseMod/Logs if a console cannot be opened.
        }
    }

    private static bool IsWindows()
    {
        var platform = Environment.OSVersion.Platform;
        return platform is PlatformID.Win32NT or PlatformID.Win32S or PlatformID.Win32Windows or PlatformID.WinCE;
    }

    private static bool ShouldShowConsole()
    {
        var value = Environment.GetEnvironmentVariable("ROSEMOD_HIDE_CONSOLE");
        if (string.IsNullOrWhiteSpace(value))
            value = Environment.GetEnvironmentVariable("MELONCOMPAT_HIDE_CONSOLE");

        return string.IsNullOrWhiteSpace(value)
            || value.Equals("0", StringComparison.OrdinalIgnoreCase)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase)
            || value.Equals("no", StringComparison.OrdinalIgnoreCase);
    }

    private static void TrySetEncoding()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
        }
        catch
        {
        }
    }

    private static void RedirectStandardStreams()
    {
        try
        {
            var output = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(output);
        }
        catch
        {
        }

        try
        {
            var error = new StreamWriter(Console.OpenStandardError(), Encoding.UTF8) { AutoFlush = true };
            Console.SetError(error);
        }
        catch
        {
        }

        try
        {
            Console.SetIn(new StreamReader(Console.OpenStandardInput(), Encoding.UTF8));
        }
        catch
        {
        }
    }

    private static void TryEnableVirtualTerminal()
    {
        try
        {
            var handle = GetStdHandle(StdOutputHandle);
            if (handle == IntPtr.Zero || handle == InvalidHandleValue)
                return;

            if (!GetConsoleMode(handle, out var mode))
                return;

            SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
        }
        catch
        {
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetConsoleTitle(string title);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out int lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, int dwMode);

    private const int StdOutputHandle = -11;
    private const int EnableVirtualTerminalProcessing = 0x0004;
    private static readonly IntPtr InvalidHandleValue = new(-1);

}

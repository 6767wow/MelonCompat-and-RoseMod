namespace MelonLoader.Logging
{
    public readonly struct ColorARGB : IEquatable<ColorARGB>
    {
        public ColorARGB(byte a, byte r, byte g, byte b)
        {
            A = a;
            R = r;
            G = g;
            B = b;
        }

        public byte A { get; }
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }

        public static ColorARGB FromArgb(uint argb) => new((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

        public static ColorARGB FromArgb(byte alpha, byte red, byte green, byte blue) => new(alpha, red, green, blue);

        public static ColorARGB FromArgb(byte red, byte green, byte blue) => new(255, red, green, blue);

        public static ColorARGB FromArgb(byte alpha, ColorARGB baseColor) => new(alpha, baseColor.R, baseColor.G, baseColor.B);

        public static ColorARGB FromConsoleColor(ConsoleColor color) => color switch
        {
            ConsoleColor.Black => Black,
            ConsoleColor.DarkBlue => DarkBlue,
            ConsoleColor.DarkGreen => DarkGreen,
            ConsoleColor.DarkCyan => DarkCyan,
            ConsoleColor.DarkRed => DarkRed,
            ConsoleColor.DarkMagenta => DarkMagenta,
            ConsoleColor.DarkYellow => Goldenrod,
            ConsoleColor.Gray => Gray,
            ConsoleColor.DarkGray => DarkGray,
            ConsoleColor.Blue => Blue,
            ConsoleColor.Green => Green,
            ConsoleColor.Cyan => Cyan,
            ConsoleColor.Red => Red,
            ConsoleColor.Magenta => Magenta,
            ConsoleColor.Yellow => Yellow,
            _ => White
        };

        public static ColorARGB Transparent => new(0, 255, 255, 255);
        public static ColorARGB Black => new(255, 0, 0, 0);
        public static ColorARGB White => new(255, 255, 255, 255);
        public static ColorARGB Gray => new(255, 128, 128, 128);
        public static ColorARGB DarkGray => new(255, 169, 169, 169);
        public static ColorARGB Red => new(255, 255, 0, 0);
        public static ColorARGB DarkRed => new(255, 139, 0, 0);
        public static ColorARGB Green => new(255, 0, 128, 0);
        public static ColorARGB DarkGreen => new(255, 0, 100, 0);
        public static ColorARGB Blue => new(255, 0, 0, 255);
        public static ColorARGB DarkBlue => new(255, 0, 0, 139);
        public static ColorARGB DarkCyan => new(255, 0, 139, 139);
        public static ColorARGB DarkMagenta => new(255, 139, 0, 139);
        public static ColorARGB Yellow => new(255, 255, 255, 0);
        public static ColorARGB Cyan => new(255, 0, 255, 255);
        public static ColorARGB Magenta => new(255, 255, 0, 255);
        public static ColorARGB Fuchsia => Magenta;
        public static ColorARGB Orange => new(255, 255, 165, 0);
        public static ColorARGB Purple => new(255, 128, 0, 128);
        public static ColorARGB Lime => new(255, 0, 255, 0);
        public static ColorARGB Pink => new(255, 255, 192, 203);
        public static ColorARGB Aqua => Cyan;
        public static ColorARGB Navy => new(255, 0, 0, 128);
        public static ColorARGB Teal => new(255, 0, 128, 128);
        public static ColorARGB Olive => new(255, 128, 128, 0);
        public static ColorARGB Maroon => new(255, 128, 0, 0);
        public static ColorARGB Silver => new(255, 192, 192, 192);
        public static ColorARGB Goldenrod => new(255, 218, 165, 32);

        public bool Equals(ColorARGB other) => A == other.A && R == other.R && G == other.G && B == other.B;

        public override bool Equals(object? obj) => obj is ColorARGB other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + A;
                hash = hash * 31 + R;
                hash = hash * 31 + G;
                hash = hash * 31 + B;
                return hash;
            }
        }

        public static bool operator ==(ColorARGB left, ColorARGB right) => left.Equals(right);

        public static bool operator !=(ColorARGB left, ColorARGB right) => !left.Equals(right);
    }
}

namespace MelonLoader
{
using MelonLoader.Logging;

public class MelonLogger
{
    public static ColorARGB DefaultMelonColor = ColorARGB.Magenta;
    public static ColorARGB DefaultTextColor = ColorARGB.White;

    public static void Msg(object message) => BepInExCompat.CompatLog.Message(message?.ToString() ?? string.Empty);
    public static void Msg(string message) => BepInExCompat.CompatLog.Message(message);
    public static void Msg(string message, params object?[] args) => Msg(BepInExCompat.CompatLog.Format(message, args));
    public static void Msg(ConsoleColor color, object message) => Msg(message);
    public static void Msg(ConsoleColor color, string message) => Msg(message);
    public static void Msg(ConsoleColor color, string message, params object?[] args) => Msg(message, args);
    public static void Msg(ColorARGB color, object message) => Msg(message);
    public static void Msg(ColorARGB color, string message) => Msg(message);
    public static void Msg(ColorARGB color, string message, params object?[] args) => Msg(message, args);
    public static void MsgDirect(ColorARGB color, string message) => Msg(message);
    public static void MsgPastel(object message) => Msg(message);
    public static void MsgPastel(string message) => Msg(message);
    public static void MsgPastel(string message, params object?[] args) => Msg(message, args);
    public static void MsgPastel(ConsoleColor color, object message) => Msg(message);
    public static void MsgPastel(ConsoleColor color, string message) => Msg(message);
    public static void MsgPastel(ConsoleColor color, string message, params object?[] args) => Msg(message, args);
    public static void MsgPastel(ColorARGB color, object message) => Msg(message);
    public static void MsgPastel(ColorARGB color, string message) => Msg(message);
    public static void MsgPastel(ColorARGB color, string message, params object?[] args) => Msg(message, args);
    public static void MsgPastelDirect(ColorARGB color, string message) => Msg(message);

    public static void Log(object message) => Msg(message);
    public static void Log(string message) => Msg(message);
    public static void Log(string message, params object?[] args) => Msg(message, args);
    public static void Log(ConsoleColor color, object message) => Msg(message);
    public static void Log(ConsoleColor color, string message) => Msg(message);
    public static void Log(ConsoleColor color, string message, params object?[] args) => Msg(message, args);

    public static void Warning(object message) => BepInExCompat.CompatLog.Warning(message?.ToString() ?? string.Empty);
    public static void Warning(string message) => BepInExCompat.CompatLog.Warning(message);
    public static void Warning(string message, params object?[] args) => Warning(BepInExCompat.CompatLog.Format(message, args));
    public static void Warning(string message, Exception exception) => BepInExCompat.CompatLog.Warning($"{message}{Environment.NewLine}{exception}");
    public static void LogWarning(string message) => Warning(message);
    public static void LogWarning(string message, params object?[] args) => Warning(message, args);

    public static void Error(object message) => BepInExCompat.CompatLog.Error(message?.ToString() ?? string.Empty);
    public static void Error(string message) => BepInExCompat.CompatLog.Error(message);
    public static void Error(string message, params object?[] args) => Error(BepInExCompat.CompatLog.Format(message, args));
    public static void Error(string message, Exception exception) => BepInExCompat.CompatLog.Error(exception, message);
    public static void LogError(string message) => Error(message);
    public static void LogError(string message, params object?[] args) => Error(message, args);

    public static void BigError(string message, string? title = null) => Error(string.IsNullOrWhiteSpace(title) ? message : $"{title}: {message}");

    public static void WriteLine(int count = 1)
    {
        for (var i = 0; i < Math.Max(1, count); i++)
            Msg(string.Empty);
    }

    public static void WriteLine(ColorARGB color, int count = 1) => WriteLine(count);

    public sealed class Instance
    {
        public Instance(string name)
        {
            Name = name;
            Color = DefaultMelonColor;
        }

        public Instance(string name, ConsoleColor color)
            : this(name, ColorARGB.FromConsoleColor(color))
        {
        }

        public Instance(string name, ColorARGB color)
        {
            Name = name;
            Color = color;
        }

        public string Name { get; }
        public ColorARGB Color { get; }

        public void Msg(object message) => MelonLogger.Msg(Prefix(message));
        public void Msg(string message) => MelonLogger.Msg(Prefix(message));
        public void Msg(string message, params object?[] args) => Msg(BepInExCompat.CompatLog.Format(message, args));
        public void Msg(ConsoleColor color, object message) => Msg(message);
        public void Msg(ConsoleColor color, string message) => Msg(message);
        public void Msg(ConsoleColor color, string message, params object?[] args) => Msg(message, args);
        public void Msg(ColorARGB color, object message) => Msg(message);
        public void Msg(ColorARGB color, string message) => Msg(message);
        public void Msg(ColorARGB color, string message, params object?[] args) => Msg(message, args);
        public void MsgPastel(object message) => Msg(message);
        public void MsgPastel(string message) => Msg(message);
        public void MsgPastel(string message, params object?[] args) => Msg(message, args);
        public void MsgPastel(ConsoleColor color, object message) => Msg(message);
        public void MsgPastel(ConsoleColor color, string message) => Msg(message);
        public void MsgPastel(ConsoleColor color, string message, params object?[] args) => Msg(message, args);
        public void MsgPastel(ColorARGB color, object message) => Msg(message);
        public void MsgPastel(ColorARGB color, string message) => Msg(message);
        public void MsgPastel(ColorARGB color, string message, params object?[] args) => Msg(message, args);

        public void Warning(object message) => MelonLogger.Warning(Prefix(message));
        public void Warning(string message) => MelonLogger.Warning(Prefix(message));
        public void Warning(string message, params object?[] args) => Warning(BepInExCompat.CompatLog.Format(message, args));
        public void Warning(string message, Exception exception) => MelonLogger.Warning(Prefix(message), exception);
        public void Error(object message) => MelonLogger.Error(Prefix(message));
        public void Error(string message) => MelonLogger.Error(Prefix(message));
        public void Error(string message, params object?[] args) => Error(BepInExCompat.CompatLog.Format(message, args));
        public void Error(string message, Exception exception) => BepInExCompat.CompatLog.Error(exception, Prefix(message));
        public void BigError(string message) => Error(message);
        public void WriteLine(int count = 1) => MelonLogger.WriteLine(count);
        public void WriteLine(ColorARGB color, int count = 1) => MelonLogger.WriteLine(color, count);
        public void WriteSpacer() => WriteLine();

        private string Prefix(object? message) => $"[{Name}] {message ?? string.Empty}";
    }
}

public sealed class MelonModLogger : MelonLogger
{
}

public static class MelonDebug
{
    public static bool IsEnabled() => true;
    public static void Msg(object message) => BepInExCompat.CompatLog.Debug(message?.ToString() ?? string.Empty);
    public static void Msg(string message) => BepInExCompat.CompatLog.Debug(message);
    public static void Msg(string message, params object?[] args) => Msg(BepInExCompat.CompatLog.Format(message, args));
    public static void Error(string message) => BepInExCompat.CompatLog.Error(message);
}
}

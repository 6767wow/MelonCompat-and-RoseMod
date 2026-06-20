using System.Reflection;

namespace RoseMod;

internal static class RoseModIl2CppFixBridge
{
    public static void Install(RoseModPaths paths, RoseModStartupOptions options)
    {
        if (!options.Backend.Equals("IL2CPP", StringComparison.OrdinalIgnoreCase))
            return;

        var fixesPath = Path.Combine(paths.Core, "RoseMod.Il2CppFixes.dll");
        if (!File.Exists(fixesPath))
        {
            RoseModLog.Warning("MelonCompat Il2CppInterop fixes are missing; IL2CPP class injection will stay guarded.");
            return;
        }

        try
        {
            var assembly = Assembly.LoadFrom(fixesPath);
            var type = assembly.GetType("RoseMod.Il2CppFixes.RoseModIl2CppInteropFixes", throwOnError: true)!;
            var install = type.GetMethod("Install", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(type.FullName, "Install");
            install.Invoke(null, null);

            var installed = type.GetProperty("InstalledSuccessfully", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as bool?;
            if (installed == true)
                RoseModLog.Info("Installed MelonCompat Il2CppInterop compatibility fixes.");
            else
                RoseModLog.Warning("MelonCompat Il2CppInterop fixes did not report a successful install; IL2CPP class injection will stay guarded.");
        }
        catch (Exception ex)
        {
            RoseModLog.Error(Unwrap(ex), "Failed to install RoseMod Il2CppInterop fixes.");
        }
    }

    private static Exception Unwrap(Exception ex)
    {
        return ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
    }
}

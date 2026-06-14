namespace MelonLoader.Utils;

public static class MelonEnvironment
{
    private const string OurRuntimeName = "net6";

    public static bool IsDotnetRuntime => true;
    public static bool IsMonoRuntime => false;
    public static string MelonBaseDirectory => global::MelonLoader.MelonUtils.BaseDirectory;
    public static string GameExecutablePath => global::MelonLoader.MelonUtils.GetApplicationPath();
    public static string MelonLoaderDirectory => global::MelonLoader.MelonUtils.MelonLoaderDirectory;
    public static string GameRootDirectory => global::MelonLoader.MelonUtils.GameDirectory;
    public static string DependenciesDirectory => Path.Combine(MelonLoaderDirectory, "Dependencies");
    public static string SupportModuleDirectory => Path.Combine(DependenciesDirectory, "SupportModules");
    public static string CompatibilityLayerDirectory => Path.Combine(DependenciesDirectory, "CompatibilityLayers");
    public static string Il2CppAssemblyGeneratorDirectory => Path.Combine(DependenciesDirectory, "Il2CppAssemblyGenerator");
    public static string ModsDirectory => global::MelonLoader.MelonHandler.ModsDirectory;
    public static string PluginsDirectory => global::MelonLoader.MelonHandler.PluginsDirectory;
    public static string UserLibsDirectory => global::MelonLoader.MelonUtils.UserLibsDirectory;
    public static string UserDataDirectory => global::MelonLoader.MelonUtils.UserDataDirectory;
    public static string MelonLoaderLogsDirectory => Path.Combine(MelonLoaderDirectory, "Logs");
    public static string OurRuntimeDirectory => Path.Combine(MelonLoaderDirectory, OurRuntimeName);
    public static string GameExecutableName => Path.GetFileNameWithoutExtension(GameExecutablePath);
    public static string UnityGameDataDirectory => global::MelonLoader.MelonUtils.GetGameDataDirectory();
    public static string UnityGameManagedDirectory => global::MelonLoader.MelonUtils.GetManagedDirectory();
    public static string Il2CppDataDirectory => Path.Combine(UnityGameDataDirectory, "il2cpp_data");
    public static string UnityPlayerPath => Path.Combine(GameRootDirectory, "UnityPlayer.dll");
    public static string MelonManagedDirectory => Path.Combine(DependenciesDirectory, "Mono");
    public static string Il2CppAssembliesDirectory => Path.Combine(MelonLoaderDirectory, "Il2CppAssemblies");
}

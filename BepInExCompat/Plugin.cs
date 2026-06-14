using BepInEx;
#if BEPINEX_IL2CPP
using BepInEx.Unity.IL2CPP;

namespace MelonLoader.BepInExCompat;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "dev.jayde.melonloader.bepinex6.il2cpp.compat";
    public const string PluginName = "MelonLoader Compatibility for BepInEx IL2CPP";
    public const string PluginVersion = "0.7.3";

    public override void Load()
    {
        CompatLog.Initialize(Log);
        MelonEnvironment.Initialize(Paths.PluginPath, Paths.GameDataPath, string.Empty);
        CompatAssemblyResolver.Install(Paths.PluginPath);
        InteropNamespaceRewriter.Initialize(Paths.BepInExRootPath);
        ClassInjectorCompatibilityPatches.Install();
        InstallEventPump();

        CompatLog.Info("MelonLoader 0.5.7-0.7.3 compatibility shim loaded under BepInEx 6 IL2CPP.");
        MelonAssemblyLoader.LoadFromPluginsDirectory(Paths.PluginPath);
        MelonEventPumpBehaviour.PublishCurrentScene();
    }

    private void InstallEventPump()
    {
        try
        {
            AddComponent<MelonEventPumpBehaviour>();
            CompatLog.Info("Installed Unity frame and scene callback bridge for MelonLoader mods.");
        }
        catch (Exception ex)
        {
            CompatLog.Warning($"Failed to install Unity callback bridge; Update/GUI/scene callbacks will be unavailable: {ex.Message}");
        }
    }
}
#endif

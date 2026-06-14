#if BEPINEX_MONO
using BepInEx;
using BepInEx.Unity.Mono;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MelonLoader.BepInExCompat;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "dev.jayde.melonloader.bepinex6.mono.compat";
    public const string PluginName = "MelonLoader Compatibility for BepInEx Mono";
    public const string PluginVersion = "0.7.3";

    private int lastSceneBuildIndex = int.MinValue;
    private string lastSceneName = string.Empty;

    private void Awake()
    {
        CompatLog.Initialize(Logger);
        MelonEnvironment.Initialize(Paths.PluginPath, Paths.GameDataPath, Application.unityVersion);
        CompatAssemblyResolver.Install(Paths.PluginPath);
        SceneManager.sceneUnloaded += OnSceneUnloaded;

        CompatLog.Info("MelonLoader 0.5.7-0.7.3 compatibility shim loaded under BepInEx 6 Mono.");
        MelonAssemblyLoader.LoadFromPluginsDirectory(Paths.PluginPath);
        PublishCurrentScene(force: true);
    }

    private void Update()
    {
        PublishCurrentScene(force: false);
        MelonEventPump.Update();
        CompatUnityDriver.PumpManagedCoroutines();
    }

    private void FixedUpdate()
    {
        MelonEventPump.FixedUpdate();
    }

    private void LateUpdate()
    {
        MelonEventPump.LateUpdate();
    }

    private void OnGUI()
    {
        MelonEventPump.GUI();
    }

    private void OnApplicationQuit()
    {
        MelonEventPump.ApplicationQuit();
    }

    private void OnDestroy()
    {
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }

    private void OnSceneUnloaded(Scene scene)
    {
        try
        {
            MelonEventPump.SceneWasUnloaded(scene.buildIndex, scene.name ?? string.Empty);
        }
        catch (Exception ex)
        {
            CompatLog.Warning($"Failed to publish MelonLoader scene-unloaded callback: {ex.Message}");
        }
    }

    private void PublishCurrentScene(bool force)
    {
        try
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return;

            var sceneName = scene.name ?? string.Empty;
            var buildIndex = scene.buildIndex;

            if (!force && buildIndex == lastSceneBuildIndex && sceneName.Equals(lastSceneName, StringComparison.Ordinal))
                return;

            lastSceneBuildIndex = buildIndex;
            lastSceneName = sceneName;

            MelonEventPump.SceneWasLoaded(buildIndex, sceneName);
            MelonEventPump.SceneWasInitialized(buildIndex, sceneName);
        }
        catch (Exception ex)
        {
            CompatLog.Warning($"Failed to publish MelonLoader scene callbacks: {ex.Message}");
        }
    }
}
#endif

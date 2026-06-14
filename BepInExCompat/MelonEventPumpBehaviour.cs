using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MelonLoader.BepInExCompat;

public sealed class MelonEventPumpBehaviour : MonoBehaviour
{
    private static MelonEventPumpBehaviour? instance;

    private int lastSceneBuildIndex = int.MinValue;
    private string lastSceneName = string.Empty;

    public MelonEventPumpBehaviour(IntPtr pointer)
        : base(pointer)
    {
    }

    public void Awake()
    {
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Update()
    {
        PublishCurrentSceneIfChanged(force: false);
        MelonEventPump.Update();
        CompatUnityDriver.PumpManagedCoroutines();
    }

    public void FixedUpdate()
    {
        MelonEventPump.FixedUpdate();
    }

    public void LateUpdate()
    {
        MelonEventPump.LateUpdate();
    }

    public void OnGUI()
    {
        MelonEventPump.GUI();
    }

    public void OnApplicationQuit()
    {
        MelonEventPump.ApplicationQuit();
    }

    internal static void PublishCurrentScene()
    {
        instance?.PublishCurrentSceneIfChanged(force: true);
    }

    private void PublishCurrentSceneIfChanged(bool force)
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

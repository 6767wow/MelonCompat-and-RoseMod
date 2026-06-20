using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace MelonLoader.BepInExCompat;

internal sealed class RoseModMelonSupportComponent : MonoBehaviour
{
    private static RoseModMelonSupportComponent? instance;
    private bool isQuitting;

    public RoseModMelonSupportComponent(IntPtr pointer)
        : base(pointer)
    {
    }

    public static bool Create()
    {
        if (instance is not null)
        {
            CompatLog.Info("Unity MonoBehaviour frame callback bridge is already installed.");
            return true;
        }

        ClassInjector.RegisterTypeInIl2Cpp<RoseModMelonSupportComponent>();

        var gameObject = new GameObject("RoseMod MelonLoader Support");
        gameObject.hideFlags = HideFlags.DontSave;
        UnityEngine.Object.DontDestroyOnLoad(gameObject);
        instance = gameObject.AddComponent(Il2CppType.Of<RoseModMelonSupportComponent>()).TryCast<RoseModMelonSupportComponent>();

        CompatLog.Info("Installed Unity MonoBehaviour frame and scene callback bridge for MelonLoader mods.");
        return instance is not null;
    }

    private void Awake()
    {
        if (instance is null)
            instance = this;

        UnityEngine.Object.DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (instance != this)
            return;

        MelonEventPump.ApplicationLateStart();
        MelonEventPumpBehaviour.PublishCurrentScene();
    }

    private void Update()
    {
        if (instance != this)
            return;

        isQuitting = false;
        MelonEventPumpBehaviour.PumpFrameFromUnityComponent();
    }

    private void FixedUpdate()
    {
        if (instance != this)
            return;

        MelonEventPumpBehaviour.PumpFixedFrameFromUnityComponent();
    }

    private void LateUpdate()
    {
        if (instance != this)
            return;

        MelonEventPumpBehaviour.PumpLateFrameFromUnityComponent();
    }

    private void OnGUI()
    {
        if (instance != this)
            return;

        MelonEventPumpBehaviour.PumpGuiFromUnityComponent();
    }

    private void OnApplicationQuit()
    {
        if (instance != this)
            return;

        isQuitting = true;
        MelonEventPumpBehaviour.PumpQuitFromUnityComponent();
    }

    private void OnDestroy()
    {
        if (instance != this)
            return;

        if (!isQuitting)
        {
            instance = null;
            Create();
        }
    }
}

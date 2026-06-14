namespace MelonLoader;

internal static class MelonEventPump
{
    private static bool started;
    private static bool lateStarted;

    public static void ApplicationStart()
    {
        if (started)
            return;

        started = true;
        MelonEvents.OnApplicationStart.Invoke();

        foreach (var plugin in MelonHandler.Plugins.ToArray())
            plugin.InvokeSafe(nameof(MelonPlugin.OnApplicationStarted), plugin.OnApplicationStarted);

        foreach (var melon in MelonHandler.Mods.Cast<MelonBase>().Concat(MelonHandler.Plugins).ToArray())
            melon.InvokeSafe(nameof(MelonBase.OnApplicationStart), melon.OnApplicationStart);
    }

    public static void ApplicationLateStart()
    {
        if (lateStarted)
            return;

        lateStarted = true;
        MelonEvents.OnApplicationLateStart.Invoke();
        foreach (var melon in MelonHandler.Mods.Cast<MelonBase>().Concat(MelonHandler.Plugins).ToArray())
            melon.InvokeSafe(nameof(MelonBase.OnLateInitializeMelon), melon.OnLateInitializeMelon);
    }

    public static void Update()
    {
        MelonEvents.OnUpdate.Invoke();
        foreach (var melon in MelonHandler.Mods.Cast<MelonBase>().Concat(MelonHandler.Plugins).ToArray())
            melon.InvokeSafe(nameof(MelonBase.OnUpdate), melon.OnUpdate);
    }

    public static void FixedUpdate()
    {
        MelonEvents.OnFixedUpdate.Invoke();
        foreach (var melon in MelonHandler.Mods.Cast<MelonBase>().Concat(MelonHandler.Plugins).ToArray())
            melon.InvokeSafe(nameof(MelonBase.OnFixedUpdate), melon.OnFixedUpdate);
    }

    public static void LateUpdate()
    {
        MelonEvents.OnLateUpdate.Invoke();
        foreach (var melon in MelonHandler.Mods.Cast<MelonBase>().Concat(MelonHandler.Plugins).ToArray())
            melon.InvokeSafe(nameof(MelonBase.OnLateUpdate), melon.OnLateUpdate);
    }

    public static void GUI()
    {
        MelonEvents.OnGUI.Invoke();
        foreach (var melon in MelonHandler.Mods.Cast<MelonBase>().Concat(MelonHandler.Plugins).ToArray())
            melon.InvokeSafe(nameof(MelonBase.OnGUI), melon.OnGUI);
    }

    public static void ApplicationQuit()
    {
        MelonEvents.OnApplicationQuit.Invoke();
        foreach (var melon in MelonHandler.Mods.Cast<MelonBase>().Concat(MelonHandler.Plugins).ToArray())
            melon.InvokeSafe(nameof(MelonBase.OnApplicationQuit), melon.OnApplicationQuit);

        foreach (var melon in MelonHandler.Mods.Cast<MelonBase>().Concat(MelonHandler.Plugins).ToArray())
            melon.InvokeSafe(nameof(MelonBase.OnDeinitializeMelon), melon.OnDeinitializeMelon);

        MelonEvents.OnApplicationDefiniteQuit.Invoke();
    }

    public static void SceneWasLoaded(int buildIndex, string sceneName)
    {
        MelonEvents.OnSceneWasLoaded.Invoke(buildIndex, sceneName);
        foreach (var mod in MelonHandler.Mods.ToArray())
        {
            mod.InvokeSafe(nameof(MelonMod.OnSceneWasLoaded), () => mod.OnSceneWasLoaded(buildIndex, sceneName));
            mod.InvokeSafe(nameof(MelonMod.OnLevelWasLoaded), () => mod.OnLevelWasLoaded(buildIndex));
        }
    }

    public static void SceneWasInitialized(int buildIndex, string sceneName)
    {
        MelonEvents.OnSceneWasInitialized.Invoke(buildIndex, sceneName);
        foreach (var mod in MelonHandler.Mods.ToArray())
        {
            mod.InvokeSafe(nameof(MelonMod.OnSceneWasInitialized), () => mod.OnSceneWasInitialized(buildIndex, sceneName));
            mod.InvokeSafe(nameof(MelonMod.OnLevelWasInitialized), () => mod.OnLevelWasInitialized(buildIndex));
        }
    }

    public static void SceneWasUnloaded(int buildIndex, string sceneName)
    {
        MelonEvents.OnSceneWasUnloaded.Invoke(buildIndex, sceneName);
        foreach (var mod in MelonHandler.Mods.ToArray())
            mod.InvokeSafe(nameof(MelonMod.OnSceneWasUnloaded), () => mod.OnSceneWasUnloaded(buildIndex, sceneName));
    }
}

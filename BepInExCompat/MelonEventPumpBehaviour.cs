using System.Threading;
using System.Reflection;
using HarmonyLib;
using HarmonyX = HarmonyLib.Harmony;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace MelonLoader.BepInExCompat;

public sealed class MelonEventPumpBehaviour : MonoBehaviour
{
    private static MelonEventPumpBehaviour? instance;
    private static bool installed;
    private static bool sceneEventBridgeInstalled;
    private static UnityAction? beforeRenderAction;
    private static UnityAction<Scene, LoadSceneMode>? sceneLoadedAction;
    private static UnityAction<Scene>? sceneUnloadedAction;
    private static PlayerLoopSystem.UpdateFunction? playerLoopUpdateFunction;
    private static Thread? pumpThread;
    private static bool pumpRunning;
    private static int pumpQueued;
    private static int unityEventWarningWritten;
    private static int harmonyPumpWarningWritten;
    private static int firstFramePumpWritten;
    private static int lastPumpedFrame = int.MinValue;
    private static int staticLastSceneBuildIndex = int.MinValue;
    private static string staticLastSceneName = string.Empty;
    private static readonly object SynchronizationPumpGate = new();
    private static HarmonyX? standaloneHarmony;
    private static HarmonyX StandaloneHarmony => standaloneHarmony ??= new HarmonyX("dev.jayde.rosemod.melonloader.eventpump");

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

    internal static void InstallStandalone()
    {
        if (installed)
            return;

        if (!MelonHandler.Mods.Any() && !MelonHandler.Plugins.Any())
        {
            CompatLog.Info("No loaded MelonLoader mods; Unity callback bridge was not installed.");
            return;
        }

        if (IsStandaloneBridgeDisabled())
        {
            CompatLog.Warning("Unity callback bridge is disabled for this launch by RoseMod/UserData/disable-event-bridge.txt.");
            return;
        }

        installed = true;
        try
        {
            if (TryInstallUnityEventBridge())
                return;

            if (TryInstallSynchronizationContextPump())
                return;

            if (TryInstallPlayerLoopBridge())
                return;

            if (TryInstallInjectedBehaviour())
                return;

            if (TryInstallManagedPatchPump())
                return;

            if (IsHarmonyPumpEnabled() && TryInstallHarmonyPump())
                return;

            if (sceneEventBridgeInstalled)
            {
                CompatLog.Warning("Installed MelonLoader scene callbacks, but no frame/update callback bridge was available.");
                return;
            }

            installed = false;
            CompatLog.Warning("Could not install a Unity callback bridge for MelonLoader scene/update callbacks.");
        }
        catch (Exception ex)
        {
            installed = false;
            CompatLog.Warning($"Failed to install Unity application callback bridge: {ex.Message}");
        }
    }

    private static bool TryInstallInjectedBehaviour()
    {
        try
        {
            return RoseModMelonSupportComponent.Create();
        }
        catch (Exception ex)
        {
            CompatLog.Warning($"Failed to install Unity MonoBehaviour callback bridge: {ex.Message}");
            return false;
        }
    }

    private static bool IsStandaloneBridgeDisabled()
    {
        var value = FirstEnvironmentValue("ROSEMOD_DISABLE_EVENT_BRIDGE", "MELONCOMPAT_DISABLE_EVENT_BRIDGE");
        if (IsTruthy(value))
        {
            return true;
        }

        return UserDataFileExists("disable-event-bridge.txt");
    }

    private static bool IsInjectedBridgeEnabled()
    {
        var value = FirstEnvironmentValue("ROSEMOD_ENABLE_INJECTED_EVENT_PUMP", "MELONCOMPAT_ENABLE_INJECTED_EVENT_PUMP");
        return IsTruthy(value) || UserDataFileExists("enable-injected-event-pump.txt");
    }

    private static bool IsHarmonyPumpEnabled()
    {
        var value = FirstEnvironmentValue("ROSEMOD_ENABLE_HARMONY_EVENT_PUMP", "MELONCOMPAT_ENABLE_HARMONY_EVENT_PUMP");
        if (IsTruthy(value))
        {
            return true;
        }

        return UserDataFileExists("enable-harmony-event-pump.txt");
    }

    private static bool IsPlayerLoopBridgeEnabled()
    {
        var value = FirstEnvironmentValue("ROSEMOD_ENABLE_PLAYERLOOP_EVENT_PUMP", "MELONCOMPAT_ENABLE_PLAYERLOOP_EVENT_PUMP");
        return IsTruthy(value) || UserDataFileExists("enable-playerloop-event-pump.txt");
    }

    private static bool TryInstallSynchronizationContextPump()
    {
        lock (SynchronizationPumpGate)
        {
            if (pumpRunning && pumpThread?.IsAlive == true)
                return true;

            var context = SynchronizationContext.Current;
            if (context is null)
                return false;

            var contextType = context.GetType();
            if (contextType == typeof(SynchronizationContext))
                return false;

            pumpRunning = true;
            pumpThread = new Thread(() => PumpLoop(context))
            {
                IsBackground = true,
                Name = "RoseMod MelonLoader Event Pump"
            };
            pumpThread.Start();
            QueuePump(context);
            CompatLog.Info("Installed Unity synchronization-context frame and scene callback bridge for MelonLoader mods.");
            return true;
        }
    }

    private static bool TryInstallPlayerLoopBridge()
    {
        try
        {
            playerLoopUpdateFunction ??= DelegateSupport.ConvertDelegate<PlayerLoopSystem.UpdateFunction>((Action)OnPlayerLoopUpdate);
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (PlayerLoopContainsRoseModPump(loop))
            {
                CompatLog.Info("Unity PlayerLoop frame callback bridge is already installed.");
                return true;
            }

            if (!TryAppendPlayerLoopPump(ref loop))
                return false;

            PlayerLoop.SetPlayerLoop(loop);
            CompatLog.Info("Installed Unity PlayerLoop frame and scene callback bridge for MelonLoader mods.");
            return true;
        }
        catch (Exception ex)
        {
            CompatLog.Warning($"Failed to install Unity PlayerLoop callback bridge: {ex.Message}");
            return false;
        }
    }

    private static void OnPlayerLoopUpdate()
    {
        OnBeforeRender();
    }

    private static bool TryAppendPlayerLoopPump(ref PlayerLoopSystem loop)
    {
        var updateType = Il2CppType.Of<UnityEngine.PlayerLoop.Update>();
        if (TryAppendPlayerLoopPump(ref loop, updateType))
            return true;

        var rootSystems = ToManagedArray(loop.subSystemList);
        if (rootSystems.Length == 0)
            return false;

        Array.Resize(ref rootSystems, rootSystems.Length + 1);
        rootSystems[rootSystems.Length - 1] = CreatePlayerLoopPumpSystem();
        loop.subSystemList = new Il2CppReferenceArray<PlayerLoopSystem>(rootSystems);
        return true;
    }

    private static bool TryAppendPlayerLoopPump(ref PlayerLoopSystem loop, Il2CppSystem.Type targetType)
    {
        var systems = ToManagedArray(loop.subSystemList);
        for (var i = 0; i < systems.Length; i++)
        {
            var child = systems[i];
            if (SameIl2CppType(child.type, targetType))
            {
                var childSystems = ToManagedArray(child.subSystemList);
                Array.Resize(ref childSystems, childSystems.Length + 1);
                childSystems[childSystems.Length - 1] = CreatePlayerLoopPumpSystem();
                child.subSystemList = new Il2CppReferenceArray<PlayerLoopSystem>(childSystems);
                systems[i] = child;
                loop.subSystemList = new Il2CppReferenceArray<PlayerLoopSystem>(systems);
                return true;
            }

            if (TryAppendPlayerLoopPump(ref child, targetType))
            {
                systems[i] = child;
                loop.subSystemList = new Il2CppReferenceArray<PlayerLoopSystem>(systems);
                return true;
            }
        }

        return false;
    }

    private static PlayerLoopSystem CreatePlayerLoopPumpSystem()
    {
        return new PlayerLoopSystem
        {
            type = Il2CppType.Of<UnityEngine.PlayerLoop.Update>(),
            updateDelegate = playerLoopUpdateFunction
        };
    }

    private static bool PlayerLoopContainsRoseModPump(PlayerLoopSystem loop)
    {
        if (loop.updateDelegate is not null && playerLoopUpdateFunction is not null && loop.updateDelegate.Equals(playerLoopUpdateFunction))
            return true;

        foreach (var child in ToManagedArray(loop.subSystemList))
        {
            if (PlayerLoopContainsRoseModPump(child))
                return true;
        }

        return false;
    }

    private static PlayerLoopSystem[] ToManagedArray(Il2CppReferenceArray<PlayerLoopSystem>? systems)
    {
        if (systems is null)
            return Array.Empty<PlayerLoopSystem>();

        var values = new PlayerLoopSystem[systems.Length];
        for (var i = 0; i < values.Length; i++)
            values[i] = systems[i];
        return values;
    }

    private static bool SameIl2CppType(Il2CppSystem.Type? left, Il2CppSystem.Type? right)
    {
        if (left is null || right is null)
            return false;

        try
        {
            return left.FullName == right.FullName;
        }
        catch
        {
            return left.Equals(right);
        }
    }

    private static bool TryInstallManagedPatchPump()
    {
        try
        {
            var postfix = typeof(MelonEventPumpBehaviour).GetMethod(nameof(PumpFromHarmonyCallback), BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException(typeof(MelonEventPumpBehaviour).FullName, nameof(PumpFromHarmonyCallback));
            var harmonyPostfix = new HarmonyMethod(postfix);
            var patchedCount = 0;

            foreach (var melon in MelonHandler.Mods.Concat<MelonBase>(MelonHandler.Plugins))
            {
                var assembly = melon.MelonAssembly?.Assembly;
                if (assembly is null)
                    continue;

                foreach (var method in FindManagedHarmonyPatchMethods(assembly))
                {
                    if (!IsManagedPatchMethodSafeForCallbackPump(method, out var skipReason))
                        continue;

                    try
                    {
                        StandaloneHarmony.Patch(method, postfix: harmonyPostfix);
                        patchedCount++;
                    }
                    catch (Exception ex)
                    {
                        CompatLog.Warning($"Failed to piggyback Unity callback bridge on managed patch {method.DeclaringType?.FullName}.{method.Name}: {ex.Message}");
                    }
                }
            }

            if (patchedCount <= 0)
                return false;

            CompatLog.Info($"Installed managed Harmony patch callback bridge for MelonLoader mods on {patchedCount} already-patched method(s).");
            return true;
        }
        catch (Exception ex)
        {
            CompatLog.Warning($"Failed to install managed Harmony patch callback bridge: {ex.Message}");
            return false;
        }
    }

    private static IEnumerable<MethodInfo> FindManagedHarmonyPatchMethods(Assembly assembly)
    {
        foreach (var type in GetLoadableTypes(assembly))
        {
            if (!HasHarmonyPatchAttribute(type))
                continue;

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (method.ContainsGenericParameters)
                    continue;

                if (method.Name is "Prefix" or "Postfix" or "Finalizer")
                    yield return method;
            }
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null)!;
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }

    private static bool IsManagedPatchMethodSafeForCallbackPump(MethodInfo method, out string reason)
    {
        var declaringTypeName = method.DeclaringType?.FullName ?? string.Empty;
        if (declaringTypeName.IndexOf("DropDown", StringComparison.OrdinalIgnoreCase) >= 0
            || declaringTypeName.IndexOf("dropDownOptions", StringComparison.OrdinalIgnoreCase) >= 0
            || declaringTypeName.IndexOf("AudioSource", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            reason = "dropdown and audio patch callbacks can re-enter fragile Unity native code.";
            return false;
        }

        foreach (var parameter in method.GetParameters())
        {
            var parameterType = parameter.ParameterType;
            if (parameterType.IsByRef || parameterType.IsPointer)
            {
                reason = $"parameter {parameter.Name} uses by-ref or pointer state.";
                return false;
            }

            var parameterTypeName = parameterType.FullName ?? parameterType.Name;
            if (parameterTypeName.StartsWith("Il2Cpp.", StringComparison.Ordinal)
                || parameterTypeName.IndexOf(".Il2Cpp.", StringComparison.Ordinal) >= 0
                || parameterTypeName.IndexOf("dropDownOptions", StringComparison.OrdinalIgnoreCase) >= 0
                || parameterTypeName.IndexOf("UnityEngine.AudioSource", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                reason = $"parameter {parameter.Name} uses fragile native Unity type {parameterTypeName}.";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static bool HasHarmonyPatchAttribute(MemberInfo member)
    {
        try
        {
            return member.GetCustomAttributes(inherit: false)
                .Any(attribute => attribute.GetType().FullName == "HarmonyLib.HarmonyPatch"
                    || attribute.GetType().FullName == "HarmonyLib.HarmonyPatchAttribute"
                    || attribute.GetType().FullName == "Harmony.HarmonyPatch"
                    || attribute.GetType().FullName == "Harmony.HarmonyPatchAttribute");
        }
        catch
        {
            return false;
        }
    }

    private static bool TryInstallHarmonyPump()
    {
        try
        {
            var timeType = FindUnityType("UnityEngine.Time")
                ?? throw new TypeLoadException("UnityEngine.Time");
            var target = timeType.GetMethod("get_deltaTime", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null)
                ?? throw new MissingMethodException(timeType.FullName, "get_deltaTime");
            var postfix = typeof(MelonEventPumpBehaviour).GetMethod(nameof(PumpFromHarmonyCallback), BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException(typeof(MelonEventPumpBehaviour).FullName, nameof(PumpFromHarmonyCallback));

            StandaloneHarmony.Patch(target, postfix: new HarmonyMethod(postfix));
            CompatLog.Info("Installed Harmony Time.deltaTime scene/update callback bridge for MelonLoader mods.");
            return true;
        }
        catch (Exception ex)
        {
            CompatLog.Warning($"Failed to install Harmony callback bridge: {ex.Message}");
            return false;
        }
    }

    private static Type? FindUnityType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var type = assembly.GetType(fullName, throwOnError: false);
                if (type is not null)
                    return type;
            }
            catch
            {
            }
        }

        return Type.GetType(fullName + ", UnityEngine.AudioModule", throwOnError: false)
            ?? Type.GetType(fullName + ", UnityEngine.CoreModule", throwOnError: false);
    }

    private static bool TryInstallUnityEventBridge()
    {
        var sceneLoadedSubscribed = false;
        var sceneUnloadedSubscribed = false;

        try
        {
            beforeRenderAction ??= DelegateSupport.ConvertDelegate<UnityAction>((Action)OnBeforeRender);
            sceneLoadedAction ??= DelegateSupport.ConvertDelegate<UnityAction<Scene, LoadSceneMode>>((Action<Scene, LoadSceneMode>)OnSceneLoaded);
            sceneUnloadedAction ??= DelegateSupport.ConvertDelegate<UnityAction<Scene>>((Action<Scene>)OnSceneUnloaded);

            SceneManager.sceneLoaded += sceneLoadedAction;
            sceneLoadedSubscribed = true;
            SceneManager.sceneUnloaded += sceneUnloadedAction;
            sceneUnloadedSubscribed = true;

            try
            {
                Application.add_onBeforeRender(beforeRenderAction);
            }
            catch (Exception ex)
            {
                sceneEventBridgeInstalled = true;
                CompatLog.Info($"Unity frame callback bridge is unavailable; keeping scene callbacks and trying fallback update bridge: {ex.Message}");
                return false;
            }

            sceneEventBridgeInstalled = true;
            CompatLog.Info("Installed Unity event frame and scene callback bridge for MelonLoader mods.");
            return true;
        }
        catch (Exception ex)
        {
            TryUninstallUnityEventBridge(sceneLoadedSubscribed, sceneUnloadedSubscribed);
            CompatLog.Warning($"Failed to install Unity event callback bridge: {ex.Message}");
            return false;
        }
    }

    private static void TryUninstallUnityEventBridge(bool sceneLoadedSubscribed, bool sceneUnloadedSubscribed)
    {
        try
        {
            if (sceneUnloadedSubscribed && sceneUnloadedAction is not null)
                SceneManager.sceneUnloaded -= sceneUnloadedAction;
        }
        catch
        {
        }

        try
        {
            if (sceneLoadedSubscribed && sceneLoadedAction is not null)
                SceneManager.sceneLoaded -= sceneLoadedAction;
        }
        catch
        {
        }
    }

    private static void OnBeforeRender()
    {
        try
        {
            var frame = GetFrameCountSafe();
            if (frame >= 0 && frame == lastPumpedFrame)
                return;

            lastPumpedFrame = frame;
            PumpFromApplicationCallback();
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref unityEventWarningWritten, 1) == 0)
                CompatLog.Warning($"Unity event callback bridge failed; suppressing repeat warnings: {ex.Message}");
        }
    }

    private static void PumpFromHarmonyCallback()
    {
        try
        {
            TryInstallSynchronizationContextPump();

            var frame = GetFrameCountSafe();
            if (frame >= 0 && frame == lastPumpedFrame)
                return;

            lastPumpedFrame = frame;
            PumpFromApplicationCallback();
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref harmonyPumpWarningWritten, 1) == 0)
                CompatLog.Warning($"Harmony callback bridge failed; suppressing repeat warnings: {ex.Message}");
        }
    }

    internal static void InstallTargetMethodPump(IEnumerable<MethodBase> targets, string sourceName)
    {
        if (!IsTargetMethodPumpEnabled())
            return;

        var postfix = typeof(MelonEventPumpBehaviour).GetMethod(nameof(PumpFromHarmonyCallback), BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(typeof(MelonEventPumpBehaviour).FullName, nameof(PumpFromHarmonyCallback));
        var harmonyPostfix = new HarmonyMethod(postfix);
        var installed = 0;

        foreach (var target in targets)
        {
            if (!IsSafeTargetMethodPumpTarget(target, out var skipReason))
                continue;

            if (target.ContainsGenericParameters || !RememberPumpTarget(target))
                continue;

            try
            {
                StandaloneHarmony.Patch(target, postfix: harmonyPostfix);
                installed++;
            }
            catch (Exception ex)
            {
                CompatLog.Warning($"Failed to install target-method callback bridge on {target.DeclaringType?.FullName}.{target.Name}: {ex.Message}");
            }
        }

        if (installed > 0)
            CompatLog.Info($"Installed target-method callback bridge for {sourceName} on {installed} original method(s).");
    }

    private static bool IsTargetMethodPumpEnabled()
    {
        var value = FirstEnvironmentValue("ROSEMOD_ENABLE_TARGET_METHOD_EVENT_PUMP", "MELONCOMPAT_ENABLE_TARGET_METHOD_EVENT_PUMP");
        return IsTruthy(value) || UserDataFileExists("enable-target-method-event-pump.txt");
    }

    private static bool IsSafeTargetMethodPumpTarget(MethodBase? target, out string reason)
    {
        if (target is null)
        {
            reason = string.Empty;
            return false;
        }

        var declaringTypeName = target.DeclaringType?.FullName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(declaringTypeName) || declaringTypeName.StartsWith("DMD<", StringComparison.Ordinal))
        {
            reason = "generated Harmony methods are not stable frame-pump targets.";
            return false;
        }

        if (!target.Name.Equals("Update", StringComparison.Ordinal))
        {
            reason = "only parameterless Update methods are used as default frame-pump targets.";
            return false;
        }

        if (target.GetParameters().Length != 0)
        {
            reason = "callback target has parameters.";
            return false;
        }

        if (declaringTypeName.StartsWith("UnityEngine.", StringComparison.Ordinal)
            || declaringTypeName.IndexOf("AudioSource", StringComparison.OrdinalIgnoreCase) >= 0
            || declaringTypeName.IndexOf("dropDownOptions", StringComparison.OrdinalIgnoreCase) >= 0
            || declaringTypeName.IndexOf("soundEffects", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            reason = "target is a fragile Unity/native helper that can crash when bridged.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static readonly object PumpTargetsGate = new();
    private static readonly HashSet<string> PumpTargets = new(StringComparer.Ordinal);

    private static bool RememberPumpTarget(MethodBase target)
    {
        var key = BuildPumpTargetKey(target);
        lock (PumpTargetsGate)
            return PumpTargets.Add(key);
    }

    private static string BuildPumpTargetKey(MethodBase target)
    {
        try
        {
            return $"{target.Module.ModuleVersionId:N}:{target.MetadataToken}";
        }
        catch
        {
            var parameters = string.Join(",", target.GetParameters().Select(parameter => parameter.ParameterType.FullName ?? parameter.ParameterType.Name));
            return $"{target.DeclaringType?.AssemblyQualifiedName}|{target.Name}|{parameters}";
        }
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        try
        {
            TryInstallSynchronizationContextPump();
            PublishScene(scene, force: true);
            PumpFromApplicationCallback();
        }
        catch (Exception ex)
        {
            CompatLog.Warning($"Failed to publish MelonLoader scene-loaded callback: {ex.Message}");
        }
    }

    private static void OnSceneUnloaded(Scene scene)
    {
        try
        {
            if (TryReadScene(scene, out var buildIndex, out var sceneName))
                MelonEventPump.SceneWasUnloaded(buildIndex, sceneName);
        }
        catch (Exception ex)
        {
            CompatLog.Warning($"Failed to publish MelonLoader scene-unloaded callback: {ex.Message}");
        }
    }

    private static int GetFrameCountSafe()
    {
        try
        {
            return Time.frameCount;
        }
        catch
        {
            return -1;
        }
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
        if (instance is not null)
            instance.PublishCurrentSceneIfChanged(force: true);
        else
            PublishCurrentSceneIfChangedStatic(force: true);
    }

    private static void PumpFromApplicationCallback()
    {
        TryInstallSynchronizationContextPump();
        if (Interlocked.Exchange(ref firstFramePumpWritten, 1) == 0)
            CompatLog.Info("MelonLoader frame pump is running.");

        PublishCurrentSceneIfChangedStatic(force: false);
        SkippedHarmonyPatchSubstitutes.Pump();
        MelonEventPump.Update();
        MelonEventPump.LateUpdate();
        CompatUnityDriver.PumpManagedCoroutines();
    }

    internal static void PumpFrameFromUnityComponent()
    {
        PublishCurrentSceneIfChangedStatic(force: false);
        SkippedHarmonyPatchSubstitutes.Pump();
        MelonEventPump.Update();
        CompatUnityDriver.PumpManagedCoroutines();
    }

    internal static void PumpFixedFrameFromUnityComponent()
    {
        MelonEventPump.FixedUpdate();
    }

    internal static void PumpLateFrameFromUnityComponent()
    {
        MelonEventPump.LateUpdate();
    }

    internal static void PumpGuiFromUnityComponent()
    {
        MelonEventPump.GUI();
    }

    internal static void PumpQuitFromUnityComponent()
    {
        MelonEventPump.ApplicationQuit();
    }

    private static void PumpLoop(SynchronizationContext context)
    {
        while (pumpRunning)
        {
            QueuePump(context);
            Thread.Sleep(16);
        }
    }

    private static void QueuePump(SynchronizationContext context)
    {
        if (Interlocked.Exchange(ref pumpQueued, 1) != 0)
            return;

        context.Post(_ =>
        {
            try
            {
                PumpFromApplicationCallback();
            }
            finally
            {
                Volatile.Write(ref pumpQueued, 0);
            }
        }, null);
    }

    private void PublishCurrentSceneIfChanged(bool force)
    {
        try
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return;

            if (!TryReadScene(scene, out var buildIndex, out var sceneName))
                return;

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

    private static void PublishCurrentSceneIfChangedStatic(bool force)
    {
        try
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return;

            PublishScene(scene, force);
        }
        catch (Exception ex)
        {
            CompatLog.Warning($"Failed to publish MelonLoader scene callbacks: {ex.Message}");
        }
    }

    private static void PublishScene(Scene scene, bool force)
    {
        if (!TryReadScene(scene, out var buildIndex, out var sceneName))
            return;

        if (!force && buildIndex == staticLastSceneBuildIndex && sceneName.Equals(staticLastSceneName, StringComparison.Ordinal))
            return;

        staticLastSceneBuildIndex = buildIndex;
        staticLastSceneName = sceneName;

        CompatLog.Info($"Published MelonLoader scene callbacks for '{sceneName}' (build {buildIndex}).");

        MelonEventPump.SceneWasLoaded(buildIndex, sceneName);
        MelonEventPump.SceneWasInitialized(buildIndex, sceneName);
    }

    private static bool TryReadScene(Scene scene, out int buildIndex, out string sceneName)
    {
        buildIndex = -1;
        sceneName = string.Empty;
        if (!scene.IsValid())
            return false;

        sceneName = scene.name ?? string.Empty;
        buildIndex = scene.buildIndex;
        return true;
    }

    private static string? FirstEnvironmentValue(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static bool IsTruthy(string? value)
    {
        return value is not null
            && (value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || value.Equals("on", StringComparison.OrdinalIgnoreCase));
    }

    private static bool UserDataFileExists(string fileName)
    {
        try
        {
            return File.Exists(Path.Combine(Environment.CurrentDirectory, "RoseMod", "UserData", fileName))
                || File.Exists(Path.Combine(Environment.CurrentDirectory, "MelonCompat", "UserData", fileName));
        }
        catch
        {
            return false;
        }
    }
}

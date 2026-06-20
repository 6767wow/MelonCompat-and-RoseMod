using System.Collections;
using System.Reflection;
#if !BEPINEX_MONO
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
#endif

namespace MelonLoader.BepInExCompat;

internal static class SkippedHarmonyPatchSubstitutes
{
#if BEPINEX_MONO
    public static bool TryRegister(Type patchType, string skipReason, out string substituteReason)
    {
        substituteReason = string.Empty;
        return false;
    }

    public static void Pump()
    {
    }
#else
    private const float GrannyActivitySweepInterval = 2.0f;
    private const float DayTransitionSweepInterval = 0.25f;

    private static readonly object Gate = new();
    private static readonly List<MethodInfo> grannyActivityPostfixes = new();
    private static readonly HashSet<string> registered = new(StringComparer.Ordinal);

    private static float nextGrannyActivitySweep;
    private static float nextDayTransitionSweep;
    private static int lastPumpFrame = int.MinValue;
    private static Type? enemyAIGrannyType;
    private static Type? endDayType;
    private static Type? startNewDayType;
    private static bool dayTransitionWatcherRegistered;
    private static bool lastEndDayActive;
    private static bool haveLastStartNewDayCounter;
    private static float lastStartNewDayCounter;
    private static int lastDayTransitionSceneBuildIndex = int.MinValue;

    public static bool TryRegister(Type patchType, string skipReason, out string substituteReason)
    {
        substituteReason = string.Empty;
        var fullName = patchType.FullName ?? patchType.Name;

        lock (Gate)
        {
            if (!registered.Add(fullName))
            {
                substituteReason = "already registered a controlled substitute.";
                return true;
            }

            if (fullName.Equals("BaldiHelpsGranny.AudioSourcePlayPatch", StringComparison.Ordinal))
            {
                substituteReason = "menu music is handled by the mod update loop; no UnityEngine.AudioSource.Play detour or substitute polling is installed.";
                return true;
            }

            if (fullName.Equals("BaldiHelpsGranny.DropDownOptionsStartPatch", StringComparison.Ordinal)
                || fullName.Equals("BaldiHelpsGranny.DropDownOptionsDiffOptionsPatch", StringComparison.Ordinal))
            {
                substituteReason = "difficulty is handled by the mod update loop; no dropDownOptions detour or substitute polling is installed.";
                return true;
            }

            if (fullName.Equals("BaldiHelpsGranny.EnemyAIGrannyDecisionsPatch", StringComparison.Ordinal)
                || fullName.Equals("BaldiHelpsGranny.EnemyAIGrannyFollowPlayerPatch", StringComparison.Ordinal)
                || fullName.Equals("BaldiHelpsGranny.EnemyAIGrannyNewNavPatch", StringComparison.Ordinal))
            {
                var method = patchType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(static candidate => candidate.Name == "Postfix" && candidate.GetParameters().Length == 1);
                if (method is null)
                    return false;

                if (grannyActivityPostfixes.Count == 0)
                    grannyActivityPostfixes.Add(method);
                enemyAIGrannyType ??= method.GetParameters()[0].ParameterType;
                substituteReason = "controlled low-frequency EnemyAIGranny sweep refreshes the mod's Granny reference without detouring Granny Unity 6 AI methods.";
                return true;
            }

            if (fullName.Equals("BaldiHelpsGranny.SoundEffectsGunShootPatch", StringComparison.Ordinal))
            {
                substituteReason = "gun hit handling is covered by GunShootHandleHitPatch; no soundEffects.GunShoot detour or substitute polling is installed.";
                return true;
            }

            if (fullName.Equals("BaldiHelpsGranny.ShootGunUpdatePatch", StringComparison.Ordinal))
            {
                substituteReason = "gun hit handling is covered by GunShootHandleHitPatch; no hot shootGun.Update native detour is installed.";
                return true;
            }

            if (fullName.Equals("BaldiHelpsGranny.SoundEffectsPlayerCaughtPatch", StringComparison.Ordinal)
                || fullName.Equals("BaldiHelpsGranny.SoundEffectsPlayerCaughtNightmarePatch", StringComparison.Ordinal))
            {
                substituteReason = "catch flow keeps the game's original sound handling; no soundEffects catch native detour is installed.";
                return true;
            }

            if (fullName.Equals("BaldiHelpsGranny.EndDayPatch", StringComparison.Ordinal)
                || fullName.Equals("BaldiHelpsGranny.StartNewDayPatch", StringComparison.Ordinal))
            {
                dayTransitionWatcherRegistered = true;
                substituteReason = "day-transition notifications are injected at safe StartCoroutine call sites and mirrored from Granny day-cycle state; no endDay/startNewDay coroutine detour is installed.";
                return true;
            }
        }

        return false;
    }

    public static void Pump()
    {
        if (!HasSubstitutes())
            return;

        var frame = GetFrameCountSafe();
        if (frame >= 0 && frame == lastPumpFrame)
            return;

        if (frame >= 0)
            lastPumpFrame = frame;

        var now = GetTimeSafe();
        if (now >= nextGrannyActivitySweep)
        {
            nextGrannyActivitySweep = now + GrannyActivitySweepInterval;
            SweepEnemyAIGranny();
        }

        if (now >= nextDayTransitionSweep)
        {
            nextDayTransitionSweep = now + DayTransitionSweepInterval;
            SweepBaldiDayTransitions();
        }
    }

    private static bool HasSubstitutes()
    {
        lock (Gate)
            return grannyActivityPostfixes.Count > 0 || dayTransitionWatcherRegistered;
    }

    private static void SweepEnemyAIGranny()
    {
        Type? targetType;
        MethodInfo[] methods;
        lock (Gate)
        {
            targetType = enemyAIGrannyType;
            methods = grannyActivityPostfixes.ToArray();
        }

        if (targetType is null || methods.Length == 0)
            return;

        var method = methods[0];
        foreach (var instance in FindObjects(targetType))
            InvokeSafely(method, CoerceIl2CppObject(instance, method.GetParameters()[0].ParameterType));
    }

    private static void SweepBaldiDayTransitions()
    {
        if (!IsDayTransitionWatcherRegistered())
            return;

        ResetDayTransitionBaselineForSceneChange();

        var endDayActive = IsAnyEndDayActive();
        if (endDayActive && !lastEndDayActive)
            UnityPhysicsCompatibility.NotifyBaldiDayTransition("endDay.EndDay");
        else if (!endDayActive && lastEndDayActive)
            UnityPhysicsCompatibility.NotifyBaldiDayTransition("startNewDay.newDay");

        lastEndDayActive = endDayActive;

        if (!TryReadStartNewDayCounter(out var currentCounter))
        {
            haveLastStartNewDayCounter = false;
            return;
        }

        if (!haveLastStartNewDayCounter)
        {
            haveLastStartNewDayCounter = true;
            lastStartNewDayCounter = currentCounter;
            return;
        }

        if (Math.Abs(currentCounter - lastStartNewDayCounter) <= 0.01f)
            return;

        lastStartNewDayCounter = currentCounter;
        UnityPhysicsCompatibility.NotifyBaldiDayTransition("startNewDay.newDay");
    }

    private static bool IsDayTransitionWatcherRegistered()
    {
        lock (Gate)
            return dayTransitionWatcherRegistered;
    }

    private static void ResetDayTransitionBaselineForSceneChange()
    {
        int buildIndex;
        try
        {
            var scene = SceneManager.GetActiveScene();
            buildIndex = scene.IsValid() ? scene.buildIndex : int.MinValue;
        }
        catch
        {
            return;
        }

        if (buildIndex == lastDayTransitionSceneBuildIndex)
            return;

        lastDayTransitionSceneBuildIndex = buildIndex;
        lastEndDayActive = false;
        haveLastStartNewDayCounter = false;
        lastStartNewDayCounter = 0f;
    }

    private static bool IsAnyEndDayActive()
    {
        endDayType ??= FindLoadedType("endDay", "Il2Cpp.endDay");
        if (endDayType is null)
            return false;

        foreach (var instance in FindObjects(endDayType))
        {
            if (TryReadBoolMember(instance, "enDayStart", out var active) && active)
                return true;
        }

        return false;
    }

    private static bool TryReadStartNewDayCounter(out float counter)
    {
        counter = 0f;
        startNewDayType ??= FindLoadedType("startNewDay", "Il2Cpp.startNewDay");
        if (startNewDayType is null)
            return false;

        foreach (var instance in FindObjects(startNewDayType))
        {
            if (TryReadFloatMember(instance, "daysCounter", out counter))
                return true;
        }

        return false;
    }

    private static IEnumerable<UnityEngine.Object> FindObjects(Type targetType)
    {
        Il2CppSystem.Type il2CppType;
        try
        {
            il2CppType = Il2CppType.From(targetType);
        }
        catch (Exception ex)
        {
            CompatLog.Warning($"Deferred substitute object sweep for {targetType.FullName}: {ex.Message}");
            yield break;
        }

        IEnumerable? objects;
        try
        {
            objects = UnityEngine.Object.FindObjectsOfType(il2CppType);
        }
        catch (Exception ex)
        {
            CompatLog.Warning($"Failed substitute object sweep for {targetType.FullName}: {ex.Message}");
            yield break;
        }

        if (objects is null)
            yield break;

        foreach (var item in objects)
        {
            if (item is UnityEngine.Object unityObject && unityObject != null)
                yield return unityObject;
        }
    }

    private static object? CoerceIl2CppObject(UnityEngine.Object? value, Type targetType)
    {
        if (value is null)
            return null;

        if (targetType.IsInstanceOfType(value))
            return value;

        try
        {
            var pointerProperty = value.GetType().GetProperty("Pointer", BindingFlags.Public | BindingFlags.Instance)
                ?? value.GetType().BaseType?.GetProperty("Pointer", BindingFlags.Public | BindingFlags.Instance);
            var pointer = pointerProperty?.GetValue(value);
            var constructor = targetType.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(IntPtr) }, null);
            if (pointer is IntPtr address && address != IntPtr.Zero && constructor is not null)
                return constructor.Invoke(new object[] { address });
        }
        catch (Exception ex)
        {
            CompatLog.Warning($"Failed to coerce {value.GetType().FullName} to {targetType.FullName}: {ex.Message}");
        }

        return value;
    }

    private static Type? FindLoadedType(params string[] fullNames)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var fullName in fullNames)
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
        }

        return null;
    }

    private static bool TryReadBoolMember(object value, string memberName, out bool result)
    {
        result = false;
        if (TryReadMemberValue(value, memberName, out var raw))
        {
            if (raw is bool boolResult)
            {
                result = boolResult;
                return true;
            }

            try
            {
                result = Convert.ToBoolean(raw);
                return true;
            }
            catch
            {
            }
        }

        return false;
    }

    private static bool TryReadFloatMember(object value, string memberName, out float result)
    {
        result = 0f;
        if (TryReadMemberValue(value, memberName, out var raw))
        {
            if (raw is float floatResult)
            {
                result = floatResult;
                return true;
            }

            try
            {
                result = Convert.ToSingle(raw);
                return true;
            }
            catch
            {
            }
        }

        return false;
    }

    private static bool TryReadMemberValue(object value, string memberName, out object? result)
    {
        result = null;
        try
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = value.GetType();
            var property = type.GetProperty(memberName, flags);
            if (property is not null)
            {
                result = property.GetValue(value);
                return true;
            }

            var field = type.GetField(memberName, flags);
            if (field is not null)
            {
                result = field.GetValue(value);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static void InvokeSafely(MethodInfo method, params object?[] args)
    {
        try
        {
            if (args.Any(static arg => arg is null))
                return;

            method.Invoke(null, args);
        }
        catch (TargetInvocationException ex)
        {
            CompatLog.Warning($"Substitute callback {method.DeclaringType?.FullName}.{method.Name} failed: {ex.InnerException?.Message ?? ex.Message}");
        }
        catch (Exception ex)
        {
            CompatLog.Warning($"Substitute callback {method.DeclaringType?.FullName}.{method.Name} failed: {ex.Message}");
        }
    }

    private static float GetTimeSafe()
    {
        try
        {
            return Time.realtimeSinceStartup;
        }
        catch
        {
            return Environment.TickCount / 1000f;
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
#endif
}

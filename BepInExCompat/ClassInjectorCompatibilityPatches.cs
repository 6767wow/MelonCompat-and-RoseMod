using System.Reflection;
using HarmonyLib;

namespace MelonLoader.BepInExCompat;

internal static class ClassInjectorCompatibilityPatches
{
    public static void Install()
    {
        try
        {
            var classInjectorType = Type.GetType("Il2CppInterop.Runtime.Injection.ClassInjector, Il2CppInterop.Runtime");
            var harmony = new Harmony($"{Plugin.PluginGuid}.classinjector");

            var patched = PatchEligibilityMethod<FieldInfo>(
                harmony,
                classInjectorType,
                "IsFieldEligible",
                nameof(IsFieldEligibleFinalizer));

            patched |= PatchEligibilityMethod<MethodInfo>(
                harmony,
                classInjectorType,
                "IsMethodEligible",
                nameof(IsMethodEligibleFinalizer));

            if (patched)
                CompatLog.Info("Installed Il2CppInterop ClassInjector missing-member compatibility patch.");
            else
                CompatLog.Warning("Could not find Il2CppInterop ClassInjector eligibility methods; missing-member compatibility patch was not installed.");
        }
        catch (Exception ex)
        {
            CompatLog.Warning($"Failed to install ClassInjector compatibility patch: {ex.Message}");
        }
    }

    private static bool PatchEligibilityMethod<TMember>(Harmony harmony, Type? classInjectorType, string methodName, string finalizerName)
        where TMember : MemberInfo
    {
        var target = classInjectorType?.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        var finalizer = typeof(ClassInjectorCompatibilityPatches).GetMethod(finalizerName, BindingFlags.NonPublic | BindingFlags.Static);

        if (target is null || finalizer is null)
            return false;

        harmony.Patch(target, finalizer: new HarmonyMethod(finalizer));
        return true;
    }

    private static Exception? IsFieldEligibleFinalizer(Exception __exception, ref bool __result, FieldInfo field)
    {
        return HandleMissingMemberType(__exception, ref __result, field, "field", "field type");
    }

    private static Exception? IsMethodEligibleFinalizer(Exception __exception, ref bool __result, MethodInfo method)
    {
        return HandleMissingMemberType(__exception, ref __result, method, "method", "signature type");
    }

    private static Exception? HandleMissingMemberType(Exception __exception, ref bool __result, MemberInfo member, string memberKind, string typeKind)
    {
        if (__exception is null)
            return null;

        var typeLoadException = FindTypeLoadException(__exception);
        if (typeLoadException is null)
            return __exception;

        __result = false;
        CompatLog.Warning($"Skipped IL2CPP-injected {memberKind} {member.DeclaringType?.FullName}.{member.Name} because its {typeKind} could not load: {typeLoadException.Message}");
        return null;
    }

    private static TypeLoadException? FindTypeLoadException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TypeLoadException typeLoadException)
                return typeLoadException;
        }

        return null;
    }
}

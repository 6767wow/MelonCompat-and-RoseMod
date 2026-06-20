using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Threading;
using HarmonyLib;
using HarmonyX = HarmonyLib.Harmony;

namespace MelonLoader.BepInExCompat;

internal static class ClassInjectorCompatibilityPatches
{
#if ROSEMOD_STANDALONE
    private static readonly object GuardedClassInjectionTypesGate = new();
    private static readonly HashSet<string> GuardedClassInjectionTypes = new();
    private static int DynamicDelegateTypeCounter;
#endif

    public static void Install()
    {
        try
        {
            var classInjectorType = Type.GetType("Il2CppInterop.Runtime.Injection.ClassInjector, Il2CppInterop.Runtime");
#if ROSEMOD_STANDALONE
            var harmony = new HarmonyX("dev.jayde.rosemod.classinjector");
            PatchHarmonyDelegateFactory(harmony);
            if (ShouldGuardClassInjection())
            {
                var guardedClassInjection = PatchRoseModClassInjectionGuard(harmony, classInjectorType);
                if (guardedClassInjection)
                    CompatLog.Warning("RoseMod IL2CPP class injection guard is enabled for this launch.");
                else
                    CompatLog.Warning("RoseMod could not install the IL2CPP class injection guard.");
            }
            else
            {
                CompatLog.Info("RoseMod IL2CPP class injection guard is disabled; mod ClassInjector registrations are allowed.");
            }
#else
            var harmony = new HarmonyX($"{Plugin.PluginGuid}.classinjector");
#endif

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

            patched |= PatchRunFinalizerHook(harmony);

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

#if ROSEMOD_STANDALONE
    private static void PatchHarmonyDelegateFactory(HarmonyX harmony)
    {
        try
        {
            var factoryType = Type.GetType("HarmonyLib.DelegateTypeFactory, 0Harmony", throwOnError: false);
            var target = factoryType?.GetMethod(
                "CreateDelegateType",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Type), typeof(Type[]), typeof(CallingConvention?) },
                null);
            var prefix = typeof(ClassInjectorCompatibilityPatches).GetMethod(nameof(CreateDelegateTypePrefix), BindingFlags.NonPublic | BindingFlags.Static);
            if (target is null || prefix is null)
            {
                CompatLog.Warning("Could not find Harmony delegate factory overload; CoreCLR delegate compatibility patch was not installed.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            CompatLog.Info("Installed Harmony CoreCLR delegate factory compatibility patch.");
        }
        catch (Exception ex)
        {
            CompatLog.Warning($"Failed to install Harmony delegate factory compatibility patch: {ex.Message}");
        }
    }

    private static bool CreateDelegateTypePrefix(Type returnType, Type[] argTypes, CallingConvention? convention, ref Type __result)
    {
        __result = CreateRuntimeDelegateType(returnType, argTypes, convention);
        return false;
    }

    private static Type CreateRuntimeDelegateType(Type returnType, Type[] argTypes, CallingConvention? convention)
    {
        var id = Interlocked.Increment(ref DynamicDelegateTypeCounter);
        var assemblyName = new AssemblyName("RoseMod.HarmonyDelegateTypes." + id);
        var assembly = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        var type = module.DefineType(
            "RoseModHarmonyDelegate" + id,
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class | TypeAttributes.AnsiClass | TypeAttributes.AutoClass,
            typeof(MulticastDelegate));

        if (convention.HasValue)
        {
            var ctor = typeof(UnmanagedFunctionPointerAttribute).GetConstructor(new[] { typeof(CallingConvention) });
            if (ctor is not null)
                type.SetCustomAttribute(new CustomAttributeBuilder(ctor, new object[] { convention.Value }));
        }

        var constructor = type.DefineConstructor(
            MethodAttributes.Public | MethodAttributes.RTSpecialName | MethodAttributes.HideBySig,
            CallingConventions.Standard,
            new[] { typeof(object), typeof(IntPtr) });
        constructor.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

        var invoke = type.DefineMethod(
            "Invoke",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
            returnType,
            argTypes);
        invoke.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

        return type.CreateTypeInfo()?.AsType()
            ?? throw new InvalidOperationException("Reflection.Emit did not create a Harmony delegate type.");
    }

    private static bool PatchRoseModClassInjectionGuard(HarmonyX harmony, Type? classInjectorType)
    {
        var prefix = typeof(ClassInjectorCompatibilityPatches).GetMethod(nameof(RoseModClassInjectionGuardPrefix), BindingFlags.NonPublic | BindingFlags.Static);
        if (classInjectorType is null || prefix is null)
            return false;

        var patched = false;
        foreach (var method in classInjectorType.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (!method.Name.Equals("RegisterTypeInIl2Cpp", StringComparison.Ordinal) || method.IsGenericMethodDefinition)
                continue;

            var parameters = method.GetParameters();
            if (parameters.Length == 0 || parameters[0].ParameterType != typeof(Type))
                continue;

            harmony.Patch(method, prefix: new HarmonyMethod(prefix));
            patched = true;
        }

        return patched;
    }

    private static bool ShouldGuardClassInjection()
    {
        var guard = FirstEnvironmentValue("ROSEMOD_GUARD_CLASS_INJECTION", "MELONCOMPAT_GUARD_CLASS_INJECTION");
        if (IsEnabled(guard))
            return true;

        var env = FirstEnvironmentValue("ROSEMOD_ALLOW_CLASS_INJECTION", "MELONCOMPAT_ALLOW_CLASS_INJECTION");
        if (IsEnabled(env))
            return false;

        return File.Exists(Path.Combine(Environment.CurrentDirectory, "RoseMod", "UserData", "guard-class-injection.txt"))
            || File.Exists(Path.Combine(Environment.CurrentDirectory, "MelonCompat", "UserData", "guard-class-injection.txt"));
    }

    private static bool IsEnabled(string? value)
    {
        return value is not null
            && (value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase));
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

    private static bool RoseModClassInjectionGuardPrefix(Type type)
    {
        var name = type.FullName ?? type.Name;
        if (IsRequiredIl2CppInteropRuntimeType(type, name))
        {
            lock (GuardedClassInjectionTypesGate)
            {
                if (GuardedClassInjectionTypes.Add(name))
                    CompatLog.Info($"Allowed Il2CppInterop runtime class injection for {name}.");
            }

            return true;
        }

        lock (GuardedClassInjectionTypesGate)
        {
            if (GuardedClassInjectionTypes.Add(name))
                CompatLog.Warning($"Blocked IL2CPP class injection for {name}; RoseMod class-injection guard is enabled.");
        }

        return false;
    }

    private static bool IsRequiredIl2CppInteropRuntimeType(Type type, string name)
    {
        var assemblyName = type.Assembly.GetName().Name;
        return assemblyName?.Equals("Il2CppInterop.Runtime", StringComparison.OrdinalIgnoreCase) == true
            && name.StartsWith("Il2CppInterop.Runtime.DelegateSupport", StringComparison.Ordinal);
    }
#endif

    private static bool PatchRunFinalizerHook(HarmonyX harmony)
    {
        try
        {
            var patchType = Type.GetType("Il2CppInterop.Runtime.Injection.Hooks.GarbageCollector_RunFinalizer_Patch, Il2CppInterop.Runtime", throwOnError: false);
            var delegateType = patchType?.GetNestedType("MethodDelegate", BindingFlags.Public | BindingFlags.NonPublic);
            var hookOpenType = Type.GetType("Il2CppInterop.Runtime.Injection.Hook`1, Il2CppInterop.Runtime", throwOnError: false);
            var prefix = typeof(ClassInjectorCompatibilityPatches).GetMethod(nameof(SkipRunFinalizerHookPrefix), BindingFlags.NonPublic | BindingFlags.Static);
            if (patchType is null || delegateType is null || hookOpenType is null || prefix is null)
                return false;

            var applyHook = hookOpenType.MakeGenericType(delegateType).GetMethod("ApplyHook", BindingFlags.Public | BindingFlags.Instance);
            if (applyHook is null)
                return false;

            harmony.Patch(applyHook, prefix: new HarmonyMethod(prefix));
            return true;
        }
        catch (Exception ex)
        {
            CompatLog.Warning($"Could not patch Il2CppInterop finalizer hook fallback: {ex.Message}");
            return false;
        }
    }

    private static bool SkipRunFinalizerHookPrefix(object __instance)
    {
        var typeName = __instance.GetType().FullName;
        if (typeName?.Equals("Il2CppInterop.Runtime.Injection.Hooks.Class_FromName_Hook", StringComparison.Ordinal) == true)
        {
            CompatLog.Info("Disabled Il2CppInterop Class::FromName hook for Unity 6 stability.");
            return false;
        }

        if (!typeName?.Equals("Il2CppInterop.Runtime.Injection.Hooks.GarbageCollector_RunFinalizer_Patch", StringComparison.Ordinal) ?? true)
            return true;

        try
        {
            var poolType = Type.GetType("Il2CppInterop.Runtime.Runtime.Il2CppObjectPool, Il2CppInterop.Runtime", throwOnError: false);
            poolType?.GetProperty("DisableCaching", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, true);
        }
        catch
        {
        }

        CompatLog.Info("Disabled Il2CppInterop GarbageCollector::RunFinalizer hook; Il2Cpp object pooling is disabled for stability.");
        return false;
    }

    private static bool PatchEligibilityMethod<TMember>(HarmonyX harmony, Type? classInjectorType, string methodName, string finalizerName)
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
        CompatLog.Warning($"Ignored IL2CPP-injected {memberKind} {member.DeclaringType?.FullName}.{member.Name} because its {typeKind} could not load: {typeLoadException.Message}");
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

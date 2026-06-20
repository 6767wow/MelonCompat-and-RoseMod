using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.Runtime;
using Il2CppInterop.Runtime.Runtime.VersionSpecific.Class;
using Il2CppInterop.Runtime.Runtime.VersionSpecific.MethodInfo;
using Il2CppInterop.Runtime.Runtime.VersionSpecific.ParameterInfo;
using Il2CppInterop.Runtime.Runtime.VersionSpecific.Type;

namespace RoseMod.Il2CppFixes;

public static class RoseModIl2CppInteropFixes
{
    private static readonly Harmony Harmony = new("dev.jayde.rosemod.il2cppinteropfixes");
    private static readonly Dictionary<IntPtr, Type> TypeLookup = new();
    private static readonly Dictionary<string, Type> TypeNameLookup = new(StringComparer.Ordinal);
    private static bool installed;

    private static MethodInfo? injectorHelpersAddTypeToLookup;
    private static MethodInfo? getIl2CppTypeFullName;
    private static MethodInfo? rewriteType;
    private static MethodInfo? fixedFindType;
    private static MethodInfo? fixedAddTypeToLookup;
    private static MethodInfo? fixedFindAbstractMethods;
    private static MethodInfo? fixedIsByRef;
    private static MethodInfo? getIsByRef;

    public static bool InstalledSuccessfully { get; private set; }

    public static void Install()
    {
        if (installed)
            return;

        installed = true;
        InstalledSuccessfully = false;
        try
        {
            var classInjector = typeof(ClassInjector);
            var injectorHelpers = classInjector.Assembly.GetType("Il2CppInterop.Runtime.Injection.InjectorHelpers", throwOnError: true)!;
            injectorHelpersAddTypeToLookup = injectorHelpers.GetMethod("AddTypeToLookup", BindingFlags.Static | BindingFlags.NonPublic, new[] { typeof(Type), typeof(IntPtr) })
                ?? throw new MissingMethodException(injectorHelpers.FullName, "AddTypeToLookup");
            getIl2CppTypeFullName = classInjector.GetMethod("GetIl2CppTypeFullName", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(classInjector.FullName, "GetIl2CppTypeFullName");
            rewriteType = classInjector.GetMethod("RewriteType", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(classInjector.FullName, "RewriteType");

            var support = typeof(RoseModIl2CppInteropFixes);
            fixedFindType = support.GetMethod(nameof(FixedFindType), BindingFlags.Static | BindingFlags.NonPublic);
            fixedAddTypeToLookup = support.GetMethod(nameof(FixedAddTypeToLookup), BindingFlags.Static | BindingFlags.NonPublic);
            fixedFindAbstractMethods = support.GetMethod(nameof(FixedFindAbstractMethods), BindingFlags.Static | BindingFlags.NonPublic);
            fixedIsByRef = support.GetMethod(nameof(FixedIsByRef), BindingFlags.Static | BindingFlags.NonPublic);
            getIsByRef = typeof(Type).GetProperty(nameof(Type.IsByRef), BindingFlags.Instance | BindingFlags.Public)?.GetGetMethod()
                ?? throw new MissingMethodException(typeof(Type).FullName, "get_IsByRef");

            Patch(classInjector.GetMethod("SystemTypeFromIl2CppType", BindingFlags.Static | BindingFlags.NonPublic),
                prefix: support.GetMethod(nameof(SystemTypeFromIl2CppTypePrefix), BindingFlags.Static | BindingFlags.NonPublic),
                transpiler: support.GetMethod(nameof(SystemTypeFromIl2CppTypeTranspiler), BindingFlags.Static | BindingFlags.NonPublic));
            Patch(classInjector.GetMethod("IsTypeSupported", BindingFlags.Static | BindingFlags.NonPublic),
                transpiler: support.GetMethod(nameof(IsTypeSupportedTranspiler), BindingFlags.Static | BindingFlags.NonPublic));
            Patch(classInjector.GetMethod("ConvertMethodInfo", BindingFlags.Static | BindingFlags.NonPublic),
                transpiler: support.GetMethod(nameof(ConvertMethodInfoTranspiler), BindingFlags.Static | BindingFlags.NonPublic));
            Patch(classInjector.GetMethod("RewriteType", BindingFlags.Static | BindingFlags.NonPublic),
                prefix: support.GetMethod(nameof(RewriteTypePrefix), BindingFlags.Static | BindingFlags.NonPublic));

            Log("Installed MelonCompat Il2CppInterop compatibility fixes.");
            InstalledSuccessfully = true;
        }
        catch (Exception ex)
        {
            Log("Failed to install MelonCompat Il2CppInterop compatibility fixes: " + ex);
        }
    }

    private static void Patch(MethodInfo? target, MethodInfo? prefix = null, MethodInfo? transpiler = null)
    {
        if (target is null)
            return;

        Harmony.Patch(
            target,
            prefix is null ? null : new HarmonyMethod(prefix),
            transpiler: transpiler is null ? null : new HarmonyMethod(transpiler));
    }

    private static void Log(string message)
    {
        const string reset = "\u001b[0m";
        var gray = Color(211, 211, 211);
        var timestamp = Color(0, 255, 0);
        var theme = Color(80, 220, 140);
        var info = Color(0, 255, 255);
        var cleanText = message.Replace(reset, info);
        Console.WriteLine($"{gray}[{timestamp}{DateTime.Now:HH:mm:ss.fff}{gray}] [{theme}MelonCompat.Il2CppFixes{gray}]{info} {cleanText}{reset}");
    }

    private static string Color(byte red, byte green, byte blue) => $"\u001b[38;2;{red};{green};{blue}m";

    private static bool FixedIsByRef(Type? type)
    {
        return type is not null && (type.IsByRef || type.IsPointer);
    }

    private static Type? FixedFindType(string? typeFullName)
    {
        if (string.IsNullOrEmpty(typeFullName))
            return null;

        if (TypeNameLookup.TryGetValue(typeFullName, out var cached))
            return cached;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly is null)
                continue;

            var type = assembly.GetType("Il2Cpp." + typeFullName, throwOnError: false)
                ?? assembly.GetType("Il2Cpp" + typeFullName, throwOnError: false)
                ?? assembly.GetType(typeFullName, throwOnError: false);
            if (type is null)
                continue;

            TypeNameLookup[type.FullName ?? typeFullName] = type;
            TypeNameLookup[typeFullName] = type;
            return type;
        }

        return null;
    }

    private static void FixedAddTypeToLookup(Type type, IntPtr typePointer)
    {
        if (type is null || typePointer == IntPtr.Zero || injectorHelpersAddTypeToLookup is null)
            return;

        injectorHelpersAddTypeToLookup.Invoke(null, new object[] { type, typePointer });
        var il2CppType = IL2CPP.il2cpp_class_get_type(typePointer);
        if (il2CppType != IntPtr.Zero)
            TypeLookup[il2CppType] = type;
    }

    private unsafe static bool RewriteTypePrefix(Type? __0, ref Type? __result)
    {
        if (__0 == typeof(void*))
        {
            __result = __0;
            return false;
        }

        return true;
    }

    private unsafe static bool SystemTypeFromIl2CppTypePrefix(Il2CppTypeStruct* __0, ref Type? __result)
    {
        if ((IntPtr)__0 == IntPtr.Zero || rewriteType is null)
            return false;

        var nativeType = UnityVersionHandler.Wrap(__0);
        if ((IntPtr)nativeType.TypePointer == IntPtr.Zero)
            return false;

        if (TypeLookup.TryGetValue((IntPtr)nativeType.TypePointer, out var injectedType))
        {
            __result = (Type?)rewriteType.Invoke(null, new object[] { injectedType });
            return false;
        }

        var klass = IL2CPP.il2cpp_class_from_type((IntPtr)nativeType.TypePointer);
        if (klass == IntPtr.Zero)
            return true;

        var namePointer = IL2CPP.il2cpp_class_get_name(klass);
        if (namePointer == IntPtr.Zero)
            return true;

        var typeName = Marshal.PtrToStringAnsi(namePointer);
        if (string.IsNullOrEmpty(typeName))
            return true;

        var namespacePointer = IL2CPP.il2cpp_class_get_namespace(klass);
        var namespaceName = namespacePointer == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(namespacePointer);
        var fullName = string.IsNullOrEmpty(namespaceName) ? typeName : namespaceName + "." + typeName;

        var assemblyNamePointer = IL2CPP.il2cpp_class_get_assemblyname(klass);
        var assemblyName = assemblyNamePointer == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(assemblyNamePointer);
        foreach (var candidateAssemblyName in CandidateAssemblyNames(assemblyName))
        {
            Type? resolved = null;
            try
            {
                var assembly = Assembly.Load(candidateAssemblyName);
                resolved = assembly.GetType("Il2Cpp." + fullName, throwOnError: false)
                    ?? assembly.GetType("Il2Cpp" + fullName, throwOnError: false)
                    ?? assembly.GetType(fullName, throwOnError: false);
            }
            catch
            {
            }

            if (resolved is null)
                continue;

            __result = resolved;
            return false;
        }

        return true;
    }

    private static IEnumerable<string> CandidateAssemblyNames(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
            yield break;

        yield return "Il2Cpp" + assemblyName;
        yield return assemblyName;
        if (assemblyName.StartsWith("Il2Cpp", StringComparison.Ordinal))
            yield return assemblyName.Substring("Il2Cpp".Length);
    }

    private static IEnumerable<CodeInstruction> SystemTypeFromIl2CppTypeTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var getType = typeof(Type).GetMethod(nameof(Type.GetType), BindingFlags.Static | BindingFlags.Public, new[] { typeof(string) });
        foreach (var instruction in instructions)
        {
            if (fixedFindType is not null && getType is not null && instruction.Calls(getType))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = fixedFindType;
            }

            yield return instruction;
        }
    }

    private static IEnumerable<CodeInstruction> RegisterTypeInIl2CppTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (fixedAddTypeToLookup is not null && injectorHelpersAddTypeToLookup is not null && instruction.Calls(injectorHelpersAddTypeToLookup))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = fixedAddTypeToLookup;
            }
            else if (fixedFindAbstractMethods is not null && instruction.ToString().Contains("FindAbstractMethods", StringComparison.Ordinal))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = fixedFindAbstractMethods;
            }

            yield return instruction;
        }
    }

    private static IEnumerable<CodeInstruction> ConvertMethodInfoTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        return ReplaceIsByRef(instructions);
    }

    private static IEnumerable<CodeInstruction> IsTypeSupportedTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        return ReplaceIsByRef(instructions);
    }

    private static IEnumerable<CodeInstruction> ReplaceIsByRef(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (fixedIsByRef is not null && getIsByRef is not null && instruction.Calls(getIsByRef))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = fixedIsByRef;
            }

            yield return instruction;
        }
    }

    private unsafe static void FixedFindAbstractMethods(List<INativeMethodInfoStruct> list, INativeClassStruct klass)
    {
        if (klass.Parent != null)
            FixedFindAbstractMethods(list, UnityVersionHandler.Wrap(klass.Parent));

        for (var i = 0; i < klass.MethodCount; i++)
        {
            var baseMethod = UnityVersionHandler.Wrap(klass.Methods[i]);
            var name = Marshal.PtrToStringAnsi(baseMethod.Name);
            if (baseMethod.Flags.HasFlag(Il2CppMethodFlags.METHOD_ATTRIBUTE_ABSTRACT))
            {
                list.Add(baseMethod);
                continue;
            }

            var implemented = list.SingleOrDefault(method => IsMatchingMethod(method, baseMethod, name));
            if (implemented is not null)
                list.Remove(implemented);
        }
    }

    private unsafe static bool IsMatchingMethod(INativeMethodInfoStruct method, INativeMethodInfoStruct baseMethod, string? name)
    {
        if (Marshal.PtrToStringAnsi(method.Name) != name || method.ParametersCount != baseMethod.ParametersCount)
            return false;

        for (var i = 0; i < method.ParametersCount; i++)
        {
            var baseParameterName = IL2CPP.il2cpp_method_get_param_name(baseMethod.Pointer, (uint)i);
            var parameterName = IL2CPP.il2cpp_method_get_param_name(method.Pointer, (uint)i);
            if (Marshal.PtrToStringAnsi(baseParameterName) != Marshal.PtrToStringAnsi(parameterName))
                return false;

            if (getIl2CppTypeFullName is null)
                continue;

            var baseParameter = UnityVersionHandler.Wrap(baseMethod.Parameters, i);
            var parameter = UnityVersionHandler.Wrap(method.Parameters, i);
            var baseTypeName = (string?)getIl2CppTypeFullName.Invoke(null, new object[] { (IntPtr)baseParameter.ParameterType });
            var typeName = (string?)getIl2CppTypeFullName.Invoke(null, new object[] { (IntPtr)parameter.ParameterType });
            if (!IsSameIl2CppTypeName(baseTypeName, typeName))
                return false;
        }

        return true;
    }

    private static bool IsSameIl2CppTypeName(string? left, string? right)
    {
        if (left == right)
            return true;

        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            return false;

        return left == "Il2Cpp." + right
            || left == "Il2Cpp" + right
            || "Il2Cpp." + left == right
            || "Il2Cpp" + left == right
            || "Il2Cpp." + left == "Il2Cpp." + right
            || "Il2Cpp" + left == "Il2Cpp" + right;
    }
}

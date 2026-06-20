using System.Reflection;
using System.Reflection.Emit;
using System.Linq.Expressions;
using HarmonyLib;

namespace Harmony;

public enum ArgumentType
{
    Normal,
    Ref,
    Out,
    Pointer
}

public enum MethodType
{
    Normal,
    Getter,
    Setter,
    Constructor,
    StaticConstructor
}

public enum PropertyMethod
{
    Getter,
    Setter
}

public enum HarmonyPatchType
{
    All,
    Prefix,
    Postfix,
    Transpiler
}

public class HarmonyInstance : HarmonyLib.Harmony
{
    public HarmonyInstance(string id)
        : base(id)
    {
    }

    public static HarmonyInstance Create(string id) => new(id);

    public DynamicMethod? Patch(MethodBase original, HarmonyMethod? prefix, HarmonyMethod? postfix, HarmonyMethod? transpiler)
    {
        base.Patch(original, prefix, postfix, transpiler);
        return null;
    }

    public void Unpatch(MethodBase original, HarmonyPatchType type, string harmonyID)
    {
        base.Unpatch(original, ToLib(type), harmonyID);
    }

    private static HarmonyLib.HarmonyPatchType ToLib(HarmonyPatchType type)
    {
        return type switch
        {
            HarmonyPatchType.Prefix => HarmonyLib.HarmonyPatchType.Prefix,
            HarmonyPatchType.Postfix => HarmonyLib.HarmonyPatchType.Postfix,
            HarmonyPatchType.Transpiler => HarmonyLib.HarmonyPatchType.Transpiler,
            _ => HarmonyLib.HarmonyPatchType.All
        };
    }
}

public class HarmonyMethod : HarmonyLib.HarmonyMethod
{
    public int prioritiy;

    public HarmonyMethod()
    {
    }

    public HarmonyMethod(MethodInfo method)
        : base(method)
    {
    }

    public HarmonyMethod(Type type, string name, Type[]? parameters = null)
        : base(type, name, parameters)
    {
    }

    public static HarmonyMethod? Merge(List<HarmonyMethod> attributes)
    {
        var merged = HarmonyLib.HarmonyMethod.Merge(attributes.Cast<HarmonyLib.HarmonyMethod>().ToList());
        return FromLib(merged);
    }

    internal static HarmonyMethod? FromLib(HarmonyLib.HarmonyMethod? method)
    {
        if (method is null)
            return null;

        var legacy = new HarmonyMethod();
        HarmonyLib.HarmonyMethodExtensions.CopyTo(method, legacy);
        return legacy;
    }
}

public static class HarmonyMethodExtensions
{
    public static HarmonyMethod? Clone(HarmonyMethod method) => HarmonyMethod.FromLib(HarmonyLib.HarmonyMethodExtensions.Clone(method));
    public static void CopyTo(HarmonyMethod source, HarmonyMethod destination) => HarmonyLib.HarmonyMethodExtensions.CopyTo(source, destination);
    public static List<HarmonyMethod> GetHarmonyMethods(Type type) => HarmonyLib.HarmonyMethodExtensions.GetFromType(type).Select(HarmonyMethod.FromLib).Where(method => method is not null).Cast<HarmonyMethod>().ToList();
    public static List<HarmonyMethod> GetHarmonyMethods(MethodBase method) => HarmonyLib.HarmonyMethodExtensions.GetFromMethod(method).Select(HarmonyMethod.FromLib).Where(item => item is not null).Cast<HarmonyMethod>().ToList();
    public static HarmonyMethod? Merge(HarmonyMethod master, HarmonyMethod detail) => HarmonyMethod.FromLib(HarmonyLib.HarmonyMethodExtensions.Merge(master, detail));
}

public class HarmonyPatch : HarmonyLib.HarmonyPatch
{
    public HarmonyPatch()
    {
    }

    public HarmonyPatch(Type declaringType)
        : base(declaringType)
    {
    }

    public HarmonyPatch(Type declaringType, Type[] argumentTypes)
        : base(declaringType, argumentTypes)
    {
    }

    public HarmonyPatch(Type declaringType, string methodName)
        : base(declaringType, methodName)
    {
    }

    public HarmonyPatch(Type declaringType, string methodName, Type[] argumentTypes)
        : base(declaringType, methodName, argumentTypes)
    {
    }

    public HarmonyPatch(Type declaringType, string methodName, Type[] argumentTypes, ArgumentType[] argumentVariations)
        : base(declaringType, methodName, argumentTypes, ToLib(argumentVariations))
    {
    }

    public HarmonyPatch(Type declaringType, MethodType methodType)
        : base(declaringType, ToLib(methodType))
    {
    }

    public HarmonyPatch(Type declaringType, MethodType methodType, Type[] argumentTypes)
        : base(declaringType, ToLib(methodType), argumentTypes)
    {
    }

    public HarmonyPatch(Type declaringType, MethodType methodType, Type[] argumentTypes, ArgumentType[] argumentVariations)
        : base(declaringType, ToLib(methodType), argumentTypes, ToLib(argumentVariations))
    {
    }

    public HarmonyPatch(Type declaringType, string methodName, MethodType methodType)
        : base(declaringType, methodName, ToLib(methodType))
    {
    }

    public HarmonyPatch(string methodName)
        : base(methodName)
    {
    }

    public HarmonyPatch(string methodName, Type[] argumentTypes)
        : base(methodName, argumentTypes)
    {
    }

    public HarmonyPatch(string methodName, Type[] argumentTypes, ArgumentType[] argumentVariations)
        : base(methodName, argumentTypes, ToLib(argumentVariations))
    {
    }

    public HarmonyPatch(string methodName, MethodType methodType)
        : base(methodName, ToLib(methodType))
    {
    }

    public HarmonyPatch(MethodType methodType)
        : base(ToLib(methodType))
    {
    }

    public HarmonyPatch(MethodType methodType, Type[] argumentTypes)
        : base(ToLib(methodType), argumentTypes)
    {
    }

    public HarmonyPatch(MethodType methodType, Type[] argumentTypes, ArgumentType[] argumentVariations)
        : base(ToLib(methodType), argumentTypes, ToLib(argumentVariations))
    {
    }

    public HarmonyPatch(Type[] argumentTypes)
        : base(argumentTypes)
    {
    }

    public HarmonyPatch(Type[] argumentTypes, ArgumentType[] argumentVariations)
        : base(argumentTypes, ToLib(argumentVariations))
    {
    }

    public HarmonyPatch(string propertyName, PropertyMethod propertyMethod)
        : base(propertyName, propertyMethod == PropertyMethod.Getter ? HarmonyLib.MethodType.Getter : HarmonyLib.MethodType.Setter)
    {
    }

    public HarmonyPatch(string assemblyQualifiedDeclaringType, string methodName, MethodType methodType, Type[] argumentTypes, ArgumentType[] argumentVariations)
        : base(assemblyQualifiedDeclaringType, methodName, ToLib(methodType), argumentTypes, ToLib(argumentVariations))
    {
    }

    private static HarmonyLib.MethodType ToLib(MethodType methodType)
    {
        return methodType switch
        {
            MethodType.Getter => HarmonyLib.MethodType.Getter,
            MethodType.Setter => HarmonyLib.MethodType.Setter,
            MethodType.Constructor => HarmonyLib.MethodType.Constructor,
            MethodType.StaticConstructor => HarmonyLib.MethodType.StaticConstructor,
            _ => HarmonyLib.MethodType.Normal
        };
    }

    private static HarmonyLib.ArgumentType[]? ToLib(ArgumentType[]? values)
    {
        return values?.Select(value => value switch
        {
            ArgumentType.Ref => HarmonyLib.ArgumentType.Ref,
            ArgumentType.Out => HarmonyLib.ArgumentType.Out,
            ArgumentType.Pointer => HarmonyLib.ArgumentType.Pointer,
            _ => HarmonyLib.ArgumentType.Normal
        }).ToArray();
    }
}

public class HarmonyPatchAll : HarmonyLib.HarmonyPatchAll
{
}

public class HarmonyPrefix : HarmonyLib.HarmonyPrefix
{
}

public class HarmonyPostfix : HarmonyLib.HarmonyPostfix
{
}

public class HarmonyTranspiler : HarmonyLib.HarmonyTranspiler
{
}

public class HarmonyPrepare : HarmonyLib.HarmonyPrepare
{
}

public class HarmonyCleanup : HarmonyLib.HarmonyCleanup
{
}

public class HarmonyTargetMethod : HarmonyLib.HarmonyTargetMethod
{
}

public class HarmonyTargetMethods : HarmonyLib.HarmonyTargetMethods
{
}

public class HarmonyPriority : HarmonyLib.HarmonyPriority
{
    public HarmonyPriority(int priority)
        : base(priority)
    {
    }
}

public class HarmonyBefore : HarmonyLib.HarmonyBefore
{
    public HarmonyBefore(params string[] before)
        : base(before)
    {
    }
}

public class HarmonyAfter : HarmonyLib.HarmonyAfter
{
    public HarmonyAfter(params string[] after)
        : base(after)
    {
    }
}

public class HarmonyArgument : HarmonyLib.HarmonyArgument
{
    public HarmonyArgument(string originalName)
        : base(originalName)
    {
    }

    public HarmonyArgument(int index)
        : base(index)
    {
    }

    public HarmonyArgument(string originalName, string newName)
        : base(originalName, newName)
    {
    }

    public HarmonyArgument(int index, string name)
        : base(index, name)
    {
    }
}

public static class AccessTools
{
    public delegate ref U FieldRef<T, U>(T instance);

    public static readonly BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    public static Type? TypeByName(string name) => HarmonyLib.AccessTools.TypeByName(name);
    public static MethodInfo? Method(Type type, string name, Type[]? parameters = null, Type[]? generics = null) => HarmonyLib.AccessTools.Method(type, name, parameters, generics);
    public static MethodInfo? Method(string typeColonName, Type[]? parameters = null, Type[]? generics = null) => HarmonyLib.AccessTools.Method(typeColonName, parameters, generics);
    public static ConstructorInfo? Constructor(Type type, Type[]? parameters = null) => HarmonyLib.AccessTools.Constructor(type, parameters);
    public static FieldInfo? Field(Type type, string name) => HarmonyLib.AccessTools.Field(type, name);
    public static PropertyInfo? Property(Type type, string name) => HarmonyLib.AccessTools.Property(type, name);
    public static Type? Inner(Type type, string name) => HarmonyLib.AccessTools.Inner(type, name);
}

public delegate object FastInvokeHandler(object target, object[] parameters);
public delegate object GetterHandler(object source);
public delegate void SetterHandler(object source, object value);
public delegate object InstantiationHandler();

public class DelegateTypeFactory : HarmonyLib.DelegateTypeFactory
{
}

public static class Priority
{
    public const int Last = HarmonyLib.Priority.Last;
    public const int VeryLow = HarmonyLib.Priority.VeryLow;
    public const int Low = HarmonyLib.Priority.Low;
    public const int LowerThanNormal = HarmonyLib.Priority.LowerThanNormal;
    public const int Normal = HarmonyLib.Priority.Normal;
    public const int HigherThanNormal = HarmonyLib.Priority.HigherThanNormal;
    public const int High = HarmonyLib.Priority.High;
    public const int VeryHigh = HarmonyLib.Priority.VeryHigh;
    public const int First = HarmonyLib.Priority.First;
}

public static class PatchInfoSerialization
{
    public static PatchInfo Deserialize(byte[] bytes)
    {
        return new PatchInfo();
    }

    public static int PriorityComparer(object obj, int index, int priority, string[] before, string[] after)
    {
        return priority.CompareTo(index);
    }
}

[Serializable]
public class PatchInfo : HarmonyLib.PatchInfo
{
}

[Serializable]
public class Patch : IComparable
{
    private readonly HarmonyLib.Patch patchWrapper;

    public Patch(MethodInfo patch, int index, string owner, int priority, string[] before, string[] after)
    {
        this.patch = patch;
        patchWrapper = new HarmonyLib.Patch(patch, index, owner, priority, before, after, false);
    }

    public MethodInfo patch { get; }
    public MethodInfo GetMethod(MethodBase original) => patchWrapper.GetMethod(original);
    public int CompareTo(object? obj) => patchWrapper.CompareTo(obj);
    public override bool Equals(object? obj) => patchWrapper.Equals(obj);
    public override int GetHashCode() => patchWrapper.GetHashCode();
}

public static class MethodInvoker
{
    public static FastInvokeHandler GetHandler(DynamicMethod methodInfo, Module module)
    {
        var source = HarmonyLib.MethodInvoker.GetHandler(methodInfo);
        return (target, parameters) => source(target, parameters);
    }

    public static FastInvokeHandler GetHandler(MethodInfo methodInfo)
    {
        var source = HarmonyLib.MethodInvoker.GetHandler(methodInfo);
        return (target, parameters) => source(target, parameters);
    }
}

public static class FastAccess
{
    public static InstantiationHandler CreateInstantiationHandler(Type type)
    {
        var constructorInfo = type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
        if (constructorInfo is null)
            throw new ApplicationException($"The type {type} must declare an empty constructor.");

        return () => constructorInfo.Invoke(null);
    }

    public static GetterHandler CreateGetterHandler(PropertyInfo propertyInfo)
    {
        var getter = propertyInfo.GetGetMethod(true) ?? throw new MissingMethodException(propertyInfo.DeclaringType?.FullName, "get_" + propertyInfo.Name);
        return source => getter.Invoke(source, null);
    }

    public static GetterHandler CreateGetterHandler(FieldInfo fieldInfo)
    {
        return source => fieldInfo.GetValue(source);
    }

    public static GetterHandler? CreateFieldGetter(Type type, params string[] names)
    {
        foreach (var name in names)
        {
            if (type.GetField(name, AccessTools.all) is { } field)
                return CreateGetterHandler(field);
            if (type.GetProperty(name, AccessTools.all) is { } property)
                return CreateGetterHandler(property);
        }

        return null;
    }

    public static SetterHandler CreateSetterHandler(PropertyInfo propertyInfo)
    {
        var setter = propertyInfo.GetSetMethod(true) ?? throw new MissingMethodException(propertyInfo.DeclaringType?.FullName, "set_" + propertyInfo.Name);
        return (source, value) => setter.Invoke(source, new[] { value });
    }

    public static SetterHandler CreateSetterHandler(FieldInfo fieldInfo)
    {
        return (source, value) => fieldInfo.SetValue(source, value);
    }
}

public static class GeneralExtensions
{
    public static string Join<T>(this IEnumerable<T> enumeration, Func<T, string>? converter = null, string delimiter = ", ")
    {
        return string.Join(delimiter, enumeration.Select(item => converter is null ? item?.ToString() : converter(item)));
    }

    public static string Description(this Type[] parameters) => string.Join(", ", parameters.Select(parameter => parameter.Name));
    public static string FullDescription(this MethodBase method) => HarmonyLib.GeneralExtensions.FullDescription(method);
    public static Type[] Types(this ParameterInfo[] pinfo) => pinfo.Select(parameter => parameter.ParameterType).ToArray();
    public static T? GetValueSafe<S, T>(this Dictionary<S, T> dictionary, S key) => dictionary.TryGetValue(key, out var value) ? value : default;
    public static T? GetTypedValue<T>(this Dictionary<string, object> dictionary, string key) => dictionary.TryGetValue(key, out var value) && value is T typed ? typed : default;
}

public static class CollectionExtensions
{
    public static void Do<T>(this IEnumerable<T> sequence, Action<T> action)
    {
        foreach (var item in sequence)
            action(item);
    }

    public static void DoIf<T>(this IEnumerable<T> sequence, Func<T, bool> condition, Action<T> action)
    {
        foreach (var item in sequence)
        {
            if (condition(item))
                action(item);
        }
    }

    public static IEnumerable<T> Add<T>(this IEnumerable<T> sequence, T item) => sequence.Concat(new[] { item });
    public static T[] AddRangeToArray<T>(this T[] sequence, T[] items) => sequence.Concat(items).ToArray();
    public static T[] AddToArray<T>(this T[] sequence, T item) => sequence.Concat(new[] { item }).ToArray();
}

public static class SymbolExtensions
{
    public static MethodInfo GetMethodInfo(Expression<Action> expression) => HarmonyLib.SymbolExtensions.GetMethodInfo(expression);
    public static MethodInfo GetMethodInfo<T>(Expression<Action<T>> expression) => HarmonyLib.SymbolExtensions.GetMethodInfo(expression);
    public static MethodInfo GetMethodInfo<T, TResult>(Expression<Func<T, TResult>> expression) => HarmonyLib.SymbolExtensions.GetMethodInfo(expression);
    public static MethodInfo GetMethodInfo(LambdaExpression expression) => HarmonyLib.SymbolExtensions.GetMethodInfo(expression);
}

public class HarmonyAttribute : HarmonyLib.HarmonyAttribute
{
}

public class HarmonyShield : MelonLoader.PatchShield
{
}

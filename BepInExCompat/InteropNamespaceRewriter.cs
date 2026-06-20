using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MelonLoader.BepInExCompat;

internal static class InteropNamespaceRewriter
{
    private static readonly HashSet<string> GlobalAssemblyCSharpTypes = new(StringComparer.Ordinal);
    private static readonly List<string> DependencyDirectories = new();
    private static string? InteropRootPath;
    private static bool initialized;

    public static void Initialize(string bepinexRootPath)
    {
        Initialize(bepinexRootPath, Array.Empty<string>());
    }

    public static void Initialize(string bepinexRootPath, string[] dependencyDirectories)
    {
        if (initialized)
            return;

        initialized = true;
        InteropRootPath = bepinexRootPath;
        DependencyDirectories.Clear();
        AddDependencyDirectory(Path.Combine(bepinexRootPath, "interop"));
        foreach (var directory in dependencyDirectories)
            AddDependencyDirectory(directory);

        var assemblyCSharpPath = Path.Combine(bepinexRootPath, "interop", "Assembly-CSharp.dll");
        if (!File.Exists(assemblyCSharpPath))
        {
            CompatLog.Warning($"Assembly-CSharp interop assembly was not found at {assemblyCSharpPath}; Il2Cpp namespace fixups are disabled.");
            return;
        }

        try
        {
            using var resolver = CreateResolver(assemblyCSharpPath);
            using var module = ModuleDefinition.ReadModule(assemblyCSharpPath, new ReaderParameters { AssemblyResolver = resolver });
            foreach (var type in Flatten(module.Types))
            {
                if (string.IsNullOrEmpty(type.Namespace))
                    GlobalAssemblyCSharpTypes.Add(type.Name);
            }

            CompatLog.Info($"Indexed {GlobalAssemblyCSharpTypes.Count} global Assembly-CSharp interop type(s) for Il2Cpp namespace fixups.");
        }
        catch (Exception ex)
        {
            CompatLog.Warning($"Failed to index Assembly-CSharp interop types: {ex.Message}");
        }
    }

    public static Assembly LoadAssemblyWithFixups(string path)
    {
        try
        {
            using var resolver = CreateResolver(path);
            using var module = ModuleDefinition.ReadModule(path, new ReaderParameters
            {
                ReadWrite = false,
                InMemory = true,
                AssemblyResolver = resolver
            });
            var typeReferenceRewriteCount = 0;
            var stringRewriteCount = 0;
            var unityCompatibilityRewriteCount = 0;

            foreach (var reference in module.GetTypeReferences())
            {
                if (!ShouldRewriteAssemblyCSharpTypeReference(reference))
                    continue;

                reference.Namespace = string.Empty;
                typeReferenceRewriteCount++;
            }

            RewriteAttributes(module.Assembly.CustomAttributes, ref typeReferenceRewriteCount, ref stringRewriteCount);
            foreach (var type in Flatten(module.Types))
                RewriteMember(type, ref typeReferenceRewriteCount, ref stringRewriteCount, ref unityCompatibilityRewriteCount);

            if (typeReferenceRewriteCount == 0 && stringRewriteCount == 0 && unityCompatibilityRewriteCount == 0)
                return Assembly.LoadFrom(path);

            using var output = new MemoryStream();
            module.Write(output);
            CompatLog.Info($"Rewrote {typeReferenceRewriteCount} Assembly-CSharp Il2Cpp type reference(s), {stringRewriteCount} string reference(s), and {unityCompatibilityRewriteCount} Unity compatibility call(s) in {Path.GetFileName(path)} before loading.");
            return Assembly.Load(output.ToArray());
        }
        catch (Exception ex)
        {
            CompatLog.Warning($"Failed to apply Il2Cpp namespace fixups to {Path.GetFileName(path)}: {ex.Message}");
            return Assembly.LoadFrom(path);
        }
    }

    private static void AddDependencyDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;

        var fullPath = Path.GetFullPath(directory);
        if (!DependencyDirectories.Any(path => path.Equals(fullPath, StringComparison.OrdinalIgnoreCase)))
            DependencyDirectories.Add(fullPath);
    }

    private static DefaultAssemblyResolver CreateResolver(string primaryAssemblyPath)
    {
        var resolver = new DefaultAssemblyResolver();
        AddResolverDirectory(resolver, Path.GetDirectoryName(primaryAssemblyPath));
        AddResolverDirectory(resolver, InteropRootPath is null ? null : Path.Combine(InteropRootPath, "interop"));
        foreach (var directory in DependencyDirectories)
            AddResolverDirectory(resolver, directory);
        return resolver;
    }

    private static void AddResolverDirectory(DefaultAssemblyResolver resolver, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;

        resolver.AddSearchDirectory(Path.GetFullPath(directory));
    }

    private static bool IsAssemblyCSharpReference(IMetadataScope scope)
    {
        return scope.Name.Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldRewriteAssemblyCSharpTypeReference(TypeReference reference)
    {
        return reference.Namespace.Equals("Il2Cpp", StringComparison.Ordinal)
            && IsAssemblyCSharpReference(reference.Scope)
            && GlobalAssemblyCSharpTypes.Contains(reference.Name);
    }

    private static void RewriteMember(TypeDefinition type, ref int typeReferenceRewriteCount, ref int stringRewriteCount, ref int physicsRewriteCount)
    {
        RewriteAttributes(type.CustomAttributes, ref typeReferenceRewriteCount, ref stringRewriteCount);

        foreach (var field in type.Fields)
            RewriteAttributes(field.CustomAttributes, ref typeReferenceRewriteCount, ref stringRewriteCount);

        foreach (var property in type.Properties)
            RewriteAttributes(property.CustomAttributes, ref typeReferenceRewriteCount, ref stringRewriteCount);

        foreach (var eventDefinition in type.Events)
            RewriteAttributes(eventDefinition.CustomAttributes, ref typeReferenceRewriteCount, ref stringRewriteCount);

        foreach (var method in type.Methods)
        {
            RewriteAttributes(method.CustomAttributes, ref typeReferenceRewriteCount, ref stringRewriteCount);
            RewriteMethodBody(method, ref stringRewriteCount, ref physicsRewriteCount);

            foreach (var parameter in method.Parameters)
                RewriteAttributes(parameter.CustomAttributes, ref typeReferenceRewriteCount, ref stringRewriteCount);
        }
    }

    private static void RewriteMethodBody(MethodDefinition method, ref int stringRewriteCount, ref int physicsRewriteCount)
    {
        if (!method.HasBody)
            return;

        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.Operand is string value && TryRewriteIl2CppTypeString(value, out var rewritten))
            {
                instruction.Operand = rewritten;
                stringRewriteCount++;
                continue;
            }

#if !BEPINEX_MONO
            if (TryRewriteUnityCompatibilityCall(method.Module, instruction))
                physicsRewriteCount++;
#endif
        }
    }

#if !BEPINEX_MONO
    private static bool TryRewriteUnityCompatibilityCall(ModuleDefinition module, Instruction instruction)
    {
        if (instruction.OpCode.Code is not (Code.Call or Code.Callvirt))
            return false;

        if (instruction.Operand is not MethodReference method)
            return false;

        MethodInfo? replacement = null;
        if (method.DeclaringType.FullName.Equals("UnityEngine.Rigidbody", StringComparison.Ordinal))
        {
            replacement = method.Name switch
            {
                "set_linearVelocity" => typeof(UnityPhysicsCompatibility).GetMethod(nameof(UnityPhysicsCompatibility.SetLinearVelocity), BindingFlags.Public | BindingFlags.Static),
                "set_angularVelocity" => typeof(UnityPhysicsCompatibility).GetMethod(nameof(UnityPhysicsCompatibility.SetAngularVelocity), BindingFlags.Public | BindingFlags.Static),
                _ => null
            };
        }
        else if (method.DeclaringType.FullName.Equals("UnityEngine.Collider", StringComparison.Ordinal)
            && method.Name.Equals("ClosestPoint", StringComparison.Ordinal)
            && method.Parameters.Count == 1
            && method.Parameters[0].ParameterType.FullName.Equals("UnityEngine.Vector3", StringComparison.Ordinal))
        {
            replacement = typeof(UnityPhysicsCompatibility).GetMethod(nameof(UnityPhysicsCompatibility.ClosestPoint), BindingFlags.Public | BindingFlags.Static);
        }
        else if (method.DeclaringType.FullName.Equals("UnityEngine.Physics", StringComparison.Ordinal)
            && method.Name.Equals("ClosestPoint", StringComparison.Ordinal))
        {
            replacement = typeof(UnityPhysicsCompatibility).GetMethod(nameof(UnityPhysicsCompatibility.PhysicsClosestPoint), BindingFlags.Public | BindingFlags.Static);
        }
        else if (method.DeclaringType.FullName.Equals("UnityEngine.MonoBehaviour", StringComparison.Ordinal)
            && method.Name.Equals("StartCoroutine", StringComparison.Ordinal)
            && method.Parameters.Count == 1
            && method.Parameters[0].ParameterType.FullName.Equals("System.String", StringComparison.Ordinal))
        {
            replacement = typeof(UnityPhysicsCompatibility).GetMethod(nameof(UnityPhysicsCompatibility.StartCoroutineString), BindingFlags.Public | BindingFlags.Static);
        }

        if (replacement is null)
            return false;

        instruction.OpCode = OpCodes.Call;
        instruction.Operand = module.ImportReference(replacement);
        return true;
    }
#endif

    private static void RewriteAttributes(Mono.Collections.Generic.Collection<CustomAttribute> attributes, ref int typeReferenceRewriteCount, ref int stringRewriteCount)
    {
        foreach (var attribute in attributes)
        {
            for (var i = 0; i < attribute.ConstructorArguments.Count; i++)
                attribute.ConstructorArguments[i] = RewriteAttributeArgument(attribute.ConstructorArguments[i], ref typeReferenceRewriteCount, ref stringRewriteCount);

            for (var i = 0; i < attribute.Properties.Count; i++)
                attribute.Properties[i] = RewriteNamedArgument(attribute.Properties[i], ref typeReferenceRewriteCount, ref stringRewriteCount);

            for (var i = 0; i < attribute.Fields.Count; i++)
                attribute.Fields[i] = RewriteNamedArgument(attribute.Fields[i], ref typeReferenceRewriteCount, ref stringRewriteCount);
        }
    }

    private static Mono.Cecil.CustomAttributeNamedArgument RewriteNamedArgument(Mono.Cecil.CustomAttributeNamedArgument argument, ref int typeReferenceRewriteCount, ref int stringRewriteCount)
    {
        return new Mono.Cecil.CustomAttributeNamedArgument(
            argument.Name,
            RewriteAttributeArgument(argument.Argument, ref typeReferenceRewriteCount, ref stringRewriteCount));
    }

    private static CustomAttributeArgument RewriteAttributeArgument(CustomAttributeArgument argument, ref int typeReferenceRewriteCount, ref int stringRewriteCount)
    {
        if (argument.Value is TypeReference typeReference && ShouldRewriteAssemblyCSharpTypeReference(typeReference))
        {
            typeReference.Namespace = string.Empty;
            typeReferenceRewriteCount++;
            return new CustomAttributeArgument(argument.Type, typeReference);
        }

        if (argument.Value is string value && TryRewriteIl2CppTypeString(value, out var rewritten))
        {
            stringRewriteCount++;
            return new CustomAttributeArgument(argument.Type, rewritten);
        }

        if (argument.Value is not CustomAttributeArgument[] arguments)
            return argument;

        var changed = false;
        var rewrittenArguments = new CustomAttributeArgument[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            var rewrittenArgument = RewriteAttributeArgument(arguments[i], ref typeReferenceRewriteCount, ref stringRewriteCount);
            changed |= !Equals(rewrittenArgument.Value, arguments[i].Value);
            rewrittenArguments[i] = rewrittenArgument;
        }

        return changed
            ? new CustomAttributeArgument(argument.Type, rewrittenArguments)
            : argument;
    }

    private static bool TryRewriteIl2CppTypeString(string value, out string rewritten)
    {
        const string prefix = "Il2Cpp.";
        rewritten = value;

        if (!value.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var typeName = value.Substring(prefix.Length);
        if (!GlobalAssemblyCSharpTypes.Contains(typeName))
            return false;

        rewritten = typeName;
        return true;
    }

    private static IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in Flatten(type.NestedTypes))
                yield return nested;
        }
    }
}

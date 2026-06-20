# BepInEx Compatibility

RoseMod includes BepInEx facade assemblies so BepInEx-style plugins can load without a full BepInEx runtime.

## Main Facade Assemblies

```text
BepInEx.Core.dll
BepInEx.Unity.Mono.dll
BepInEx.Unity.IL2CPP.dll
BepInEx.dll
```

These provide commonly referenced BepInEx types:

- `BepInPlugin`.
- `BepInDependency`.
- `BepInProcess`.
- `PluginInfo`.
- `Chainloader`.
- `Paths`.
- `ManualLogSource`.
- `ConfigFile`.
- `BaseUnityPlugin`.
- `BasePlugin`.

## Mono Plugins

Mono BepInEx plugins usually inherit:

```csharp
BepInEx.Unity.Mono.BaseUnityPlugin
```

RoseMod provides that type and activates plugin components through Unity object/component creation when possible.

## IL2CPP Plugins

IL2CPP BepInEx plugins usually inherit:

```csharp
BepInEx.Unity.IL2CPP.BasePlugin
```

RoseMod provides a `BasePlugin` facade with:

- `Info`.
- `Log`.
- `Config`.
- `Load()`.
- `Unload()`.
- `AddComponent<T>()` placeholder behavior.

## Patchers

RoseMod supports patchers from:

```text
RoseMod/Patchers
```

It also creates a compatibility view for old tools that expect:

```text
BepInEx/patchers
```

Supported patcher styles include:

- Runtime/Harmony-style patchers.
- BepInEx 5 static `TargetDLLs`, `Initialize`, and `Patch(AssemblyDefinition)` patchers.
- Cecil methods with common target attributes.

Cecil-patched outputs are written to:

```text
RoseMod/PatchedAssemblies
```

If Unity has already loaded a target assembly, RoseMod cannot replace the live assembly during that launch. In that case it logs that an earlier preloader stage is required for that particular rewrite.

## Plugin Serialization

Some BepInEx 5 Mono mods depend on plugin-defined `MonoBehaviour` types being cloneable/serializable by Unity.

RoseMod adds a managed serialization fallback:

- It tracks plugin roots.
- It patches Unity clone/instantiate methods.
- It copies serialized fields for plugin component types.

This was added for MTM101-style plugin-type serialization failures.

## Limits

The BepInEx facade is not a full BepInEx source drop. Plugins can still fail if they require:

- Exact BepInEx chainloader internals.
- Exact preloader timing.
- Private/internal BepInEx APIs.
- BepInEx-specific native detour implementations.
- BepInEx configuration edge cases not covered by the facade.


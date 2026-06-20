# MelonLoader Compatibility

MelonCompat and RoseMod expose a MelonLoader facade for mods built against MelonLoader 0.5.7 through 0.7.3.

## Why the DLL Is Named MelonLoader.dll

MelonLoader mods usually reference an assembly named:

```text
MelonLoader
```

Many also expect a version such as:

```text
0.7.3.0
```

If the compatibility DLL had a different assembly name, the mod could fail before code even runs. Therefore the facade is built as:

```text
MelonLoader.dll
```

## Covered Areas

Implemented or partially implemented areas include:

- `MelonMod`.
- `MelonPlugin`.
- `MelonBase`.
- `MelonInfoAttribute`.
- `MelonGameAttribute`.
- `MelonProcessAttribute`.
- Priority, color, version, and platform attributes.
- Melon lifecycle callbacks.
- Scene callbacks.
- Update, fixed update, late update, and GUI callbacks.
- `MelonLogger`.
- `MelonPreferences`.
- Legacy `MelonPrefs`.
- `MelonCoroutines`.
- `MelonEnvironment` compatibility.
- Basic `MelonAssembly` and `MelonHandler` behavior.
- Legacy Harmony facade namespaces/classes.

## Lifecycle Mapping

Common mapping:

```text
OnEarlyInitializeMelon
OnInitializeMelon
OnApplicationStart
OnLateInitializeMelon
OnUpdate
OnFixedUpdate
OnLateUpdate
OnGUI
OnSceneWasLoaded
OnSceneWasInitialized
OnSceneWasUnloaded
```

These are invoked by compatibility code. Exact ordering can differ from real MelonLoader, especially when running under BepInEx or when RoseMod must wait for Unity frame callbacks.

## Real Mod Validation

The project used real mods during testing because fake sample mods did not expose enough missing API surface.

Important lesson:

```text
Build success != runtime compatibility.
```

The facade must be tested against mods that use:

- MelonLoader utility namespaces.
- Legacy Harmony.
- IL2CPP interop.
- Unity scene transitions.
- Custom title screens.
- Unity component injection.
- Game-specific patching.

## Common Failure Modes

Missing facade type:

```text
TypeLoadException
Could not load type MelonLoader.Utils.MelonEnvironment
```

Missing Unity assembly:

```text
FileNotFoundException: UnityEngine.CoreModule
```

IL2CPP runtime not initialized:

```text
Il2CppInteropRuntime is not yet initialized
```

Callback bridge unavailable:

```text
Unity frame callback bridge is unavailable
```

These are documented in [[Error Catalog]].


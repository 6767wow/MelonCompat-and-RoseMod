# Architecture

MelonCompat has two related systems: the BepInEx-powered shim and the optional RoseMod standalone framework.

## High-Level Layout

```text
MelonCompat
|-- BepInExCompat/          BepInEx plugin shim behavior
|-- MelonLoaderApi/         MelonLoader facade API
|-- RoseMod/                Standalone framework managed host
|-- Native/RoseMod.Native/  C++ native bootstrap and WinHTTP proxy
|-- Installer/              CLI installer and embedded payload
|-- TauriInstaller/         GUI installer
|-- CompatVerifier/         Facade/API verifier
```

## BepInEx Shim Mode

In shim mode, BepInEx is the actual loader.

Installed files:

```text
BepInEx/plugins/MelonLoader.dll
BepInEx/plugins/Mono.Cecil.dll
BepInEx/plugins/MelonLoaderMods/*.dll
```

Important behavior:

- `MelonLoader.dll` is intentionally named that way.
- It exposes MelonLoader facade types.
- It scans `BepInEx/plugins/MelonLoaderMods`.
- It invokes Melon lifecycle methods.
- It maps logging through BepInEx logging.
- It performs IL2CPP namespace fixups where needed.
- It applies Harmony patches for loaded melon assemblies.

## RoseMod Mode

RoseMod is standalone and installs its own bootstrap.

Installed files:

```text
winhttp.dll
rosemod_config.ini
RoseMod/Core
RoseMod/MelonMods
RoseMod/BepInExPlugins
RoseMod/Patchers
RoseMod/interop
RoseMod/Il2CppAssemblies
RoseMod/UserData
RoseMod/UserLibs
RoseMod/Logs
```

Native startup:

1. Unity loads `winhttp.dll` from the game root.
2. RoseMod's proxy forwards real WinHTTP calls to Windows.
3. It detects backend and paths.
4. It logs to `RoseMod/Logs/RoseMod.native.log`.
5. Mono backend starts through Unity's Mono runtime.
6. IL2CPP backend starts through CoreCLR hosting.
7. Managed code enters `RoseMod.RoseModEntrypoint`.

Managed startup:

1. `RoseModRuntime` initializes logging and paths.
2. Assembly resolver indexes game and RoseMod assemblies.
3. IL2CPP runtime support is initialized when needed.
4. Compatibility fixes are installed.
5. Patcher bridge runs.
6. MelonLoader mods are scanned and loaded.
7. BepInEx-style plugins are scanned and loaded.
8. Unity callbacks pump update/scene events.

## Compatibility Facades

RoseMod does not ship full MelonLoader or full BepInEx source. It ships compatibility facades with the same public names where mods need them.

Important facade assemblies:

```text
MelonLoader.dll
BepInEx.Core.dll
BepInEx.Unity.Mono.dll
BepInEx.Unity.IL2CPP.dll
BepInEx.dll
```

Why facade assemblies matter:

- Mods reference assembly names, not project names.
- A mod compiled against `MelonLoader.dll` needs an assembly named `MelonLoader`.
- A BepInEx plugin often expects `BepInEx.Core`, `BepInEx.Unity.Mono`, or `BepInEx.Unity.IL2CPP`.
- Missing types cause `TypeLoadException`, `MissingMethodException`, or `FileNotFoundException`.

## Backend Differences

Mono:

- Unity managed assemblies are already loadable.
- BepInEx-style Mono plugins often inherit `BaseUnityPlugin`.
- Unity `MonoBehaviour` activation is available.
- Serialization issues can happen with plugin-defined component types.

IL2CPP:

- Managed game types are not directly available unless generated interop assemblies exist.
- Mods often reference `UnityEngine.CoreModule` and generated `Assembly-CSharp`.
- `Il2CppInterop.Runtime` must be initialized before class injection.
- Native trampoline or unstripping errors can crash in game-specific cases.


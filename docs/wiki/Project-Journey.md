# Project Journey

This page records how MelonCompat turned from a simple BepInEx shim into RoseMod, a standalone universal Unity mod framework attempt.

## 1. Original Goal

The first goal was direct:

> Make a BepInEx plugin that lets users put MelonLoader mod DLLs in the BepInEx plugins folder.

The first design used BepInEx as the real loader. The compatibility DLL was named `MelonLoader.dll` because MelonLoader-built mods reference an assembly with that name. The shim had to expose enough of the MelonLoader public API for those mods to resolve their references.

The early acceptance criteria were:

- It must build as a BepInEx plugin.
- It must scan mod DLLs from BepInEx plugin folders.
- It must print Melon-style metadata such as mod name, version, author, and callbacks.
- It must run real mods, not just compile.

## 2. First Runtime Failures

The first working builds were not enough. Real mods failed because the facade had missing types or missing assembly identity details.

Important early failure class:

```text
TypeLoadException
MelonLoader.Utils.MelonEnvironment
```

That proved that compile success was not proof of compatibility. The project needed runtime validation against real mods, especially mods that referenced MelonLoader utility namespaces, legacy Harmony namespaces, preferences, logging, and environment APIs.

## 3. MelonLoader Facade Growth

The MelonLoader facade grew to cover:

- `MelonMod`.
- `MelonPlugin`.
- `MelonInfo`, `MelonGame`, `MelonProcess`, and version/platform attributes.
- Lifecycle callbacks like `OnInitializeMelon`, `OnApplicationStart`, `OnUpdate`, scene callbacks, and GUI callbacks.
- `MelonLogger`.
- `MelonPreferences` and old `MelonPrefs` patterns.
- `MelonCoroutines`.
- Legacy Harmony facade classes for older MelonLoader-era mods.
- `MelonLoader.Utils` and environment compatibility.

The loader also needed assembly identity:

```text
Assembly name: MelonLoader
Assembly version: 0.7.3.0
```

That was required because mods compiled against MelonLoader try to resolve `MelonLoader, Version=0.7.3.0`.

## 4. GUI Installer

The project then added an installer instead of asking users to copy DLLs manually.

Installer goals:

- Scan Steam libraries for Unity games.
- Display game icons and backend/platform status.
- Detect Mono vs IL2CPP.
- Detect BepInEx and MelonLoader installs.
- Install BepInEx when missing.
- Refuse unsafe installs when required files are missing.
- Ask before deleting MelonLoader.
- Migrate old MelonLoader mods into the BepInEx compatibility folder.

The GUI started as an Electron-style idea, then moved to Tauri for a smaller native installer.

## 5. Standalone Framework Request

The project scope expanded into a standalone loader:

> Make UniWork a true solo framework like MelonLoader and BepInEx, able to run both mod types.

The first name was UniWork. The final name became RoseMod.

This required a major design change:

- Do not depend on BepInEx at runtime.
- Use a native bootstrap that starts with the game.
- Host managed code directly.
- Provide both MelonLoader and BepInEx facade assemblies.
- Provide folders for both mod styles.
- Generate or import IL2CPP interop.
- Provide BepInEx patcher support.
- Provide logging and console behavior similar to MelonLoader.

## 6. Native Bootstrap

RoseMod moved to a C++ `winhttp.dll` proxy:

- Proxies WinHTTP exports to the real Windows `winhttp.dll`.
- Detects the game root.
- Detects Mono vs IL2CPP.
- Writes `RoseMod/Logs/RoseMod.native.log`.
- Starts Mono host for Mono games.
- Starts CoreCLR host for IL2CPP games.
- Enters `RoseMod.RoseModEntrypoint`.

This removed the Doorstop/BepInEx runtime requirement for RoseMod.

## 7. IL2CPP Work

IL2CPP support required:

- Generated interop assemblies.
- `UnityEngine.CoreModule.dll`.
- `Assembly-CSharp.dll` interop when available.
- `Il2CppInterop.Runtime`.
- Class injector compatibility fixes.
- A guard for known native trampoline crashes.

Several failures came from trying to run IL2CPP mods before the Il2CppInterop runtime was initialized or before interop assemblies were present.

## 8. Patchers and Serialization

BepInEx-style plugin support needed more than loading plugin DLLs.

Added support areas:

- BepInEx config facade.
- BepInEx logging facade.
- Plugin metadata facade.
- `BaseUnityPlugin` for Mono plugins.
- `BasePlugin` for IL2CPP plugins.
- Patcher folders and Cecil patcher support.
- Compatibility view for old `BepInEx/patchers`.
- Plugin-type serialization fallback for BepInEx 5 Mono mods that depend on plugin `MonoBehaviour` serialization.

## 9. Renaming UniWork to RoseMod

The framework was renamed from UniWork to RoseMod.

Renamed areas:

- `UniWork/` source became `RoseMod/`.
- `UniWork.Core.dll` became `RoseMod.Core.dll`.
- Native namespace moved from `uniwork` to `rosemod`.
- Installed folder is `RoseMod/`.
- Logs are `RoseMod/Logs/RoseMod.log` and `RoseMod/Logs/RoseMod.native.log`.
- CLI uses `--install-rosemod`.
- README and GUI display both MelonCompat and RoseMod logos.

The old UniWork public name was removed from current source except for historical documentation.

## 10. Current Lesson

The biggest lesson is that loader compatibility cannot be proven by a successful build. It needs:

- Real mod DLL tests.
- Assembly reference inspection.
- Exact error logging.
- Facade surface comparisons.
- Per-backend testing.
- Game-specific logs.
- Repeated testing of lifecycle callbacks, patching, interop, and Unity component behavior.


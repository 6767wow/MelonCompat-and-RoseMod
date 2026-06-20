# RoseMod Standalone Framework

RoseMod is the optional standalone framework built into MelonCompat.

It was created because a BepInEx compatibility shim alone was not enough for the requested goal of a single system that could load both MelonLoader and BepInEx mod types.

## Goals

RoseMod's goals:

- Be installed by the MelonCompat installer.
- Run without BepInEx at runtime.
- Run in Mono and IL2CPP Unity games.
- Load MelonLoader-style mods.
- Load BepInEx-style plugins.
- Provide MelonLoader-style logging and lifecycle behavior.
- Provide BepInEx-style logging/config/plugin metadata.
- Provide a patcher folder.
- Generate or import IL2CPP interop assemblies.

## Non-Goals

RoseMod is not currently:

- A complete clone of MelonLoader internals.
- A complete clone of BepInEx internals.
- Guaranteed to support every native hook pattern.
- Guaranteed to match every exact lifecycle ordering edge case.
- Guaranteed to support every Unity version without game-specific testing.

## Folder Layout

```text
RoseMod/
|-- Core/              Runtime, facade DLLs, dependencies
|-- MelonMods/         MelonLoader-style mod DLLs
|-- BepInExPlugins/    BepInEx-style plugin DLLs
|-- Patchers/          BepInEx-style patcher DLLs
|-- interop/           Generated IL2CPP interop assemblies
|-- Il2CppAssemblies/  Optional extra IL2CPP assemblies
|-- UserData/          Shared user data
|-- UserLibs/          Extra user libraries
|-- Logs/              Managed and native logs
|-- Backups/           Previous bootstrap/BepInEx backups
```

## Native Bootstrap

RoseMod uses a C++ WinHTTP proxy:

```text
winhttp.dll
```

This file is placed next to the game executable. Unity loads it because the game imports WinHTTP. RoseMod then forwards WinHTTP calls to the real Windows system DLL.

Native responsibilities:

- Find game root.
- Detect backend.
- Locate `RoseMod/Core`.
- Load Mono host or CoreCLR host.
- Call into `RoseMod.Core`.
- Write native startup logs.

## Managed Runtime

Managed runtime responsibilities:

- Set up logs.
- Index assemblies.
- Resolve facade dependencies.
- Initialize IL2CPP runtime support.
- Apply compatibility fixes.
- Run patchers.
- Load MelonLoader mods.
- Load BepInEx plugins.
- Pump Unity frame and scene callbacks.

## Important Environment Variables

```text
ROSEMOD_HIDE_CONSOLE=1
```

Hides the RoseMod console window while still writing logs.

```text
ROSEMOD_ENABLE_INJECTED_EVENT_PUMP=1
```

Enables the direct injected `MonoBehaviour` event pump for debugging.

```text
ROSEMOD_DISABLE_BUILTIN_PATCH_GUARDS=1
```

Disables built-in Harmony crash guards for debugging only.

## Logs

Native:

```text
RoseMod/Logs/RoseMod.native.log
```

Managed:

```text
RoseMod/Logs/RoseMod.log
```

Use both logs when diagnosing startup issues. The native log tells whether RoseMod entered the managed runtime. The managed log tells whether mod scanning, interop, patchers, and plugin activation worked.


# MelonCompat Wiki

MelonCompat is a Unity mod-loader compatibility project. It started as a BepInEx plugin that lets MelonLoader mod DLLs load from BepInEx, then grew into RoseMod, an optional standalone framework that tries to load both MelonLoader-style mods and BepInEx-style plugins from one install.

This wiki documents the full journey, the architecture, the install layout, the build/release process, and the major errors that happened while building the framework.

## Current Scope

MelonCompat has two install modes:

- **MelonCompat shim**: runs under BepInEx 6 and exposes a `MelonLoader.dll` facade so supported MelonLoader mods can be loaded from `BepInEx/plugins/MelonLoaderMods`.
- **RoseMod**: optional standalone framework installed by MelonCompat. It uses its own native `winhttp.dll` bootstrap, installs to `RoseMod/`, and tries to load both MelonLoader mods and BepInEx plugins without depending on BepInEx at runtime.

Target compatibility:

- Unity Mono games.
- Unity IL2CPP games.
- MelonLoader mods built for MelonLoader 0.5.7 through 0.7.3.
- BepInEx-style plugins through RoseMod's facade assemblies.

## Important Pages

- [[Project Journey]]
- [[Architecture]]
- [[RoseMod Standalone Framework]]
- [[MelonLoader Compatibility]]
- [[BepInEx Compatibility]]
- [[Installer and Release]]
- [[Build Guide]]
- [[Error Catalog]]
- [[Troubleshooting]]
- [[Runtime Logs]]
- [[Compatibility Testing]]

## Project Status

As of the RoseMod rename release, the repo contains:

- `BepInExCompat/`: shared BepInEx shim code.
- `MelonLoaderApi/`: MelonLoader public API facade.
- `RoseMod/`: standalone RoseMod managed runtime and facades.
- `Native/RoseMod.Native/`: C++ WinHTTP bootstrap and native host.
- `Installer/`: CLI backend and embedded payload.
- `TauriInstaller/`: GUI installer.
- `CompatVerifier/`: Cecil-based API surface verifier.

RoseMod is experimental. It is a real loader with its own bootstrap and compatibility layers, but it is not a byte-for-byte reimplementation of MelonLoader or BepInEx. Some mods can still require extra facade coverage, exact lifecycle behavior, native hook behavior, or game-specific compatibility patches.


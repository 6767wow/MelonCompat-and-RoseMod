# Troubleshooting

Use this page when a game launches but mods do not work, the game crashes, or the installer fails.

## First Checks

Check these files:

```text
RoseMod/Logs/RoseMod.native.log
RoseMod/Logs/RoseMod.log
BepInEx/LogOutput.log
```

Check these folders:

```text
RoseMod/Core
RoseMod/MelonMods
RoseMod/BepInExPlugins
RoseMod/Patchers
RoseMod/interop
BepInEx/plugins/MelonLoaderMods
```

## If RoseMod Does Not Start

Look for:

```text
RoseMod.native.log
```

If the native log is missing:

- `winhttp.dll` may not be installed beside the game exe.
- The game may not load WinHTTP.
- Another loader may have replaced the bootstrap.
- Antivirus may have blocked the DLL.

If native log exists but managed log is missing:

- `RoseMod/Core/RoseMod.Core.dll` may be missing.
- CoreCLR support files may be missing for IL2CPP.
- Mono host may not be available for Mono backend.

## If Mods Do Not Load

Check:

- MelonLoader mods are in `RoseMod/MelonMods`.
- BepInEx plugins are in `RoseMod/BepInExPlugins`.
- The file is a managed `.dll`, not a native plugin.
- Dependencies are present in `RoseMod/UserLibs`, `RoseMod/Core`, or game managed folders.
- The log does not say the DLL was skipped as native/non-managed.

## If IL2CPP Mods Fail

Check:

```text
RoseMod/interop/UnityEngine.CoreModule.dll
RoseMod/interop/Assembly-CSharp.dll
```

If missing:

- Run installer again.
- Let interop generation finish.
- Copy existing `BepInEx/interop` into `RoseMod/interop`.
- Make sure the interop matches the exact game version.

## If BepInEx Patchers Do Not Work

Use:

```text
RoseMod/Patchers
```

For older BepInEx 5 patchers that insist on:

```text
BepInEx/patchers
```

RoseMod can create a compatibility view when no real BepInEx install exists.

Do not use a normal `.lnk` shortcut as a patcher redirect. Many loaders and patchers do not treat it as a directory.

## If Game Crashes After a Specific Event

Examples:

- After jumpscare.
- After day one.
- During title screen replacement.
- During scene change.

Steps:

1. Remove all mods except the one being tested.
2. Reproduce the crash.
3. Check the last 50 lines of `RoseMod.log`.
4. Check the last 50 lines of `RoseMod.native.log`.
5. Confirm generated interop matches the current game build.
6. Check whether the mod uses class injection, Harmony transpilers, or native IL2CPP hooks.

## If a Custom Title Screen Does Not Appear

Likely causes:

- Mod loaded too late.
- Scene callback was not invoked.
- Asset dependency missing.
- Harmony patch skipped or failed.
- Interop class names do not match.

Check logs for:

```text
OnInitializeMelon failed
OnSceneWasLoaded
Harmony patch failed
Failed to apply Il2Cpp namespace fixups
```

## If the Console Is Hidden

Logs still write to disk.

To show the console, make sure this is not set:

```text
ROSEMOD_HIDE_CONSOLE=1
```

## If the Installer Cannot Remove BepInEx

If the game is under Program Files, Windows may deny moves.

Options:

- Keep BepInEx folder but use RoseMod's bootstrap.
- Run installer as Administrator.
- Manually move the old `BepInEx` folder after closing the game and Steam.


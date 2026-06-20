# Runtime Logs

Logs are the main debugging tool.

## BepInEx Shim Logs

Path:

```text
BepInEx/LogOutput.log
```

Used for:

- Normal MelonCompat shim.
- BepInEx-powered installs.
- BepInEx plugin load failures.
- MelonLoader facade errors under BepInEx.

Useful log lines:

```text
MelonLoader compatibility shim loaded
Scanning DLL(s)
Loaded melon(s)
Failed to instantiate
OnInitializeMelon failed
TypeLoadException
MissingMethodException
```

## RoseMod Native Logs

Path:

```text
RoseMod/Logs/RoseMod.native.log
```

Used for:

- Native bootstrap startup.
- Backend detection.
- CoreCLR host startup.
- Mono host startup.
- Native host failures.

Useful log lines:

```text
RoseMod Native
Game root
Backend
CoreCLR host initialized
Unity Mono host found
RoseMod native startup failed
```

If this file is absent, RoseMod probably never entered native startup.

## RoseMod Managed Logs

Path:

```text
RoseMod/Logs/RoseMod.log
```

Used for:

- Managed runtime startup.
- Assembly resolver indexing.
- IL2CPP runtime startup.
- Patcher scan.
- Melon mod scan.
- BepInEx plugin scan.
- Callback errors.

Useful log lines:

```text
Logging initialized
Indexed managed assemblies
Initialized Il2CppInterop runtime
Installed RoseMod Il2CppInterop compatibility fixes
Scanning DLL(s) in RoseMod/MelonMods
Scanning DLL(s) in RoseMod/BepInExPlugins
Loaded BepInEx-style plugin
Loaded melon
OnInitializeMelon failed
```

## Reading Errors

When reporting an error, include:

- Game name.
- Unity backend.
- MelonCompat or RoseMod mode.
- Full last error block.
- Full stack trace.
- Whether BepInEx or MelonLoader was installed before.
- Whether `RoseMod/interop` exists.
- Whether the mod works in real MelonLoader or real BepInEx.

## Error Severity

Info:

- Startup progress.
- Scans.
- Loaded mods.

Warning:

- Missing optional interop.
- Skipped unsupported DLL.
- Fallback path used.

Error:

- Mod failed to load.
- Callback threw.
- Required runtime failed.
- Native/managed host startup failed.


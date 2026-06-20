# Error Catalog

This is the main error history for MelonCompat and RoseMod. It documents the failure signature, likely cause, fix, and current status.

## How to Use This Page

1. Find the exact log line or exception.
2. Check whether the error is install-time, native startup, managed startup, mod-load, patcher, or build-time.
3. Apply the listed fix.
4. Re-test with `RoseMod/Logs/RoseMod.native.log`, `RoseMod/Logs/RoseMod.log`, or `BepInEx/LogOutput.log`.

## 1. Missing MelonLoader Facade Type

Signature:

```text
TypeLoadException
MelonLoader.Utils.MelonEnvironment
```

Cause:

The mod was compiled against real MelonLoader and referenced a type that the facade did not expose yet.

Fix:

- Add the missing namespace/type/member to `MelonLoaderApi/`.
- Keep assembly name as `MelonLoader`.
- Keep assembly version compatible with MelonLoader 0.7.3.
- Validate with a real mod, not only a sample mod.

Status:

The facade now includes `MelonEnvironment` compatibility and more utility coverage, but future mods can still expose missing types.

## 2. UnityEngine.CoreModule Missing

Signature:

```text
System.IO.FileNotFoundException: Could not load file or assembly 'UnityEngine.CoreModule, Version=0.0.0.0'
```

Cause:

The mod references Unity assemblies, but RoseMod did not have generated or copied Unity interop assemblies available.

Fix:

- For IL2CPP: populate `RoseMod/interop`.
- Ensure `UnityEngine.CoreModule.dll` exists.
- Import existing `BepInEx/interop` if available.
- Let the installer generate interop with Cpp2IL and Il2CppInterop.

Status:

Installer can import existing interop and generate RoseMod interop. Mods can still fail if game-specific interop is incomplete.

## 3. Assembly-CSharp Interop Missing

Signature:

```text
Assembly-CSharp interop assembly was not found
Il2Cpp namespace fixups are disabled
```

Cause:

Game-specific generated `Assembly-CSharp.dll` was missing from interop.

Fix:

- Generate IL2CPP interop.
- Copy `Assembly-CSharp.dll` into `RoseMod/interop`.
- For BepInEx installs, copy `BepInEx/interop` into `RoseMod/interop`.

Status:

This is a warning when only game-type namespace fixups are disabled. Mods referencing game classes usually require it.

## 4. Il2CppInteropRuntime Not Initialized

Signature:

```text
System.InvalidOperationException: Il2CppInteropRuntime is not yet initialized. Call Il2CppInteropRuntime.Create and Il2CppInteropRuntime.Start first.
```

Cause:

An IL2CPP mod called `ClassInjector.RegisterTypeInIl2Cpp` before Il2CppInterop runtime startup was complete.

Fix:

- Initialize Il2CppInterop before mod callbacks.
- Start IL2CPP host through RoseMod runtime before loading melons.
- Install RoseMod Il2CppInterop fixes.

Status:

RoseMod initializes Il2CppInterop runtime and installs compatibility fixes before mod loading.

## 5. DispatchProxy Sealed Proxy Error

Signature:

```text
System.ArgumentException: The base type 'UniWorkDetourProviderProxy' cannot be sealed. (Parameter 'TProxy')
```

Cause:

The first standalone framework attempt used a sealed DispatchProxy proxy type. `DispatchProxy` requires a non-sealed proxy class.

Fix:

- Replace or redesign proxy type.
- Avoid sealed proxy base for DispatchProxy.
- Later RoseMod native/CoreCLR approach reduced reliance on that broken shape.

Status:

Resolved by redesigning the IL2CPP/native hosting path.

## 6. System.Runtime 6.0 Resolution Failure During Cecil Fixups

Signature:

```text
Failed to apply Il2Cpp namespace fixups
Failed to resolve assembly: 'System.Runtime, Version=6.0.0.0'
```

Cause:

The Cecil resolver did not know about all trusted platform assemblies or runtime framework assemblies needed by the mod.

Fix:

- Register trusted platform assembly paths.
- Index `RoseMod/Core`.
- Index game managed folders and interop folders.
- Avoid applying fixups when the resolver cannot safely resolve all dependencies.

Status:

RoseMod indexes managed assemblies and registers trusted platform assembly paths for CoreCLR resolution.

## 7. ManualLogSource Argument Mismatch

Signature:

```text
Object of type 'System.String' cannot be converted to type 'BepInEx.Logging.ManualLogSource'
```

Cause:

The bridge called a method using an old argument shape. A facade method expected `ManualLogSource`, but the bridge passed a string.

Fix:

- Update bridge invocation to match the facade signature.
- Keep BepInEx logging facade constructors and method overloads close to BepInEx expectations.

Status:

Resolved in the RoseMod bridge/facade wiring.

## 8. No RoseMod Console Appears

Signature:

```text
No console window appears.
Logs may still be written.
```

Cause:

Console visibility changed multiple times during development. Later builds intentionally support hidden console behavior so games can launch without an extra window.

Fix:

- Check `RoseMod/Logs/RoseMod.log`.
- Check `RoseMod/Logs/RoseMod.native.log`.
- Ensure `ROSEMOD_HIDE_CONSOLE` is not set to `1`, `true`, or `yes` if you want a console.

Status:

Console behavior is controlled by environment/config. Logs are the source of truth.

## 9. Console Colors Missing

Signature:

```text
Warnings/errors not colored like MelonLoader.
```

Cause:

Early logging wrote plain text to console.

Fix:

- Add ANSI color output.
- Warnings use yellow.
- Errors use red.
- Mod names and timestamps use MelonLoader-like formatting.

Status:

Colorized logging exists in RoseMod logging paths when the console supports ANSI escape codes.

## 10. Game Crashes After Jumpscare or Scene Change

Signature:

```text
Game closes after event, scene transition, jumpscare, or day transition.
```

Likely causes:

- Harmony native trampoline crash.
- IL2CPP class injection mismatch.
- Unity object/component lifetime mismatch.
- Missing scene callback bridge.
- Mod-specific patch hitting an unsupported game method.

Fix path:

1. Check `RoseMod.native.log` for native host failure.
2. Check `RoseMod.log` for last managed error.
3. Disable suspect mods.
4. Test with only one mod.
5. Keep built-in patch guards enabled.
6. Ensure interop assemblies match the exact game version.

Status:

RoseMod includes some built-in crash guards, but game-specific native crashes can still require targeted patches.

## 11. Game Freezes or Slows During Loading

Signature:

```text
Game stops responding during startup/loading.
```

Likely causes:

- Heavy interop generation.
- Large assembly scanning.
- Patchers running during startup.
- Harmony patch discovery across too many assemblies.
- Slow antivirus/file IO in game folder.

Fixes added:

- Assembly indexing.
- Managed DLL filtering.
- Skipping native/non-managed DLLs.
- Guarded patching.
- Reusing generated interop when available.

Recommended user fix:

- Let interop generation finish once.
- Avoid regenerating interop on every launch.
- Keep only needed mod DLLs in mod folders.

## 12. BepInEx Folder Removal Denied

Signature:

```text
Access to the path '...\BepInEx' is denied.
```

Cause:

Windows denied moving a folder under Program Files or another protected library path.

Fix:

- Continue RoseMod install.
- Leave old BepInEx folder in place.
- Replace the active bootstrap.
- Run installer as Administrator if removal is required.

Status:

Installer now handles this as a recoverable condition and reports it clearly.

## 13. Program Compatibility Assistant Appears

Signature:

```text
This program might not have installed correctly
```

Cause:

Windows Program Compatibility Assistant can trigger when an installer exits without a conventional installer manifest or when files are written under protected folders.

Fix:

- Add application manifest.
- Use clearer installer exit behavior.
- Prefer running with proper permissions for Program Files game libraries.

Status:

Installer includes a manifest.

## 14. Patcher Folder Redirect Does Not Work

Signature:

```text
BepInEx patcher still looks for BepInEx/patchers
Shortcut does not work
```

Cause:

Some patchers inspect exact paths or need a real directory/junction, not a Windows shortcut file.

Fix:

- RoseMod uses `RoseMod/Patchers`.
- When no real BepInEx exists, RoseMod creates a compatibility view at `BepInEx/patchers`.
- Use a directory junction when possible.

Status:

RoseMod has a compatibility path for older BepInEx 5 tools.

## 15. Unity Frame Callback Bridge Unavailable

Signature:

```text
Unity frame callback bridge is unavailable; keeping scene callbacks and trying fallback update bridge: Method unstripping failed
```

Cause:

RoseMod could not patch a Unity frame method or the target method was stripped/unavailable.

Fix:

- Fall back to managed Harmony callback pumping.
- Keep scene callbacks active.
- Optional debugging path: enable injected event pump.

Debug option:

```text
ROSEMOD_ENABLE_INJECTED_EVENT_PUMP=1
```

Status:

This warning does not always mean total failure. It means the preferred frame bridge failed and fallback behavior is used.

## 16. BaseUnityPlugin Missing During Build

Signature:

```text
error CS0234: The type or namespace name 'BaseUnityPlugin' does not exist in the namespace 'BepInEx'
```

Cause:

MSBuild resolved an older `BepInEx.Core.dll` from a generated `dist` folder instead of the freshly built RoseMod facade.

Fix:

Restrict assembly search paths in facade projects:

```xml
<AssemblySearchPaths>{HintPathFromItem};{TargetFrameworkDirectory};{RawFileName}</AssemblySearchPaths>
```

Status:

Fixed in RoseMod BepInEx Unity facade projects.

## 17. PluginInfo.Type Missing During Build

Signature:

```text
error CS0117: 'PluginInfo' does not contain a definition for 'Type'
```

Cause:

Same stale `BepInEx.Core.dll` resolution issue as the BaseUnityPlugin build error.

Fix:

Use the freshly built facade assembly and remove stale candidate assembly precedence.

Status:

Fixed.

## 18. PluginInfo.Instance Type Mismatch

Signature:

```text
Cannot implicitly convert type 'object' to 'BepInEx.BaseUnityPlugin'
```

Cause:

`PluginInfo.Instance` was typed for Mono `BaseUnityPlugin`, but IL2CPP plugins use `BasePlugin`.

Fix:

Change `PluginInfo.Instance` to:

```csharp
object?
```

Status:

Fixed.

## 19. Unity Physics Types Missing During Build

Signature:

```text
The type or namespace name 'Rigidbody' could not be found
The type or namespace name 'Collider' could not be found
```

Cause:

The compile reference folder had `UnityEngine.CoreModule.dll` but not `UnityEngine.PhysicsModule.dll`.

Fix:

Build `RoseMod.MelonLoader.csproj` with a full Unity managed/interop reference set containing:

```text
UnityEngine.CoreModule.dll
UnityEngine.PhysicsModule.dll
```

Status:

Fixed by using a fuller game interop reference path.

## 20. Parallel Publish File Lock

Signature:

```text
CS2012: Cannot open ... MelonCompatInstaller.dll for writing
```

Cause:

Two `dotnet publish` commands wrote to the same project intermediate output at the same time.

Fix:

Run installer publishes sequentially.

Status:

Resolved by publishing one output folder at a time.


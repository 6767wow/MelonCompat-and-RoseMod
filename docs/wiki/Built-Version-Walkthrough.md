# Built Version Walkthrough

This page explains the built/release version step by step. It is for users or maintainers looking at a ZIP, extracted release folder, or installed game folder.

## 1. Release ZIP

The release ZIP is the user-facing package. Its exact folder names can change by release, but the expected contents are:

```text
MelonCompat-RoseMod-<version>/
MelonCompat-RoseMod-<version>/MelonCompat Installer.exe
MelonCompat-RoseMod-<version>/backend/MelonCompatInstaller.exe
MelonCompat-RoseMod-<version>/cli/
MelonCompat-RoseMod-<version>/cli/MelonCompatInstaller.exe
```

The GUI is the normal user entry point. The CLI backend is used by the GUI and can also be used directly for debugging.

## 2. GUI Installer EXE

The GUI is built from:

```text
TauriInstaller/
```

User flow:

1. Launch `MelonCompat Installer.exe`.
2. The app scans Steam libraries for Unity games.
3. The app shows game icon, platform, backend, BepInEx, MelonLoader, and RoseMod status.
4. Select a game.
5. Choose the MelonCompat tab or RoseMod tab.
6. Pick mod DLLs if using the normal MelonCompat shim.
7. Click install.
8. Watch backend output in the GUI log panel.

The GUI does not directly edit the game folder. It calls the backend EXE with arguments.

## 3. CLI Backend EXE

The backend is built from:

```text
Installer/MelonCompatInstaller.csproj
```

Useful commands:

```text
MelonCompatInstaller.exe --game "D:\Games\GameName\Game.exe" --doctor
MelonCompatInstaller.exe --game "D:\Games\GameName\Game.exe" --install-bepinex --run-game-before-shim --melon "C:\Mods\Example.dll" --yes
MelonCompatInstaller.exe --game "D:\Games\GameName\Game.exe" --install-rosemod --yes
MelonCompatInstaller.exe --game "D:\Games\GameName\Game.exe" --install-rosemod --remove-bepinex --yes
```

Backend jobs:

1. Detect the Unity game.
2. Detect backend and architecture.
3. Detect BepInEx.
4. Detect MelonLoader.
5. Detect RoseMod.
6. Print an install plan.
7. Install the selected system.

## 4. Normal MelonCompat Installed Layout

Normal MelonCompat mode requires BepInEx 6.

Installed files:

```text
BepInEx/plugins/MelonLoader.dll
BepInEx/plugins/Mono.Cecil.dll
BepInEx/plugins/MelonLoaderMods/
```

What each file/folder does:

- `MelonLoader.dll`: compatibility facade and BepInEx-hosted loader.
- `Mono.Cecil.dll`: dependency used for assembly inspection/fixups.
- `MelonLoaderMods`: where supported MelonLoader mod DLLs go.

Runtime flow:

1. Unity starts.
2. BepInEx starts.
3. BepInEx loads `MelonLoader.dll`.
4. MelonCompat scans `BepInEx/plugins/MelonLoaderMods`.
5. It loads MelonLoader-style mods through the facade.
6. Logs go to `BepInEx/LogOutput.log`.

## 5. RoseMod Installed Layout

RoseMod mode does not require BepInEx at runtime.

Installed files/folders:

```text
winhttp.dll
RoseMod/Core/
RoseMod/MelonMods/
RoseMod/BepInExPlugins/
RoseMod/Patchers/
RoseMod/interop/
RoseMod/Il2CppAssemblies/
RoseMod/UserData/
RoseMod/UserLibs/
RoseMod/Logs/
dotnet/
```

Folder meaning:

- `winhttp.dll`: native C++ bootstrap/proxy loaded by the game.
- `RoseMod/Core`: managed runtime and facade DLLs.
- `RoseMod/MelonMods`: MelonLoader-style mods.
- `RoseMod/BepInExPlugins`: BepInEx-style plugins.
- `RoseMod/Patchers`: BepInEx-style patchers.
- `RoseMod/interop`: IL2CPP generated interop assemblies.
- `RoseMod/Il2CppAssemblies`: additional IL2CPP support assemblies.
- `RoseMod/UserData`: user/debug config files.
- `RoseMod/UserLibs`: extra user-provided dependency DLLs.
- `RoseMod/Logs`: RoseMod log files.
- `dotnet`: CoreCLR runtime files for IL2CPP startup.

Runtime flow:

1. Unity starts.
2. Unity loads local `winhttp.dll`.
3. RoseMod native bootstrap starts.
4. The proxy forwards real WinHTTP calls.
5. The native host detects Mono or IL2CPP.
6. Mono games start the Mono host path.
7. IL2CPP games start the CoreCLR host path.
8. Managed RoseMod starts.
9. RoseMod initializes logging, resolver, interop, patchers, and callbacks.
10. RoseMod loads MelonLoader-style mods.
11. RoseMod loads BepInEx-style plugins.

## 6. RoseMod Core DLLs

Typical `RoseMod/Core` contents include:

```text
RoseMod.Core.dll
MelonLoader.dll
BepInEx.Core.dll
BepInEx.Unity.Mono.dll
BepInEx.Unity.IL2CPP.dll
BepInEx.dll
0Harmony.dll
Mono.Cecil.dll
Il2CppInterop.*.dll
RoseMod.Il2CppFixes.dll
```

What they mean:

- `RoseMod.Core.dll`: standalone runtime.
- `MelonLoader.dll`: MelonLoader facade.
- `BepInEx.*.dll`: BepInEx facades.
- `0Harmony.dll`: Harmony patching dependency.
- `Mono.Cecil.dll`: assembly reading/patching dependency.
- `Il2CppInterop.*.dll`: IL2CPP support.
- `RoseMod.Il2CppFixes.dll`: compatibility fixes.

## 7. Logs in Built Installs

Normal MelonCompat shim:

```text
BepInEx/LogOutput.log
```

RoseMod native:

```text
RoseMod/Logs/RoseMod.native.log
```

RoseMod managed:

```text
RoseMod/Logs/RoseMod.log
```

Debugging rule:

- If `RoseMod.native.log` is missing, the native bootstrap probably did not start.
- If native log exists but managed log is missing, the native host probably failed before entering managed RoseMod.
- If managed log exists but the mod does not work, check mod loading, patching, callbacks, and interop.

## 8. Interop in Built Installs

IL2CPP mods often need:

```text
RoseMod/interop/UnityEngine.CoreModule.dll
RoseMod/interop/Assembly-CSharp.dll
```

Installer behavior:

1. If BepInEx interop exists, copy it into RoseMod.
2. If interop is missing, run RoseMod interop preparation where possible.
3. Log warnings when essential interop is missing.

Important:

- Missing interop can still let the loader start while mods fail later.

## 9. Patcher Compatibility in Built Installs

Primary folder:

```text
RoseMod/Patchers
```

Legacy compatibility view:

```text
BepInEx/patchers
```

The legacy view exists because some BepInEx 5 tools hard-code `BepInEx/patchers`.

## 10. Backups in Built Installs

RoseMod backs up replaced bootstrap files under:

```text
RoseMod/Backups/<timestamp>
```

Examples:

- old `winhttp.dll`
- Doorstop files
- BepInEx folder if removal is requested and permissions allow it

## 11. Installed Debug Toggles

Common debug toggles can be set with environment variables or files in `RoseMod/UserData`.

Examples:

```text
ROSEMOD_ENABLE_INJECTED_EVENT_PUMP=1
RoseMod/UserData/enable-injected-event-pump.txt

ROSEMOD_DISABLE_BUILTIN_PATCH_GUARDS=1
RoseMod/UserData/disable-built-in-patch-guards.txt
```

Use these only for debugging because they can change crash risk.

## 12. How to Verify a Built Release

For a release ZIP:

1. Extract to a clean folder.
2. Launch the GUI.
3. Run diagnostics on a known Unity game.
4. Install normal MelonCompat into a BepInEx 6 game.
5. Check `BepInEx/LogOutput.log`.
6. Install RoseMod into a separate test game.
7. Check `RoseMod.native.log`.
8. Check `RoseMod.log`.
9. Put a simple MelonLoader mod in `RoseMod/MelonMods`.
10. Put a simple BepInEx plugin in `RoseMod/BepInExPlugins`.
11. Put a BepInEx patcher in `RoseMod/Patchers`.
12. Test a complex mod such as Baldi Helps Granny.

## 13. Source Package vs Release Package

Source package:

- Contains code and project files.
- Used by GitHub and coders.
- Requires .NET, Rust/Tauri, and Visual Studio C++ tools to build.

Release package:

- Contains built EXEs and payloads.
- Used by players.
- Should not require Visual Studio or the .NET SDK.

Do not upload the release ZIP as the source tree. Do not upload the source ZIP as the consumer release.

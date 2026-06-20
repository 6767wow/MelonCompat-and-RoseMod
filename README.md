# MelonCompat & RoseMod

<p align="center">
  <img src="docs/assets/meloncompat-logo.png" alt="MelonCompat logo" width="120" />
  <img src="docs/assets/rosemod-logo.png" alt="RoseMod logo" width="120" />
</p>

MelonCompat is a BepInEx 6 compatibility shim for running supported MelonLoader mods in Unity games.

It supports both Unity backends:

- Mono games
- IL2CPP games

It targets MelonLoader mod DLLs made for MelonLoader 0.5.7 through 0.7.3. This is a compatibility layer, not the full MelonLoader runtime, so some mods that depend on deeper MelonLoader internals may still need fixes.

## For Players

Download the release zip, extract it, and run:

```text
MelonCompat Installer/MelonCompat Installer.exe
```

The installer scans Steam libraries for Unity games and shows each game's icon, platform, Unity backend, BepInEx status, and MelonLoader status.

The installer has two tabs:

- `MelonCompat`: the normal BepInEx-powered compatibility install.
- `RoseMod`: the optional standalone RoseMod install.

Use `Diagnostics` before installing if a game has been crashing or a mod is not loading. It checks the selected game, backend detection, installed loader state, and the embedded MelonCompat/RoseMod payload without changing files.

Basic flow:

1. Select a Unity game.
2. Click `Melon DLLs` if you want to choose specific MelonLoader mod DLLs.
3. Click `Install`.
4. If BepInEx is missing, the installer asks before downloading and installing the matching BepInEx 6 Mono or IL2CPP package.
5. After installing BepInEx, it launches the game once. Close the game after it reaches the menu so the installer can continue.
6. The installer then adds the MelonCompat shim and copies selected MelonLoader mod DLLs into `BepInEx/plugins/MelonLoaderMods`.

If MelonLoader is already installed, the installer asks before removing it. If you accept, DLLs from the old MelonLoader `Mods` folder are migrated into `BepInEx/plugins/MelonLoaderMods` before MelonLoader files are deleted.

## RoseMod

RoseMod is the optional standalone loader built into MelonCompat. It now installs its own C++ `winhttp.dll` bootstrap and does not require BepInEx or Doorstop at runtime. It is meant to provide one downloadable system for both supported Unity backends:

- Mono games
- IL2CPP games

It can load:

- MelonLoader-style mods built for MelonLoader 0.5.7 through 0.7.3
- BepInEx-style plugins through included BepInEx API facades

- MelonLoader-style console/logging
- MelonLoader mod loading through the MelonCompat `MelonLoader.dll` facade
- BepInEx-style plugin loading through RoseMod's BepInEx API facades
- Shared folders for both mod styles

RoseMod installs to:

```text
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

Use `RoseMod/MelonMods` for MelonLoader-style mods, `RoseMod/BepInExPlugins` for BepInEx-style plugins, and `RoseMod/Patchers` for BepInEx preloader-style patchers. For older BepInEx 5 mods/tools that look for `BepInEx/patchers`, RoseMod creates a compatibility view that points back to `RoseMod/Patchers` when no real BepInEx install is present.

When RoseMod is installed, the installer writes a native C++ `winhttp.dll` proxy at the game root. That proxy forwards WinHTTP calls to the real Windows `winhttp.dll`, detects the Unity backend, writes `RoseMod/Logs/RoseMod.native.log`, then starts the managed compatibility host only because MelonLoader and BepInEx mod DLLs are managed .NET assemblies. For IL2CPP games it also installs the flat CoreCLR runtime files needed by the native host. Existing bootstrap files are backed up under `RoseMod/Backups/<timestamp>` before they are replaced.

Native startup logs are written to `RoseMod/Logs/RoseMod.native.log`. Managed loader logs are written to `RoseMod/Logs/RoseMod.log`. The runtime uses `ROSEMOD_*` environment variables. A few old `MELONCOMPAT_*` aliases are still accepted for debugging compatibility.

For IL2CPP games, RoseMod indexes `RoseMod/interop` and `RoseMod/Il2CppAssemblies` for generated Unity/game interop assemblies. Mods that reference `UnityEngine.CoreModule` or `Assembly-CSharp` need those generated interop DLLs available in one of those folders.

If the selected game already has generated BepInEx interop, the RoseMod installer copies it into `RoseMod/interop` during install so the running framework does not depend on the installed BepInEx folder. If BepInEx is installed, the RoseMod tab asks whether to remove it after RoseMod installs. Choosing removal moves the `BepInEx` folder into `RoseMod/Backups/<timestamp>/BepInEx`.

Current status: RoseMod is experimental but wired as a real loader. It can discover and load both MelonLoader-style DLLs and BepInEx-style plugin DLLs through the included compatibility facades. The included verifier now checks the public MelonLoader 0.7.3 and BepInEx.Core public type surface against the RoseMod facades, plus real mod DLL references when supplied.

BepInEx patcher support is included for runtime/Harmony-style patchers, BepInEx 5 static patchers with `TargetDLLs` / `Initialize` / `Patch(AssemblyDefinition)`, and common Cecil `[TargetAssembly]` / `[TargetType]` patch methods. Cecil-patched outputs are written to `RoseMod/PatchedAssemblies`; if Unity already loaded the target assembly, RoseMod logs that the rewrite could not replace the live assembly in that launch.

For BepInEx 5 Mono mods that rely on plugin-type serialization, RoseMod also installs a managed Unity clone fallback before plugin activation. It patches Unity clone/instantiate paths and copies serialized fields for plugin `MonoBehaviour`s loaded from `RoseMod/BepInExPlugins` or `RoseMod/MelonMods`, covering mods that still fail the MTM101-style serialization test even after `FixPluginTypesSerialization` initializes.

RoseMod publishes MelonLoader-style update and scene callbacks through Unity scene events by default. If Unity frame events are unavailable, it falls back to managed Harmony callback pumping. The direct injected `MonoBehaviour` pump is opt-in for debugging with `ROSEMOD_ENABLE_INJECTED_EVENT_PUMP=1` or `RoseMod/UserData/enable-injected-event-pump.txt`.

RoseMod has a small built-in Harmony crash guard list for known Unity 6 IL2CPP native trampoline crashes. For debugging only, set `ROSEMOD_DISABLE_BUILTIN_PATCH_GUARDS=1` or create `RoseMod/UserData/disable-built-in-patch-guards.txt` to force guarded patches back on.

## What Gets Installed

For the selected game, MelonCompat installs:

```text
BepInEx/plugins/MelonLoader.dll
BepInEx/plugins/Mono.Cecil.dll
BepInEx/plugins/MelonLoaderMods/*.dll
```

The `MelonLoader.dll` name is intentional. MelonLoader mods reference an assembly named `MelonLoader`, so the shim uses that identity while running under BepInEx.

## Command Line

The release zip also includes a CLI backend:

```powershell
cli/MelonCompatInstaller.exe --game "D:\Games\GameName\GameName.exe" --install-bepinex --run-game-before-shim --melon "C:\Mods\ExampleMelon.dll" --yes
```

Useful options:

```text
--backend <auto|mono|il2cpp>
--install-rosemod         Install the optional standalone RoseMod loader.
--install-bepinex          Download and install BepInEx 6 when missing for the MelonCompat shim.
--bepinex-zip <zip>        Install BepInEx 6 from a local zip, or use the archive as RoseMod's support source.
--run-game-before-shim     Launch the game once before installing the shim after a BepInEx install.
--remove-bepinex           With --install-rosemod, move an existing BepInEx folder into RoseMod/Backups after install.
--remove-melonloader       Remove an existing MelonLoader install.
--migrate-melon-mods       Migrate DLLs from MelonLoader/Mods before removal.
--force-payload            Replace existing BepInEx/plugins/MelonLoader.dll.
--doctor                   Validate the selected game and embedded payload without installing.
--dry-run                  Detect and print the plan without writing files.
```

## Compatibility

Supported MelonLoader mod range:

- MelonLoader 0.5.7
- MelonLoader 0.6.x
- MelonLoader 0.7.x through 0.7.3

Supported Unity/BepInEx targets:

- BepInEx 6 Mono
- BepInEx 6 IL2CPP

Implemented compatibility surface:

- `MelonInfo`, `MelonGame`, `MelonProcess`, priority, color, platform, and version attributes
- `MelonMod` and `MelonPlugin` discovery from DLLs in `BepInEx/plugins`
- `OnEarlyInitializeMelon`, `OnInitializeMelon`, `OnApplicationStart`, `OnLateInitializeMelon`
- Unity frame callbacks: `OnUpdate`, `OnFixedUpdate`, `OnLateUpdate`, `OnGUI`
- Scene callbacks: `OnSceneWasLoaded`, `OnSceneWasInitialized`, `OnSceneWasUnloaded`
- Basic managed `MelonCoroutines.Start` and `Stop`
- BepInEx-backed `MelonLogger` output
- Harmony `PatchAll` against loaded melon assemblies
- Harmony patch target fixups for BepInEx IL2CPP interop assemblies that expose game types without the `Il2Cpp.` namespace
- Minimal in-memory `MelonPreferences` and `MelonPrefs`
- Legacy `Harmony.*` facade classes for older MelonLoader 0.5.7-era mods
- BepInEx logging, config, plugin metadata, and Unity base-plugin API facades for RoseMod

## Limits

MelonCompat is not a complete replacement for MelonLoader. Mods can still fail if they require exact MelonLoader lifecycle ordering, native hooks, generated interop details, internal MelonLoader classes, or native IL2CPP detours that crash in a specific Unity/game build.

Legacy BepInEx 5 installs are detected but not used. The installer expects BepInEx 6 for both Mono and IL2CPP games.

## For Coders

Build requirements:

- .NET SDK 8 or newer
- Node.js and npm
- Rust/Cargo
- Windows for the packaged Tauri installer

Build everything:

```powershell
dotnet build MelonLoader.BepInExCompat.csproj -c Release
dotnet build MelonLoader.BepInExCompat.Mono.csproj -c Release
dotnet build RoseMod.BepInEx.Core.csproj -c Release
dotnet build RoseMod.BepInEx.Unity.Mono.csproj -c Release
dotnet build RoseMod.BepInEx.Unity.IL2CPP.csproj -c Release
dotnet build RoseMod.MelonLoader.csproj -c Release
dotnet build RoseMod.Core.csproj -c Release
cmd /c "call ""C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"" >nul && msbuild Native\RoseMod.Native\RoseMod.Native.vcxproj /p:Configuration=Release /p:Platform=x64"
dotnet build CompatVerifier/CompatVerifier.csproj -c Release
dotnet build Installer/MelonCompatInstaller.csproj -c Release
dotnet run --project CompatVerifier/CompatVerifier.csproj -c Release
dotnet publish Installer/MelonCompatInstaller.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/installer
dotnet publish Installer/MelonCompatInstaller.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o TauriInstaller/backend
pushd TauriInstaller
npm install
npm run build
popd
```

Build outputs:

```text
bin/Release/net6.0/MelonLoader.dll              # IL2CPP shim
bin/Mono/Release/net6.0/MelonLoader.dll         # Mono shim
bin/Native/Release/winhttp.dll                  # RoseMod C++ native bootstrap
dist/installer/MelonCompatInstaller.exe         # CLI backend
TauriInstaller/src-tauri/target/release/meloncompat-installer-tauri.exe  # Tauri GUI app
bin/Release/netstandard2.0/RoseMod.Core.dll                   # RoseMod standalone core
bin/RoseMod/MelonLoader/Release/netstandard2.0/MelonLoader.dll
bin/RoseMod/BepInEx.*/*/netstandard2.0                        # RoseMod BepInEx facades
```

Source layout:

```text
BepInExCompat/        Shared shim implementation
MelonLoaderApi/       MelonLoader API facade types
Installer/            CLI installer and install engine
TauriInstaller/       Tauri GUI wrapper
Native/               C++ RoseMod native bootstrap and WinHTTP proxy
RoseMod/              Standalone RoseMod source
CompatVerifier/       Cecil-based facade coverage and required-member verifier
```

BepInEx packages are downloaded from BepInEx bleeding-edge builds for normal MelonCompat installs. RoseMod can also reuse that archive as a support source for CoreCLR and Il2CppInterop files without installing or requiring BepInEx at runtime.

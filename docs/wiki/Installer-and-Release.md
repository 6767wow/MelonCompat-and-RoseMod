# Installer and Release

MelonCompat ships with a Tauri GUI and a CLI backend.

## GUI Installer

The GUI is in:

```text
TauriInstaller/
```

It scans Steam libraries for Unity games and shows:

- Game name.
- Steam icon/profile image.
- Platform.
- Unity backend.
- BepInEx install status.
- MelonLoader install status.
- RoseMod install status.

Tabs:

- `MelonCompat`: install the BepInEx-powered compatibility shim.
- `RoseMod`: install the standalone RoseMod framework.

## CLI Backend

The CLI backend is:

```text
MelonCompatInstaller.exe
```

Useful commands:

```powershell
MelonCompatInstaller.exe --game "D:\Games\Game\Game.exe" --install-bepinex --run-game-before-shim --melon "C:\Mods\Mod.dll" --yes
MelonCompatInstaller.exe --game "D:\Games\Game\Game.exe" --install-rosemod --yes
MelonCompatInstaller.exe --game "D:\Games\Game\Game.exe" --doctor
```

## BepInEx Detection

The installer detects:

```text
BepInEx/core/BepInEx.Core.dll
BepInEx/core/BepInEx.dll
BepInEx/core/BepInEx.Unity.IL2CPP.dll
BepInEx/core/BepInEx.Unity.Mono.dll
```

The normal MelonCompat shim requires BepInEx. If it is missing, the installer asks before installing BepInEx.

RoseMod does not require BepInEx at runtime. If BepInEx exists, the RoseMod tab can ask whether to remove it after installation.

## MelonLoader Detection

The installer detects old MelonLoader installs and asks before removal.

If migration is enabled, it copies old mods from:

```text
MelonLoader/Mods
```

to:

```text
BepInEx/plugins/MelonLoaderMods
```

or, for RoseMod:

```text
RoseMod/MelonMods
```

## IL2CPP Interop Handling

RoseMod can:

- Copy existing BepInEx-generated interop into `RoseMod/interop`.
- Generate its own interop assemblies with Cpp2IL and Il2CppInterop generator.

Required IL2CPP inputs:

```text
GameAssembly.dll
<Game>_Data/il2cpp_data/Metadata/global-metadata.dat
```

Expected output:

```text
RoseMod/interop/UnityEngine.CoreModule.dll
```

## Release Artifact Layout

The release zip layout:

```text
MelonCompatInstaller.exe
backend/MelonCompatInstaller.exe
backend/MelonCompatInstaller.pdb
cli/MelonCompatInstaller.exe
cli/MelonCompatInstaller.pdb
README.md
docs/assets/meloncompat-logo.png
docs/assets/rosemod-logo.png
```

The root `MelonCompatInstaller.exe` is the Tauri GUI.

The `backend` folder is used by the GUI.

The `cli` folder is for direct command-line use.

## Access Denied During BepInEx Removal

Games installed under Program Files can block directory moves:

```text
Access to the path '...\BepInEx' is denied.
```

Current behavior:

- RoseMod install continues where possible.
- The old BepInEx folder is left in place.
- RoseMod replaces the active bootstrap.
- A marker file can be written explaining the failed removal.
- Running the installer as Administrator can move the folder into `RoseMod/Backups`.


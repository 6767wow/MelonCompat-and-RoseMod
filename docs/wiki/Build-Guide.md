# Build Guide

This page documents the build process used for the RoseMod rename release.

## Requirements

- Windows.
- .NET SDK 8 or newer.
- Visual Studio C++ build tools.
- Python 3.10+ and PyQt6 for RoseV IDE.
- Node.js and npm.
- Rust/Cargo for Tauri.
- A valid Unity reference set for building the MelonLoader facade.

## Managed Builds

```powershell
dotnet build MelonLoader.BepInExCompat.csproj -c Release
dotnet build MelonLoader.BepInExCompat.Mono.csproj -c Release
dotnet build RoseMod.BepInEx.Core.csproj -c Release
dotnet build RoseMod.BepInEx.Unity.Mono.csproj -c Release
dotnet build RoseMod.BepInEx.Unity.IL2CPP.csproj -c Release
dotnet build RoseMod.MelonLoader.csproj -c Release
dotnet build RoseMod.Core.csproj -c Release
dotnet build RoseMod.Il2CppFixes.csproj -c Release
dotnet build RoseMod.BepInEx5.Mono.csproj -c Release
dotnet build CompatVerifier/CompatVerifier.csproj -c Release
```

## Native Build

```powershell
cmd /c "call ""C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"" >nul && msbuild Native\RoseMod.Native\RoseMod.Native.vcxproj /p:Configuration=Release /p:Platform=x64"
```

Expected output:

```text
bin/Native/Release/winhttp.dll
```

## Installer Publish

```powershell
dotnet publish Installer/MelonCompatInstaller.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/installer
dotnet publish Installer/MelonCompatInstaller.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o TauriInstaller/backend
```

## Tauri Build

```powershell
pushd TauriInstaller
npm install
npm run build
popd
.\scripts\package-release.ps1 -Version v0.8.1-installer-fix
```

Expected output:

```text
TauriInstaller/src-tauri/target/release/meloncompat-installer-tauri.exe
dist/MelonCompat-RoseMod-<version>/MelonCompat Installer.exe
dist/MelonCompat-RoseMod-<version>/backend/MelonCompatInstaller.exe
```

The packaged top-level `MelonCompat Installer.exe` must be the Tauri GUI. The larger CLI backend belongs under `backend/` and `cli/`.

## RoseV IDE

RoseV IDE is a native PyQt app, not a Tauri/web app.

```powershell
pushd RoseV/IDE
python -m pip install -r requirements.txt
python rosev_ide.py
popd
```

To build a standalone executable:

```powershell
pushd RoseV/IDE
.\build_pyqt_exe.ps1
popd
```

## Special Build Notes

### Stale Candidate Assembly References

The RoseMod BepInEx facade projects had to prevent MSBuild from resolving old payload DLLs before the intended hint path.

Symptom:

```text
BepInEx.BaseUnityPlugin does not exist
PluginInfo does not contain a definition for Type
```

Actual cause:

MSBuild's default `{CandidateAssemblyFiles}` found an older `BepInEx.Core.dll` under `dist/github-source/.../Installer/Payload/...` before the freshly built `bin/RoseMod/BepInEx.Core/...` output.

Fix:

```xml
<AssemblySearchPaths>{HintPathFromItem};{TargetFrameworkDirectory};{RawFileName}</AssemblySearchPaths>
```

### RoseMod.MelonLoader Reference Set

`RoseMod.MelonLoader.csproj` needs Unity assemblies and IL2CPP assemblies for compile-time references.

Useful properties:

```powershell
/p:GameInteropPath="path\to\interop-or-managed-folder"
/p:RoseModCorePath="path\to\RoseMod\Core"
```

If `UnityEngine.PhysicsModule.dll` is missing, types like `Rigidbody` and `Collider` fail.

If `UnityEngine.CoreModule.dll` lacks `UnityEngine.LowLevel.PlayerLoopSystem`, the frame callback bridge cannot compile.

### Parallel Publish Collision

Running two `dotnet publish` commands for the same installer project in parallel can fail with:

```text
CS2012: Cannot open ... MelonCompatInstaller.dll for writing
```

Run those publishes one at a time.

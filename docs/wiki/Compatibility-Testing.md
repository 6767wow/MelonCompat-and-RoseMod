# Compatibility Testing

Compatibility testing should use real mods, real games, and real logs.

## Why Real Mods Matter

Sample mods usually use only simple APIs:

- `MelonMod`.
- One log line.
- Maybe `OnUpdate`.

Real mods use:

- Generated IL2CPP types.
- Scene callbacks.
- Harmony patches.
- Coroutines.
- Preferences.
- Asset loading.
- Unity component injection.
- Loader internals.
- Game-specific patch timing.

That is why a facade can compile and still fail.

## Minimum Test Matrix

Test each release with:

- One Mono Unity game.
- One IL2CPP Unity game.
- One simple MelonLoader mod.
- One complex MelonLoader mod.
- One simple BepInEx plugin.
- One BepInEx plugin with config/logging.
- One BepInEx patcher.
- One game with no BepInEx installed.
- One game with existing BepInEx installed.
- One game with old MelonLoader installed.

## Test Steps for MelonCompat Shim

1. Install BepInEx.
2. Launch once so BepInEx initializes.
3. Install MelonCompat.
4. Put mod DLLs in:

```text
BepInEx/plugins/MelonLoaderMods
```

5. Launch game.
6. Check `BepInEx/LogOutput.log`.

Pass conditions:

- Shim loads.
- Mods are discovered.
- Mod metadata is logged.
- Lifecycle callbacks run.
- No missing facade type errors.

## Test Steps for RoseMod

1. Install RoseMod from the RoseMod tab.
2. Put MelonLoader mods in:

```text
RoseMod/MelonMods
```

3. Put BepInEx plugins in:

```text
RoseMod/BepInExPlugins
```

4. Put patchers in:

```text
RoseMod/Patchers
```

5. Launch game.
6. Check both RoseMod logs.

Pass conditions:

- Native log starts.
- Managed log starts.
- Backend is correct.
- Interop is ready for IL2CPP.
- Mods/plugins are discovered.
- Callbacks run.
- No unmanaged crash occurs.

## Regression Checks

Before release:

```powershell
dotnet build Installer/MelonCompatInstaller.csproj -c Release
dotnet build RoseMod.Core.csproj -c Release
dotnet build RoseMod.MelonLoader.csproj -c Release
dotnet build RoseMod.BepInEx.Core.csproj -c Release
dotnet build RoseMod.BepInEx.Unity.Mono.csproj -c Release
dotnet build RoseMod.BepInEx.Unity.IL2CPP.csproj -c Release
dotnet run --project CompatVerifier/CompatVerifier.csproj -c Release
```

Also check:

- No old framework names in source.
- README has both logos.
- Release zip includes `docs/assets`.
- Tauri GUI builds.
- Native `winhttp.dll` builds.


const { app, BrowserWindow, dialog, ipcMain, shell } = require("electron");
const childProcess = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");

let mainWindow;

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 900,
    height: 760,
    minWidth: 720,
    minHeight: 620,
    title: "MelonCompat Installer",
    backgroundColor: "#151719",
    icon: path.join(__dirname, "..", "assets", "icon.png"),
    webPreferences: {
      preload: path.join(__dirname, "preload.js"),
      contextIsolation: true,
      nodeIntegration: false
    }
  });

  mainWindow.removeMenu();
  mainWindow.loadFile(path.join(__dirname, "index.html"));
}

app.whenReady().then(createWindow);

app.on("window-all-closed", () => {
  if (process.platform !== "darwin") app.quit();
});

app.on("activate", () => {
  if (BrowserWindow.getAllWindows().length === 0) createWindow();
});

ipcMain.handle("scan-games", async () => {
  const games = await scanInstalledGames();
  return games.sort((a, b) => Number(b.canInstall) - Number(a.canInstall) || a.name.localeCompare(b.name));
});

ipcMain.handle("add-game", async () => {
  const result = await dialog.showOpenDialog(mainWindow, {
    title: "Select Unity game executable",
    filters: [{ name: "Executable", extensions: ["exe"] }],
    properties: ["openFile"]
  });

  if (result.canceled || result.filePaths.length === 0) return null;
  return createManualGame(result.filePaths[0]);
});

ipcMain.handle("pick-melons", async () => {
  const result = await dialog.showOpenDialog(mainWindow, {
    title: "Select MelonLoader mod DLLs",
    filters: [{ name: "DLL files", extensions: ["dll"] }],
    properties: ["openFile", "multiSelections"]
  });

  return result.canceled ? [] : result.filePaths;
});

ipcMain.handle("install-game", async (_event, request) => {
  const game = request.game;
  const bepinex = detectBepInEx(game.rootDirectory);
  const melonloader = detectMelonLoader(game.rootDirectory);
  const args = ["--game", game.executablePath || game.rootDirectory, "--yes"];

  if (melonloader.exists) {
    const removeResult = await dialog.showMessageBox(mainWindow, {
      type: "warning",
      buttons: ["Remove MelonLoader", "Cancel"],
      defaultId: 0,
      cancelId: 1,
      title: "MelonLoader detected",
      message: `${request.name || "This game"} already has MelonLoader installed.`,
      detail: `MelonCompat uses BepInEx, so the installer should remove MelonLoader first. It will migrate ${melonloader.modDllCount} DLL(s) from the MelonLoader Mods folder before deleting MelonLoader files.`
    });

    if (removeResult.response !== 0) {
      throw new Error("Install cancelled because MelonLoader was left installed.");
    }

    args.push("--remove-melonloader", "--migrate-melon-mods");
  }

  if (!bepinex.exists) {
    const installResult = await dialog.showMessageBox(mainWindow, {
      type: "question",
      buttons: ["Install BepInEx & Run Game", "Cancel"],
      defaultId: 0,
      cancelId: 1,
      title: "BepInEx is missing",
      message: "BepInEx 6 is not installed for this game.",
      detail: "The installer will download the matching BepInEx 6 package, extract it into the game folder, launch the game once, wait for you to close it, then install the MelonCompat shim."
    });

    if (installResult.response !== 0) {
      throw new Error("Install cancelled because BepInEx is missing.");
    }

    args.push("--install-bepinex", "--run-game-before-shim");
  } else if (!canInstall(game, bepinex)) {
    throw new Error("This game needs BepInEx 6 with the same backend as the Unity game.");
  }

  const backend = getBackendPath();
  if (!fs.existsSync(backend)) {
    throw new Error(`Backend installer not found: ${backend}`);
  }

  if (request.forcePayload) args.push("--force-payload");
  for (const melonPath of request.melonPaths || []) {
    args.push("--melon", melonPath);
  }

  return await runBackend(backend, args);
});

ipcMain.handle("open-log", async (_event, gameRoot) => {
  const logPath = path.join(gameRoot, "BepInEx", "LogOutput.log");
  if (fs.existsSync(logPath)) await shell.openPath(logPath);
  else await shell.openPath(path.join(gameRoot, "BepInEx"));
});

function getBackendPath() {
  if (app.isPackaged) {
    return path.join(process.resourcesPath, "backend", "MelonCompatInstaller.exe");
  }

  return path.join(__dirname, "..", "backend", "MelonCompatInstaller.exe");
}

function runBackend(exe, args) {
  return new Promise((resolve, reject) => {
    const child = childProcess.spawn(exe, args, {
      windowsHide: true,
      stdio: ["ignore", "pipe", "pipe"]
    });

    let output = "";
    const append = (chunk) => {
      const text = chunk.toString();
      output += text;
      for (const line of text.split(/\r?\n/).filter(Boolean)) {
        mainWindow?.webContents.send("install-output", line);
      }
    };

    child.stdout.on("data", append);
    child.stderr.on("data", append);
    child.on("error", reject);
    child.on("close", (code) => {
      if (code === 0) resolve({ ok: true, output });
      else reject(new Error(output.trim() || `Backend exited with code ${code}`));
    });
  });
}

async function scanInstalledGames() {
  const games = [];
  const seen = new Set();

  for (const library of findSteamLibraries()) {
    const steamapps = path.join(library, "steamapps");
    const common = path.join(steamapps, "common");
    if (!fs.existsSync(steamapps) || !fs.existsSync(common)) continue;

    for (const manifest of fs.readdirSync(steamapps).filter((file) => /^appmanifest_\d+\.acf$/i.test(file))) {
      const values = parseVdfPairs(fs.readFileSync(path.join(steamapps, manifest), "utf8"));
      const installDir = values.installdir;
      if (!installDir) continue;

      const root = path.join(common, installDir);
      if (!fs.existsSync(root)) continue;

      const game = await tryCreateGame(root, values.name || installDir, "Steam");
      if (game && !seen.has(game.game.rootDirectory.toLowerCase())) {
        seen.add(game.game.rootDirectory.toLowerCase());
        games.push(game);
      }
    }
  }

  return games;
}

async function createManualGame(exePath) {
  const root = path.dirname(exePath);
  return await tryCreateGame(root, path.basename(exePath, ".exe"), "Manual", exePath);
}

async function tryCreateGame(root, name, platform, explicitExe = null) {
  try {
    const game = detectUnityGame(root, explicitExe);
    if (!game || game.backend === "Unknown") return null;

    const bepinex = detectBepInEx(game.rootDirectory);
    const melonloader = detectMelonLoader(game.rootDirectory);
    return {
      name,
      platform,
      game,
      bepinex,
      melonloader,
      canInstall: canInstall(game, bepinex),
      icon: await getIconDataUrl(game.executablePath)
    };
  } catch {
    return null;
  }
}

function detectUnityGame(root, explicitExe = null) {
  const exe = explicitExe || findGameExecutable(root);
  if (!exe) return null;

  const dataDirectory = path.join(root, `${path.basename(exe, ".exe")}_Data`);
  if (!fs.existsSync(dataDirectory)) return null;

  const backend =
    fs.existsSync(path.join(root, "GameAssembly.dll")) ||
    fs.existsSync(path.join(dataDirectory, "il2cpp_data"))
      ? "Il2Cpp"
      : fs.existsSync(path.join(dataDirectory, "Managed"))
        ? "Mono"
        : "Unknown";

  return {
    rootDirectory: root,
    executablePath: exe,
    dataDirectory,
    backend,
    architecture: readPeArchitecture(exe)
  };
}

function findGameExecutable(root) {
  try {
    return fs.readdirSync(root)
      .filter((file) => file.toLowerCase().endsWith(".exe"))
      .map((file) => path.join(root, file))
      .filter((exe) => fs.existsSync(path.join(root, `${path.basename(exe, ".exe")}_Data`)))
      .sort((a, b) => path.basename(a).length - path.basename(b).length)[0] || null;
  } catch {
    return null;
  }
}

function detectBepInEx(root) {
  const core = path.join(root, "BepInEx", "core");
  if (!fs.existsSync(core)) return { exists: false, majorVersion: 0, backend: "Unknown" };

  const majorVersion = fs.existsSync(path.join(core, "BepInEx.Core.dll"))
    ? 6
    : fs.existsSync(path.join(core, "BepInEx.dll"))
      ? 5
      : 0;

  const backend = fs.existsSync(path.join(core, "BepInEx.Unity.IL2CPP.dll"))
    ? "Il2Cpp"
    : fs.existsSync(path.join(core, "BepInEx.Unity.Mono.dll"))
      ? "Mono"
      : "Unknown";

  return { exists: true, majorVersion, backend };
}

function canInstall(game, bepinex) {
  if (!bepinex.exists) return game.backend !== "Unknown";

  return bepinex.majorVersion === 6 &&
    bepinex.backend !== "Unknown" &&
    (game.backend === "Unknown" || game.backend === bepinex.backend);
}

function detectMelonLoader(root) {
  const melonDirectory = path.join(root, "MelonLoader");
  const doorstopConfig = path.join(root, "doorstop_config.ini");
  const doorstopReferencesMelonLoader = fs.existsSync(doorstopConfig) &&
    safeReadText(doorstopConfig).toLowerCase().includes("melonloader");

  const exists = fs.existsSync(melonDirectory) || doorstopReferencesMelonLoader;
  if (!exists) return { exists: false, modDllCount: 0 };

  return {
    exists: true,
    modDllCount: countDlls(path.join(root, "Mods"))
  };
}

function countDlls(directory) {
  if (!fs.existsSync(directory)) return 0;

  let count = 0;
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) count += countDlls(fullPath);
    else if (entry.isFile() && entry.name.toLowerCase().endsWith(".dll")) count++;
  }
  return count;
}

function safeReadText(file) {
  try {
    return fs.readFileSync(file, "utf8");
  } catch {
    return "";
  }
}

async function getIconDataUrl(exePath) {
  try {
    if (!exePath || !fs.existsSync(exePath)) return null;
    const image = await app.getFileIcon(exePath, { size: "normal" });
    return image.isEmpty() ? null : image.toDataURL();
  } catch {
    return null;
  }
}

function findSteamLibraries() {
  const roots = new Set();
  for (const root of findSteamRoots()) {
    if (!fs.existsSync(root)) continue;
    roots.add(root);

    const libraryFile = path.join(root, "steamapps", "libraryfolders.vdf");
    if (!fs.existsSync(libraryFile)) continue;

    const text = fs.readFileSync(libraryFile, "utf8");
    for (const match of text.matchAll(/"path"\s+"([^"]+)"/gi)) {
      const library = match[1].replace(/\\\\/g, "\\");
      if (fs.existsSync(library)) roots.add(library);
    }
  }

  for (const drive of "CDEFGHIJKLMNOPQRSTUVWXYZ") {
    const candidate = `${drive}:\\SteamLibrary`;
    if (fs.existsSync(candidate)) roots.add(candidate);
  }

  return [...roots];
}

function findSteamRoots() {
  const roots = new Set();
  for (const key of [
    ["HKCU\\Software\\Valve\\Steam", "SteamPath"],
    ["HKCU\\Software\\Valve\\Steam", "InstallPath"],
    ["HKLM\\Software\\WOW6432Node\\Valve\\Steam", "InstallPath"]
  ]) {
    const value = readRegistryValue(key[0], key[1]);
    if (value) roots.add(value.replace(/\//g, "\\"));
  }

  for (const candidate of [
    path.join(process.env["ProgramFiles(x86)"] || "C:\\Program Files (x86)", "Steam"),
    path.join(process.env.ProgramFiles || "C:\\Program Files", "Steam")
  ]) {
    if (fs.existsSync(candidate)) roots.add(candidate);
  }

  return [...roots];
}

function readRegistryValue(key, valueName) {
  try {
    const output = childProcess.execFileSync("reg", ["query", key, "/v", valueName], {
      windowsHide: true,
      encoding: "utf8"
    });
    const line = output.split(/\r?\n/).find((entry) => entry.includes(valueName));
    if (!line) return null;
    const parts = line.trim().split(/\s{2,}/);
    return parts[2] || null;
  } catch {
    return null;
  }
}

function parseVdfPairs(text) {
  const values = {};
  for (const match of text.matchAll(/"([^"]+)"\s+"([^"]*)"/g)) {
    values[match[1].toLowerCase()] = match[2];
  }
  return values;
}

function readPeArchitecture(exePath) {
  try {
    const buffer = fs.readFileSync(exePath);
    if (buffer.readUInt16LE(0) !== 0x5a4d) return "Unknown";
    const peOffset = buffer.readInt32LE(0x3c);
    if (buffer.readUInt32LE(peOffset) !== 0x00004550) return "Unknown";
    const machine = buffer.readUInt16LE(peOffset + 4);
    if (machine === 0x014c) return "X86";
    if (machine === 0x8664) return "X64";
    return "Unknown";
  } catch {
    return "Unknown";
  }
}

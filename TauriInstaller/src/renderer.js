const invoke = window.__TAURI__.core.invoke;
const listen = window.__TAURI__.event.listen;

const state = {
  games: [],
  selected: null,
  melonPaths: []
};

const gamesEl = document.getElementById("games");
const searchEl = document.getElementById("search");
const selectedNameEl = document.getElementById("selectedName");
const selectedMetaEl = document.getElementById("selectedMeta");
const rosemodNameEl = document.getElementById("rosemodName");
const rosemodMetaEl = document.getElementById("rosemodMeta");
const installEl = document.getElementById("install");
const installRoseModEl = document.getElementById("installRoseMod");
const pickMelonsEl = document.getElementById("pickMelons");
const forcePayloadEl = document.getElementById("forcePayload");
const diagnoseEl = document.getElementById("diagnose");
const logEl = document.getElementById("log");

document.getElementById("refresh").addEventListener("click", refreshGames);
document.getElementById("addGame").addEventListener("click", addGame);
diagnoseEl.addEventListener("click", diagnoseSelected);
pickMelonsEl.addEventListener("click", pickMelons);
installEl.addEventListener("click", installSelected);
installRoseModEl.addEventListener("click", installRoseModSelected);
searchEl.addEventListener("input", renderGames);
for (const button of document.querySelectorAll(".tab-button")) {
  button.addEventListener("click", () => setActiveTab(button.dataset.tab));
}

listen("install-output", (event) => log(event.payload));
refreshGames();

async function refreshGames() {
  setBusy(true);
  log("Scanning installed Steam Unity games...");
  try {
    state.games = await invoke("scan_games");
    if (state.selected) {
      state.selected = state.games.find((game) => game.game.rootDirectory === state.selected.game.rootDirectory) || null;
    }
    log(`Found ${state.games.length} Unity game(s).`);
    renderGames();
    updateSelection();
  } catch (error) {
    log(`Scan failed: ${formatError(error)}`);
  } finally {
    setBusy(false);
  }
}

async function addGame() {
  const game = await invoke("add_game");
  if (!game) return;

  state.games = state.games.filter((entry) => entry.game.rootDirectory !== game.game.rootDirectory);
  state.games.unshift(game);
  state.selected = game;
  renderGames();
  updateSelection();
  log(`Added ${game.name}.`);
}

async function pickMelons() {
  const paths = await invoke("pick_melons");
  if (!paths || paths.length === 0) return;

  state.melonPaths = paths;
  pickMelonsEl.textContent = `Melon DLLs (${paths.length})`;
  log(`Selected ${paths.length} melon DLL(s).`);
}

async function installSelected() {
  if (!state.selected || !state.selected.canInstall) return;

  const game = state.selected;
  const request = {
    name: game.name,
    game: game.game,
    melonPaths: state.melonPaths,
    forcePayload: forcePayloadEl.checked,
    installBepInEx: false,
    runGameBeforeShim: false,
    removeMelonLoader: false,
    migrateMelonMods: false
  };

  if (game.melonloader?.exists) {
    const ok = confirm(`${game.name} already has MelonLoader installed.\n\nRemove MelonLoader and migrate ${game.melonloader.modDllCount} DLL(s) from Mods?`);
    if (!ok) return;
    request.removeMelonLoader = true;
    request.migrateMelonMods = true;
  }

  if (!game.bepinex.exists) {
    const ok = confirm("BepInEx 6 is missing.\n\nInstall BepInEx, launch the game once, then install MelonCompat?");
    if (!ok) return;
    request.installBepInEx = true;
    request.runGameBeforeShim = true;
  }

  setBusy(true);
  log(`Installing for ${game.name}...`);
  try {
    await invoke("install_game", { request });
    log("Install complete.");
    await refreshGames();
  } catch (error) {
    log(`Install failed: ${formatError(error)}`);
  } finally {
    setBusy(false);
  }
}

async function installRoseModSelected() {
  if (!state.selected) return;

  const game = state.selected;
  const request = {
    name: game.name,
    game: game.game,
    removeBepInEx: false
  };

  if (game.game.backend === "Il2Cpp" && !game.rosemod?.interopReady && !game.bepinex?.interopReady) {
    log("RoseMod/interop is not populated yet. IL2CPP mods that reference UnityEngine.CoreModule or Assembly-CSharp may need generated interop DLLs in RoseMod/interop.");
  }

  if (game.bepinex?.exists) {
    request.removeBepInEx = confirm(`${game.name} already has BepInEx installed.\n\nRemove BepInEx after RoseMod installs? Existing generated interop will be copied into RoseMod/interop first.`);
  }

  setBusy(true);
  log(`Installing RoseMod for ${game.name}...`);
  try {
    await invoke("install_rosemod", { request });
    log("RoseMod install complete.");
    await refreshGames();
  } catch (error) {
    log(`RoseMod install failed: ${formatError(error)}`);
  } finally {
    setBusy(false);
  }
}

async function diagnoseSelected() {
  if (!state.selected) return;

  const game = state.selected;
  const request = {
    name: game.name,
    game: game.game,
    melonPaths: state.melonPaths
  };

  setBusy(true);
  log(`Running diagnostics for ${game.name}...`);
  try {
    await invoke("diagnose_game", { request });
    log("Diagnostics complete.");
  } catch (error) {
    log(`Diagnostics failed: ${formatError(error)}`);
  } finally {
    setBusy(false);
  }
}

function renderGames() {
  const query = searchEl.value.trim().toLowerCase();
  gamesEl.innerHTML = "";

  const filtered = state.games.filter((game) => {
    const haystack = `${game.name} ${game.platform} ${game.game.backend} ${game.bepinex.backend} ${game.bepinex.majorVersion} ${game.melonloader?.exists ? "melonloader" : ""} ${game.rosemod?.exists ? "rosemod" : ""}`.toLowerCase();
    return haystack.includes(query);
  });

  if (filtered.length === 0) {
    const empty = document.createElement("div");
    empty.className = "empty";
    empty.textContent = "No matching Unity games found.";
    gamesEl.appendChild(empty);
    return;
  }

  for (const game of filtered) {
    const card = document.createElement("button");
    card.type = "button";
    card.className = [
      "game-card",
      game.canInstall ? "" : "blocked",
      state.selected?.game.rootDirectory === game.game.rootDirectory ? "selected" : ""
    ].filter(Boolean).join(" ");
    card.addEventListener("click", () => {
      state.selected = game;
      renderGames();
      updateSelection();
    });

    const icon = document.createElement("img");
    icon.className = "game-icon";
    icon.alt = "";
    icon.src = game.icon || "./logo-cropped.png";
    card.appendChild(icon);

    const name = document.createElement("div");
    name.className = "game-name";
    name.innerHTML = `<strong>${escapeHtml(game.name)}</strong><span>${escapeHtml(game.game.rootDirectory)}</span>`;
    card.appendChild(name);

    card.appendChild(badge(game.platform));
    card.appendChild(badge(game.game.backend));
    card.appendChild(badge(bepinexText(game), bepinexTone(game)));
    card.appendChild(badge(melonLoaderText(game), game.melonloader?.exists ? "warn" : ""));
    card.appendChild(badge(roseModText(game), game.rosemod?.exists ? "good" : ""));

    gamesEl.appendChild(card);
  }
}

function updateSelection() {
  if (!state.selected) {
    selectedNameEl.textContent = "No game selected";
    selectedMetaEl.textContent = "Select a Unity game to install or update MelonCompat.";
    rosemodNameEl.textContent = "No game selected";
    rosemodMetaEl.textContent = "Select a Unity game to install the optional RoseMod loader.";
    installEl.disabled = true;
    installRoseModEl.disabled = true;
    diagnoseEl.disabled = true;
    installEl.textContent = "Install";
    return;
  }

  const game = state.selected;
  selectedNameEl.textContent = game.name;
  selectedMetaEl.textContent = statusText(game);
  installEl.disabled = !game.canInstall;
  diagnoseEl.disabled = false;
  installEl.textContent = game.bepinex.exists ? "Install" : "Install BepInEx";

  rosemodNameEl.textContent = game.name;
  const interopText = game.game.backend === "Il2Cpp" && !game.rosemod?.interopReady
    ? game.bepinex?.interopReady
      ? " | existing interop will be imported into RoseMod"
      : " | RoseMod/interop not populated"
    : "";
  rosemodMetaEl.textContent = game.rosemod?.exists
    ? `${game.platform} | ${game.game.backend} | RoseMod is installed at ${game.rosemod.corePath}${interopText}`
    : `${game.platform} | ${game.game.backend} | installs standalone RoseMod loader files${interopText}`;
  installRoseModEl.disabled = false;
  installRoseModEl.textContent = game.rosemod?.exists ? "Update RoseMod" : "Install RoseMod";
}

function badge(text, tone = "") {
  const el = document.createElement("span");
  el.className = ["badge", tone].filter(Boolean).join(" ");
  el.textContent = text;
  return el;
}

function bepinexText(game) {
  if (!game.bepinex.exists) return "Needs BepInEx";
  if (game.bepinex.majorVersion !== 6) return `BepInEx ${game.bepinex.majorVersion}`;
  if (game.bepinex.interopReady) return `BepInEx 6 ${game.bepinex.backend} + interop`;
  return `BepInEx 6 ${game.bepinex.backend}`;
}

function bepinexTone(game) {
  if (!game.bepinex.exists) return "warn";
  return game.canInstall ? "good" : "bad";
}

function melonLoaderText(game) {
  if (!game.melonloader?.exists) return "No ML";
  return `MelonLoader${game.melonloader.modDllCount ? ` ${game.melonloader.modDllCount}` : ""}`;
}

function roseModText(game) {
  return game.rosemod?.exists ? "RoseMod" : "No RM";
}

function statusText(game) {
  if (game.canInstall) {
    if (!game.bepinex.exists) {
      return `${game.platform} | ${game.game.backend} | BepInEx 6 will be installed, the game will run once, then the shim will be installed`;
    }

    const melonText = game.melonloader?.exists ? " | MelonLoader will be removed first" : "";
    return `${game.platform} | ${game.game.backend} | ${bepinexText(game)} | ${game.game.architecture}${melonText}`;
  }
  if (!game.bepinex.exists) {
    return `${game.platform} | ${game.game.backend} | blocked: Unity backend could not be detected`;
  }
  if (game.bepinex.majorVersion !== 6) {
    return `${game.platform} | ${game.game.backend} | blocked: BepInEx 6 required`;
  }
  return `${game.platform} | ${game.game.backend} | blocked: BepInEx backend mismatch`;
}

function setBusy(busy) {
  document.body.classList.toggle("busy", busy);
  installEl.disabled = busy || !state.selected?.canInstall;
  installRoseModEl.disabled = busy || !state.selected;
  diagnoseEl.disabled = busy || !state.selected;
}

function setActiveTab(tab) {
  for (const button of document.querySelectorAll(".tab-button")) {
    button.classList.toggle("active", button.dataset.tab === tab);
  }

  document.getElementById("meloncompatPanel").classList.toggle("active", tab === "meloncompat");
  document.getElementById("rosemodPanel").classList.toggle("active", tab === "rosemod");
}

function log(message) {
  const time = new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
  logEl.textContent += `[${time}] ${message}\n`;
  logEl.scrollTop = logEl.scrollHeight;
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll("\"", "&quot;");
}

function formatError(error) {
  return typeof error === "string" ? error : error?.message || String(error);
}

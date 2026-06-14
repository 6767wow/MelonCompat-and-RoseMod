const state = {
  games: [],
  selected: null,
  melonPaths: []
};

const gamesEl = document.getElementById("games");
const searchEl = document.getElementById("search");
const selectedNameEl = document.getElementById("selectedName");
const selectedMetaEl = document.getElementById("selectedMeta");
const installEl = document.getElementById("install");
const pickMelonsEl = document.getElementById("pickMelons");
const forcePayloadEl = document.getElementById("forcePayload");
const logEl = document.getElementById("log");

document.getElementById("refresh").addEventListener("click", refreshGames);
document.getElementById("addGame").addEventListener("click", addGame);
pickMelonsEl.addEventListener("click", pickMelons);
installEl.addEventListener("click", installSelected);
searchEl.addEventListener("input", renderGames);

window.melonCompat.onInstallOutput((line) => log(line));

refreshGames();

async function refreshGames() {
  setBusy(true);
  log("Scanning installed Steam Unity games...");
  try {
    state.games = await window.melonCompat.scanGames();
    if (state.selected) {
      state.selected = state.games.find((game) => game.game.rootDirectory === state.selected.game.rootDirectory) || null;
    }
    log(`Found ${state.games.length} Unity game(s).`);
    renderGames();
    updateSelection();
  } catch (error) {
    log(`Scan failed: ${error.message}`);
  } finally {
    setBusy(false);
  }
}

async function addGame() {
  const game = await window.melonCompat.addGame();
  if (!game) return;

  state.games = state.games.filter((entry) => entry.game.rootDirectory !== game.game.rootDirectory);
  state.games.unshift(game);
  state.selected = game;
  renderGames();
  updateSelection();
  log(`Added ${game.name}.`);
}

async function pickMelons() {
  const paths = await window.melonCompat.pickMelons();
  if (!paths || paths.length === 0) return;

  state.melonPaths = paths;
  pickMelonsEl.textContent = `Melon DLLs (${paths.length})`;
  log(`Selected ${paths.length} melon DLL(s).`);
}

async function installSelected() {
  if (!state.selected || !state.selected.canInstall) return;

  setBusy(true);
  log(`Installing for ${state.selected.name}...`);
  try {
    await window.melonCompat.installGame({
      name: state.selected.name,
      game: state.selected.game,
      melonPaths: state.melonPaths,
      forcePayload: forcePayloadEl.checked
    });
    log("Install complete.");
    await refreshGames();
  } catch (error) {
    log(`Install failed: ${error.message}`);
  } finally {
    setBusy(false);
  }
}

function renderGames() {
  const query = searchEl.value.trim().toLowerCase();
  gamesEl.innerHTML = "";

  const filtered = state.games.filter((game) => {
    const haystack = `${game.name} ${game.platform} ${game.game.backend} ${game.bepinex.backend} ${game.bepinex.majorVersion} ${game.melonloader?.exists ? "melonloader" : ""}`.toLowerCase();
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
    icon.src = game.icon || "../assets/logo-cropped.png";
    card.appendChild(icon);

    const name = document.createElement("div");
    name.className = "game-name";
    name.innerHTML = `<strong>${escapeHtml(game.name)}</strong><span>${escapeHtml(game.game.rootDirectory)}</span>`;
    card.appendChild(name);

    card.appendChild(badge(game.platform));
    card.appendChild(badge(game.game.backend));
    card.appendChild(badge(bepinexText(game), bepinexTone(game)));
    card.appendChild(badge(melonLoaderText(game), game.melonloader?.exists ? "warn" : ""));

    gamesEl.appendChild(card);
  }
}

function updateSelection() {
  if (!state.selected) {
    selectedNameEl.textContent = "No game selected";
    selectedMetaEl.textContent = "Select a Unity game to install or update MelonCompat.";
    installEl.disabled = true;
    installEl.textContent = "Install";
    return;
  }

  const game = state.selected;
  selectedNameEl.textContent = game.name;
  selectedMetaEl.textContent = statusText(game);
  installEl.disabled = !game.canInstall;
  installEl.textContent = game.bepinex.exists ? "Install" : "Install BepInEx";
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

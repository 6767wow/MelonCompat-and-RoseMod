const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("melonCompat", {
  scanGames: () => ipcRenderer.invoke("scan-games"),
  addGame: () => ipcRenderer.invoke("add-game"),
  pickMelons: () => ipcRenderer.invoke("pick-melons"),
  installGame: (request) => ipcRenderer.invoke("install-game", request),
  openLog: (gameRoot) => ipcRenderer.invoke("open-log", gameRoot),
  onInstallOutput: (callback) => {
    const listener = (_event, line) => callback(line);
    ipcRenderer.on("install-output", listener);
    return () => ipcRenderer.removeListener("install-output", listener);
  }
});

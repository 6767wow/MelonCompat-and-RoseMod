const tauri = window.__TAURI__;
const invoke = tauri?.core?.invoke;
const listen = tauri?.event?.listen;

const sample = `rosev "My RoseV Mod" id "com.example.myrosevmod" version "1.0.0" author "Me"
namespace MyMods
class MyRoseVMod

use unity
use melonloader
use bepinex
use rosemod

import csharp
import unity.core
import unity.ai
import unity.scene
import unity.ui
import melonloader.core
import melonloader.utils
import bepinex.core
import bepinex.config
import bepinex.logging
import rosemod
import static System.Math

native c "Native/RoseVNativeSample.c" as RoseVNativeSample

setting showHud bool true "Show a small HUD label"
field updateCount int = 0
field score double = 0.0

members {
  private int csharpOwnedCounter;

  private void FullCSharpHelper()
  {
    Log.Info($"Full C# is running inside RoseV. Counter={csharpOwnedCounter}, Score={Round(score)}");
  }
}

make announce {
  say "This is a RoseV function"
}

make optionalNativePing {
  native call RoseVNativeSample.RoseVNativePing
}

when load {
  say "{mod} loaded on {loader}"
  call announce
  csharp {
    csharpOwnedCounter++;
    FullCSharpHelper();
  }
}

when update {
  add updateCount 1
  add score 0.25
  if updateCount atleast 1 {
    call announce
  }

  repeat 2 {
    csharp "csharpOwnedCounter++;"
  }

  every 300 {
    say "Still alive in {mod}"
  }
}

when gui {
  unity "if (setting_showHud != null && setting_showHud.Value) UnityEngine.GUI.Label(new UnityEngine.Rect(20, 20, 420, 24), \\"RoseV mod running\\");"
}
`;

const state = {
  path: null,
  name: "Untitled.rosev",
  contents: sample,
  dirty: false,
  workspace: [],
  diagnostics: [],
  compiling: false,
  status: "Ready"
};

const editor = document.getElementById("editor");
const lineNumbers = document.getElementById("lineNumbers");
const filePath = document.getElementById("filePath");
const currentTab = document.getElementById("currentTab");
const dirtyFlag = document.getElementById("dirtyFlag");
const fileList = document.getElementById("fileList");
const outline = document.getElementById("outline");
const workspaceCount = document.getElementById("workspaceCount");
const outlineCount = document.getElementById("outlineCount");
const diagnosticsEl = document.getElementById("bottom-diagnostics");
const outputEl = document.getElementById("bottom-output");
const generatedEl = document.getElementById("bottom-generated");
const palette = document.getElementById("palette");
const paletteInput = document.getElementById("paletteInput");
const paletteList = document.getElementById("paletteList");
const compileButton = document.getElementById("compileFile");
const modeStatus = document.getElementById("modeStatus");
const cursorPosition = document.getElementById("cursorPosition");
const diagnosticSummary = document.getElementById("diagnosticSummary");
const compileState = document.getElementById("compileState");

const commands = [
  { name: "New RoseV File", detail: "Create an empty RoseV source", run: newFile },
  { name: "Open File", detail: "Open a .rosev file", run: openFile },
  { name: "Open Folder", detail: "Open a workspace folder", run: openFolder },
  { name: "Save File", detail: "Save the current file", run: saveFile },
  { name: "Compile RoseV", detail: "Generate C# from the current RoseV file", run: compileFile },
  { name: "Format Document", detail: "Normalize indentation and trim trailing space", run: formatDocument },
  { name: "Load Everything Sample", detail: "Replace editor contents with a sample", run: loadSample },
  { name: "Insert when load", detail: "Add a load lifecycle block", run: () => insertSnippet("load") },
  { name: "Insert when update", detail: "Add an update lifecycle block", run: () => insertSnippet("update") },
  { name: "Insert function", detail: "Add a reusable make block", run: () => insertSnippet("function") },
  { name: "Insert import", detail: "Import built-in RoseV packs or C# namespaces", run: () => insertSnippet("import") },
  { name: "Insert setting", detail: "Add a simple config setting", run: () => insertSnippet("setting") },
  { name: "Insert C# block", detail: "Embed full C# in the current event or function", run: () => insertSnippet("csharp") },
  { name: "Insert members block", detail: "Add full C# fields and methods to the generated class", run: () => insertSnippet("members") },
  { name: "Insert Unity synvert", detail: "Switch into C# mode for Unity API code", run: () => insertSnippet("unitySynvert") },
  { name: "Insert native companion", detail: "Declare a C/C++/ASM native DLL bridge", run: () => insertSnippet("native") }
];

editor.value = state.contents;
renderAll();

if (listen) {
  listen("rosev-output", event => appendOutput(String(event.payload)));
}

document.getElementById("newFile").addEventListener("click", newFile);
document.getElementById("openFile").addEventListener("click", openFile);
document.getElementById("openFolder").addEventListener("click", openFolder);
document.getElementById("loadSample").addEventListener("click", loadSample);
document.getElementById("saveFile").addEventListener("click", saveFile);
document.getElementById("formatFile").addEventListener("click", formatDocument);
compileButton.addEventListener("click", compileFile);
document.getElementById("commandButton").addEventListener("click", openPalette);

for (const button of document.querySelectorAll(".activity-button")) {
  button.addEventListener("click", () => setPanel(button.dataset.panel));
}

for (const button of document.querySelectorAll(".bottom-tab")) {
  button.addEventListener("click", () => setBottom(button.dataset.bottom));
}

for (const button of document.querySelectorAll(".snippet")) {
  button.addEventListener("click", () => insertSnippet(button.dataset.snippet));
}

editor.addEventListener("input", () => {
  state.contents = editor.value;
  state.dirty = true;
  renderAll();
});

editor.addEventListener("scroll", () => {
  lineNumbers.scrollTop = editor.scrollTop;
});

editor.addEventListener("click", renderCursor);
editor.addEventListener("keyup", renderCursor);
editor.addEventListener("select", renderCursor);

editor.addEventListener("keydown", event => {
  if (event.key === "Tab") {
    event.preventDefault();
    indentSelection(event.shiftKey ? -1 : 1);
    return;
  }

  if (event.key === "Enter") {
    event.preventDefault();
    smartNewLine();
    return;
  }
});

document.addEventListener("keydown", event => {
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "s") {
    event.preventDefault();
    saveFile();
  }
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "o") {
    event.preventDefault();
    openFile();
  }
  if ((event.ctrlKey || event.metaKey) && event.key === "Enter") {
    event.preventDefault();
    compileFile();
  }
  if ((event.ctrlKey || event.metaKey) && event.shiftKey && event.key.toLowerCase() === "f") {
    event.preventDefault();
    formatDocument();
  }
  if ((event.ctrlKey || event.metaKey) && event.shiftKey && event.key.toLowerCase() === "p") {
    event.preventDefault();
    openPalette();
  }
  if (event.key === "Escape") {
    closePalette();
  }
});

palette.addEventListener("click", event => {
  if (event.target === palette) closePalette();
});

paletteInput.addEventListener("input", renderPalette);

async function newFile() {
  if (!confirmDirty()) return;
  state.path = null;
  state.name = "Untitled.rosev";
  state.contents = `rosev "New RoseV Mod" id "com.example.newmod" version "1.0.0" author "Me"
namespace MyMods
class NewRoseVMod

import csharp
import unity.core
import melonloader.core
import bepinex.core
import rosemod

use unity
use melonloader
use bepinex
use rosemod

when load {
  say "{mod} loaded"
}
`;
  state.dirty = false;
  editor.value = state.contents;
  setStatus("New file ready");
  renderAll();
}

async function openFile() {
  if (!invoke) {
    appendOutput("Open File needs the Tauri desktop app. Use the sample or paste code in browser preview.");
    setStatus("Open needs desktop app");
    return;
  }
  if (!confirmDirty()) return;

  try {
    const file = await invoke("open_file");
    if (file) applyFile(file);
  } catch (error) {
    appendOutput(`Open failed: ${formatError(error)}`);
    setStatus("Open failed");
  }
}

async function openFolder() {
  if (!invoke) {
    appendOutput("Open Folder needs the Tauri desktop app.");
    setStatus("Folder needs desktop app");
    return;
  }

  try {
    state.workspace = await invoke("open_folder");
    renderFiles();
    const fileCount = state.workspace.filter(entry => entry.kind === "file").length;
    setStatus(`Opened ${fileCount} ${fileCount === 1 ? "file" : "files"}`);
  } catch (error) {
    appendOutput(`Open folder failed: ${formatError(error)}`);
    setStatus("Open folder failed");
  }
}

async function saveFile() {
  if (!invoke) {
    downloadText(state.name, state.contents);
    state.dirty = false;
    setStatus("Downloaded file");
    renderChrome();
    return true;
  }

  try {
    const file = await invoke("save_file", {
      request: {
        path: state.path,
        contents: state.contents
      }
    });
    applyFile(file);
    appendOutput(`Saved ${file.path}`);
    setStatus("Saved file");
    return true;
  } catch (error) {
    appendOutput(`Save failed: ${formatError(error)}`);
    setStatus("Save failed");
    return false;
  }
}

async function compileFile() {
  if (state.compiling) return;
  setBottom("output");
  outputEl.textContent = "";

  if (!invoke) {
    appendOutput("Compile needs the Tauri desktop app so it can run RoseV.exe.");
    appendOutput("Browser preview can edit and validate syntax only.");
    setStatus("Compile needs desktop app");
    return;
  }

  setCompiling(true);
  setStatus("Compiling...");

  try {
    if (!state.path || state.dirty) {
      appendOutput("Saving before compile...");
      const saved = await saveFile();
      if (!saved) return;
    }

    const result = await invoke("compile_rosev", {
      request: {
        sourcePath: state.path,
        source: state.contents,
        outputPath: null
      }
    });

    appendOutput(result.output || (result.ok ? "Compile complete." : "Compile failed."));
    if (result.ok) {
      const generated = await invoke("read_file", { path: result.outputPath });
      generatedEl.textContent = generated.contents;
      appendOutput(`Generated ${result.outputPath}`);
      setStatus("Compile complete");
      setBottom("generated");
    } else {
      setStatus("Compile failed");
    }
  } catch (error) {
    appendOutput(`Compile failed: ${formatError(error)}`);
    setStatus("Compile failed");
  } finally {
    setCompiling(false);
  }
}

async function loadSample() {
  if (!confirmDirty()) return;
  if (invoke) {
    try {
      state.contents = await invoke("sample_source");
    } catch {
      state.contents = sample;
    }
  } else {
    state.contents = sample;
  }
  state.path = null;
  state.name = "EverythingSample.rosev";
  state.dirty = false;
  editor.value = state.contents;
  setStatus("Loaded sample");
  renderAll();
}

async function openWorkspaceFile(path) {
  if (!invoke) return;
  if (!confirmDirty()) return;
  try {
    const file = await invoke("read_file", { path });
    applyFile(file);
  } catch (error) {
    appendOutput(`Open workspace file failed: ${formatError(error)}`);
    setStatus("Open failed");
  }
}

function applyFile(file) {
  if (!file) return;
  state.path = file.path;
  state.name = file.name;
  state.contents = file.contents;
  state.dirty = false;
  editor.value = state.contents;
  setStatus(`Opened ${state.name}`);
  renderAll();
}

function renderAll() {
  renderChrome();
  renderLines();
  renderOutline();
  renderDiagnostics();
  renderCursor();
}

function renderChrome() {
  filePath.textContent = state.path || "Unsaved file";
  currentTab.textContent = state.name;
  dirtyFlag.textContent = state.dirty ? "Unsaved changes" : "";
  modeStatus.textContent = invoke ? "Desktop app" : "Browser preview";
  compileState.textContent = state.compiling ? "Compiling..." : state.status;
  compileButton.textContent = state.compiling ? "Compiling" : "Compile";
  compileButton.disabled = state.compiling;
}

function renderLines() {
  const count = Math.max(1, state.contents.split(/\r?\n/).length);
  lineNumbers.textContent = Array.from({ length: count }, (_, index) => String(index + 1)).join("\n");
}

function renderFiles() {
  fileList.innerHTML = "";
  const fileCount = state.workspace.filter(entry => entry.kind === "file").length;
  workspaceCount.textContent = `${fileCount} ${fileCount === 1 ? "file" : "files"}`;

  if (!state.workspace.length) {
    fileList.innerHTML = `<div class="empty">No RoseV workspace open.</div>`;
    return;
  }

  for (const entry of state.workspace) {
    const button = document.createElement("button");
    button.className = `file-item ${entry.kind}`;
    button.textContent = entry.kind === "folder" ? entry.name : entry.name;
    button.style.paddingLeft = `${8 + entry.depth * 12}px`;
    button.title = entry.path;
    if (entry.kind === "file") button.addEventListener("click", () => openWorkspaceFile(entry.path));
    fileList.appendChild(button);
  }
}

function renderOutline() {
  const items = [];
  const lines = state.contents.split(/\r?\n/);
  lines.forEach((line, index) => {
    const trimmed = line.trim();
    if (/^(rosev|namespace|class|setting|field|native|make|when|synvert)\b/.test(trimmed)) {
      items.push({ line: index + 1, text: trimmed });
    }
  });

  outline.innerHTML = "";
  outlineCount.textContent = `${items.length} ${items.length === 1 ? "symbol" : "symbols"}`;
  if (!items.length) {
    outline.innerHTML = `<div class="empty">No symbols yet.</div>`;
    return;
  }

  for (const item of items) {
    const button = document.createElement("button");
    button.className = "outline-item";
    button.textContent = `${item.line}: ${item.text}`;
    button.addEventListener("click", () => gotoLine(item.line));
    outline.appendChild(button);
  }
}

function renderDiagnostics() {
  state.diagnostics = validateRoseV(state.contents);
  diagnosticsEl.innerHTML = "";
  const errors = state.diagnostics.filter(diagnostic => diagnostic.level === "error").length;
  const warnings = state.diagnostics.filter(diagnostic => diagnostic.level === "warn").length;
  diagnosticSummary.className = errors ? "status-error" : warnings ? "status-warn" : "status-ok";
  diagnosticSummary.textContent = errors
    ? `${errors} ${errors === 1 ? "error" : "errors"}`
    : warnings
      ? `${warnings} ${warnings === 1 ? "warning" : "warnings"}`
      : "No issues";

  if (!state.diagnostics.length) {
    diagnosticsEl.innerHTML = `<div class="diagnostic"><div class="level info">Info</div><div>No obvious RoseV syntax issues.</div></div>`;
    return;
  }

  for (const diagnostic of state.diagnostics) {
    const row = document.createElement("div");
    row.className = "diagnostic";
    row.innerHTML = `<div class="level ${diagnostic.level}">${diagnostic.level.toUpperCase()}</div><div>Line ${diagnostic.line}: ${escapeHtml(diagnostic.message)}</div>`;
    row.addEventListener("click", () => gotoLine(diagnostic.line));
    diagnosticsEl.appendChild(row);
  }
}

function renderCursor() {
  const start = editor.selectionStart || 0;
  const end = editor.selectionEnd || start;
  const before = editor.value.slice(0, start);
  const lines = before.split(/\r?\n/);
  const line = lines.length;
  const column = lines[lines.length - 1].length + 1;
  const selected = Math.abs(end - start);
  cursorPosition.textContent = `Ln ${line}, Col ${column}${selected ? `, ${selected} selected` : ""}`;
}

function setStatus(message) {
  state.status = message;
  renderChrome();
}

function setCompiling(value) {
  state.compiling = value;
  renderChrome();
}

function indentSelection(direction) {
  const start = editor.selectionStart;
  const end = editor.selectionEnd;

  if (direction > 0 && start === end) {
    insertText("  ");
    return;
  }

  const value = editor.value;
  const lineStart = value.lastIndexOf("\n", start - 1) + 1;

  if (start === end) {
    const line = value.slice(lineStart, value.indexOf("\n", lineStart) === -1 ? value.length : value.indexOf("\n", lineStart));
    const removeCount = line.startsWith("  ") ? 2 : line.startsWith("\t") || line.startsWith(" ") ? 1 : 0;
    if (!removeCount) return;

    editor.setRangeText("", lineStart, lineStart + removeCount, "end");
    const cursor = Math.max(lineStart, start - removeCount);
    editor.setSelectionRange(cursor, cursor);
    editor.dispatchEvent(new Event("input", { bubbles: true }));
    editor.focus();
    return;
  }

  const selectedEnd = end > start && value[end - 1] === "\n" ? end - 1 : end;
  const nextBreak = value.indexOf("\n", selectedEnd);
  const blockEnd = nextBreak === -1 ? value.length : nextBreak;
  const block = value.slice(lineStart, blockEnd);
  const lines = block.split("\n");
  const replacement = lines.map(line => {
    if (direction > 0) return `  ${line}`;
    if (line.startsWith("  ")) return line.slice(2);
    if (line.startsWith("\t") || line.startsWith(" ")) return line.slice(1);
    return line;
  }).join("\n");

  editor.setRangeText(replacement, lineStart, blockEnd, "end");
  editor.setSelectionRange(lineStart, lineStart + replacement.length);
  editor.dispatchEvent(new Event("input", { bubbles: true }));
  editor.focus();
}

function smartNewLine() {
  const start = editor.selectionStart;
  const value = editor.value;
  const lineStart = value.lastIndexOf("\n", start - 1) + 1;
  const lineBeforeCursor = value.slice(lineStart, start);
  const indent = (lineBeforeCursor.match(/^\s*/) || [""])[0];
  const extra = lineBeforeCursor.trim().endsWith("{") ? "  " : "";
  insertText(`\n${indent}${extra}`);
}

function formatDocument() {
  const cursor = editor.selectionStart;
  const formatted = formatRoseVSource(editor.value);
  state.contents = formatted;
  state.dirty = true;
  editor.value = formatted;
  const nextCursor = Math.min(cursor, formatted.length);
  editor.setSelectionRange(nextCursor, nextCursor);
  setStatus("Formatted document");
  renderAll();
  editor.focus();
}

function formatRoseVSource(source) {
  let depth = 0;
  return source.split(/\r?\n/).map(line => {
    const trimmed = line.replace(/\s+$/g, "").trim();
    if (!trimmed) return "";

    if (trimmed.startsWith("}")) depth = Math.max(0, depth - 1);

    const formatted = `${"  ".repeat(depth)}${trimmed}`;
    depth = Math.max(0, depth + braceDelta(trimmed));
    return formatted;
  }).join("\n");
}

function validateRoseV(source) {
  const diagnostics = [];
  const lines = source.split(/\r?\n/);
  let depth = 0;
  let hasMetadata = false;
  let rawSynvert = false;
  let rawDepth = 0;

  const headers = /^(rosev|namespace|class|use|setting|field|import|native|synvert)\b/;
  const statements = /^(say|warn|error|emit|unity|cs|csharp|let|set|add|sub|mul|div|call|return|throw|native call|synvert)\b/;
  const blocks = /^(when|every|key|make|if|repeat|while|try|members|member|cs|csharp|synvert)\b/;
  const synvertModes = new Set(["rosev", "rose", "easy", "csharp", "cs", "c#", "unity", "melonloader", "melon", "bepinex", "rosemod", "il2cpp", "harmony"]);

  lines.forEach((line, index) => {
    const lineNo = index + 1;
    const trimmed = stripComment(line).trim();
    if (!trimmed) return;
    const synvertMatch = /^synvert\s*=\s*([A-Za-z0-9_#.-]+)/.exec(trimmed);

    if (rawSynvert) {
      if (rawDepth === 0 && synvertMatch && ["rosev", "rose", "easy"].includes(synvertMatch[1].toLowerCase())) {
        rawSynvert = false;
        return;
      }

      rawDepth += braceDelta(trimmed);
      if (rawDepth <= 0 && trimmed === "}") {
        rawSynvert = false;
        rawDepth = 0;
      } else if (rawDepth < 0) {
        rawDepth = 0;
      }
      return;
    }

    if (trimmed.startsWith("rosev ")) hasMetadata = true;

    if (synvertMatch) {
      const mode = synvertMatch[1].toLowerCase();
      if (!synvertModes.has(mode)) {
        diagnostics.push({ level: "error", line: lineNo, message: `Unknown synvert language '${mode}'.` });
        return;
      }

      if (!["rosev", "rose", "easy"].includes(mode)) {
        rawSynvert = true;
        rawDepth = trimmed.endsWith("{") ? 1 : 0;
      }
      return;
    }

    if (trimmed === "}") {
      depth--;
      if (depth < 0) {
        diagnostics.push({ level: "error", line: lineNo, message: "Closing brace has no matching block." });
        depth = 0;
      }
      return;
    }

    if (blocks.test(trimmed) && !trimmed.endsWith("{")) {
      diagnostics.push({ level: "error", line: lineNo, message: "Block commands must end with {." });
    }

    if (!headers.test(trimmed) && !statements.test(trimmed) && !blocks.test(trimmed)) {
      diagnostics.push({ level: "warn", line: lineNo, message: "Unknown RoseV command. Use the Help panel for valid commands." });
    }

    if ((trimmed.match(/"/g) || []).length % 2 !== 0) {
      diagnostics.push({ level: "error", line: lineNo, message: "Quoted text is not closed." });
    }

    if (trimmed.endsWith("{")) depth++;
  });

  if (!hasMetadata) diagnostics.unshift({ level: "warn", line: 1, message: "Add a rosev metadata line so the generated mod has a name, id, and version." });
  if (rawSynvert) diagnostics.push({ level: "warn", line: lines.length, message: "synvert raw syntax mode reaches the end of the file. Add synvert = rosev when you return to RoseV commands." });
  if (depth > 0) diagnostics.push({ level: "error", line: lines.length, message: `${depth} block(s) are missing a closing }.` });
  return diagnostics;
}

function braceDelta(text) {
  let inString = false;
  let inChar = false;
  let escaped = false;
  let delta = 0;
  for (const ch of text) {
    if (escaped) {
      escaped = false;
      continue;
    }
    if (ch === "\\") {
      escaped = true;
      continue;
    }
    if (!inChar && ch === "\"") {
      inString = !inString;
      continue;
    }
    if (!inString && ch === "'") {
      inChar = !inChar;
      continue;
    }
    if (inString || inChar) continue;
    if (ch === "{") delta++;
    if (ch === "}") delta--;
  }
  return delta;
}

function stripComment(line) {
  let inString = false;
  let escaped = false;
  for (let i = 0; i < line.length; i++) {
    const ch = line[i];
    if (escaped) {
      escaped = false;
      continue;
    }
    if (ch === "\\") {
      escaped = true;
      continue;
    }
    if (ch === "\"") inString = !inString;
    if (!inString && ch === "#") return line.slice(0, i);
  }
  return line;
}

function setPanel(panel) {
  for (const button of document.querySelectorAll(".activity-button")) button.classList.toggle("active", button.dataset.panel === panel);
  for (const section of document.querySelectorAll(".panel")) section.classList.toggle("active", section.id === `panel-${panel}`);
}

function setBottom(panel) {
  for (const button of document.querySelectorAll(".bottom-tab")) button.classList.toggle("active", button.dataset.bottom === panel);
  for (const section of document.querySelectorAll(".bottom-content")) section.classList.toggle("active", section.id === `bottom-${panel}`);
}

function openPalette() {
  palette.classList.remove("hidden");
  paletteInput.value = "";
  renderPalette();
  paletteInput.focus();
}

function closePalette() {
  palette.classList.add("hidden");
}

function renderPalette() {
  const query = paletteInput.value.trim().toLowerCase();
  const visible = commands.filter(command => `${command.name} ${command.detail}`.toLowerCase().includes(query));
  paletteList.innerHTML = "";
  for (const command of visible) {
    const button = document.createElement("button");
    button.className = "palette-item";
    button.innerHTML = `<strong>${escapeHtml(command.name)}</strong><span>${escapeHtml(command.detail)}</span>`;
    button.addEventListener("click", () => {
      closePalette();
      command.run();
    });
    paletteList.appendChild(button);
  }
}

function insertSnippet(kind) {
  const snippets = {
    load: `\nwhen load {\n  say "{mod} loaded on {loader}"\n}\n`,
    update: `\nwhen update {\n  every 300 {\n    say "Update alive"\n  }\n}\n`,
    function: `\nmake announce {\n  say "Reusable function called"\n}\n`,
    import: `\nimport csharp\nimport unity.core\nimport melonloader.core\nimport bepinex.core\nimport rosemod\n# Optional examples:\n# import unity.inputsystem\n# import unity.textmeshpro\n# import Il2CppInterop.Runtime when IL2CPP_REFERENCES\n`,
    setting: `\nsetting enabled bool true "Enable this feature"\n`,
    field: `\nfield counter int = 0\n`,
    if: `\nif enabled is true {\n  say "Feature is enabled"\n}\n`,
    key: `\nkey F8 {\n  warn "F8 pressed"\n}\n`,
    unity: `\nunity "UnityEngine.Debug.Log(\\"RoseV Unity escape\\");"\n`,
    csharp: `\nsynvert = csharp\nvar message = $"Running on {Context.Loader}";\nLog.Info(message);\nsynvert = rosev\n`,
    members: `\nsynvert = csharp\nprivate int fullCSharpCounter;\n\nprivate void FullCSharpHelper()\n{\n  fullCSharpCounter++;\n  Log.Info($"Full C# helper ran {fullCSharpCounter} time(s).");\n}\nsynvert = rosev\n`,
    unitySynvert: `\nsynvert = unity\nUnityEngine.Debug.Log("Unity is C# here, using UnityEngine imports.");\nsynvert = rosev\n`,
    native: `\nnative c "Native/MyNativeCode.c" as MyNativeCode\n\nmake callNative {\n  native call MyNativeCode.MyNativeFunction\n}\n`
  };
  insertText(snippets[kind] || "");
}

function insertText(text) {
  const start = editor.selectionStart;
  const end = editor.selectionEnd;
  editor.setRangeText(text, start, end, "end");
  editor.dispatchEvent(new Event("input", { bubbles: true }));
  editor.focus();
}

function gotoLine(line) {
  const lines = editor.value.split(/\r?\n/);
  let position = 0;
  for (let i = 0; i < Math.max(0, line - 1); i++) position += lines[i].length + 1;
  editor.focus();
  editor.setSelectionRange(position, position);
}

function appendOutput(message) {
  outputEl.textContent += `${message}\n`;
  outputEl.scrollTop = outputEl.scrollHeight;
}

function confirmDirty() {
  return !state.dirty || confirm("Discard unsaved changes?");
}

function downloadText(name, text) {
  const blob = new Blob([text], { type: "text/plain;charset=utf-8" });
  const link = document.createElement("a");
  link.href = URL.createObjectURL(blob);
  link.download = name || "Untitled.rosev";
  link.click();
  URL.revokeObjectURL(link.href);
}

function formatError(error) {
  return typeof error === "string" ? error : JSON.stringify(error);
}

function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, ch => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    "\"": "&quot;",
    "'": "&#039;"
  })[ch]);
}

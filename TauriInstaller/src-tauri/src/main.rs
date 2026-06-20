use base64::{engine::general_purpose::STANDARD, Engine as _};
use serde::{Deserialize, Serialize};
use std::{
    collections::{HashMap, HashSet},
    fs,
    io::{BufRead, BufReader, Read},
    path::{Path, PathBuf},
    process::{Command, Stdio},
    sync::{Arc, Mutex},
    thread,
};
use tauri::{Emitter, Manager, Window};

#[cfg(windows)]
use std::os::windows::process::CommandExt;

const CREATE_NO_WINDOW: u32 = 0x08000000;

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct GameEntry {
    name: String,
    platform: String,
    game: GameInfo,
    bepinex: BepInExInstall,
    melonloader: MelonLoaderInstall,
    rosemod: RoseModInstall,
    can_install: bool,
    icon: Option<String>,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct GameInfo {
    root_directory: String,
    executable_path: Option<String>,
    data_directory: String,
    backend: String,
    architecture: String,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct BepInExInstall {
    exists: bool,
    major_version: u32,
    backend: String,
    interop_ready: bool,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct MelonLoaderInstall {
    exists: bool,
    mod_dll_count: usize,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RoseModInstall {
    exists: bool,
    core_path: String,
    interop_ready: bool,
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct InstallRequest {
    name: String,
    game: GameInfo,
    melon_paths: Vec<String>,
    force_payload: bool,
    install_bep_in_ex: bool,
    run_game_before_shim: bool,
    remove_melon_loader: bool,
    migrate_melon_mods: bool,
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RoseModRequest {
    name: String,
    game: GameInfo,
    remove_bep_in_ex: bool,
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct DiagnoseRequest {
    name: String,
    game: GameInfo,
    melon_paths: Vec<String>,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct BackendResult {
    ok: bool,
    output: String,
}

#[tauri::command]
fn scan_games() -> Result<Vec<GameEntry>, String> {
    let mut games = scan_installed_games();
    games.sort_by(|left, right| {
        right
            .can_install
            .cmp(&left.can_install)
            .then_with(|| left.name.to_lowercase().cmp(&right.name.to_lowercase()))
    });
    Ok(games)
}

#[tauri::command]
fn add_game() -> Result<Option<GameEntry>, String> {
    let Some(path) = rfd::FileDialog::new()
        .set_title("Select Unity game executable")
        .add_filter("Executable", &["exe"])
        .pick_file()
    else {
        return Ok(None);
    };

    let root = path
        .parent()
        .ok_or_else(|| "Selected executable has no parent directory.".to_string())?;
    Ok(try_create_game(
        root,
        path.file_stem().and_then(|name| name.to_str()).unwrap_or("Game"),
        "Manual",
        Some(path.as_path()),
        None,
        &[],
    ))
}

#[tauri::command]
fn pick_melons() -> Result<Vec<String>, String> {
    let Some(paths) = rfd::FileDialog::new()
        .set_title("Select MelonLoader mod DLLs")
        .add_filter("DLL files", &["dll"])
        .pick_files()
    else {
        return Ok(Vec::new());
    };

    Ok(paths.into_iter().map(path_to_string).collect())
}

#[tauri::command]
fn install_game(app: tauri::AppHandle, window: Window, request: InstallRequest) -> Result<BackendResult, String> {
    let backend = get_backend_path(&app)?;
    let mut args = vec![
        "--game".to_string(),
        request
            .game
            .executable_path
            .clone()
            .unwrap_or_else(|| request.game.root_directory.clone()),
        "--yes".to_string(),
    ];

    if request.install_bep_in_ex {
        args.push("--install-bepinex".to_string());
    }
    if request.run_game_before_shim {
        args.push("--run-game-before-shim".to_string());
    }
    if request.remove_melon_loader {
        args.push("--remove-melonloader".to_string());
    }
    if request.migrate_melon_mods {
        args.push("--migrate-melon-mods".to_string());
    }
    if request.force_payload {
        args.push("--force-payload".to_string());
    }
    for melon_path in request.melon_paths {
        args.push("--melon".to_string());
        args.push(melon_path);
    }

    let _ = window.emit("install-output", format!("Running backend for {}...", request.name));
    run_backend(&backend, args, window)
}

#[tauri::command]
fn install_rosemod(app: tauri::AppHandle, window: Window, request: RoseModRequest) -> Result<BackendResult, String> {
    let backend = get_backend_path(&app)?;
    let mut args = vec![
        "--game".to_string(),
        request
            .game
            .executable_path
            .clone()
            .unwrap_or_else(|| request.game.root_directory.clone()),
        "--install-rosemod".to_string(),
        "--yes".to_string(),
    ];

    if request.remove_bep_in_ex {
        args.push("--remove-bepinex".to_string());
    }

    let _ = window.emit("install-output", format!("Running RoseMod backend for {}...", request.name));
    run_backend(&backend, args, window)
}

#[tauri::command]
fn diagnose_game(app: tauri::AppHandle, window: Window, request: DiagnoseRequest) -> Result<BackendResult, String> {
    let backend = get_backend_path(&app)?;
    let mut args = vec![
        "--game".to_string(),
        request
            .game
            .executable_path
            .clone()
            .unwrap_or_else(|| request.game.root_directory.clone()),
        "--doctor".to_string(),
    ];

    for melon_path in request.melon_paths {
        args.push("--melon".to_string());
        args.push(melon_path);
    }

    let _ = window.emit("install-output", format!("Running diagnostics for {}...", request.name));
    run_backend(&backend, args, window)
}

#[tauri::command]
fn open_log(game_root: String) -> Result<(), String> {
    let rosemod_log_path = PathBuf::from(&game_root).join("RoseMod").join("Logs").join("RoseMod.log");
    let old_framework_log_path = PathBuf::from(&game_root).join("MelonCompat").join("Logs").join("MelonCompat.log");
    let log_path = PathBuf::from(&game_root).join("BepInEx").join("LogOutput.log");
    let target = if rosemod_log_path.exists() {
        rosemod_log_path
    } else if old_framework_log_path.exists() {
        old_framework_log_path
    } else if log_path.exists() {
        log_path
    } else {
        PathBuf::from(game_root)
    };

    Command::new("explorer")
        .arg(target)
        .spawn()
        .map(|_| ())
        .map_err(|error| error.to_string())
}

fn scan_installed_games() -> Vec<GameEntry> {
    let mut games = Vec::new();
    let mut seen = HashSet::new();
    let icon_caches = find_steam_icon_cache_roots();

    for library in find_steam_libraries() {
        let steamapps = library.join("steamapps");
        let common = steamapps.join("common");
        if !steamapps.exists() || !common.exists() {
            continue;
        }

        let Ok(entries) = fs::read_dir(&steamapps) else {
            continue;
        };

        for entry in entries.flatten() {
            let path = entry.path();
            let Some(name) = path.file_name().and_then(|name| name.to_str()) else {
                continue;
            };
            if !name.to_lowercase().starts_with("appmanifest_") || !name.to_lowercase().ends_with(".acf") {
                continue;
            }

            let Ok(text) = fs::read_to_string(&path) else {
                continue;
            };
            let values = parse_vdf_pairs(&text);
            let Some(install_dir) = values.get("installdir") else {
                continue;
            };
            let root = common.join(install_dir);
            if !root.exists() {
                continue;
            }

            let app_id = values
                .get("appid")
                .cloned()
                .or_else(|| app_id_from_manifest_path(&path));
            let game_name = values.get("name").map(String::as_str).unwrap_or(install_dir);
            if let Some(game) = try_create_game(&root, game_name, "Steam", None, app_id.as_deref(), &icon_caches) {
                let key = game.game.root_directory.to_lowercase();
                if seen.insert(key) {
                    games.push(game);
                }
            }
        }
    }

    games
}

fn try_create_game(
    root: &Path,
    name: &str,
    platform: &str,
    explicit_exe: Option<&Path>,
    app_id: Option<&str>,
    icon_caches: &[PathBuf],
) -> Option<GameEntry> {
    let game = detect_unity_game(root, explicit_exe)?;
    if game.backend == "Unknown" {
        return None;
    }

    let bepinex = detect_bepinex(Path::new(&game.root_directory));
    let melonloader = detect_melonloader(Path::new(&game.root_directory));
    let rosemod = detect_rosemod(Path::new(&game.root_directory));
    let can_install = can_install(&game, &bepinex);

    Some(GameEntry {
        name: name.to_string(),
        platform: platform.to_string(),
        game,
        bepinex,
        melonloader,
        rosemod,
        can_install,
        icon: find_steam_icon_data_url(app_id, icon_caches),
    })
}

fn detect_unity_game(root: &Path, explicit_exe: Option<&Path>) -> Option<GameInfo> {
    let exe = explicit_exe.map(PathBuf::from).or_else(|| find_game_executable(root))?;
    let stem = exe.file_stem()?.to_str()?;
    let data_directory = root.join(format!("{stem}_Data"));
    if !data_directory.exists() {
        return None;
    }

    let backend = if root.join("GameAssembly.dll").exists() || data_directory.join("il2cpp_data").exists() {
        "Il2Cpp"
    } else if data_directory.join("Managed").exists() {
        "Mono"
    } else {
        "Unknown"
    };

    Some(GameInfo {
        root_directory: path_to_string(root.to_path_buf()),
        executable_path: Some(path_to_string(exe.clone())),
        data_directory: path_to_string(data_directory),
        backend: backend.to_string(),
        architecture: read_pe_architecture(&exe),
    })
}

fn find_game_executable(root: &Path) -> Option<PathBuf> {
    let mut exes = fs::read_dir(root)
        .ok()?
        .flatten()
        .map(|entry| entry.path())
        .filter(|path| path.extension().and_then(|ext| ext.to_str()).is_some_and(|ext| ext.eq_ignore_ascii_case("exe")))
        .filter(|path| {
            path.file_stem()
                .and_then(|stem| stem.to_str())
                .map(|stem| root.join(format!("{stem}_Data")).exists())
                .unwrap_or(false)
        })
        .collect::<Vec<_>>();

    exes.sort_by_key(|path| path.file_name().map(|name| name.len()).unwrap_or(usize::MAX));
    exes.into_iter().next()
}

fn detect_bepinex(root: &Path) -> BepInExInstall {
    let core = root.join("BepInEx").join("core");
    if !core.exists() {
        return BepInExInstall {
            exists: false,
            major_version: 0,
            backend: "Unknown".to_string(),
            interop_ready: false,
        };
    }

    let major_version = if core.join("BepInEx.Core.dll").exists() {
        6
    } else if core.join("BepInEx.dll").exists() {
        5
    } else {
        0
    };

    let backend = if core.join("BepInEx.Unity.IL2CPP.dll").exists() {
        "Il2Cpp"
    } else if core.join("BepInEx.Unity.Mono.dll").exists() {
        "Mono"
    } else {
        "Unknown"
    };

    BepInExInstall {
        exists: true,
        major_version,
        backend: backend.to_string(),
        interop_ready: root
            .join("BepInEx")
            .join("interop")
            .join("UnityEngine.CoreModule.dll")
            .exists(),
    }
}

fn detect_melonloader(root: &Path) -> MelonLoaderInstall {
    let doorstop_config = root.join("doorstop_config.ini");
    let doorstop_references_melonloader = fs::read_to_string(&doorstop_config)
        .map(|text| text.to_lowercase().contains("melonloader"))
        .unwrap_or(false);
    let exists = root.join("MelonLoader").exists() || doorstop_references_melonloader;

    MelonLoaderInstall {
        exists,
        mod_dll_count: if exists { count_dlls(&root.join("Mods")) } else { 0 },
    }
}

fn detect_rosemod(root: &Path) -> RoseModInstall {
    let rosemod_root = root.join("RoseMod");
    let core = rosemod_root.join("Core");
    RoseModInstall {
        exists: core.join("RoseMod.Core.dll").exists(),
        core_path: path_to_string(core),
        interop_ready: rosemod_root
            .join("interop")
            .join("UnityEngine.CoreModule.dll")
            .exists(),
    }
}

fn app_id_from_manifest_path(path: &Path) -> Option<String> {
    let name = path.file_stem()?.to_str()?;
    name.strip_prefix("appmanifest_").map(|value| value.to_string())
}

fn find_steam_icon_cache_roots() -> Vec<PathBuf> {
    let mut roots = HashSet::new();
    for root in find_steam_roots().into_iter().chain(find_steam_libraries()) {
        let cache = root.join("appcache").join("librarycache");
        if cache.exists() {
            roots.insert(cache);
        }
    }

    roots.into_iter().collect()
}

fn find_steam_icon_data_url(app_id: Option<&str>, icon_caches: &[PathBuf]) -> Option<String> {
    let app_id = app_id?;
    for cache in icon_caches {
        let directory = cache.join(app_id);
        if directory.exists() {
            if let Some(icon) = best_icon_in_directory(&directory).and_then(|path| image_data_url(&path)) {
                return Some(icon);
            }
        }

        if let Some(icon) = best_flat_icon(cache, app_id).and_then(|path| image_data_url(&path)) {
            return Some(icon);
        }
    }

    None
}

fn best_icon_in_directory(directory: &Path) -> Option<PathBuf> {
    let mut specific = Vec::new();
    let mut fallbacks = Vec::new();

    for entry in fs::read_dir(directory).ok()?.flatten() {
        let path = entry.path();
        if !is_supported_image(&path) {
            continue;
        }

        let name = path.file_name()?.to_string_lossy().to_lowercase();
        if name.starts_with("library_") || name.starts_with("header") || name.starts_with("hero") || name.starts_with("logo") {
            fallbacks.push(path);
        } else {
            specific.push(path);
        }
    }

    specific.sort_by_key(|path| fs::metadata(path).map(|meta| meta.len()).unwrap_or(u64::MAX));
    fallbacks.sort_by_key(|path| {
        let name = path.file_name().map(|value| value.to_string_lossy().to_lowercase()).unwrap_or_default();
        if name == "library_600x900.jpg" {
            0
        } else if name == "header.jpg" {
            1
        } else if name == "logo.png" {
            2
        } else {
            3
        }
    });

    specific.into_iter().next().or_else(|| fallbacks.into_iter().next())
}

fn best_flat_icon(cache: &Path, app_id: &str) -> Option<PathBuf> {
    let mut candidates = fs::read_dir(cache)
        .ok()?
        .flatten()
        .map(|entry| entry.path())
        .filter(|path| {
            let Some(name) = path.file_name().and_then(|value| value.to_str()) else {
                return false;
            };
            is_supported_image(path) && name.starts_with(app_id) && name.to_lowercase().contains("icon")
        })
        .collect::<Vec<_>>();

    candidates.sort_by_key(|path| fs::metadata(path).map(|meta| meta.len()).unwrap_or(u64::MAX));
    candidates.into_iter().next()
}

fn is_supported_image(path: &Path) -> bool {
    path.extension()
        .and_then(|extension| extension.to_str())
        .is_some_and(|extension| matches!(extension.to_lowercase().as_str(), "png" | "jpg" | "jpeg" | "webp"))
}

fn image_data_url(path: &Path) -> Option<String> {
    let bytes = fs::read(path).ok()?;
    let mime = match path.extension()?.to_str()?.to_lowercase().as_str() {
        "png" => "image/png",
        "jpg" | "jpeg" => "image/jpeg",
        "webp" => "image/webp",
        _ => return None,
    };
    Some(format!("data:{mime};base64,{}", STANDARD.encode(bytes)))
}

fn can_install(game: &GameInfo, bepinex: &BepInExInstall) -> bool {
    if !bepinex.exists {
        return game.backend != "Unknown";
    }

    bepinex.major_version == 6
        && bepinex.backend != "Unknown"
        && (game.backend == "Unknown" || game.backend == bepinex.backend)
}

fn count_dlls(directory: &Path) -> usize {
    let Ok(entries) = fs::read_dir(directory) else {
        return 0;
    };

    entries
        .flatten()
        .map(|entry| {
            let path = entry.path();
            if path.is_dir() {
                count_dlls(&path)
            } else if path.extension().and_then(|ext| ext.to_str()).is_some_and(|ext| ext.eq_ignore_ascii_case("dll")) {
                1
            } else {
                0
            }
        })
        .sum()
}

fn find_steam_libraries() -> Vec<PathBuf> {
    let mut roots = HashSet::new();
    for root in find_steam_roots() {
        if !root.exists() {
            continue;
        }

        roots.insert(root.clone());
        let library_file = root.join("steamapps").join("libraryfolders.vdf");
        if let Ok(text) = fs::read_to_string(library_file) {
            for line in text.lines() {
                let parts = quoted_parts(line);
                if parts.len() >= 2 && parts[0].eq_ignore_ascii_case("path") {
                    let path = PathBuf::from(parts[1].replace("\\\\", "\\"));
                    if path.exists() {
                        roots.insert(path);
                    }
                }
            }
        }
    }

    for drive in 'C'..='Z' {
        let candidate = PathBuf::from(format!("{drive}:\\SteamLibrary"));
        if candidate.exists() {
            roots.insert(candidate);
        }
    }

    roots.into_iter().collect()
}

fn find_steam_roots() -> Vec<PathBuf> {
    let mut roots = HashSet::new();
    for (key, value_name) in [
        ("HKCU\\Software\\Valve\\Steam", "SteamPath"),
        ("HKCU\\Software\\Valve\\Steam", "InstallPath"),
        ("HKLM\\Software\\WOW6432Node\\Valve\\Steam", "InstallPath"),
    ] {
        if let Some(value) = read_registry_value(key, value_name) {
            roots.insert(PathBuf::from(value.replace('/', "\\")));
        }
    }

    if let Some(program_files_x86) = std::env::var_os("ProgramFiles(x86)") {
        roots.insert(PathBuf::from(program_files_x86).join("Steam"));
    }
    if let Some(program_files) = std::env::var_os("ProgramFiles") {
        roots.insert(PathBuf::from(program_files).join("Steam"));
    }

    roots.into_iter().collect()
}

fn read_registry_value(key: &str, value_name: &str) -> Option<String> {
    let output = Command::new("reg")
        .args(["query", key, "/v", value_name])
        .output()
        .ok()?;
    if !output.status.success() {
        return None;
    }

    let text = String::from_utf8_lossy(&output.stdout);
    for line in text.lines() {
        let trimmed = line.trim_start();
        if !trimmed.starts_with(value_name) {
            continue;
        }

        let Some(type_start) = trimmed.find("REG_") else {
            continue;
        };
        let after_type = &trimmed[type_start..];
        let Some(type_end) = after_type.find(char::is_whitespace) else {
            continue;
        };
        let value = after_type[type_end..].trim();
        if !value.is_empty() {
            return Some(value.to_string());
        }
    }

    None
}

fn parse_vdf_pairs(text: &str) -> HashMap<String, String> {
    let mut values = HashMap::new();
    for line in text.lines() {
        let parts = quoted_parts(line);
        if parts.len() >= 2 {
            values.insert(parts[0].to_lowercase(), parts[1].clone());
        }
    }
    values
}

fn quoted_parts(line: &str) -> Vec<String> {
    let mut parts = Vec::new();
    let mut current = String::new();
    let mut in_quote = false;

    for ch in line.chars() {
        if ch == '"' {
            if in_quote {
                parts.push(current.clone());
                current.clear();
            }
            in_quote = !in_quote;
        } else if in_quote {
            current.push(ch);
        }
    }

    parts
}

fn read_pe_architecture(exe_path: &Path) -> String {
    let Ok(buffer) = fs::read(exe_path) else {
        return "Unknown".to_string();
    };
    if buffer.len() < 0x40 || u16_at(&buffer, 0) != Some(0x5A4D) {
        return "Unknown".to_string();
    }

    let Some(pe_offset) = u32_at(&buffer, 0x3C).map(|value| value as usize) else {
        return "Unknown".to_string();
    };
    if pe_offset + 6 >= buffer.len() || u32_at(&buffer, pe_offset) != Some(0x0000_4550) {
        return "Unknown".to_string();
    }

    match u16_at(&buffer, pe_offset + 4) {
        Some(0x014c) => "X86".to_string(),
        Some(0x8664) => "X64".to_string(),
        _ => "Unknown".to_string(),
    }
}

fn u16_at(buffer: &[u8], offset: usize) -> Option<u16> {
    Some(u16::from_le_bytes(buffer.get(offset..offset + 2)?.try_into().ok()?))
}

fn u32_at(buffer: &[u8], offset: usize) -> Option<u32> {
    Some(u32::from_le_bytes(buffer.get(offset..offset + 4)?.try_into().ok()?))
}

fn get_backend_path(app: &tauri::AppHandle) -> Result<PathBuf, String> {
    let mut candidates = Vec::new();
    if let Ok(exe) = std::env::current_exe() {
        if let Some(parent) = exe.parent() {
            candidates.push(parent.join("backend").join("MelonCompatInstaller.exe"));
            candidates.push(parent.join("resources").join("backend").join("MelonCompatInstaller.exe"));
        }
    }

    if let Ok(resource_dir) = app.path().resource_dir() {
        candidates.push(resource_dir.join("backend").join("MelonCompatInstaller.exe"));
        candidates.push(resource_dir.join("MelonCompatInstaller.exe"));
    }

    candidates.push(PathBuf::from("backend").join("MelonCompatInstaller.exe"));
    candidates.push(PathBuf::from("..").join("dist").join("installer").join("MelonCompatInstaller.exe"));

    candidates
        .into_iter()
        .find(|path| path.exists())
        .ok_or_else(|| "Backend installer was not found beside the Tauri app.".to_string())
}

fn run_backend(backend: &Path, args: Vec<String>, window: Window) -> Result<BackendResult, String> {
    let mut command = Command::new(backend);
    command.args(args).stdin(Stdio::null()).stdout(Stdio::piped()).stderr(Stdio::piped());

    #[cfg(windows)]
    command.creation_flags(CREATE_NO_WINDOW);

    let mut child = command.spawn().map_err(|error| error.to_string())?;
    let output = Arc::new(Mutex::new(String::new()));
    let mut handles = Vec::new();

    if let Some(stdout) = child.stdout.take() {
        handles.push(spawn_reader(stdout, window.clone(), output.clone()));
    }
    if let Some(stderr) = child.stderr.take() {
        handles.push(spawn_reader(stderr, window.clone(), output.clone()));
    }

    let status = child.wait().map_err(|error| error.to_string())?;
    for handle in handles {
        let _ = handle.join();
    }

    let output_text = output.lock().map(|value| value.clone()).unwrap_or_default();
    if status.success() {
        Ok(BackendResult {
            ok: true,
            output: output_text,
        })
    } else {
        Err(if output_text.trim().is_empty() {
            format!("Backend exited with code {:?}", status.code())
        } else {
            output_text
        })
    }
}

fn spawn_reader<R>(reader: R, window: Window, output: Arc<Mutex<String>>) -> thread::JoinHandle<()>
where
    R: Read + Send + 'static,
{
    thread::spawn(move || {
        let reader = BufReader::new(reader);
        for line in reader.lines().map_while(Result::ok) {
            if let Ok(mut output) = output.lock() {
                output.push_str(&line);
                output.push('\n');
            }
            let _ = window.emit("install-output", line);
        }
    })
}

fn path_to_string(path: PathBuf) -> String {
    path.to_string_lossy().to_string()
}

fn main() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![
            scan_games,
            add_game,
            pick_melons,
            install_game,
            install_rosemod,
            diagnose_game,
            open_log
        ])
        .run(tauri::generate_context!())
        .expect("failed to run MelonCompat Tauri installer");
}

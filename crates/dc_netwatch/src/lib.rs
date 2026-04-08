//! SysAdmin mod auto-dispatches technicians for broken/EOL devices

use dc_api::*;
use std::sync::{Mutex, OnceLock};

const PORTRAIT_PNG: &[u8] = include_bytes!("../assets/SysAdmin.jpg");

const EMPLOYEE_ID: &str = "sysadmin";
const EMPLOYEE_NAME: &str = "SysAdmin";
const EMPLOYEE_DESC: &str = "Automatically dispatches technicians to repair broken devices and replace end-of-life hardware.";
const SALARY_PER_HOUR: f32 = 10000.0;
const REQUIRED_REPUTATION: f32 = 2000.0;

const SCAN_INTERVAL: f32 = 5.0;
const MAX_DISPATCHES_PER_SCAN: u32 = 4;

static STATE: OnceLock<Mutex<NetWatchState>> = OnceLock::new();

struct NetWatchState {
    hired: bool,
    scan_timer: f32,
    total_dispatches: u32,
    broken_repairs: u32,
    eol_replacements: u32,
    idle_log_counter: u32,
    config_log_counter: u32,
}

impl NetWatchState {
    fn new() -> Self {
        Self {
            hired: false,
            scan_timer: 0.0,
            total_dispatches: 0,
            broken_repairs: 0,
            eol_replacements: 0,
            idle_log_counter: 0,
            config_log_counter: 0,
        }
    }
}

fn with_state<F, R>(f: F) -> Option<R>
where
    F: FnOnce(&mut NetWatchState) -> R,
{
    STATE
        .get()
        .and_then(|m| m.lock().ok())
        .map(|mut s| f(&mut s))
}

fn scan_and_dispatch(api: &Api, state: &mut NetWatchState) {
    if api.version() < 4 {
        dc_api::crash_log("[scan] API version < 4, v4 primitives not available");
        return;
    }

    let repair_broken_servers = api
        .config_get_bool("sysadmin", "repair_broken_servers")
        .unwrap_or(true);
    let repair_broken_switches = api
        .config_get_bool("sysadmin", "repair_broken_switches")
        .unwrap_or(true);
    let replace_eol_servers = api
        .config_get_bool("sysadmin", "replace_eol_servers")
        .unwrap_or(true);
    let replace_eol_switches = api
        .config_get_bool("sysadmin", "replace_eol_switches")
        .unwrap_or(true);
    let prioritize_switches = api
        .config_get_bool("sysadmin", "prioritize_switches")
        .unwrap_or(false);
    let max_dispatches = api
        .config_get_int("sysadmin", "max_dispatches")
        .unwrap_or(MAX_DISPATCHES_PER_SCAN as i32) as u32;

    state.config_log_counter += 1;
    let should_log_config = state.config_log_counter >= 6;
    if should_log_config {
        state.config_log_counter = 0;
    }

    if !repair_broken_servers
        && !repair_broken_switches
        && !replace_eol_servers
        && !replace_eol_switches
    {
        if should_log_config {
            dc_api::crash_log("[scan] ALL dispatch categories disabled skipping entire scan");
        }
        return;
    }

    let free_techs = api.get_free_technician_count().unwrap_or(0);
    if free_techs == 0 {
        state.idle_log_counter += 1;
        if state.idle_log_counter >= 6 {
            state.idle_log_counter = 0;
        }
        return;
    }

    let broken_servers = api.get_broken_server_count().unwrap_or(0);
    let broken_switches = api.get_broken_switch_count().unwrap_or(0);
    let eol_servers = api.get_eol_server_count().unwrap_or(0);
    let eol_switches = api.get_eol_switch_count().unwrap_or(0);

    let total_candidates = broken_servers + broken_switches + eol_servers + eol_switches;
    if total_candidates == 0 {
        state.idle_log_counter += 1;
        if state.idle_log_counter >= 6 {
            state.idle_log_counter = 0;
        }
        return;
    }

    state.idle_log_counter = 0;

    let mut dispatched: u32 = 0;

    struct DispatchStep {
        enabled: bool,
        is_repair: bool,
        label: &'static str,
    }

    let steps: [DispatchStep; 4] = if prioritize_switches {
        [
            DispatchStep {
                enabled: repair_broken_switches,
                is_repair: true,
                label: "broken switch (repair)",
            },
            DispatchStep {
                enabled: repair_broken_servers,
                is_repair: true,
                label: "broken server (repair)",
            },
            DispatchStep {
                enabled: replace_eol_switches,
                is_repair: false,
                label: "switch EOL (replacement)",
            },
            DispatchStep {
                enabled: replace_eol_servers,
                is_repair: false,
                label: "server EOL (replacement)",
            },
        ]
    } else {
        [
            DispatchStep {
                enabled: repair_broken_servers,
                is_repair: true,
                label: "broken server (repair)",
            },
            DispatchStep {
                enabled: repair_broken_switches,
                is_repair: true,
                label: "broken switch (repair)",
            },
            DispatchStep {
                enabled: replace_eol_servers,
                is_repair: false,
                label: "server EOL (replacement)",
            },
            DispatchStep {
                enabled: replace_eol_switches,
                is_repair: false,
                label: "switch EOL (replacement)",
            },
        ]
    };

    for (i, step) in steps.iter().enumerate() {
        if !step.enabled {
            dc_api::crash_log(&format!(
                "[scan] step {}: '{}' DISABLED, skipping",
                i, step.label
            ));
            continue;
        }

        dc_api::crash_log(&format!(
            "[scan] step {}: '{}' ENABLED, dispatching...",
            i, step.label
        ));

        while dispatched < max_dispatches {
            let result = match (step.is_repair, step.label.contains("switch")) {
                (true, false) => api.dispatch_repair_server(),
                (true, true) => api.dispatch_repair_switch(),
                (false, false) => api.dispatch_replace_server(),
                (false, true) => api.dispatch_replace_switch(),
            };

            match result {
                Some(1) => {
                    dispatched += 1;
                    state.total_dispatches += 1;
                    if step.is_repair {
                        state.broken_repairs += 1;
                    } else {
                        state.eol_replacements += 1;
                    }
                }
                Some(-1) => {
                    return;
                }
                _ => {
                    dc_api::crash_log(&format!(
                        "[scan] step {}: '{}' — no more targets",
                        i, step.label
                    ));
                    break;
                }
            }
        }

        if dispatched >= max_dispatches {
            break;
        }
    }
}

fn deploy_portrait(api: &Api) {
    let dir = std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(|d| d.join("UserData").join("ModAssets")));

    let Some(dir) = dir else {
        api.log_warning("[SysAdmin] Could not determine game directory for portrait deployment.");
        return;
    };

    if let Err(e) = std::fs::create_dir_all(&dir) {
        api.log_warning(&format!("[SysAdmin] Failed to create ModAssets dir: {}", e));
        return;
    }

    let png = dir.join(format!("{}.png", EMPLOYEE_ID));
    let jpg = dir.join(format!("{}.jpg", EMPLOYEE_ID));
    if png.exists() || jpg.exists() {
        return;
    }

    match std::fs::write(&png, PORTRAIT_PNG) {
        Ok(_) => api.log_info(&format!("[SysAdmin] Portrait deployed to {:?}", png)),
        Err(e) => api.log_warning(&format!("[SysAdmin] Failed to write portrait: {}", e)),
    }
}

#[dc_api::mod_entry(
    id = "sysadmin",
    name = "SysAdmin",
    version = "2.0.0",
    author = "Joniii",
    description = "Hireable SysAdmin employee: automatically dispatches technicians for broken/EOL devices."
)]
fn init(api: &Api) -> bool {
    if api.version() < 4 {
        api.log_error("[SysAdmin] Requires API v4+! Device/technician primitives not available.");
        api.log_error("[SysAdmin] Please update RustBridge.dll to the latest version.");
        return false;
    }

    let _ = STATE.set(Mutex::new(NetWatchState::new()));

    deploy_portrait(api);

    if api.version() >= 5 {
        match api.register_custom_employee(
            EMPLOYEE_ID,
            EMPLOYEE_NAME,
            EMPLOYEE_DESC,
            SALARY_PER_HOUR,
            REQUIRED_REPUTATION,
            true,
        ) {
            Some(1) => {
                api.log_info("[SysAdmin] Registered in HR System. Hire me from the computer!");
            }
            Some(0) => {
                api.log_warning("[SysAdmin] Already registered in HR System (duplicate id?).");
            }
            Some(code) => {
                api.log_warning(&format!(
                    "[SysAdmin] HR registration returned unexpected code: {}",
                    code
                ));
            }
            None => {
                api.log_warning("[SysAdmin] Could not register in HR System (API too old?).");
            }
        }

        if let Some(true) = api.is_custom_employee_hired(EMPLOYEE_ID) {
            with_state(|s| s.hired = true);
        }
    } else {
        with_state(|s| s.hired = true);
    }

    if api.version() >= 8 {
        api.config_register_bool(
            "sysadmin",
            "repair_broken_servers",
            "Repair Broken Servers",
            true,
            "Dispatch technicians to repair broken (dead) servers",
        );
        api.config_register_bool(
            "sysadmin",
            "repair_broken_switches",
            "Repair Broken Switches",
            true,
            "Dispatch technicians to repair broken (dead) switches",
        );
        api.config_register_bool(
            "sysadmin",
            "replace_eol_servers",
            "Replace EOL Servers",
            true,
            "Dispatch technicians to replace end-of-life servers",
        );
        api.config_register_bool(
            "sysadmin",
            "replace_eol_switches",
            "Replace EOL Switches",
            true,
            "Dispatch technicians to replace end-of-life switches",
        );
        api.config_register_bool(
            "sysadmin",
            "prioritize_switches",
            "Prioritize Switches",
            false,
            "Fix switches before servers (default: servers first)",
        );
        api.config_register_float(
            "sysadmin",
            "scan_interval",
            "Scan Interval (seconds)",
            5.0,
            1.0,
            60.0,
            "How often the SysAdmin checks for broken/EOL devices",
        );
        api.config_register_int(
            "sysadmin",
            "max_dispatches",
            "Max Dispatches Per Scan",
            10,
            1,
            50,
            "Maximum technician dispatches per scan cycle",
        );
        api.log_info("[SysAdmin] Configuration entries registered.");
    }

    api.log_info("[SysAdmin] Initialized successfully.");
    true
}

#[dc_api::on_update]
fn update(api: &Api, dt: f32) {
    let scan_interval = api
        .config_get_float("sysadmin", "scan_interval")
        .unwrap_or(SCAN_INTERVAL);

    let should_scan = with_state(|state| {
        if !state.hired {
            return false;
        }

        state.scan_timer += dt;
        if state.scan_timer >= scan_interval {
            state.scan_timer = 0.0;
            true
        } else {
            false
        }
    });

    if should_scan == Some(true) {
        with_state(|state| {
            scan_and_dispatch(api, state);
        });
    }
}

#[dc_api::on_scene_loaded]
fn scene_loaded(api: &Api, name: &str) {
    api.log_info(&format!("[SysAdmin] Scene loaded: {}", name));

    if api.version() >= 5 {
        if let Some(hired) = api.is_custom_employee_hired(EMPLOYEE_ID) {
            with_state(|s| s.hired = hired);
        }
    }

    if let Some(true) = with_state(|s| s.hired) {
        if api.version() >= 4 {
            let total_techs = api.get_total_technician_count().unwrap_or(0);
            let free_techs = api.get_free_technician_count().unwrap_or(0);
        }
    }
}

#[dc_api::on_event]
fn handle_event(api: &Api, event: Event) {
    match event {
        Event::CustomEmployeeHired { ref employee_id } if employee_id == EMPLOYEE_ID => {
            with_state(|s| s.hired = true);
            api.log_info("[SysAdmin] Hired! Starting infrastructure monitoring.");
        }

        Event::CustomEmployeeFired { ref employee_id } if employee_id == EMPLOYEE_ID => {
            with_state(|s| s.hired = false);
            api.log_info("[SysAdmin] Fired. Stopping all automated dispatches.");

            if let Some((total, repairs, replacements)) =
                with_state(|s| (s.total_dispatches, s.broken_repairs, s.eol_replacements))
            {
                api.log_info(&format!(
                    "[SysAdmin] Session stats before dismissal dispatches: {} (repairs: {}, replacements: {})",
                    total, repairs, replacements
                ));
            }
        }

        Event::GameLoaded => {
            if api.version() >= 5 {
                if let Some(hired) = api.is_custom_employee_hired(EMPLOYEE_ID) {
                    with_state(|s| s.hired = hired);
                    if hired {
                        api.log_info("[SysAdmin] Game loaded SysAdmin is hired, resuming.");
                    } else {
                        api.log_info("[SysAdmin] Game loaded SysAdmin not hired.");
                    }
                }
            } else {
                with_state(|s| s.hired = true);
            }
        }

        _ => {}
    }
}

#[dc_api::on_shutdown]
fn shutdown(api: &Api) {
    api.log_info("[SysAdmin] Shutting down. Goodbye!");

    with_state(|s| s.hired = false);
}

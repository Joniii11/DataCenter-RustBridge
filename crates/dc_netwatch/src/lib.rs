//! SysAdmin mod — auto-dispatches technicians for broken/EOL devices.
//!
//! The SysAdmin is a **hireable employee** in the HR System panel. The player
//! must hire the SysAdmin before auto-dispatch activates. Firing the SysAdmin
//! disables all automation (but keeps statistics for the session).
//!
//! This mod uses `dc_api` proc macros to eliminate all FFI boilerplate.

use dc_api::*;
use std::sync::{Mutex, OnceLock};

const EMPLOYEE_ID: &str = "sysadmin";
const EMPLOYEE_NAME: &str = "SysAdmin";
const EMPLOYEE_DESC: &str = "Automatically dispatches technicians to repair broken devices and replace end-of-life hardware.";
const SALARY_PER_HOUR: f32 = 4500.0;
const REQUIRED_REPUTATION: f32 = 500.0;

const SCAN_INTERVAL: f32 = 5.0;
const MAX_DISPATCHES_PER_SCAN: u32 = 10;

static STATE: OnceLock<Mutex<NetWatchState>> = OnceLock::new();

/// IDs for the 7 extra technician employees (must match TechnicianHiring.cs)
const TECH_IDS: [&str; 7] = [
    "tech_extra_1",
    "tech_extra_2",
    "tech_extra_3",
    "tech_extra_4",
    "tech_extra_5",
    "tech_extra_6",
    "tech_extra_7",
];

struct NetWatchState {
    /// Whether the SysAdmin is currently hired (toggled by HR events).
    hired: bool,
    scan_timer: f32,
    // statistics (persist across hire/fire within a session)
    total_dispatches: u32,
    broken_repairs: u32,
    eol_replacements: u32,
    /// Counter to throttle "nothing to do" logs (every 6th scan = ~30s)
    idle_log_counter: u32,
    /// How many extra technicians are currently hired through us
    extra_techs_hired: u32,
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
            extra_techs_hired: 0,
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

    let free_techs = api.get_free_technician_count().unwrap_or(0);
    if free_techs == 0 {
        state.idle_log_counter += 1;
        if state.idle_log_counter >= 6 {
            state.idle_log_counter = 0;
            dc_api::crash_log("[scan] no free technicians, skipping");
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
            dc_api::crash_log(&format!(
                "[scan] no candidates (broken_srv={} broken_sw={} eol_srv={} eol_sw={} free_techs={})",
                broken_servers, broken_switches, eol_servers, eol_switches, free_techs
            ));
        }
        return;
    }

    // We have work to do — reset idle counter
    state.idle_log_counter = 0;

    dc_api::crash_log(&format!(
        "[scan] candidates: broken_srv={} broken_sw={} eol_srv={} eol_sw={} free_techs={}",
        broken_servers, broken_switches, eol_servers, eol_switches, free_techs
    ));

    let mut dispatched: u32 = 0;

    // Priority order: broken devices first (they're actively down), then EOL

    // 1. Broken servers (highest priority — actively down)
    while dispatched < MAX_DISPATCHES_PER_SCAN {
        match api.dispatch_repair_server() {
            Some(1) => {
                dispatched += 1;
                state.total_dispatches += 1;
                state.broken_repairs += 1;
                api.log_info("[SysAdmin] Dispatched technician: broken server (repair)");
            }
            Some(-1) => {
                dc_api::crash_log("[dispatch] repair server: no free technician, stopping");
                return;
            }
            other => {
                dc_api::crash_log(&format!(
                    "[dispatch] repair server: no more targets (result={:?})",
                    other
                ));
                break;
            }
        }
    }

    // 2. Broken switches
    while dispatched < MAX_DISPATCHES_PER_SCAN {
        match api.dispatch_repair_switch() {
            Some(1) => {
                dispatched += 1;
                state.total_dispatches += 1;
                state.broken_repairs += 1;
                api.log_info("[SysAdmin] Dispatched technician: broken switch (repair)");
            }
            Some(-1) => {
                dc_api::crash_log("[dispatch] repair switch: no free technician, stopping");
                return;
            }
            other => {
                dc_api::crash_log(&format!(
                    "[dispatch] repair switch: no more targets (result={:?})",
                    other
                ));
                break;
            }
        }
    }

    // 3. EOL servers
    while dispatched < MAX_DISPATCHES_PER_SCAN {
        match api.dispatch_replace_server() {
            Some(1) => {
                dispatched += 1;
                state.total_dispatches += 1;
                state.eol_replacements += 1;
                api.log_info("[SysAdmin] Dispatched technician: server EOL (replacement)");
            }
            Some(-1) => {
                dc_api::crash_log("[dispatch] replace server: no free technician, stopping");
                return;
            }
            other => {
                dc_api::crash_log(&format!(
                    "[dispatch] replace server: no more targets (result={:?})",
                    other
                ));
                break;
            }
        }
    }

    // 4. EOL switches
    while dispatched < MAX_DISPATCHES_PER_SCAN {
        match api.dispatch_replace_switch() {
            Some(1) => {
                dispatched += 1;
                state.total_dispatches += 1;
                state.eol_replacements += 1;
                api.log_info("[SysAdmin] Dispatched technician: switch EOL (replacement)");
            }
            Some(-1) => {
                dc_api::crash_log("[dispatch] replace switch: no free technician, stopping");
                return;
            }
            other => {
                dc_api::crash_log(&format!(
                    "[dispatch] replace switch: no more targets (result={:?})",
                    other
                ));
                break;
            }
        }
    }

    if dispatched > 0 {
        api.log_info(&format!(
            "[SysAdmin] Scan complete, dispatched {} technician(s). Total: {} (repairs: {}, replacements: {})",
            dispatched, state.total_dispatches, state.broken_repairs, state.eol_replacements
        ));
    }
}

#[dc_api::mod_entry(
    id = "sysadmin",
    name = "SysAdmin",
    version = "1.1.0",
    author = "Joniii",
    description = "Hireable SysAdmin employee — automatically dispatches technicians for broken/EOL devices."
)]
fn init(api: &Api) -> bool {
    if api.version() < 4 {
        api.log_error("[SysAdmin] Requires API v4+! Device/technician primitives not available.");
        api.log_error("[SysAdmin] Please update DataCenterModLoader to the latest version.");
        return false;
    }

    let _ = STATE.set(Mutex::new(NetWatchState::new()));

    // Register as a hireable employee in the HR System panel (requires API v5)
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
            api.log_info("[SysAdmin] Already hired — resuming operations.");
        }
    } else {
        api.log_warning("[SysAdmin] API v5 not available — falling back to auto-enable mode.");
        with_state(|s| s.hired = true);
    }

    api.log_info("[SysAdmin] Initialized successfully.");
    true
}

#[dc_api::on_update]
fn update(api: &Api, dt: f32) {
    let should_scan = with_state(|state| {
        if !state.hired {
            return false;
        }

        state.scan_timer += dt;
        if state.scan_timer >= SCAN_INTERVAL {
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

    // Re-check hire state after scene transitions
    if api.version() >= 5 {
        if let Some(hired) = api.is_custom_employee_hired(EMPLOYEE_ID) {
            with_state(|s| s.hired = hired);
            if hired {
                api.log_info("[SysAdmin] Hired — watching your infrastructure.");
            } else {
                api.log_info("[SysAdmin] Not hired. Open HR System to hire me!");
            }
        }
    }

    if let Some(true) = with_state(|s| s.hired) {
        if let Some(total) = with_state(|s| s.total_dispatches) {
            api.log_info(&format!("[SysAdmin] Total dispatches so far: {}", total));
        }

        if api.version() >= 4 {
            let total_techs = api.get_total_technician_count().unwrap_or(0);
            let free_techs = api.get_free_technician_count().unwrap_or(0);
            api.log_info(&format!(
                "[SysAdmin] Technicians: {}/{} available",
                free_techs, total_techs
            ));
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
                    "[SysAdmin] Session stats before dismissal — dispatches: {} (repairs: {}, replacements: {})",
                    total, repairs, replacements
                ));
            }
        }

        // ── Extra technician hire/fire events ──────────────────────────
        Event::CustomEmployeeHired { ref employee_id }
            if TECH_IDS.iter().any(|id| id == employee_id) =>
        {
            let count = with_state(|s| {
                s.extra_techs_hired += 1;
                s.extra_techs_hired
            })
            .unwrap_or(0);
            api.log_info(&format!(
                "[SysAdmin] Extra technician hired: {} (total extra: {})",
                employee_id, count
            ));
            if api.version() >= 4 {
                let total = api.get_total_technician_count().unwrap_or(0);
                let free = api.get_free_technician_count().unwrap_or(0);
                api.log_info(&format!(
                    "[SysAdmin] Technician pool now: {}/{} available",
                    free, total
                ));
            }
        }

        Event::CustomEmployeeFired { ref employee_id }
            if TECH_IDS.iter().any(|id| id == employee_id) =>
        {
            let count = with_state(|s| {
                s.extra_techs_hired = s.extra_techs_hired.saturating_sub(1);
                s.extra_techs_hired
            })
            .unwrap_or(0);
            api.log_info(&format!(
                "[SysAdmin] Extra technician fired: {} (total extra: {})",
                employee_id, count
            ));
            if api.version() >= 4 {
                let total = api.get_total_technician_count().unwrap_or(0);
                let free = api.get_free_technician_count().unwrap_or(0);
                api.log_info(&format!(
                    "[SysAdmin] Technician pool now: {}/{} available",
                    free, total
                ));
            }
        }

        Event::DayEnded { day } => {
            let is_hired = with_state(|s| s.hired).unwrap_or(false);
            if !is_hired {
                return;
            }

            if let Some((total, repairs, replacements, extra)) = with_state(|s| {
                (
                    s.total_dispatches,
                    s.broken_repairs,
                    s.eol_replacements,
                    s.extra_techs_hired,
                )
            }) {
                api.log_info(&format!(
                    "[SysAdmin] Day {} report — dispatches: {} (repairs: {}, replacements: {}), extra techs: {}",
                    day, total, repairs, replacements, extra
                ));
            }
        }

        Event::GameLoaded => {
            // Re-check hire state after loading a save
            if api.version() >= 5 {
                if let Some(hired) = api.is_custom_employee_hired(EMPLOYEE_ID) {
                    with_state(|s| s.hired = hired);
                    if hired {
                        api.log_info("[SysAdmin] Game loaded — SysAdmin is hired, resuming.");
                    } else {
                        api.log_info("[SysAdmin] Game loaded — SysAdmin not hired.");
                    }
                }

                // Re-count extra technicians that were restored from save
                let mut extra_count = 0u32;
                for tech_id in &TECH_IDS {
                    if let Some(true) = api.is_custom_employee_hired(tech_id) {
                        extra_count += 1;
                    }
                }
                with_state(|s| s.extra_techs_hired = extra_count);
                if extra_count > 0 {
                    api.log_info(&format!(
                        "[SysAdmin] Game loaded — {} extra technician(s) restored from save.",
                        extra_count
                    ));
                }
            } else {
                // Fallback: re-enable
                with_state(|s| s.hired = true);
                api.log_info("[SysAdmin] Game loaded, NetWatch re-enabled (legacy mode).");
            }
        }

        Event::ServerBroken => {
            let is_hired = with_state(|s| s.hired).unwrap_or(false);
            if is_hired {
                api.log_info("[SysAdmin] Server broken detected, will dispatch on next scan.");
            }
        }

        Event::SwitchBroken => {
            let is_hired = with_state(|s| s.hired).unwrap_or(false);
            if is_hired {
                api.log_info("[SysAdmin] Switch broken detected, will dispatch on next scan.");
            }
        }

        _ => {}
    }
}

#[dc_api::on_shutdown]
fn shutdown(api: &Api) {
    let is_hired = with_state(|s| s.hired).unwrap_or(false);

    if let Some((total, repairs, replacements)) =
        with_state(|s| (s.total_dispatches, s.broken_repairs, s.eol_replacements))
    {
        api.log_info(&format!(
            "[SysAdmin] Shutting down (hired={}). Final stats — dispatches: {} (repairs: {}, replacements: {}). Goodbye!",
            is_hired, total, repairs, replacements
        ));
    } else {
        api.log_info("[SysAdmin] Shutting down. Goodbye!");
    }

    with_state(|s| s.hired = false);
}

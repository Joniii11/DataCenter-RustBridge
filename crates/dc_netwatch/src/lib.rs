//! SysAdmin mod — auto-dispatches technicians for broken/EOL devices.
//! This mod uses `dc_api` proc macros to eliminate all FFI boilerplate.

use dc_api::*;
use std::sync::{Mutex, OnceLock};

const SCAN_INTERVAL: f32 = 5.0;
const MAX_DISPATCHES_PER_SCAN: u32 = 2;
const DAILY_SALARY: f64 = 500.0;

static STATE: OnceLock<Mutex<NetWatchState>> = OnceLock::new();

struct NetWatchState {
    enabled: bool,
    scan_timer: f32,
    last_day: i64, // -1 = not yet seen

    // statistics
    total_dispatches: u32,
    broken_repairs: u32,
    eol_replacements: u32,
}

impl NetWatchState {
    fn new() -> Self {
        Self {
            enabled: false,
            scan_timer: 0.0,
            last_day: -1,
            total_dispatches: 0,
            broken_repairs: 0,
            eol_replacements: 0,
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

fn handle_salary(api: &Api, state: &mut NetWatchState) {
    let day = match api.get_day() {
        Some(d) => d as i64,
        None => return,
    };

    if state.last_day < 0 {
        state.last_day = day;
        return;
    }

    if day != state.last_day {
        state.last_day = day;

        let money = api.get_player_money();
        api.set_player_money(money - DAILY_SALARY);
        api.log_info(&format!(
            "[SysAdmin] Day {}, deducted daily salary: ${:.0}",
            day, DAILY_SALARY
        ));
    }
}

fn scan_and_dispatch(api: &Api, state: &mut NetWatchState) {
    if api.version() < 4 {
        dc_api::crash_log("[scan] API version < 4, v4 primitives not available");
        return;
    }

    let free_techs = api.get_free_technician_count().unwrap_or(0);
    if free_techs == 0 {
        return;
    }

    let broken_servers = api.get_broken_server_count().unwrap_or(0);
    let broken_switches = api.get_broken_switch_count().unwrap_or(0);
    let eol_servers = api.get_eol_server_count().unwrap_or(0);
    let eol_switches = api.get_eol_switch_count().unwrap_or(0);

    let total_candidates = broken_servers + broken_switches + eol_servers + eol_switches;
    if total_candidates == 0 {
        return;
    }

    dc_api::crash_log(&format!(
        "[scan] candidates: broken_srv={} broken_sw={} eol_srv={} eol_sw={} free_techs={}",
        broken_servers, broken_switches, eol_servers, eol_switches, free_techs
    ));

    let mut dispatched: u32 = 0;

    // Priority order: broken devices first (they're actively down), then EOL

    // 1. Broken servers
    if dispatched < MAX_DISPATCHES_PER_SCAN && broken_servers > 0 {
        if let Some(result) = api.dispatch_repair_server() {
            if result == 1 {
                dispatched += 1;
                state.total_dispatches += 1;
                state.broken_repairs += 1;
                api.log_info("[SysAdmin] Dispatched technician: broken server (repair)");
            } else if result == -1 {
                dc_api::crash_log("[dispatch] repair server: no free technician");
            }
        }
    }

    // 2. Broken switches
    if dispatched < MAX_DISPATCHES_PER_SCAN && broken_switches > 0 {
        if let Some(result) = api.dispatch_repair_switch() {
            if result == 1 {
                dispatched += 1;
                state.total_dispatches += 1;
                state.broken_repairs += 1;
                api.log_info("[SysAdmin] Dispatched technician: broken switch (repair)");
            } else if result == -1 {
                dc_api::crash_log("[dispatch] repair switch: no free technician");
            }
        }
    }

    // 3. EOL servers
    if dispatched < MAX_DISPATCHES_PER_SCAN && eol_servers > 0 {
        if let Some(result) = api.dispatch_replace_server() {
            if result == 1 {
                dispatched += 1;
                state.total_dispatches += 1;
                state.eol_replacements += 1;
                api.log_info("[SysAdmin] Dispatched technician: server EOL (replacement)");
            } else if result == -1 {
                dc_api::crash_log("[dispatch] replace server: no free technician");
            }
        }
    }

    // 4. EOL switches
    if dispatched < MAX_DISPATCHES_PER_SCAN && eol_switches > 0 {
        if let Some(result) = api.dispatch_replace_switch() {
            if result == 1 {
                dispatched += 1;
                state.total_dispatches += 1;
                state.eol_replacements += 1;
                api.log_info("[SysAdmin] Dispatched technician: switch EOL (replacement)");
            } else if result == -1 {
                dc_api::crash_log("[dispatch] replace switch: no free technician");
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
    version = "1.0.0",
    author = "Joniii",
    description = "Schedules automatically technicians for broken/EOL devices."
)]
fn init(api: &Api) -> bool {
    if api.version() < 4 {
        api.log_error("[SysAdmin] Requires API v4+! Device/technician primitives not available.");
        api.log_error("[SysAdmin] Please update DataCenterModLoader to the latest version.");
        return false;
    }

    let _ = STATE.set(Mutex::new(NetWatchState::new()));
    with_state(|s| s.enabled = true);

    api.log_info("[SysAdmin] NetWatch enabled. Watching your infrastructure 24/7.");
    true
}

#[dc_api::on_update]
fn update(api: &Api, dt: f32) {
    let should_scan = with_state(|state| {
        if !state.enabled {
            return false;
        }

        handle_salary(api, state);

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

    with_state(|s| s.enabled = true);

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

#[dc_api::on_event]
fn handle_event(api: &Api, event: Event) {
    match event {
        Event::DayEnded { day } => {
            if let Some((total, repairs, replacements)) =
                with_state(|s| (s.total_dispatches, s.broken_repairs, s.eol_replacements))
            {
                api.log_info(&format!(
                    "[SysAdmin] Day {}, dispatches: {} (repairs: {}, replacements: {})",
                    day, total, repairs, replacements
                ));
            }
        }
        Event::GameLoaded => {
            with_state(|s| {
                s.enabled = true;
                s.last_day = -1;
            });
            api.log_info("[SysAdmin] Game loaded, NetWatch re-enabled.");
        }
        Event::ServerBroken => {
            api.log_info("[SysAdmin] Server broken detected, will dispatch on next scan.");
        }
        Event::SwitchBroken => {
            api.log_info("[SysAdmin] Switch broken detected, will dispatch on next scan.");
        }
        _ => {}
    }
}

#[dc_api::on_shutdown]
fn shutdown(api: &Api) {
    if let Some((total, repairs, replacements)) =
        with_state(|s| (s.total_dispatches, s.broken_repairs, s.eol_replacements))
    {
        api.log_info(&format!(
            "[SysAdmin] Shutting down. Final stats, dispatches: {} (repairs: {}, replacements: {}). Goodbye!",
            total, repairs, replacements
        ));
    } else {
        api.log_info("[SysAdmin] Shutting down. Goodbye!");
    }

    with_state(|s| s.enabled = false);
}

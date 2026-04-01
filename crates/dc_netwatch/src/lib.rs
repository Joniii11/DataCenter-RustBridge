//! SysAdmin mod — auto-dispatches technicians for broken/EOL devices.

use dc_api::events::{self, read_payload, Event};
use dc_api::*;
use std::ffi::c_char;
use std::io::Write;
use std::path::PathBuf;
use std::sync::OnceLock;

static API: OnceLock<Api> = OnceLock::new();

fn crash_log_path() -> PathBuf {
    let base = std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(|d| d.to_path_buf()))
        .unwrap_or_else(|| PathBuf::from("."));
    base.join("dc_sysadmin_crash.log")
}

fn crash_log(msg: &str) {
    let _ = (|| -> std::io::Result<()> {
        let mut f = std::fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(crash_log_path())?;
        let timestamp = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map(|d| d.as_secs())
            .unwrap_or(0);
        writeln!(f, "[{}] {}", timestamp, msg)?;
        Ok(())
    })();
}

fn setup_panic_hook() {
    std::panic::set_hook(Box::new(|info| {
        let mut message = String::from("PANIC: ");

        if let Some(s) = info.payload().downcast_ref::<&str>() {
            message.push_str(s);
        } else if let Some(s) = info.payload().downcast_ref::<String>() {
            message.push_str(s);
        } else {
            message.push_str("<non-string panic payload>");
        }

        if let Some(loc) = info.location() {
            message.push_str(&format!(
                " at {}:{}:{}",
                loc.file(),
                loc.line(),
                loc.column()
            ));
        }

        let bt = std::backtrace::Backtrace::capture();
        if bt.status() == std::backtrace::BacktraceStatus::Captured {
            message.push_str(&format!("\nBacktrace:\n{}", bt));
        }

        crash_log(&message);
    }));
}

fn api() -> Option<&'static Api> {
    API.get()
}

fn panic_payload_to_string(payload: &Box<dyn std::any::Any + Send>) -> String {
    if let Some(s) = payload.downcast_ref::<&str>() {
        (*s).to_owned()
    } else if let Some(s) = payload.downcast_ref::<String>() {
        s.clone()
    } else {
        "<non-string panic>".to_owned()
    }
}

#[no_mangle]
pub extern "C" fn mod_info() -> ModInfo {
    let result = std::panic::catch_unwind(|| {
        ModInfo::new(
            "sysadmin",
            "SysAdmin",
            "1.0.0",
            "Joniii",
            "Autonomous system administrator \u{2014} monitors your infrastructure 24/7 and dispatches technicians when devices fail or approach EOL. Costs $500/day.",
        )
    });

    match result {
        Ok(info) => info,
        Err(e) => {
            crash_log(&format!(
                "[mod_info] caught panic: {}",
                panic_payload_to_string(&e)
            ));
            ModInfo {
                id: std::ptr::null(),
                name: std::ptr::null(),
                version: std::ptr::null(),
                author: std::ptr::null(),
                description: std::ptr::null(),
            }
        }
    }
}

#[no_mangle]
pub extern "C" fn mod_init(game_api: &'static GameAPI) -> bool {
    setup_panic_hook();
    crash_log("[mod_init] >>> enter");

    let result = std::panic::catch_unwind(|| {
        let api = unsafe { Api::from_raw(game_api) };

        api.log_info("[SysAdmin] Mod loaded — initializing NetWatch system...");
        api.log_info(&format!("[SysAdmin] API version: {}", api.version()));

        api.set_netwatch_enabled(true);
        api.log_info("[SysAdmin] NetWatch enabled. Watching your infrastructure 24/7.");

        let _ = API.set(api);
        true
    });

    match result {
        Ok(v) => {
            crash_log("[mod_init] <<< exit (success)");
            v
        }
        Err(e) => {
            crash_log(&format!(
                "[mod_init] caught panic: {}",
                panic_payload_to_string(&e)
            ));
            false
        }
    }
}

#[no_mangle]
pub extern "C" fn mod_on_scene_loaded(scene_name: *const c_char) {
    let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        if scene_name.is_null() {
            return;
        }
        let name = unsafe { std::ffi::CStr::from_ptr(scene_name) }.to_string_lossy();
        let Some(api) = api() else {
            crash_log("[mod_on_scene_loaded] API not initialised, skipping");
            return;
        };

        api.log_info(&format!("[SysAdmin] Scene loaded: {}", name));

        // re-enable in case scene transition reset it
        api.set_netwatch_enabled(true);

        if let Some(stats) = api.get_netwatch_stats() {
            api.log_info(&format!("[SysAdmin] Total dispatches so far: {}", stats));
        }
    }));

    if let Err(e) = result {
        crash_log(&format!(
            "[mod_on_scene_loaded] caught panic: {}",
            panic_payload_to_string(&e)
        ));
    }
}

#[no_mangle]
pub extern "C" fn mod_on_event(event_id: u32, event_data: *const u8, data_size: u32) {
    let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        let Some(event) = events::decode(event_id, event_data, data_size) else {
            return;
        };

        let Some(api) = api() else {
            crash_log("[mod_on_event] API not initialised, skipping");
            return;
        };

        match event {
            Event::Unknown { event_id: 900 } => {
                // NetWatch dispatch — decode our mod-specific payload
                if let Some(d) = read_payload::<NetWatchDispatchedData>(event_data, data_size) {
                    let dev = if d.device_type == 0 {
                        "Server"
                    } else {
                        "Switch"
                    };
                    let rsn = if d.reason == 0 {
                        "broken (repair)"
                    } else {
                        "EOL warning (replacement)"
                    };
                    api.log_info(&format!(
                        "[SysAdmin] Dispatched technician: {} {}",
                        dev, rsn
                    ));
                    if let Some(stats) = api.get_netwatch_stats() {
                        api.log_info(&format!("[SysAdmin] Total dispatches: {}", stats));
                    }
                }
            }
            Event::DayEnded { day } => {
                let stats = api.get_netwatch_stats().unwrap_or(0);
                api.log_info(&format!(
                    "[SysAdmin] Day {} \u{2014} Salary deducted. Total dispatches so far: {}",
                    day, stats
                ));
            }
            Event::GameLoaded => {
                api.set_netwatch_enabled(true);
                api.log_info("[SysAdmin] Game loaded — NetWatch re-enabled.");
            }
            _ => {} // not our problem
        }
    }));

    if let Err(e) = result {
        crash_log(&format!(
            "[mod_on_event] caught panic (event_id={}): {}",
            event_id,
            panic_payload_to_string(&e)
        ));
    }
}

#[no_mangle]
pub extern "C" fn mod_shutdown() {
    let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        if let Some(api) = api() {
            api.set_netwatch_enabled(false);
            let stats = api.get_netwatch_stats().unwrap_or(0);
            api.log_info(&format!(
                "[SysAdmin] Shutting down. Final dispatch count: {}. Goodbye!",
                stats
            ));
        } else {
            crash_log("[mod_shutdown] API not initialised, nothing to do");
        }
    }));

    if let Err(e) = result {
        crash_log(&format!(
            "[mod_shutdown] caught panic: {}",
            panic_payload_to_string(&e)
        ));
    }
}

// NetWatch dispatch payload (event id 900), mod-specific.
// device_type: 0 = server, 1 = switch
// reason: 0 = broken, 1 = eol_warning
#[repr(C)]
#[derive(Debug, Clone, Copy)]
struct NetWatchDispatchedData {
    device_type: i32,
    reason: i32,
}

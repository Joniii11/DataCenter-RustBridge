//! Game events dispatched from C# Harmony hooks to Rust mods via `mod_on_event`.
//!
//! Export `mod_on_event(event_id, data, size)` in your mod and use `decode()` to
//! get a friendly `Event` enum you can match on.

pub const EVENT_MONEY_CHANGED: u32 = 100;
pub const EVENT_XP_CHANGED: u32 = 101;
pub const EVENT_REPUTATION_CHANGED: u32 = 102;

pub const EVENT_SERVER_POWERED: u32 = 200;
pub const EVENT_SERVER_BROKEN: u32 = 201;
pub const EVENT_SERVER_REPAIRED: u32 = 202;
pub const EVENT_SERVER_INSTALLED: u32 = 203;

pub const EVENT_DAY_ENDED: u32 = 300;

pub const EVENT_CUSTOMER_ACCEPTED: u32 = 400;

pub const EVENT_SHOP_CHECKOUT: u32 = 500;

pub const EVENT_EMPLOYEE_HIRED: u32 = 600;
pub const EVENT_EMPLOYEE_FIRED: u32 = 601;

pub const EVENT_GAME_SAVED: u32 = 700;
pub const EVENT_GAME_LOADED: u32 = 701;

/// Shared layout for money/xp/reputation change events.
#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct ValueChangedData {
    pub old_value: f64,
    pub new_value: f64,
    pub delta: f64,
}

#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct ServerPoweredData {
    pub powered_on: u32, // 1 = on, 0 = off
}

#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct DayEndedData {
    pub day: u32,
}

#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct CustomerAcceptedData {
    pub customer_id: i32,
}

// High-level event enum

#[derive(Debug, Clone)]
pub enum Event {
    MoneyChanged {
        old_value: f64,
        new_value: f64,
        delta: f64,
    },
    XPChanged {
        old_value: f64,
        new_value: f64,
        delta: f64,
    },
    ReputationChanged {
        old_value: f64,
        new_value: f64,
        delta: f64,
    },

    ServerPowered {
        powered_on: bool,
    },
    ServerBroken,
    ServerRepaired,
    ServerInstalled,

    DayEnded {
        day: u32,
    },

    CustomerAccepted {
        customer_id: i32,
    },

    ShopCheckout,

    EmployeeHired,
    EmployeeFired,

    GameSaved,
    GameLoaded,

    /// Unknown event from a newer host. Lets older mods gracefully ignore new events.
    Unknown {
        event_id: u32,
    },
}

impl Event {
    pub fn is_economy(&self) -> bool {
        matches!(
            self,
            Event::MoneyChanged { .. } | Event::XPChanged { .. } | Event::ReputationChanged { .. }
        )
    }

    pub fn is_server(&self) -> bool {
        matches!(
            self,
            Event::ServerPowered { .. }
                | Event::ServerBroken
                | Event::ServerRepaired
                | Event::ServerInstalled
        )
    }

    pub fn is_time(&self) -> bool {
        matches!(self, Event::DayEnded { .. })
    }

    pub fn is_save_load(&self) -> bool {
        matches!(self, Event::GameSaved | Event::GameLoaded)
    }

    pub fn id(&self) -> u32 {
        match self {
            Event::MoneyChanged { .. } => EVENT_MONEY_CHANGED,
            Event::XPChanged { .. } => EVENT_XP_CHANGED,
            Event::ReputationChanged { .. } => EVENT_REPUTATION_CHANGED,
            Event::ServerPowered { .. } => EVENT_SERVER_POWERED,
            Event::ServerBroken => EVENT_SERVER_BROKEN,
            Event::ServerRepaired => EVENT_SERVER_REPAIRED,
            Event::ServerInstalled => EVENT_SERVER_INSTALLED,
            Event::DayEnded { .. } => EVENT_DAY_ENDED,
            Event::CustomerAccepted { .. } => EVENT_CUSTOMER_ACCEPTED,
            Event::ShopCheckout => EVENT_SHOP_CHECKOUT,
            Event::EmployeeHired => EVENT_EMPLOYEE_HIRED,
            Event::EmployeeFired => EVENT_EMPLOYEE_FIRED,
            Event::GameSaved => EVENT_GAME_SAVED,
            Event::GameLoaded => EVENT_GAME_LOADED,
            Event::Unknown { event_id } => *event_id,
        }
    }
}
/// Decode raw FFI event args into a friendly `Event`. Returns `None` if the
/// data pointer is null/too small for events that need a payload.
pub fn decode(event_id: u32, data: *const u8, size: u32) -> Option<Event> {
    match event_id {
        EVENT_MONEY_CHANGED => {
            let d = read_data::<ValueChangedData>(data, size)?;
            Some(Event::MoneyChanged {
                old_value: d.old_value,
                new_value: d.new_value,
                delta: d.delta,
            })
        }
        EVENT_XP_CHANGED => {
            let d = read_data::<ValueChangedData>(data, size)?;
            Some(Event::XPChanged {
                old_value: d.old_value,
                new_value: d.new_value,
                delta: d.delta,
            })
        }
        EVENT_REPUTATION_CHANGED => {
            let d = read_data::<ValueChangedData>(data, size)?;
            Some(Event::ReputationChanged {
                old_value: d.old_value,
                new_value: d.new_value,
                delta: d.delta,
            })
        }
        EVENT_SERVER_POWERED => {
            let d = read_data::<ServerPoweredData>(data, size)?;
            Some(Event::ServerPowered {
                powered_on: d.powered_on != 0,
            })
        }
        EVENT_SERVER_BROKEN => Some(Event::ServerBroken),
        EVENT_SERVER_REPAIRED => Some(Event::ServerRepaired),
        EVENT_SERVER_INSTALLED => Some(Event::ServerInstalled),
        EVENT_DAY_ENDED => {
            let d = read_data::<DayEndedData>(data, size)?;
            Some(Event::DayEnded { day: d.day })
        }
        EVENT_CUSTOMER_ACCEPTED => {
            let d = read_data::<CustomerAcceptedData>(data, size)?;
            Some(Event::CustomerAccepted {
                customer_id: d.customer_id,
            })
        }
        EVENT_SHOP_CHECKOUT => Some(Event::ShopCheckout),
        EVENT_EMPLOYEE_HIRED => Some(Event::EmployeeHired),
        EVENT_EMPLOYEE_FIRED => Some(Event::EmployeeFired),
        EVENT_GAME_SAVED => Some(Event::GameSaved),
        EVENT_GAME_LOADED => Some(Event::GameLoaded),
        other => Some(Event::Unknown { event_id: other }),
    }
}

pub type ModOnEventFn = unsafe extern "C" fn(event_id: u32, event_data: *const u8, data_size: u32);

fn read_data<T: Copy>(data: *const u8, size: u32) -> Option<T> {
    let expected = std::mem::size_of::<T>();
    if expected == 0 {
        return Some(unsafe { std::mem::zeroed() });
    }
    if data.is_null() || (size as usize) < expected {
        return None;
    }
    Some(unsafe { std::ptr::read_unaligned(data as *const T) })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn decode_money_changed() {
        let data = ValueChangedData {
            old_value: 100.0,
            new_value: 200.0,
            delta: 100.0,
        };
        let ptr = &data as *const ValueChangedData as *const u8;
        let size = std::mem::size_of::<ValueChangedData>() as u32;

        let event = decode(EVENT_MONEY_CHANGED, ptr, size).unwrap();
        match event {
            Event::MoneyChanged {
                old_value,
                new_value,
                delta,
            } => {
                assert!((old_value - 100.0).abs() < f64::EPSILON);
                assert!((new_value - 200.0).abs() < f64::EPSILON);
                assert!((delta - 100.0).abs() < f64::EPSILON);
            }
            _ => panic!("Expected MoneyChanged, got {:?}", event),
        }
    }

    #[test]
    fn decode_server_powered() {
        let on = ServerPoweredData { powered_on: 1 };
        let off = ServerPoweredData { powered_on: 0 };
        let size = std::mem::size_of::<ServerPoweredData>() as u32;

        match decode(EVENT_SERVER_POWERED, &on as *const _ as *const u8, size).unwrap() {
            Event::ServerPowered { powered_on } => assert!(powered_on),
            _ => panic!("Expected ServerPowered"),
        }
        match decode(EVENT_SERVER_POWERED, &off as *const _ as *const u8, size).unwrap() {
            Event::ServerPowered { powered_on } => assert!(!powered_on),
            _ => panic!("Expected ServerPowered"),
        }
    }

    #[test]
    fn decode_no_data_events() {
        assert!(matches!(
            decode(EVENT_SERVER_BROKEN, std::ptr::null(), 0),
            Some(Event::ServerBroken)
        ));
        assert!(matches!(
            decode(EVENT_SERVER_REPAIRED, std::ptr::null(), 0),
            Some(Event::ServerRepaired)
        ));
        assert!(matches!(
            decode(EVENT_SERVER_INSTALLED, std::ptr::null(), 0),
            Some(Event::ServerInstalled)
        ));
        assert!(matches!(
            decode(EVENT_SHOP_CHECKOUT, std::ptr::null(), 0),
            Some(Event::ShopCheckout)
        ));
        assert!(matches!(
            decode(EVENT_EMPLOYEE_HIRED, std::ptr::null(), 0),
            Some(Event::EmployeeHired)
        ));
        assert!(matches!(
            decode(EVENT_EMPLOYEE_FIRED, std::ptr::null(), 0),
            Some(Event::EmployeeFired)
        ));
        assert!(matches!(
            decode(EVENT_GAME_SAVED, std::ptr::null(), 0),
            Some(Event::GameSaved)
        ));
        assert!(matches!(
            decode(EVENT_GAME_LOADED, std::ptr::null(), 0),
            Some(Event::GameLoaded)
        ));
    }

    #[test]
    fn decode_day_ended() {
        let data = DayEndedData { day: 42 };
        let event = decode(
            EVENT_DAY_ENDED,
            &data as *const _ as *const u8,
            std::mem::size_of::<DayEndedData>() as u32,
        )
        .unwrap();
        assert!(matches!(event, Event::DayEnded { day: 42 }));
    }

    #[test]
    fn decode_customer_accepted() {
        let data = CustomerAcceptedData { customer_id: 7 };
        let event = decode(
            EVENT_CUSTOMER_ACCEPTED,
            &data as *const _ as *const u8,
            std::mem::size_of::<CustomerAcceptedData>() as u32,
        )
        .unwrap();
        assert!(matches!(event, Event::CustomerAccepted { customer_id: 7 }));
    }

    #[test]
    fn decode_unknown_event() {
        assert!(matches!(
            decode(9999, std::ptr::null(), 0),
            Some(Event::Unknown { event_id: 9999 })
        ));
    }

    #[test]
    fn null_pointer_for_data_event_returns_none() {
        assert!(decode(EVENT_MONEY_CHANGED, std::ptr::null(), 0).is_none());
    }

    #[test]
    fn too_small_size_returns_none() {
        let data = ValueChangedData {
            old_value: 1.0,
            new_value: 2.0,
            delta: 1.0,
        };
        assert!(decode(EVENT_MONEY_CHANGED, &data as *const _ as *const u8, 4).is_none());
    }

    #[test]
    fn event_id_roundtrip() {
        let cases: Vec<(Event, u32)> = vec![
            (
                Event::MoneyChanged {
                    old_value: 0.0,
                    new_value: 0.0,
                    delta: 0.0,
                },
                EVENT_MONEY_CHANGED,
            ),
            (
                Event::XPChanged {
                    old_value: 0.0,
                    new_value: 0.0,
                    delta: 0.0,
                },
                EVENT_XP_CHANGED,
            ),
            (
                Event::ReputationChanged {
                    old_value: 0.0,
                    new_value: 0.0,
                    delta: 0.0,
                },
                EVENT_REPUTATION_CHANGED,
            ),
            (
                Event::ServerPowered { powered_on: true },
                EVENT_SERVER_POWERED,
            ),
            (Event::ServerBroken, EVENT_SERVER_BROKEN),
            (Event::ServerRepaired, EVENT_SERVER_REPAIRED),
            (Event::ServerInstalled, EVENT_SERVER_INSTALLED),
            (Event::DayEnded { day: 1 }, EVENT_DAY_ENDED),
            (
                Event::CustomerAccepted { customer_id: 1 },
                EVENT_CUSTOMER_ACCEPTED,
            ),
            (Event::ShopCheckout, EVENT_SHOP_CHECKOUT),
            (Event::EmployeeHired, EVENT_EMPLOYEE_HIRED),
            (Event::EmployeeFired, EVENT_EMPLOYEE_FIRED),
            (Event::GameSaved, EVENT_GAME_SAVED),
            (Event::GameLoaded, EVENT_GAME_LOADED),
            (Event::Unknown { event_id: 9999 }, 9999),
        ];
        for (event, expected_id) in cases {
            assert_eq!(event.id(), expected_id, "ID mismatch for {:?}", event);
        }
    }

    #[test]
    fn event_category_helpers() {
        assert!(Event::MoneyChanged {
            old_value: 0.0,
            new_value: 0.0,
            delta: 0.0
        }
        .is_economy());
        assert!(!Event::ServerBroken.is_economy());
        assert!(Event::ServerBroken.is_server());
        assert!(Event::ServerPowered { powered_on: true }.is_server());
        assert!(!Event::GameSaved.is_server());
        assert!(Event::DayEnded { day: 1 }.is_time());
        assert!(Event::GameSaved.is_save_load());
        assert!(Event::GameLoaded.is_save_load());
        assert!(!Event::ServerBroken.is_save_load());
    }
}

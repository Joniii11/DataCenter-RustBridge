//! Game events dispatched from C# to Rust mods via `mod_on_event`.
//!
//! # Architecture
//! C# Harmony patches detect game state changes and send them across FFI as
//! `(event_id: u32, data: *const u8, data_size: u32)` triples.
//! Rust mods receive these in their `mod_on_event` export and can use [`decode`]
//! to get a typed [`Event`] enum.
//!
//! # Module structure
//! - [`event_id`] — numeric event identifiers ([`EventId`])
//! - [`category`] — event categories ([`EventCategory`])
//! - [`payload`] — `#[repr(C)]` FFI data structs
//! - [`event`] — decoded event enum ([`Event`])
//!
//! # Adding new events
//! 1. Add a variant to [`EventId`] with the next free ID in the right category range
//! 2. Add a `#[repr(C)]` payload struct in [`payload`] (if the event carries data)
//! 3. Add a variant to [`Event`] (with payload fields if needed)
//! 4. Handle it in [`decode`]
//! 5. Add the matching C# constant in `EventIds` and a Harmony patch

pub mod category;
pub mod event;
pub mod event_id;
pub mod payload;

pub use category::EventCategory;
pub use event::Event;
pub use event_id::EventId;
pub use payload::*;

/// FFI signature for `mod_on_event` exports.
pub type ModOnEventFn = unsafe extern "C" fn(event_id: u32, event_data: *const u8, data_size: u32);

/// Decode raw FFI event data into a typed [`Event`].
/// Returns `None` if a required payload is missing or too small.
pub fn decode(event_id: u32, data: *const u8, size: u32) -> Option<Event> {
    match EventId::from_raw(event_id) {
        Some(EventId::MoneyChanged) => {
            let d = read_payload::<ValueChangedData>(data, size)?;
            Some(Event::MoneyChanged {
                old_value: d.old_value,
                new_value: d.new_value,
                delta: d.delta,
            })
        }
        Some(EventId::XpChanged) => {
            let d = read_payload::<ValueChangedData>(data, size)?;
            Some(Event::XpChanged {
                old_value: d.old_value,
                new_value: d.new_value,
                delta: d.delta,
            })
        }
        Some(EventId::ReputationChanged) => {
            let d = read_payload::<ValueChangedData>(data, size)?;
            Some(Event::ReputationChanged {
                old_value: d.old_value,
                new_value: d.new_value,
                delta: d.delta,
            })
        }
        Some(EventId::ServerPowered) => {
            let d = read_payload::<ServerPoweredData>(data, size)?;
            Some(Event::ServerPowered {
                powered_on: d.powered_on != 0,
            })
        }
        Some(EventId::ServerBroken) => Some(Event::ServerBroken),
        Some(EventId::ServerRepaired) => Some(Event::ServerRepaired),
        Some(EventId::ServerInstalled) => Some(Event::ServerInstalled),
        Some(EventId::CableConnected) => Some(Event::CableConnected),
        Some(EventId::CableDisconnected) => Some(Event::CableDisconnected),
        Some(EventId::ServerCustomerChanged) => {
            let d = read_payload::<ServerCustomerChangedData>(data, size)?;
            Some(Event::ServerCustomerChanged {
                new_customer_id: d.new_customer_id,
            })
        }
        Some(EventId::ServerAppChanged) => {
            let d = read_payload::<ServerAppChangedData>(data, size)?;
            Some(Event::ServerAppChanged {
                new_app_id: d.new_app_id,
            })
        }
        Some(EventId::RackUnmounted) => Some(Event::RackUnmounted),
        Some(EventId::SwitchBroken) => Some(Event::SwitchBroken),
        Some(EventId::SwitchRepaired) => Some(Event::SwitchRepaired),
        Some(EventId::DayEnded) => {
            let d = read_payload::<DayEndedData>(data, size)?;
            Some(Event::DayEnded { day: d.day })
        }
        Some(EventId::MonthEnded) => {
            let d = read_payload::<MonthEndedData>(data, size)?;
            Some(Event::MonthEnded { month: d.month })
        }
        Some(EventId::CustomerAccepted) => {
            let d = read_payload::<CustomerAcceptedData>(data, size)?;
            Some(Event::CustomerAccepted {
                customer_id: d.customer_id,
            })
        }
        Some(EventId::CustomerSatisfied) => {
            let d = read_payload::<CustomerSatisfiedData>(data, size)?;
            Some(Event::CustomerSatisfied {
                customer_base_id: d.customer_base_id,
            })
        }
        Some(EventId::CustomerUnsatisfied) => {
            let d = read_payload::<CustomerSatisfiedData>(data, size)?;
            Some(Event::CustomerUnsatisfied {
                customer_base_id: d.customer_base_id,
            })
        }
        Some(EventId::ShopCheckout) => Some(Event::ShopCheckout),
        Some(EventId::ShopItemAdded) => {
            let d = read_payload::<ShopItemAddedData>(data, size)?;
            Some(Event::ShopItemAdded {
                item_id: d.item_id,
                price: d.price,
                item_type: d.item_type,
            })
        }
        Some(EventId::ShopCartCleared) => Some(Event::ShopCartCleared),
        Some(EventId::ShopItemRemoved) => {
            let d = read_payload::<ShopItemRemovedData>(data, size)?;
            Some(Event::ShopItemRemoved { uid: d.uid })
        }
        Some(EventId::EmployeeHired) => Some(Event::EmployeeHired),
        Some(EventId::EmployeeFired) => Some(Event::EmployeeFired),
        Some(EventId::GameSaved) => Some(Event::GameSaved),
        Some(EventId::GameLoaded) => Some(Event::GameLoaded),
        Some(EventId::GameAutoSaved) => Some(Event::GameAutoSaved),
        Some(EventId::WallPurchased) => Some(Event::WallPurchased),
        None => Some(Event::Unknown { event_id }),
    }
}

/// Decode a `#[repr(C)]` payload from raw FFI event data.
/// Returns `None` if the pointer is null or the buffer is too small.
/// Use this in mods that define their own custom events.
pub fn read_payload<T: Copy>(data: *const u8, size: u32) -> Option<T> {
    let expected = std::mem::size_of::<T>();
    if expected == 0 {
        return Some(unsafe { std::mem::zeroed() });
    }
    if data.is_null() || (size as usize) < expected {
        return None;
    }
    Some(unsafe { std::ptr::read_unaligned(data as *const T) })
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

#[cfg(test)]
mod tests {
    use super::*;

    // ── decode integration tests ───────────────────────────────────

    #[test]
    fn decode_value_changed() {
        let data = ValueChangedData {
            old_value: 100.0,
            new_value: 250.0,
            delta: 150.0,
        };
        let ptr = &data as *const _ as *const u8;
        let size = std::mem::size_of::<ValueChangedData>() as u32;

        let evt = decode(EventId::MoneyChanged as u32, ptr, size).unwrap();
        match evt {
            Event::MoneyChanged {
                old_value,
                new_value,
                delta,
            } => {
                assert!((old_value - 100.0).abs() < f64::EPSILON);
                assert!((new_value - 250.0).abs() < f64::EPSILON);
                assert!((delta - 150.0).abs() < f64::EPSILON);
            }
            other => panic!("expected MoneyChanged, got {:?}", other),
        }
    }

    #[test]
    fn decode_server_powered() {
        let data = ServerPoweredData { powered_on: 1 };
        let ptr = &data as *const _ as *const u8;
        let size = std::mem::size_of::<ServerPoweredData>() as u32;

        let evt = decode(EventId::ServerPowered as u32, ptr, size).unwrap();
        match evt {
            Event::ServerPowered { powered_on } => assert!(powered_on),
            other => panic!("expected ServerPowered, got {:?}", other),
        }

        let data_off = ServerPoweredData { powered_on: 0 };
        let evt_off = decode(
            EventId::ServerPowered as u32,
            &data_off as *const _ as *const u8,
            size,
        )
        .unwrap();
        match evt_off {
            Event::ServerPowered { powered_on } => assert!(!powered_on),
            other => panic!("expected ServerPowered(OFF), got {:?}", other),
        }
    }

    #[test]
    fn decode_shop_item_added() {
        let data = ShopItemAddedData {
            item_id: 5,
            price: 1500,
            item_type: 4,
        };
        let ptr = &data as *const _ as *const u8;
        let size = std::mem::size_of::<ShopItemAddedData>() as u32;

        let evt = decode(EventId::ShopItemAdded as u32, ptr, size).unwrap();
        match evt {
            Event::ShopItemAdded {
                item_id,
                price,
                item_type,
            } => {
                assert_eq!(item_id, 5);
                assert_eq!(price, 1500);
                assert_eq!(item_type, 4);
            }
            other => panic!("expected ShopItemAdded, got {:?}", other),
        }
    }

    #[test]
    fn decode_simple_events() {
        let simple = [
            (EventId::ServerBroken, "ServerBroken"),
            (EventId::ServerRepaired, "ServerRepaired"),
            (EventId::ServerInstalled, "ServerInstalled"),
            (EventId::CableConnected, "CableConnected"),
            (EventId::CableDisconnected, "CableDisconnected"),
            (EventId::RackUnmounted, "RackUnmounted"),
            (EventId::SwitchBroken, "SwitchBroken"),
            (EventId::SwitchRepaired, "SwitchRepaired"),
            (EventId::ShopCheckout, "ShopCheckout"),
            (EventId::ShopCartCleared, "ShopCartCleared"),
            (EventId::EmployeeHired, "EmployeeHired"),
            (EventId::EmployeeFired, "EmployeeFired"),
            (EventId::GameSaved, "GameSaved"),
            (EventId::GameLoaded, "GameLoaded"),
            (EventId::GameAutoSaved, "GameAutoSaved"),
            (EventId::WallPurchased, "WallPurchased"),
        ];
        for (id, name) in simple {
            let evt = decode(id as u32, std::ptr::null(), 0)
                .unwrap_or_else(|| panic!("decode returned None for {}", name));
            assert_eq!(evt.raw_id(), id as u32, "raw_id mismatch for {}", name);
        }
    }

    #[test]
    fn decode_unknown_event() {
        let evt = decode(9999, std::ptr::null(), 0).unwrap();
        assert_eq!(evt.raw_id(), 9999);
        assert!(evt.event_id().is_none());
    }

    #[test]
    fn decode_payload_too_small_returns_none() {
        // MoneyChanged needs ValueChangedData (24 bytes), give it 4
        assert!(decode(EventId::MoneyChanged as u32, [0u8; 4].as_ptr(), 4).is_none());
    }

    #[test]
    fn decode_null_pointer_for_payload_event_returns_none() {
        assert!(decode(EventId::MoneyChanged as u32, std::ptr::null(), 0).is_none());
    }

    // ── Event enum cross-module tests ──────────────────────────────

    #[test]
    fn event_id_method() {
        assert_eq!(
            Event::MoneyChanged {
                old_value: 0.0,
                new_value: 0.0,
                delta: 0.0
            }
            .event_id(),
            Some(EventId::MoneyChanged)
        );
        assert_eq!(Event::GameSaved.event_id(), Some(EventId::GameSaved));
        assert_eq!(Event::Unknown { event_id: 9999 }.event_id(), None);
    }

    #[test]
    fn unknown_events_fail_all_category_checks() {
        let u = Event::Unknown { event_id: 9999 };
        assert!(!u.is_economy());
        assert!(!u.is_server());
        assert!(!u.is_time());
        assert!(!u.is_customer());
        assert!(!u.is_shop());
        assert!(!u.is_employee());
        assert!(!u.is_save_load());
        assert!(!u.is_building());
    }
}

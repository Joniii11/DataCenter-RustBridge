//! Game events dispatched from C# to Rust mods via `mod_on_event`.
//!
//! # Architecture
//! C# Harmony patches detect game state changes and send them across FFI as
//! `(event_id: u32, data: *const u8, data_size: u32)` triples.
//! Rust mods receive these in their `mod_on_event` export and can use [`decode`]
//! to get a typed [`Event`] enum.
//!
//! # Adding new events
//! 1. Add a variant to [`EventId`] with the next free ID in the right category range
//! 2. Add a variant to [`Event`] (with payload struct if needed)
//! 3. Handle it in [`decode`]
//! 4. Add the matching C# constant in `EventIds` and a Harmony patch

use std::fmt;

/// All known event IDs, grouped by category.
///
/// Each category uses its own hundred-range:
/// - **1xx**: Economy (money, xp, reputation)
/// - **2xx**: Server & infrastructure
/// - **3xx**: Time
/// - **4xx**: Customer
/// - **5xx**: Shop
/// - **6xx**: Employee
/// - **7xx**: Persistence (save/load)
/// - **8xx**: Building
#[repr(u32)]
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum EventId {
    // economy (1xx)
    MoneyChanged = 100,
    XpChanged = 101,
    ReputationChanged = 102,

    // server (2xx)
    ServerPowered = 200,
    ServerBroken = 201,
    ServerRepaired = 202,
    ServerInstalled = 203,
    CableConnected = 204,
    CableDisconnected = 205,
    ServerCustomerChanged = 206,
    ServerAppChanged = 207,
    RackUnmounted = 208,
    SwitchBroken = 209,
    SwitchRepaired = 210,

    // time (3xx)
    DayEnded = 300,
    MonthEnded = 301,

    // customer (4xx)
    CustomerAccepted = 400,
    CustomerSatisfied = 401,
    CustomerUnsatisfied = 402,

    // shop (5xx)
    ShopCheckout = 500,
    ShopItemAdded = 501,
    ShopCartCleared = 502,
    ShopItemRemoved = 503,

    // employee (6xx)
    EmployeeHired = 600,
    EmployeeFired = 601,

    // persistence (7xx)
    GameSaved = 700,
    GameLoaded = 701,
    GameAutoSaved = 702,

    // building (8xx)
    WallPurchased = 800,
}

impl EventId {
    /// Every known event ID in definition order.
    pub const ALL: &[Self] = &[
        Self::MoneyChanged,
        Self::XpChanged,
        Self::ReputationChanged,
        Self::ServerPowered,
        Self::ServerBroken,
        Self::ServerRepaired,
        Self::ServerInstalled,
        Self::CableConnected,
        Self::CableDisconnected,
        Self::ServerCustomerChanged,
        Self::ServerAppChanged,
        Self::RackUnmounted,
        Self::SwitchBroken,
        Self::SwitchRepaired,
        Self::DayEnded,
        Self::MonthEnded,
        Self::CustomerAccepted,
        Self::CustomerSatisfied,
        Self::CustomerUnsatisfied,
        Self::ShopCheckout,
        Self::ShopItemAdded,
        Self::ShopCartCleared,
        Self::ShopItemRemoved,
        Self::EmployeeHired,
        Self::EmployeeFired,
        Self::GameSaved,
        Self::GameLoaded,
        Self::GameAutoSaved,
        Self::WallPurchased,
    ];

    /// Convert a raw `u32` to a known event ID (returns `None` for unknown IDs).
    pub fn from_raw(id: u32) -> Option<Self> {
        Self::ALL.iter().copied().find(|e| *e as u32 == id)
    }

    /// Raw `u32` value.
    pub fn as_u32(self) -> u32 {
        self as u32
    }

    /// Human-readable name (e.g. `"MoneyChanged"`).
    pub fn name(self) -> &'static str {
        match self {
            Self::MoneyChanged => "MoneyChanged",
            Self::XpChanged => "XpChanged",
            Self::ReputationChanged => "ReputationChanged",
            Self::ServerPowered => "ServerPowered",
            Self::ServerBroken => "ServerBroken",
            Self::ServerRepaired => "ServerRepaired",
            Self::ServerInstalled => "ServerInstalled",
            Self::CableConnected => "CableConnected",
            Self::CableDisconnected => "CableDisconnected",
            Self::ServerCustomerChanged => "ServerCustomerChanged",
            Self::ServerAppChanged => "ServerAppChanged",
            Self::RackUnmounted => "RackUnmounted",
            Self::SwitchBroken => "SwitchBroken",
            Self::SwitchRepaired => "SwitchRepaired",
            Self::DayEnded => "DayEnded",
            Self::MonthEnded => "MonthEnded",
            Self::CustomerAccepted => "CustomerAccepted",
            Self::CustomerSatisfied => "CustomerSatisfied",
            Self::CustomerUnsatisfied => "CustomerUnsatisfied",
            Self::ShopCheckout => "ShopCheckout",
            Self::ShopItemAdded => "ShopItemAdded",
            Self::ShopCartCleared => "ShopCartCleared",
            Self::ShopItemRemoved => "ShopItemRemoved",
            Self::EmployeeHired => "EmployeeHired",
            Self::EmployeeFired => "EmployeeFired",
            Self::GameSaved => "GameSaved",
            Self::GameLoaded => "GameLoaded",
            Self::GameAutoSaved => "GameAutoSaved",
            Self::WallPurchased => "WallPurchased",
        }
    }

    /// Which category this event belongs to.
    pub fn category(self) -> EventCategory {
        match self {
            Self::MoneyChanged | Self::XpChanged | Self::ReputationChanged => {
                EventCategory::Economy
            }
            Self::ServerPowered
            | Self::ServerBroken
            | Self::ServerRepaired
            | Self::ServerInstalled
            | Self::CableConnected
            | Self::CableDisconnected
            | Self::ServerCustomerChanged
            | Self::ServerAppChanged
            | Self::RackUnmounted
            | Self::SwitchBroken
            | Self::SwitchRepaired => EventCategory::Server,
            Self::DayEnded | Self::MonthEnded => EventCategory::Time,
            Self::CustomerAccepted | Self::CustomerSatisfied | Self::CustomerUnsatisfied => {
                EventCategory::Customer
            }
            Self::ShopCheckout
            | Self::ShopItemAdded
            | Self::ShopCartCleared
            | Self::ShopItemRemoved => EventCategory::Shop,
            Self::EmployeeHired | Self::EmployeeFired => EventCategory::Employee,
            Self::GameSaved | Self::GameLoaded | Self::GameAutoSaved => EventCategory::Persistence,
            Self::WallPurchased => EventCategory::Building,
        }
    }
}

impl fmt::Display for EventId {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}({})", self.name(), self.as_u32())
    }
}

/// Event categories (derived from the hundred-range of the event ID).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum EventCategory {
    Economy,
    Server,
    Time,
    Customer,
    Shop,
    Employee,
    Persistence,
    Building,
}

impl fmt::Display for EventCategory {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Self::Economy => "Economy",
            Self::Server => "Server",
            Self::Time => "Time",
            Self::Customer => "Customer",
            Self::Shop => "Shop",
            Self::Employee => "Employee",
            Self::Persistence => "Persistence",
            Self::Building => "Building",
        })
    }
}

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

#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct CustomerSatisfiedData {
    pub customer_base_id: i32,
}

#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct ServerCustomerChangedData {
    pub new_customer_id: i32,
}

#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct ServerAppChangedData {
    pub new_app_id: i32,
}

#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct MonthEndedData {
    pub month: i32,
}

#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct ShopItemAddedData {
    pub item_id: i32,
    pub price: i32,
    pub item_type: i32,
}

#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct ShopItemRemovedData {
    pub uid: i32,
}

/// A decoded game event with its payload.
/// Use [`decode`] to convert raw FFI data into this type.
#[derive(Debug, Clone)]
pub enum Event {
    MoneyChanged {
        old_value: f64,
        new_value: f64,
        delta: f64,
    },
    XpChanged {
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
    CableConnected,
    CableDisconnected,
    ServerCustomerChanged {
        new_customer_id: i32,
    },
    ServerAppChanged {
        new_app_id: i32,
    },
    RackUnmounted,
    SwitchBroken,
    SwitchRepaired,

    DayEnded {
        day: u32,
    },
    MonthEnded {
        month: i32,
    },

    CustomerAccepted {
        customer_id: i32,
    },
    CustomerSatisfied {
        customer_base_id: i32,
    },
    CustomerUnsatisfied {
        customer_base_id: i32,
    },

    ShopCheckout,
    ShopItemAdded {
        item_id: i32,
        price: i32,
        item_type: i32,
    },
    ShopCartCleared,
    ShopItemRemoved {
        uid: i32,
    },

    EmployeeHired,
    EmployeeFired,

    GameSaved,
    GameLoaded,
    GameAutoSaved,

    WallPurchased,

    /// Unknown event
    Unknown {
        event_id: u32,
    },
}

impl Event {
    /// The known [`EventId`], or `None` for [`Event::Unknown`].
    pub fn event_id(&self) -> Option<EventId> {
        EventId::from_raw(self.raw_id())
    }

    /// Raw numeric event ID (works for all variants including Unknown).
    pub fn raw_id(&self) -> u32 {
        match self {
            Self::MoneyChanged { .. } => EventId::MoneyChanged as u32,
            Self::XpChanged { .. } => EventId::XpChanged as u32,
            Self::ReputationChanged { .. } => EventId::ReputationChanged as u32,
            Self::ServerPowered { .. } => EventId::ServerPowered as u32,
            Self::ServerBroken => EventId::ServerBroken as u32,
            Self::ServerRepaired => EventId::ServerRepaired as u32,
            Self::ServerInstalled => EventId::ServerInstalled as u32,
            Self::CableConnected => EventId::CableConnected as u32,
            Self::CableDisconnected => EventId::CableDisconnected as u32,
            Self::ServerCustomerChanged { .. } => EventId::ServerCustomerChanged as u32,
            Self::ServerAppChanged { .. } => EventId::ServerAppChanged as u32,
            Self::RackUnmounted => EventId::RackUnmounted as u32,
            Self::SwitchBroken => EventId::SwitchBroken as u32,
            Self::SwitchRepaired => EventId::SwitchRepaired as u32,
            Self::DayEnded { .. } => EventId::DayEnded as u32,
            Self::MonthEnded { .. } => EventId::MonthEnded as u32,
            Self::CustomerAccepted { .. } => EventId::CustomerAccepted as u32,
            Self::CustomerSatisfied { .. } => EventId::CustomerSatisfied as u32,
            Self::CustomerUnsatisfied { .. } => EventId::CustomerUnsatisfied as u32,
            Self::ShopCheckout => EventId::ShopCheckout as u32,
            Self::ShopItemAdded { .. } => EventId::ShopItemAdded as u32,
            Self::ShopCartCleared => EventId::ShopCartCleared as u32,
            Self::ShopItemRemoved { .. } => EventId::ShopItemRemoved as u32,
            Self::EmployeeHired => EventId::EmployeeHired as u32,
            Self::EmployeeFired => EventId::EmployeeFired as u32,
            Self::GameSaved => EventId::GameSaved as u32,
            Self::GameLoaded => EventId::GameLoaded as u32,
            Self::GameAutoSaved => EventId::GameAutoSaved as u32,
            Self::WallPurchased => EventId::WallPurchased as u32,
            Self::Unknown { event_id } => *event_id,
        }
    }

    /// Backward-compatible alias for [`raw_id`](Self::raw_id).
    pub fn id(&self) -> u32 {
        self.raw_id()
    }

    pub fn is_economy(&self) -> bool {
        self.event_id()
            .map_or(false, |id| id.category() == EventCategory::Economy)
    }

    pub fn is_server(&self) -> bool {
        self.event_id()
            .map_or(false, |id| id.category() == EventCategory::Server)
    }

    pub fn is_time(&self) -> bool {
        self.event_id()
            .map_or(false, |id| id.category() == EventCategory::Time)
    }

    pub fn is_customer(&self) -> bool {
        self.event_id()
            .map_or(false, |id| id.category() == EventCategory::Customer)
    }

    pub fn is_shop(&self) -> bool {
        self.event_id()
            .map_or(false, |id| id.category() == EventCategory::Shop)
    }

    pub fn is_employee(&self) -> bool {
        self.event_id()
            .map_or(false, |id| id.category() == EventCategory::Employee)
    }

    pub fn is_save_load(&self) -> bool {
        self.event_id()
            .map_or(false, |id| id.category() == EventCategory::Persistence)
    }

    pub fn is_building(&self) -> bool {
        self.event_id()
            .map_or(false, |id| id.category() == EventCategory::Building)
    }
}

impl fmt::Display for Event {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::MoneyChanged {
                old_value,
                new_value,
                delta,
            } => write!(
                f,
                "MoneyChanged({:.2} -> {:.2}, delta: {:+.2})",
                old_value, new_value, delta
            ),
            Self::XpChanged {
                old_value,
                new_value,
                delta,
            } => write!(
                f,
                "XpChanged({:.1} -> {:.1}, delta: {:+.1})",
                old_value, new_value, delta
            ),
            Self::ReputationChanged {
                old_value,
                new_value,
                delta,
            } => write!(
                f,
                "ReputationChanged({:.1} -> {:.1}, delta: {:+.1})",
                old_value, new_value, delta
            ),
            Self::ServerPowered { powered_on } => write!(
                f,
                "ServerPowered({})",
                if *powered_on { "ON" } else { "OFF" }
            ),
            Self::ServerBroken => write!(f, "ServerBroken"),
            Self::ServerRepaired => write!(f, "ServerRepaired"),
            Self::ServerInstalled => write!(f, "ServerInstalled"),
            Self::CableConnected => write!(f, "CableConnected"),
            Self::CableDisconnected => write!(f, "CableDisconnected"),
            Self::ServerCustomerChanged { new_customer_id } => {
                write!(f, "ServerCustomerChanged(customer={})", new_customer_id)
            }
            Self::ServerAppChanged { new_app_id } => {
                write!(f, "ServerAppChanged(app={})", new_app_id)
            }
            Self::RackUnmounted => write!(f, "RackUnmounted"),
            Self::SwitchBroken => write!(f, "SwitchBroken"),
            Self::SwitchRepaired => write!(f, "SwitchRepaired"),
            Self::DayEnded { day } => write!(f, "DayEnded(day={})", day),
            Self::MonthEnded { month } => write!(f, "MonthEnded(month={})", month),
            Self::CustomerAccepted { customer_id } => {
                write!(f, "CustomerAccepted(id={})", customer_id)
            }
            Self::CustomerSatisfied { customer_base_id } => {
                write!(f, "CustomerSatisfied(base={})", customer_base_id)
            }
            Self::CustomerUnsatisfied { customer_base_id } => {
                write!(f, "CustomerUnsatisfied(base={})", customer_base_id)
            }
            Self::ShopCheckout => write!(f, "ShopCheckout"),
            Self::ShopItemAdded {
                item_id,
                price,
                item_type,
            } => {
                let type_name = match item_type {
                    0 => "None",
                    1 => "Server1U",
                    2 => "Server7U",
                    3 => "Server3U",
                    4 => "Switch",
                    5 => "Rack",
                    6 => "CableSpinner",
                    7 => "PatchPanel",
                    8 => "SFPModule",
                    9 => "SFPBox",
                    _ => "Unknown",
                };
                write!(
                    f,
                    "ShopItemAdded(item={}, price={}, type={})",
                    item_id, price, type_name
                )
            }
            Self::ShopCartCleared => write!(f, "ShopCartCleared"),
            Self::ShopItemRemoved { uid } => write!(f, "ShopItemRemoved(uid={})", uid),
            Self::EmployeeHired => write!(f, "EmployeeHired"),
            Self::EmployeeFired => write!(f, "EmployeeFired"),
            Self::GameSaved => write!(f, "GameSaved"),
            Self::GameLoaded => write!(f, "GameLoaded"),
            Self::GameAutoSaved => write!(f, "GameAutoSaved"),
            Self::WallPurchased => write!(f, "WallPurchased"),
            Self::Unknown { event_id } => write!(f, "Unknown(id={})", event_id),
        }
    }
}

/// Decode raw FFI event data into a typed [`Event`].
/// Returns `None` if a required payload is missing or too small.
pub fn decode(event_id: u32, data: *const u8, size: u32) -> Option<Event> {
    match EventId::from_raw(event_id) {
        Some(EventId::MoneyChanged) => {
            let d = read_data::<ValueChangedData>(data, size)?;
            Some(Event::MoneyChanged {
                old_value: d.old_value,
                new_value: d.new_value,
                delta: d.delta,
            })
        }
        Some(EventId::XpChanged) => {
            let d = read_data::<ValueChangedData>(data, size)?;
            Some(Event::XpChanged {
                old_value: d.old_value,
                new_value: d.new_value,
                delta: d.delta,
            })
        }
        Some(EventId::ReputationChanged) => {
            let d = read_data::<ValueChangedData>(data, size)?;
            Some(Event::ReputationChanged {
                old_value: d.old_value,
                new_value: d.new_value,
                delta: d.delta,
            })
        }
        Some(EventId::ServerPowered) => {
            let d = read_data::<ServerPoweredData>(data, size)?;
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
            let d = read_data::<ServerCustomerChangedData>(data, size)?;
            Some(Event::ServerCustomerChanged {
                new_customer_id: d.new_customer_id,
            })
        }
        Some(EventId::ServerAppChanged) => {
            let d = read_data::<ServerAppChangedData>(data, size)?;
            Some(Event::ServerAppChanged {
                new_app_id: d.new_app_id,
            })
        }
        Some(EventId::RackUnmounted) => Some(Event::RackUnmounted),
        Some(EventId::SwitchBroken) => Some(Event::SwitchBroken),
        Some(EventId::SwitchRepaired) => Some(Event::SwitchRepaired),
        Some(EventId::DayEnded) => {
            let d = read_data::<DayEndedData>(data, size)?;
            Some(Event::DayEnded { day: d.day })
        }
        Some(EventId::MonthEnded) => {
            let d = read_data::<MonthEndedData>(data, size)?;
            Some(Event::MonthEnded { month: d.month })
        }
        Some(EventId::CustomerAccepted) => {
            let d = read_data::<CustomerAcceptedData>(data, size)?;
            Some(Event::CustomerAccepted {
                customer_id: d.customer_id,
            })
        }
        Some(EventId::CustomerSatisfied) => {
            let d = read_data::<CustomerSatisfiedData>(data, size)?;
            Some(Event::CustomerSatisfied {
                customer_base_id: d.customer_base_id,
            })
        }
        Some(EventId::CustomerUnsatisfied) => {
            let d = read_data::<CustomerSatisfiedData>(data, size)?;
            Some(Event::CustomerUnsatisfied {
                customer_base_id: d.customer_base_id,
            })
        }
        Some(EventId::ShopCheckout) => Some(Event::ShopCheckout),
        Some(EventId::ShopItemAdded) => {
            let d = read_data::<ShopItemAddedData>(data, size)?;
            Some(Event::ShopItemAdded {
                item_id: d.item_id,
                price: d.price,
                item_type: d.item_type,
            })
        }
        Some(EventId::ShopCartCleared) => Some(Event::ShopCartCleared),
        Some(EventId::ShopItemRemoved) => {
            let d = read_data::<ShopItemRemovedData>(data, size)?;
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
    fn all_contains_every_variant() {
        // if you add a new EventId variant, add it to ALL too
        assert_eq!(EventId::ALL.len(), 29);
    }

    #[test]
    fn event_id_roundtrip() {
        for &id in EventId::ALL {
            assert_eq!(
                EventId::from_raw(id.as_u32()),
                Some(id),
                "roundtrip failed for {:?}",
                id
            );
        }
    }

    #[test]
    fn event_id_unknown_returns_none() {
        assert_eq!(EventId::from_raw(9999), None);
        assert_eq!(EventId::from_raw(0), None);
        assert_eq!(EventId::from_raw(199), None);
    }

    #[test]
    fn event_id_display() {
        assert_eq!(EventId::MoneyChanged.to_string(), "MoneyChanged(100)");
        assert_eq!(EventId::GameLoaded.to_string(), "GameLoaded(701)");
        assert_eq!(EventId::ShopCheckout.to_string(), "ShopCheckout(500)");
    }

    #[test]
    fn event_id_categories() {
        assert_eq!(EventId::MoneyChanged.category(), EventCategory::Economy);
        assert_eq!(EventId::XpChanged.category(), EventCategory::Economy);
        assert_eq!(
            EventId::ReputationChanged.category(),
            EventCategory::Economy
        );
        assert_eq!(EventId::ServerPowered.category(), EventCategory::Server);
        assert_eq!(EventId::ServerBroken.category(), EventCategory::Server);
        assert_eq!(EventId::ServerRepaired.category(), EventCategory::Server);
        assert_eq!(EventId::ServerInstalled.category(), EventCategory::Server);
        assert_eq!(EventId::CableConnected.category(), EventCategory::Server);
        assert_eq!(EventId::CableDisconnected.category(), EventCategory::Server);
        assert_eq!(
            EventId::ServerCustomerChanged.category(),
            EventCategory::Server
        );
        assert_eq!(EventId::ServerAppChanged.category(), EventCategory::Server);
        assert_eq!(EventId::RackUnmounted.category(), EventCategory::Server);
        assert_eq!(EventId::SwitchBroken.category(), EventCategory::Server);
        assert_eq!(EventId::SwitchRepaired.category(), EventCategory::Server);
        assert_eq!(EventId::DayEnded.category(), EventCategory::Time);
        assert_eq!(EventId::MonthEnded.category(), EventCategory::Time);
        assert_eq!(
            EventId::CustomerAccepted.category(),
            EventCategory::Customer
        );
        assert_eq!(
            EventId::CustomerSatisfied.category(),
            EventCategory::Customer
        );
        assert_eq!(
            EventId::CustomerUnsatisfied.category(),
            EventCategory::Customer
        );
        assert_eq!(EventId::ShopCheckout.category(), EventCategory::Shop);
        assert_eq!(EventId::ShopItemAdded.category(), EventCategory::Shop);
        assert_eq!(EventId::ShopCartCleared.category(), EventCategory::Shop);
        assert_eq!(EventId::ShopItemRemoved.category(), EventCategory::Shop);
        assert_eq!(EventId::EmployeeHired.category(), EventCategory::Employee);
        assert_eq!(EventId::EmployeeFired.category(), EventCategory::Employee);
        assert_eq!(EventId::GameSaved.category(), EventCategory::Persistence);
        assert_eq!(EventId::GameLoaded.category(), EventCategory::Persistence);
        assert_eq!(
            EventId::GameAutoSaved.category(),
            EventCategory::Persistence
        );
        assert_eq!(EventId::WallPurchased.category(), EventCategory::Building);
    }

    #[test]
    fn event_category_helpers() {
        assert!(Event::MoneyChanged {
            old_value: 0.0,
            new_value: 0.0,
            delta: 0.0
        }
        .is_economy());
        assert!(Event::XpChanged {
            old_value: 0.0,
            new_value: 0.0,
            delta: 0.0
        }
        .is_economy());
        assert!(!Event::ServerBroken.is_economy());

        assert!(Event::ServerBroken.is_server());
        assert!(Event::ServerPowered { powered_on: true }.is_server());
        assert!(Event::CableConnected.is_server());
        assert!(Event::CableDisconnected.is_server());
        assert!(Event::ServerCustomerChanged { new_customer_id: 1 }.is_server());
        assert!(Event::ServerAppChanged { new_app_id: 1 }.is_server());
        assert!(Event::RackUnmounted.is_server());
        assert!(Event::SwitchBroken.is_server());
        assert!(Event::SwitchRepaired.is_server());
        assert!(!Event::GameSaved.is_server());

        assert!(Event::DayEnded { day: 1 }.is_time());
        assert!(Event::MonthEnded { month: 3 }.is_time());
        assert!(!Event::GameSaved.is_time());

        assert!(Event::CustomerAccepted { customer_id: 1 }.is_customer());
        assert!(Event::CustomerSatisfied {
            customer_base_id: 1
        }
        .is_customer());
        assert!(Event::CustomerUnsatisfied {
            customer_base_id: 1
        }
        .is_customer());
        assert!(!Event::ServerBroken.is_customer());

        assert!(Event::ShopCheckout.is_shop());
        assert!(Event::ShopItemAdded {
            item_id: 1,
            price: 50,
            item_type: 1
        }
        .is_shop());
        assert!(Event::ShopItemRemoved { uid: 1 }.is_shop());
        assert!(Event::ShopCartCleared.is_shop());
        assert!(!Event::ServerBroken.is_shop());

        assert!(Event::EmployeeHired.is_employee());
        assert!(Event::EmployeeFired.is_employee());
        assert!(!Event::ServerBroken.is_employee());

        assert!(Event::GameSaved.is_save_load());
        assert!(Event::GameLoaded.is_save_load());
        assert!(Event::GameAutoSaved.is_save_load());
        assert!(!Event::ServerBroken.is_save_load());

        assert!(Event::WallPurchased.is_building());
        assert!(!Event::ServerBroken.is_building());

        // Unknown events return false for all categories
        assert!(!Event::Unknown { event_id: 9999 }.is_economy());
        assert!(!Event::Unknown { event_id: 9999 }.is_server());
        assert!(!Event::Unknown { event_id: 9999 }.is_building());
    }

    #[test]
    fn event_display() {
        let e = Event::MoneyChanged {
            old_value: 100.0,
            new_value: 200.0,
            delta: 100.0,
        };
        assert_eq!(
            e.to_string(),
            "MoneyChanged(100.00 -> 200.00, delta: +100.00)"
        );

        let e = Event::ServerPowered { powered_on: true };
        assert_eq!(e.to_string(), "ServerPowered(ON)");

        let e = Event::ServerPowered { powered_on: false };
        assert_eq!(e.to_string(), "ServerPowered(OFF)");

        assert_eq!(Event::ServerBroken.to_string(), "ServerBroken");
        assert_eq!(Event::DayEnded { day: 7 }.to_string(), "DayEnded(day=7)");
        assert_eq!(
            Event::Unknown { event_id: 42 }.to_string(),
            "Unknown(id=42)"
        );

        assert_eq!(Event::RackUnmounted.to_string(), "RackUnmounted");
        assert_eq!(Event::SwitchBroken.to_string(), "SwitchBroken");
        assert_eq!(Event::SwitchRepaired.to_string(), "SwitchRepaired");
        assert_eq!(
            Event::MonthEnded { month: 5 }.to_string(),
            "MonthEnded(month=5)"
        );
        assert_eq!(
            Event::ShopItemAdded {
                item_id: 10,
                price: 99,
                item_type: 2
            }
            .to_string(),
            "ShopItemAdded(item=10, price=99, type=Server2U)"
        );
        assert_eq!(Event::ShopCartCleared.to_string(), "ShopCartCleared");
        assert_eq!(
            Event::ShopItemRemoved { uid: 42 }.to_string(),
            "ShopItemRemoved(uid=42)"
        );
        assert_eq!(Event::GameAutoSaved.to_string(), "GameAutoSaved");
        assert_eq!(Event::WallPurchased.to_string(), "WallPurchased");
    }

    #[test]
    fn event_id_method() {
        let e = Event::MoneyChanged {
            old_value: 0.0,
            new_value: 0.0,
            delta: 0.0,
        };
        assert_eq!(e.event_id(), Some(EventId::MoneyChanged));

        assert_eq!(Event::GameSaved.event_id(), Some(EventId::GameSaved));
        assert_eq!(Event::Unknown { event_id: 9999 }.event_id(), None);
    }

    #[test]
    fn event_category_display() {
        assert_eq!(EventCategory::Economy.to_string(), "Economy");
        assert_eq!(EventCategory::Persistence.to_string(), "Persistence");
        assert_eq!(EventCategory::Building.to_string(), "Building");
    }
}

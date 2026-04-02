//! Decoded event enum with typed payloads and convenience helpers.

use std::fmt;

use super::category::EventCategory;
use super::event_id::EventId;

/// A decoded game event with its payload.
/// Use [`super::decode`] to convert raw FFI data into this type.
#[derive(Debug, Clone)]
pub enum Event {
    // economy (1xx)
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

    // server & infrastructure (2xx)
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

    // time (3xx)
    DayEnded {
        day: u32,
    },
    MonthEnded {
        month: i32,
    },

    // customer (4xx)
    CustomerAccepted {
        customer_id: i32,
    },
    CustomerSatisfied {
        customer_base_id: i32,
    },
    CustomerUnsatisfied {
        customer_base_id: i32,
    },

    // shop (5xx)
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

    // employee (6xx)
    EmployeeHired,
    EmployeeFired,

    // persistence (7xx)
    GameSaved,
    GameLoaded,
    GameAutoSaved,

    // building (8xx)
    WallPurchased,

    // mod systems (10xx)
    CustomEmployeeHired {
        employee_id: String,
    },
    CustomEmployeeFired {
        employee_id: String,
    },

    /// Unknown / unrecognised event — the raw ID is preserved.
    Unknown {
        event_id: u32,
    },
}

impl Event {
    /// The known [`EventId`], or `None` for [`Event::Unknown`].
    pub fn event_id(&self) -> Option<EventId> {
        EventId::from_raw(self.raw_id())
    }

    /// Raw numeric event ID (works for all variants including `Unknown`).
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
            Self::CustomEmployeeHired { .. } => EventId::CustomEmployeeHired as u32,
            Self::CustomEmployeeFired { .. } => EventId::CustomEmployeeFired as u32,
            Self::Unknown { event_id } => *event_id,
        }
    }

    /// Backward-compatible alias for [`raw_id`](Self::raw_id).
    pub fn id(&self) -> u32 {
        self.raw_id()
    }

    // ── category helpers ───────────────────────────────────────────

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

    pub fn is_mod_systems(&self) -> bool {
        self.event_id()
            .map_or(false, |id| id.category() == EventCategory::ModSystems)
    }
}

/// Human-readable name of an `ObjectInHand` item-type value from the game.
pub fn item_type_name(item_type: i32) -> &'static str {
    match item_type {
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
                write!(
                    f,
                    "ShopItemAdded(item={}, price={}, type={})",
                    item_id,
                    price,
                    item_type_name(*item_type)
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
            Self::CustomEmployeeHired { employee_id } => {
                write!(f, "CustomEmployeeHired(id={})", employee_id)
            }
            Self::CustomEmployeeFired { employee_id } => {
                write!(f, "CustomEmployeeFired(id={})", employee_id)
            }
            Self::Unknown { event_id } => write!(f, "Unknown(id={})", event_id),
        }
    }
}

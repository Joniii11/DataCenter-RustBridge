//! Numeric event identifiers grouped by category.

use std::fmt;

use super::EventCategory;

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
/// - **10xx**: Mod systems (custom employees, etc.)
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
    ObjectSpawned = 211,

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

    // mod systems (10xx)
    CustomEmployeeHired = 1000,
    CustomEmployeeFired = 1001,
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
        Self::ObjectSpawned,
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
        Self::CustomEmployeeHired,
        Self::CustomEmployeeFired,
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
            Self::ObjectSpawned => "ObjectSpawned",
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
            Self::CustomEmployeeHired => "CustomEmployeeHired",
            Self::CustomEmployeeFired => "CustomEmployeeFired",
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
            | Self::SwitchRepaired
            | Self::ObjectSpawned => EventCategory::Server,
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
            Self::CustomEmployeeHired | Self::CustomEmployeeFired => EventCategory::ModSystems,
        }
    }
}

impl fmt::Display for EventId {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}({})", self.name(), self.as_u32())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn all_contains_every_variant() {
        assert_eq!(EventId::ALL.len(), 32);
    }

    #[test]
    fn roundtrip_all() {
        for &id in EventId::ALL {
            assert_eq!(
                EventId::from_raw(id.as_u32()),
                Some(id),
                "roundtrip failed for {:?} ({})",
                id,
                id.as_u32()
            );
        }
    }

    #[test]
    fn unknown_returns_none() {
        assert_eq!(EventId::from_raw(0), None);
        assert_eq!(EventId::from_raw(99), None);
        assert_eq!(EventId::from_raw(199), None);
        assert_eq!(EventId::from_raw(999), None);
        assert_eq!(EventId::from_raw(9999), None);
        assert_eq!(EventId::from_raw(u32::MAX), None);
    }

    #[test]
    fn display_format() {
        assert_eq!(EventId::MoneyChanged.to_string(), "MoneyChanged(100)");
        assert_eq!(EventId::ServerPowered.to_string(), "ServerPowered(200)");
        assert_eq!(EventId::DayEnded.to_string(), "DayEnded(300)");
        assert_eq!(
            EventId::CustomerAccepted.to_string(),
            "CustomerAccepted(400)"
        );
        assert_eq!(EventId::ShopCheckout.to_string(), "ShopCheckout(500)");
        assert_eq!(EventId::EmployeeHired.to_string(), "EmployeeHired(600)");
        assert_eq!(EventId::GameLoaded.to_string(), "GameLoaded(701)");
        assert_eq!(EventId::WallPurchased.to_string(), "WallPurchased(800)");
        assert_eq!(
            EventId::CustomEmployeeHired.to_string(),
            "CustomEmployeeHired(1000)"
        );
        assert_eq!(
            EventId::CustomEmployeeFired.to_string(),
            "CustomEmployeeFired(1001)"
        );
        assert_eq!(EventId::ObjectSpawned.to_string(), "ObjectSpawned(211)");
    }

    #[test]
    fn no_duplicate_ids() {
        let mut seen = std::collections::HashSet::new();
        for &id in EventId::ALL {
            assert!(
                seen.insert(id.as_u32()),
                "duplicate numeric ID: {} ({:?})",
                id.as_u32(),
                id
            );
        }
    }

    #[test]
    fn no_duplicate_names() {
        let mut seen = std::collections::HashSet::new();
        for &id in EventId::ALL {
            assert!(
                seen.insert(id.name()),
                "duplicate name: {:?} ({:?})",
                id.name(),
                id
            );
        }
    }

    #[test]
    fn name_is_non_empty() {
        for &id in EventId::ALL {
            assert!(!id.name().is_empty(), "empty name for {:?}", id);
        }
    }

    #[test]
    fn ids_in_correct_category_range() {
        for &id in EventId::ALL {
            let raw = id.as_u32();
            let expected_range = match id.category() {
                EventCategory::Economy => 100..200,
                EventCategory::Server => 200..300,
                EventCategory::Time => 300..400,
                EventCategory::Customer => 400..500,
                EventCategory::Shop => 500..600,
                EventCategory::Employee => 600..700,
                EventCategory::Persistence => 700..800,
                EventCategory::Building => 800..900,
                EventCategory::ModSystems => 1000..1100,
            };
            assert!(
                expected_range.contains(&raw),
                "{:?} (id={}) not in expected range {:?} for category {:?}",
                id,
                raw,
                expected_range,
                id.category()
            );
        }
    }

    #[test]
    fn categories_per_event() {
        // economy
        assert_eq!(EventId::MoneyChanged.category(), EventCategory::Economy);
        assert_eq!(EventId::XpChanged.category(), EventCategory::Economy);
        assert_eq!(
            EventId::ReputationChanged.category(),
            EventCategory::Economy
        );

        // server
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
        assert_eq!(EventId::ObjectSpawned.category(), EventCategory::Server);

        // time
        assert_eq!(EventId::DayEnded.category(), EventCategory::Time);
        assert_eq!(EventId::MonthEnded.category(), EventCategory::Time);

        // customer
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

        // shop
        assert_eq!(EventId::ShopCheckout.category(), EventCategory::Shop);
        assert_eq!(EventId::ShopItemAdded.category(), EventCategory::Shop);
        assert_eq!(EventId::ShopCartCleared.category(), EventCategory::Shop);
        assert_eq!(EventId::ShopItemRemoved.category(), EventCategory::Shop);

        // employee
        assert_eq!(EventId::EmployeeHired.category(), EventCategory::Employee);
        assert_eq!(EventId::EmployeeFired.category(), EventCategory::Employee);

        // persistence
        assert_eq!(EventId::GameSaved.category(), EventCategory::Persistence);
        assert_eq!(EventId::GameLoaded.category(), EventCategory::Persistence);
        assert_eq!(
            EventId::GameAutoSaved.category(),
            EventCategory::Persistence
        );

        // building
        assert_eq!(EventId::WallPurchased.category(), EventCategory::Building);

        // mod systems
        assert_eq!(
            EventId::CustomEmployeeHired.category(),
            EventCategory::ModSystems
        );
        assert_eq!(
            EventId::CustomEmployeeFired.category(),
            EventCategory::ModSystems
        );
    }
}

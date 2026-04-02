//! Event categories derived from the hundred-range of the event ID.

use std::fmt;

/// Event categories (derived from the hundred-range of the event ID).
///
/// - **1xx**: [`Economy`](Self::Economy)
/// - **2xx**: [`Server`](Self::Server)
/// - **3xx**: [`Time`](Self::Time)
/// - **4xx**: [`Customer`](Self::Customer)
/// - **5xx**: [`Shop`](Self::Shop)
/// - **6xx**: [`Employee`](Self::Employee)
/// - **7xx**: [`Persistence`](Self::Persistence)
/// - **8xx**: [`Building`](Self::Building)
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
    ModSystems,
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
            Self::ModSystems => "ModSystems",
        })
    }
}

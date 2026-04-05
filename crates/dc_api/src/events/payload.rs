//! `#[repr(C)]` payload structs shared between C# and Rust across the FFI boundary.
//!
//! Each struct must match the corresponding `[StructLayout(LayoutKind.Sequential)]`
//! definition in `EventSystem.cs` on the C# side.

/// Payload for economy events: [`MoneyChanged`], [`XpChanged`], [`ReputationChanged`].
#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct ValueChangedData {
    pub old_value: f64,
    pub new_value: f64,
    pub delta: f64,
}

/// Payload for [`ServerPowered`]. `powered_on`: 1 = on, 0 = off.
#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct ServerPoweredData {
    pub powered_on: u32,
}

/// Payload for [`DayEnded`].
#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct DayEndedData {
    pub day: u32,
}

/// Payload for [`CustomerAccepted`].
#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct CustomerAcceptedData {
    pub customer_id: i32,
}

/// Payload for [`CustomerSatisfied`] and [`CustomerUnsatisfied`].
#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct CustomerSatisfiedData {
    pub customer_base_id: i32,
}

/// Payload for [`ServerCustomerChanged`].
#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct ServerCustomerChangedData {
    pub new_customer_id: i32,
}

/// Payload for [`ServerAppChanged`].
#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct ServerAppChangedData {
    pub new_app_id: i32,
}

/// Payload for [`MonthEnded`].
#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct MonthEndedData {
    pub month: i32,
}

/// Payload for [`ShopItemAdded`].
///
/// `item_type` corresponds to the game's `ObjectInHand` enum:
///
/// | Value | Type         |
/// |-------|--------------|
/// | 0     | None         |
/// | 1     | Server1U     |
/// | 2     | Server7U     |
/// | 3     | Server3U     |
/// | 4     | Switch       |
/// | 5     | Rack         |
/// | 6     | CableSpinner |
/// | 7     | PatchPanel   |
/// | 8     | SFPModule    |
/// | 9     | SFPBox       |
#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct ShopItemAddedData {
    pub item_id: i32,
    pub price: i32,
    pub item_type: i32,
}

/// Payload for [`ShopItemRemoved`].
#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct ShopItemRemovedData {
    pub uid: i32,
}

/// Payload for [`CustomEmployeeHired`] and [`CustomEmployeeFired`].
/// The `employee_id` is a null-terminated ASCII string in a fixed-size buffer.
#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct CustomEmployeeEventData {
    pub employee_id: [u8; 64],
}

impl CustomEmployeeEventData {
    /// Extract the employee ID as a string slice.
    pub fn id(&self) -> &str {
        let end = self
            .employee_id
            .iter()
            .position(|&b| b == 0)
            .unwrap_or(self.employee_id.len());
        std::str::from_utf8(&self.employee_id[..end]).unwrap_or("")
    }
}

/// Payload for [`ServerInstalled`] with server identity and rack positio
#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct ServerInstalledData {
    pub server_id: [u8; 64],
    pub object_type: u8,
    pub rack_position_uid: i32,
}

impl ServerInstalledData {
    pub fn id(&self) -> &str {
        let end = self
            .server_id
            .iter()
            .position(|&b| b == 0)
            .unwrap_or(self.server_id.len());
        std::str::from_utf8(&self.server_id[..end]).unwrap_or("")
    }
}

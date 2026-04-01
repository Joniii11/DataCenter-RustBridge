using System;
using System.Runtime.InteropServices;
using MelonLoader;

namespace DataCenterModLoader;

/// Event IDs shared with Rust mods. Must stay in sync with dc_api/src/events.rs.
public static class EventIds
{
    public const uint MoneyChanged      = 100;
    public const uint XPChanged         = 101;
    public const uint ReputationChanged = 102;

    public const uint ServerPowered   = 200;
    public const uint ServerBroken    = 201;
    public const uint ServerRepaired  = 202;
    public const uint ServerInstalled = 203;

    public const uint DayEnded = 300;

    public const uint CustomerAccepted = 400;

    public const uint ShopCheckout = 500;

    public const uint EmployeeHired = 600;
    public const uint EmployeeFired = 601;

    public const uint GameSaved  = 700;
    public const uint GameLoaded = 701;
}

// Event data structs (must match Rust #[repr(C)] layouts byte-for-byte)

[StructLayout(LayoutKind.Sequential)]
public struct ValueChangedData
{
    public double OldValue;
    public double NewValue;
    public double Delta;
}

[StructLayout(LayoutKind.Sequential)]
public struct ServerPoweredData
{
    public uint PoweredOn; // 1 = on, 0 = off
}

[StructLayout(LayoutKind.Sequential)]
public struct DayEndedData
{
    public uint Day;
}

[StructLayout(LayoutKind.Sequential)]
public struct CustomerAcceptedData
{
    public int CustomerId;
}

/// Harmony patches call Fire* methods here, and the dispatcher forwards events
/// to all Rust mods that export mod_on_event.
public static class EventDispatcher
{
    private static FFIBridge _bridge;
    private static MelonLogger.Instance _logger;

    public static void Initialize(FFIBridge bridge, MelonLogger.Instance logger)
    {
        _bridge = bridge;
        _logger = logger;
    }

    private static void DispatchWithData<T>(uint eventId, T data) where T : struct
    {
        if (_bridge == null) return;

        int size = Marshal.SizeOf<T>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(data, ptr, false);
            _bridge.DispatchEvent(eventId, ptr, (uint)size);
        }
        catch (Exception ex)
        {
            _logger?.Error($"Failed to dispatch event {eventId}: {ex.Message}");
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public static void FireSimple(uint eventId)
    {
        if (_bridge == null) return;
        try { _bridge.DispatchEvent(eventId, IntPtr.Zero, 0); }
        catch (Exception ex) { _logger?.Error($"Failed to dispatch event {eventId}: {ex.Message}"); }
    }

    public static void LogError(string message)
    {
        _logger?.Error("[Events] " + message);
    }

    public static void FireValueChanged(uint eventId, double oldValue, double newValue, double delta)
    {
        DispatchWithData(eventId, new ValueChangedData
        {
            OldValue = oldValue,
            NewValue = newValue,
            Delta = delta,
        });
    }

    public static void FireServerPowered(bool poweredOn)
    {
        DispatchWithData(EventIds.ServerPowered, new ServerPoweredData
        {
            PoweredOn = poweredOn ? 1u : 0u,
        });
    }

    public static void FireDayEnded(uint day)
    {
        DispatchWithData(EventIds.DayEnded, new DayEndedData { Day = day });
    }

    public static void FireCustomerAccepted(int customerId)
    {
        DispatchWithData(EventIds.CustomerAccepted, new CustomerAcceptedData { CustomerId = customerId });
    }
}

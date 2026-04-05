using System;
using System.Runtime.InteropServices;
using MelonLoader;

namespace DataCenterModLoader;

// must match dc_api/src/events.rs
public static class EventIds
{
    public const uint MoneyChanged = 100;
    public const uint XPChanged = 101;
    public const uint ReputationChanged = 102;

    public const uint ServerPowered = 200;
    public const uint ServerBroken = 201;
    public const uint ServerRepaired = 202;
    public const uint ServerInstalled = 203;
    public const uint CableConnected = 204;
    public const uint CableDisconnected = 205;
    public const uint ServerCustomerChanged = 206;
    public const uint ServerAppChanged = 207;
    public const uint RackUnmounted = 208;
    public const uint SwitchBroken = 209;
    public const uint SwitchRepaired = 210;

    public const uint DayEnded = 300;
    public const uint MonthEnded = 301;

    public const uint CustomerAccepted = 400;
    public const uint CustomerSatisfied = 401;
    public const uint CustomerUnsatisfied = 402;

    public const uint ShopCheckout = 500;
    public const uint ShopItemAdded = 501;
    public const uint ShopCartCleared = 502;
    public const uint ShopItemRemoved = 503;

    public const uint EmployeeHired = 600;
    public const uint EmployeeFired = 601;

    public const uint GameSaved = 700;
    public const uint GameLoaded = 701;
    public const uint GameAutoSaved = 702;

    public const uint WallPurchased = 800;

    public const uint NetWatchDispatched = 900; // 9xx = mod systems

    // mod systems (10xx)
    public const uint CustomEmployeeHired = 1000;
    public const uint CustomEmployeeFired = 1001;
}

// must match rust repr(C) layouts

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

[StructLayout(LayoutKind.Sequential)]
public struct CustomerSatisfiedData
{
    public int CustomerBaseId;
}

[StructLayout(LayoutKind.Sequential)]
public struct ServerCustomerChangedData
{
    public int NewCustomerId;
}

[StructLayout(LayoutKind.Sequential)]
public struct ServerAppChangedData
{
    public int NewAppId;
}

[StructLayout(LayoutKind.Sequential)]
public struct MonthEndedData
{
    public int Month;
}

[StructLayout(LayoutKind.Sequential)]
public struct ShopItemAddedData
{
    public int ItemId;
    public int Price;
    public int ItemType;
}

[StructLayout(LayoutKind.Sequential)]
public struct ShopItemRemovedData
{
    public int Uid;
}

[StructLayout(LayoutKind.Sequential)]
public struct NetWatchDispatchedData
{
    public int DeviceType; // 0 = server, 1 = switch
    public int Reason;     // 0 = broken, 1 = eol_warning
}

[StructLayout(LayoutKind.Sequential)]
public struct CustomEmployeeEventData
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
    public byte[] EmployeeId;

    public static CustomEmployeeEventData Create(string employeeId)
    {
        var data = new CustomEmployeeEventData { EmployeeId = new byte[64] };
        if (!string.IsNullOrEmpty(employeeId))
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(employeeId);
            Array.Copy(bytes, data.EmployeeId, Math.Min(bytes.Length, 63));
        }
        return data;
    }
}

/// Payload for ServerInstalled events. Must match Rust's ServerInstalledData layout.
[StructLayout(LayoutKind.Sequential)]
public struct ServerInstalledData
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
    public byte[] ServerId;
    public byte ObjectType;
    public int RackPositionUid;

    public static ServerInstalledData Create(string serverId, byte objectType, int rackPositionUid)
    {
        var data = new ServerInstalledData
        {
            ServerId = new byte[64],
            ObjectType = objectType,
            RackPositionUid = rackPositionUid
        };
        if (!string.IsNullOrEmpty(serverId))
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(serverId);
            Array.Copy(bytes, data.ServerId, Math.Min(bytes.Length, 63));
        }
        return data;
    }
}

public static class EventDispatcher
{
    private static FFIBridge _bridge;
    private static MelonLogger.Instance _logger;

    // dedup: harmony + il2cpp can double-fire patches
    private static uint _lastEventId;
    private static long _lastEventTick;
    private static double _lastEventPayloadHash;

    public static void Initialize(FFIBridge bridge, MelonLogger.Instance logger)
    {
        _bridge = bridge;
        _logger = logger;
    }

    private static bool IsDuplicate(uint eventId, double payloadHash = 0.0)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        long elapsed = now - _lastEventTick;
        long threshold = System.Diagnostics.Stopwatch.Frequency / 20; // ~50ms window

        bool isDup = (eventId == _lastEventId)
                     && (elapsed < threshold)
                     && (Math.Abs(payloadHash - _lastEventPayloadHash) < 0.0001);

        _lastEventId = eventId;
        _lastEventTick = now;
        _lastEventPayloadHash = payloadHash;

        return isDup;
    }

    private static void DispatchWithData<T>(uint eventId, T data, double payloadHash = 0.0) where T : struct
    {
        if (_bridge == null) return;
        if (IsDuplicate(eventId, payloadHash)) return;

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
        if (IsDuplicate(eventId)) return;
        try
        {
            _bridge.DispatchEvent(eventId, IntPtr.Zero, 0);
        }
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
        }, oldValue + newValue * 31.0);
    }

    public static void FireServerPowered(bool poweredOn)
    {
        DispatchWithData(EventIds.ServerPowered, new ServerPoweredData
        {
            PoweredOn = poweredOn ? 1u : 0u,
        }, poweredOn ? 1.0 : 0.0);
    }

    public static void FireDayEnded(uint day)
    {
        DispatchWithData(EventIds.DayEnded, new DayEndedData { Day = day }, day);
    }

    public static void FireCustomerAccepted(int customerId)
    {
        DispatchWithData(EventIds.CustomerAccepted, new CustomerAcceptedData { CustomerId = customerId }, customerId);
    }

    public static void FireCustomerSatisfied(int customerBaseId)
    {
        DispatchWithData(EventIds.CustomerSatisfied, new CustomerSatisfiedData { CustomerBaseId = customerBaseId }, customerBaseId);
    }

    public static void FireCustomerUnsatisfied(int customerBaseId)
    {
        DispatchWithData(EventIds.CustomerUnsatisfied, new CustomerSatisfiedData { CustomerBaseId = customerBaseId }, customerBaseId + 0.5);
    }

    public static void FireCableConnected()
    {
        FireSimple(EventIds.CableConnected);
    }

    public static void FireCableDisconnected()
    {
        FireSimple(EventIds.CableDisconnected);
    }

    public static void FireServerCustomerChanged(int newCustomerId)
    {
        DispatchWithData(EventIds.ServerCustomerChanged, new ServerCustomerChangedData { NewCustomerId = newCustomerId }, newCustomerId);
    }

    public static void FireServerAppChanged(int newAppId)
    {
        DispatchWithData(EventIds.ServerAppChanged, new ServerAppChangedData { NewAppId = newAppId }, newAppId);
    }

    public static void FireServerInstalled(string serverId, byte objectType, int rackPositionUid)
    {
        DispatchWithData(EventIds.ServerInstalled,
            ServerInstalledData.Create(serverId, objectType, rackPositionUid),
            serverId?.GetHashCode() ?? 0 + rackPositionUid * 31.0);
    }

    public static void FireRackUnmounted()
    {
        FireSimple(EventIds.RackUnmounted);
    }

    public static void FireSwitchBroken()
    {
        FireSimple(EventIds.SwitchBroken);
    }

    public static void FireSwitchRepaired()
    {
        FireSimple(EventIds.SwitchRepaired);
    }

    public static void FireMonthEnded(int month)
    {
        DispatchWithData(EventIds.MonthEnded, new MonthEndedData { Month = month }, month);
    }

    public static void FireShopItemAdded(int itemId, int price, int itemType)
    {
        DispatchWithData(EventIds.ShopItemAdded, new ShopItemAddedData { ItemId = itemId, Price = price, ItemType = itemType }, itemId * 1000.0 + price + itemType * 0.1);
    }

    public static void FireShopItemRemoved(int uid)
    {
        DispatchWithData(EventIds.ShopItemRemoved, new ShopItemRemovedData { Uid = uid }, uid);
    }

    public static void FireShopCartCleared()
    {
        FireSimple(EventIds.ShopCartCleared);
    }

    public static void FireGameAutoSaved()
    {
        FireSimple(EventIds.GameAutoSaved);
    }

    public static void FireWallPurchased()
    {
        FireSimple(EventIds.WallPurchased);
    }

    public static void FireNetWatchDispatched(int deviceType, int reason)
    {
        DispatchWithData(EventIds.NetWatchDispatched, new NetWatchDispatchedData
        {
            DeviceType = deviceType,
            Reason = reason
        }, deviceType * 10.0 + reason);
    }

    public static void FireCustomEmployeeHired(string employeeId)
    {
        DispatchWithData(EventIds.CustomEmployeeHired, CustomEmployeeEventData.Create(employeeId), employeeId.GetHashCode());
    }

    public static void FireCustomEmployeeFired(string employeeId)
    {
        DispatchWithData(EventIds.CustomEmployeeFired, CustomEmployeeEventData.Create(employeeId), employeeId.GetHashCode() + 0.5);
    }
}

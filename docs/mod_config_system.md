# Mod Config System — Developer Guide

The **Mod Config System** lets MelonLoader mods register, read, and write configuration entries (bools, ints, floats) that are:

- **Persisted** to JSON files under `UserData/ModConfigs/<modId>.json`
- **Editable at runtime** through an IMGUI settings panel (hotkey **F8**, or via the **Settings** button in the main menu)
- **Immediately saved** whenever a value changes

This guide is for **C# MelonLoader mod authors**. If you are writing a Rust native mod, config registration happens automatically through the FFI bridge — see the main [README](../README.md).

---

## Table of Contents

1. [Quick Start](#quick-start)
2. [Accessing the Settings Panel](#accessing-the-settings-panel)
3. [API Reference](#api-reference)
   - [Registration](#registration)
   - [Getters](#getters)
   - [Setters](#setters)
   - [Metadata](#metadata)
   - [Panel Control](#panel-control)
   - [Queries](#queries)
4. [Configuration Files](#configuration-files)
5. [Full Example Mod](#full-example-mod)
6. [Design Notes](#design-notes)

---

## Quick Start

All interaction goes through the static class **`DataCenterModLoader.ModConfigSystem`**.

```csharp
using DataCenterModLoader;

// 1. Set your mod's display info (shown in the panel header)
ModConfigSystem.SetModInfo("my_cool_mod", "YourName", "1.0.0");

// 2. Register options
ModConfigSystem.RegisterBoolOption("my_cool_mod", "god_mode",   "God Mode",       false, "Prevents all damage");
ModConfigSystem.RegisterIntOption ("my_cool_mod", "move_speed", "Move Speed",      5, 1, 20, "Player move speed multiplier");
ModConfigSystem.RegisterFloatOption("my_cool_mod", "gravity",   "Gravity Scale",   1.0f, 0.0f, 3.0f, "World gravity multiplier");

// 3. Read values at any time
bool godMode   = ModConfigSystem.GetBoolValue("my_cool_mod",  "god_mode");
int  speed     = ModConfigSystem.GetIntValue("my_cool_mod",   "move_speed");
float gravity  = ModConfigSystem.GetFloatValue("my_cool_mod", "gravity");
```

That's it. The system handles persistence, UI rendering, and save-on-change automatically.

---

## Accessing the Settings Panel

Players (and mod developers) can open the Mod Settings panel in two ways:

| Method | Description |
|--------|-------------|
| **F8** | Toggle the settings panel on/off from anywhere in the game |
| **Main Menu → Settings** | The Settings button in the main menu shows a popup with **"Game Settings"** and **"Mod Settings"** choices |

While the panel is open:

- **ESC** closes the panel (or the Settings popup if that is showing)
- The game's input/EventSystem is disabled to prevent click-through
- The panel can be **dragged** by its title bar
- Entries are scrollable if there are many options

---

## API Reference

All methods are `public static` on `DataCenterModLoader.ModConfigSystem`.

### Registration

Register config entries during your mod's initialization (e.g., `OnInitializeMelon` or `OnSceneWasLoaded`). If a persisted value exists on disk, it is loaded automatically — the `defaultValue` is only used when no saved value is found.

#### `RegisterBoolOption`

```csharp
public static bool RegisterBoolOption(
    string modId,
    string key,
    string displayName,
    bool   defaultValue,
    string description = ""
)
```

| Parameter | Description |
|-----------|-------------|
| `modId` | Unique identifier for your mod (e.g., `"my_mod"`). Used as the config filename. |
| `key` | Unique key within the mod (e.g., `"enable_feature"`). |
| `displayName` | Human-readable label shown in the settings panel. |
| `defaultValue` | Default value when no persisted config exists. |
| `description` | Optional tooltip/description shown below the entry in the panel. |

**Returns:** `true` if the entry was newly registered, `false` if the key already exists or an error occurred.

---

#### `RegisterIntOption`

```csharp
public static bool RegisterIntOption(
    string modId,
    string key,
    string displayName,
    int    defaultValue,
    int    min,
    int    max,
    string description = ""
)
```

| Parameter | Description |
|-----------|-------------|
| `modId` | Unique identifier for your mod. |
| `key` | Unique key within the mod. |
| `displayName` | Human-readable label shown in the panel. |
| `defaultValue` | Default value (clamped to `[min, max]`). |
| `min` | Minimum allowed value. |
| `max` | Maximum allowed value. |
| `description` | Optional description text. |

**Returns:** `true` if newly registered, `false` otherwise.

In the panel, int entries show **−** / **+** buttons. Hold **Shift** for larger step sizes.

---

#### `RegisterFloatOption`

```csharp
public static bool RegisterFloatOption(
    string modId,
    string key,
    string displayName,
    float  defaultValue,
    float  min,
    float  max,
    string description = ""
)
```

| Parameter | Description |
|-----------|-------------|
| `modId` | Unique identifier for your mod. |
| `key` | Unique key within the mod. |
| `displayName` | Human-readable label shown in the panel. |
| `defaultValue` | Default value (clamped to `[min, max]`). |
| `min` | Minimum allowed value. |
| `max` | Maximum allowed value. |
| `description` | Optional description text. |

**Returns:** `true` if newly registered, `false` otherwise.

Float entries also use **−** / **+** buttons with **Shift** for finer/coarser adjustment.

---

### Getters

Read the current value of a registered config entry. These are safe to call at any time — if the entry doesn't exist, the default is returned.

#### `GetBoolValue`

```csharp
public static bool GetBoolValue(string modId, string key, bool defaultValue = false)
```

Returns the current boolean value, or `defaultValue` if the entry is not found.

---

#### `GetIntValue`

```csharp
public static int GetIntValue(string modId, string key, int defaultValue = 0)
```

Returns the current integer value, or `defaultValue` if the entry is not found.

---

#### `GetFloatValue`

```csharp
public static float GetFloatValue(string modId, string key, float defaultValue = 0f)
```

Returns the current float value, or `defaultValue` if the entry is not found.

---

### Setters

Programmatically change a config value. The new value is **immediately persisted** to disk and will be reflected in the settings panel.

#### `SetBoolValue`

```csharp
public static bool SetBoolValue(string modId, string key, bool value)
```

**Returns:** `true` on success, `false` if the entry doesn't exist or has the wrong type.

---

#### `SetIntValue`

```csharp
public static bool SetIntValue(string modId, string key, int value)
```

The value is automatically clamped to the `[min, max]` range specified during registration.

**Returns:** `true` on success, `false` if the entry doesn't exist or has the wrong type.

---

#### `SetFloatValue`

```csharp
public static bool SetFloatValue(string modId, string key, float value)
```

The value is automatically clamped to the `[min, max]` range specified during registration.

**Returns:** `true` on success, `false` if the entry doesn't exist or has the wrong type.

---

### Metadata

#### `SetModInfo`

```csharp
public static void SetModInfo(string modId, string author, string version)
```

Attaches display metadata to your mod. This is shown in the settings panel header when your mod is selected:

```
┌─────────────────────────────┐
│  MY COOL MOD                │
│  by YourName — v1.0.0       │
│─────────────────────────────│
│  [x] God Mode               │
│      Prevents all damage    │
│                             │
│  Move Speed: 5   [−] [+]   │
│      Player move speed mult │
└─────────────────────────────┘
```

Call this before or after registration — either works. If not called, the panel shows just the `modId` with no author/version.

---

### Panel Control

#### `OpenPanel`

```csharp
public static void OpenPanel()
```

Programmatically opens the mod settings panel. Equivalent to the user pressing F8.

---

#### `ClosePanel`

```csharp
public static void ClosePanel()
```

Programmatically closes the mod settings panel.

---

### Queries

#### `HasOption`

```csharp
public static bool HasOption(string modId, string key)
```

Returns `true` if a config entry with the given `modId` and `key` exists (any type).

---

#### `IsPanelVisible`

```csharp
public static bool IsPanelVisible { get; }
```

Property that returns `true` if the settings panel is currently open.

---

## Configuration Files

Config files are stored as JSON at:

```
<GameRoot>/UserData/ModConfigs/<modId>.json
```

For example, a mod with `modId = "my_cool_mod"` produces:

```
UserData/ModConfigs/my_cool_mod.json
```

### File Format

```json
{
  "modId": "my_cool_mod",
  "author": "YourName",
  "version": "1.0.0",
  "entries": {
    "god_mode": {
      "type": "Bool",
      "displayName": "God Mode",
      "description": "Prevents all damage",
      "value": true,
      "default": false
    },
    "move_speed": {
      "type": "Int",
      "displayName": "Move Speed",
      "description": "Player move speed multiplier",
      "value": 10,
      "default": 5,
      "min": 1,
      "max": 20
    },
    "gravity": {
      "type": "Float",
      "displayName": "Gravity Scale",
      "description": "World gravity multiplier",
      "value": 1.5,
      "default": 1.0,
      "min": 0.0,
      "max": 3.0
    }
  }
}
```

> **Note:** You don't need to create or edit these files manually. The system creates and updates them automatically. However, players _can_ hand-edit them while the game is not running.

---

## Full Example Mod

Below is a complete, minimal MelonLoader mod that registers config options and reads them every frame.

```csharp
using MelonLoader;
using DataCenterModLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(ExampleConfigMod.ExampleMod), "Example Config Mod", "1.0.0", "AuthorName")]
[assembly: MelonGame("Waseku", "Data Center")]

namespace ExampleConfigMod
{
    public class ExampleMod : MelonMod
    {
        private const string MOD_ID = "example_config_mod";

        public override void OnInitializeMelon()
        {
            // Set display info (shown in the Mod Settings panel)
            ModConfigSystem.SetModInfo(MOD_ID, "AuthorName", "1.0.0");

            // Register all config entries
            ModConfigSystem.RegisterBoolOption(
                MOD_ID, "enable_debug_overlay",
                "Debug Overlay",
                false,
                "Show FPS and debug info on screen"
            );

            ModConfigSystem.RegisterIntOption(
                MOD_ID, "max_servers",
                "Max Servers",
                10, 1, 100,
                "Maximum number of servers to auto-manage"
            );

            ModConfigSystem.RegisterFloatOption(
                MOD_ID, "time_scale",
                "Time Scale",
                1.0f, 0.1f, 5.0f,
                "Game speed multiplier"
            );

            LoggerInstance.Msg("[ExampleMod] Config registered.");
        }

        public override void OnUpdate()
        {
            // Read live values — these update immediately when the user
            // changes them in the settings panel
            bool showDebug = ModConfigSystem.GetBoolValue(MOD_ID, "enable_debug_overlay");
            int maxServers = ModConfigSystem.GetIntValue(MOD_ID, "max_servers", 10);
            float timeScale = ModConfigSystem.GetFloatValue(MOD_ID, "time_scale", 1.0f);

            // Use the values in your mod logic
            Time.timeScale = timeScale;

            if (showDebug)
            {
                // ... draw debug overlay ...
            }
        }
    }
}
```

### Key Points

1. **Register early.** Call `RegisterBoolOption` / `RegisterIntOption` / `RegisterFloatOption` in `OnInitializeMelon()` so the entries appear in the panel immediately.

2. **Read often.** The `Get*Value` methods are cheap. Call them each frame or each update cycle to pick up live changes from the UI.

3. **Use consistent `modId` values.** The `modId` is used as the config filename and as the sidebar label in the panel. Pick something descriptive and stick with it.

4. **Descriptions are optional but encouraged.** They appear as smaller gray text below each entry in the panel, helping players understand what each option does.

5. **Don't cache values across frames** if you want the panel to feel responsive. Reading from `Get*Value` each frame ensures changes are reflected immediately.

6. **Programmatic writes are fine.** Use `Set*Value` if your mod needs to reset options, apply presets, or sync settings from another source. The panel and the JSON file will update automatically.

---

## Design Notes

### Initialization Order

The mod config system initializes **before** any mods are loaded:

```
CrashLog → FFIBridge → EventDispatcher → Harmony → ModConfigSystem → LoadAllMods → MultiplayerBridge
```

This means your `OnInitializeMelon()` can safely call `RegisterBoolOption` and friends — the system is already ready.

### Thread Safety

All `ModConfigSystem` methods run on the **main Unity thread** (the same thread as `OnUpdate`, `OnGUI`, and MelonLoader lifecycle hooks). There is no thread synchronization — do not call these methods from background threads.

### Persistence Timing

- **On registration:** if a persisted JSON file exists for the `modId`, the saved value is loaded. Otherwise the `defaultValue` is used.
- **On panel change:** when the user toggles a bool or adjusts a number in the panel, the value is saved to disk immediately.
- **On `Set*Value`:** the value is saved to disk immediately.
- **On shutdown:** all configs are saved one final time as a safety measure.

### Click-Through Prevention

When the settings panel or popup is open, the game's `EventSystem` is disabled. This prevents mouse clicks from passing through the IMGUI panel into the game world. The `EventSystem` is re-enabled when the panel closes.

### Low-Level FFI API

If you need the raw uint/int/float API (designed for cross-language FFI with Rust), the following methods are also available:

| Method | Returns | Error sentinel |
|--------|---------|----------------|
| `RegisterBool(modId, key, displayName, defaultValue, description)` | `uint` | `0` on failure, `1` on success |
| `RegisterInt(modId, key, displayName, defaultValue, min, max, description)` | `uint` | `0` on failure, `1` on success |
| `RegisterFloat(modId, key, displayName, defaultValue, min, max, description)` | `uint` | `0` on failure, `1` on success |
| `GetBool(modId, key)` | `uint` | `0xFFFFFFFF` if not found |
| `GetInt(modId, key)` | `int` | `0` if not found |
| `GetFloat(modId, key)` | `float` | `0f` if not found |

For C# mods, prefer the friendly `*Option` / `*Value` wrappers documented above.
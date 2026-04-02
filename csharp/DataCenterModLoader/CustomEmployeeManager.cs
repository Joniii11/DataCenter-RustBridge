using System;
using System.Collections.Generic;
using System.Reflection;
using Il2Cpp;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Il2CppInterop.Runtime;

namespace DataCenterModLoader;

/// <summary>
/// A custom employee registered by a Rust mod, displayed in the HR System panel.
/// </summary>
public class CustomEmployeeEntry
{
    public string EmployeeId;
    public string Name;
    public string Description;
    public float SalaryPerHour;
    public float RequiredReputation;
    public bool IsHired;
}

/// <summary>
/// Generic manager for mod-registered custom employees.
/// Handles registration, state management, and Unity UI injection into HRSystem.
///
/// Known HRSystem hierarchy (from runtime dump):
///
///   Hire - OFF [HRSystem]
///     BCG [Image]
///     Text title [TextMeshProUGUI]
///     Text technician [TextMeshProUGUI, LocalisedText]
///     Grid [GridLayoutGroup]                              ← container
///       EmployeeCard [VerticalLayoutGroup, Image]         ← card template
///         Image [Image]                                   ← portrait
///         VL [VerticalLayoutGroup]
///           text_employeeName [TextMeshProUGUI]
///           text_employeeSalary [TextMeshProUGUI]
///           text_requiredReputation [TextMeshProUGUI]
///           ButtonHire [INACTIVE] [ButtonExtended, Image]
///             TextHire [TextMeshProUGUI]
///           ButtonFire [ButtonExtended, Image]
///             TextHire [TextMeshProUGUI]
///       EmployeeCard (1) …
///       EmployeeCard (2) …
///     ConfirmBuyEmployee-OFF [INACTIVE]
///     ConfirmBFireEmployee-OFF [INACTIVE]
///     Button Return [ButtonExtended]
/// </summary>
public static class CustomEmployeeManager
{
    private static readonly List<CustomEmployeeEntry> _employees = new();
    private static readonly Dictionary<string, int> _employeeIndex = new();

    /// <summary>All registered custom employees.</summary>
    public static IReadOnlyList<CustomEmployeeEntry> Employees => _employees;

    // ── Registration API (called from FFI) ──────────────────────────────────

    /// <summary>
    /// Register a custom employee. Called by Rust mods via FFI during mod_init.
    /// Returns 1 on success, 0 if the id is already taken or invalid.
    /// </summary>
    public static int Register(string id, string name, string description, float salary, float reputation)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        if (_employeeIndex.ContainsKey(id))
        {
            CrashLog.Log($"CustomEmployee: duplicate registration rejected for id={id}");
            return 0;
        }

        var entry = new CustomEmployeeEntry
        {
            EmployeeId = id,
            Name = name ?? "Unknown",
            Description = description ?? "",
            SalaryPerHour = salary,
            RequiredReputation = reputation,
            IsHired = false,
        };

        _employeeIndex[id] = _employees.Count;
        _employees.Add(entry);

        CrashLog.Log($"CustomEmployee registered: id={id}, name={name}, salary={salary}/h, requiredRep={reputation}");
        if (Core.Instance != null)
            Core.Instance.LoggerInstance.Msg($"[CustomEmployee] Registered: {name} (id={id}, salary={salary}/h, rep={reputation})");

        return 1;
    }

    /// <summary>Check if a custom employee is currently hired.</summary>
    public static bool IsHired(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        if (_employeeIndex.TryGetValue(id, out int idx))
            return _employees[idx].IsHired;
        return false;
    }

    /// <summary>
    /// Hire a custom employee. Checks reputation requirement.
    /// Returns: 1 = hired, 0 = not found, -1 = insufficient reputation, -2 = already hired
    /// </summary>
    public static int Hire(string id)
    {
        if (!_employeeIndex.TryGetValue(id, out int idx)) return 0;
        var entry = _employees[idx];

        if (entry.IsHired) return -2;

        // Check reputation requirement
        float playerRep = 0f;
        try { playerRep = PlayerManager.instance?.playerClass?.reputation ?? 0f; } catch { }

        if (playerRep < entry.RequiredReputation)
        {
            CrashLog.Log($"CustomEmployee hire rejected: {id} requires rep {entry.RequiredReputation}, player has {playerRep}");
            if (Core.Instance != null)
                Core.Instance.LoggerInstance.Warning($"[CustomEmployee] Cannot hire {entry.Name}: need reputation {entry.RequiredReputation} (you have {playerRep:F0})");
            return -1;
        }

        entry.IsHired = true;
        CrashLog.Log($"CustomEmployee hired: {id} ({entry.Name})");
        if (Core.Instance != null)
            Core.Instance.LoggerInstance.Msg($"[CustomEmployee] Hired: {entry.Name}");

        // Fire event to Rust mods
        EventDispatcher.FireCustomEmployeeHired(id);
        return 1;
    }

    /// <summary>
    /// Fire a custom employee.
    /// Returns: 1 = fired, 0 = not found or not currently hired
    /// </summary>
    public static int Fire(string id)
    {
        if (!_employeeIndex.TryGetValue(id, out int idx)) return 0;
        var entry = _employees[idx];

        if (!entry.IsHired) return 0;

        entry.IsHired = false;
        CrashLog.Log($"CustomEmployee fired: {id} ({entry.Name})");
        if (Core.Instance != null)
            Core.Instance.LoggerInstance.Msg($"[CustomEmployee] Fired: {entry.Name}");

        // Fire event to Rust mods
        EventDispatcher.FireCustomEmployeeFired(id);
        return 1;
    }

    // ── UI Injection ────────────────────────────────────────────────────────

    /// <summary>
    /// Inject custom employee cards into the HRSystem UI panel.
    /// Called by Harmony patches on HRSystem.OnEnable.
    /// </summary>
    public static void InjectIntoHRSystem(HRSystem hrSystem)
    {
        if (_employees.Count == 0) return;

        try
        {
            var hrTransform = hrSystem.gameObject.transform;

            // Log the full hierarchy for debugging (only on first call per session)
            LogHierarchy(hrTransform, 0);

            // The employee cards live inside the "Grid" child (GridLayoutGroup)
            var grid = hrTransform.Find("Grid");
            if (grid == null)
            {
                CrashLog.Log("CustomEmployee: 'Grid' container not found in HRSystem hierarchy");
                return;
            }

            CrashLog.Log($"CustomEmployee: Found Grid with {grid.childCount} children");

            // Find a template EmployeeCard to clone (use the last original card)
            Transform templateCard = null;
            for (int i = grid.childCount - 1; i >= 0; i--)
            {
                var child = grid.GetChild(i);
                if (child.gameObject.activeSelf &&
                    child.name.StartsWith("EmployeeCard") &&
                    !child.name.StartsWith("CustomEmployee_"))
                {
                    templateCard = child;
                    break;
                }
            }

            if (templateCard == null)
            {
                CrashLog.Log("CustomEmployee: No EmployeeCard template found in Grid");
                return;
            }

            CrashLog.Log($"CustomEmployee: Using template '{templateCard.name}'");

            // Inject a card for each registered custom employee
            foreach (var entry in _employees)
            {
                string cardName = "CustomEmployee_" + entry.EmployeeId;

                // Check if already injected — just update it
                var existing = grid.Find(cardName);
                if (existing != null)
                {
                    CrashLog.Log($"CustomEmployee: Updating existing card '{cardName}'");
                    UpdateCard(existing, entry);
                    continue;
                }

                try
                {
                    CreateCard(grid, templateCard, entry, cardName);
                }
                catch (Exception ex)
                {
                    CrashLog.LogException($"CreateCard({entry.EmployeeId})", ex);
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException("InjectIntoHRSystem", ex);
        }
    }

    // ── Card creation ───────────────────────────────────────────────────────

    /// <summary>Clone a template EmployeeCard and configure it for a custom employee.</summary>
    private static void CreateCard(Transform grid, Transform template, CustomEmployeeEntry entry, string cardName)
    {
        var newCardObj = UnityEngine.Object.Instantiate(template.gameObject, grid);
        newCardObj.name = cardName;
        newCardObj.SetActive(true);

        var card = newCardObj.transform;

        // ── Set text fields by their known paths ────────────────────────
        SetTextAtPath(card, "VL/text_employeeName", entry.Name);
        SetTextAtPath(card, "VL/text_employeeSalary", $"Salary: {entry.SalaryPerHour:F0} / h");
        SetTextAtPath(card, "VL/text_requiredReputation", $"Required Reputation: {entry.RequiredReputation:F0}");

        // ── Configure hire/fire buttons ─────────────────────────────────
        SetupButtons(card, entry);

        // ── Modify portrait to visually distinguish from vanilla employees ──
        SetPortraitToSolidColor(card);

        CrashLog.Log($"CustomEmployee: Card created for '{entry.Name}' (id={entry.EmployeeId}, hired={entry.IsHired})");
    }

    /// <summary>Update an already-injected card (button state, texts).</summary>
    private static void UpdateCard(Transform card, CustomEmployeeEntry entry)
    {
        SetTextAtPath(card, "VL/text_employeeName", entry.Name);
        SetTextAtPath(card, "VL/text_employeeSalary", $"Salary: {entry.SalaryPerHour:F0} / h");
        SetTextAtPath(card, "VL/text_requiredReputation", $"Required Reputation: {entry.RequiredReputation:F0}");
        SetupButtons(card, entry);
    }

    // ── Text helpers (reflection-based, works with TMP and legacy) ──────────

    /// <summary>
    /// Set the text on a component found at a relative path.
    /// Uses .NET reflection on the Il2Cpp proxy type so we don't need a
    /// compile-time reference to TextMeshPro assemblies.
    /// </summary>
    private static void SetTextAtPath(Transform root, string path, string text)
    {
        var target = root.Find(path);
        if (target == null)
        {
            CrashLog.Log($"CustomEmployee: Path '{path}' not found under '{root.name}'");
            return;
        }

        if (TrySetTextOnTransform(target, text))
        {
            CrashLog.Log($"CustomEmployee: Set '{path}' -> '{text}'");
        }
        else
        {
            CrashLog.Log($"CustomEmployee: No settable text component at '{path}'");
        }
    }

    /// <summary>
    /// Try to set the "text" property on any text-like component attached to a transform.
    /// Returns true if a text component was found and set.
    /// </summary>
    private static bool TrySetTextOnTransform(Transform t, string text)
    {
        if (t == null) return false;

        // Strategy 1: Direct TextMeshProUGUI access (most common in this game)
        try
        {
            var tmp = t.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.text = text;
                return true;
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException("TrySetText TMP direct", ex);
        }

        // Strategy 2: Legacy UnityEngine.UI.Text
        try
        {
            var legacyText = t.GetComponent<Text>();
            if (legacyText != null)
            {
                legacyText.text = text;
                return true;
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException("TrySetText legacy Text", ex);
        }

        return false;
    }

    // ── Button setup ────────────────────────────────────────────────────────

    /// <summary>
    /// Configure the ButtonHire and ButtonFire on a card.
    ///
    /// Layout (from hierarchy dump):
    ///   VL/ButtonHire  [ButtonExtended]  → shows when NOT hired
    ///     TextHire [TextMeshProUGUI]     → label "Hire"
    ///   VL/ButtonFire  [ButtonExtended]  → shows when hired
    ///     TextHire [TextMeshProUGUI]     → label "Fire"
    ///
    /// ButtonExtended likely inherits from UnityEngine.UI.Button, so we can
    /// access onClick through the Button base component. If that fails, we
    /// fall back to searching by Il2Cpp type name.
    /// </summary>
    // Keep references to delegates so GC doesn't collect them while Il2Cpp still points to them
    private static readonly List<UnityAction> _liveCallbacks = new();

    private static void SetupButtons(Transform card, CustomEmployeeEntry entry)
    {
        var buttonHireT = card.Find("VL/ButtonHire");
        var buttonFireT = card.Find("VL/ButtonFire");

        if (buttonHireT == null || buttonFireT == null)
        {
            CrashLog.Log($"CustomEmployee: ButtonHire or ButtonFire not found (hire={buttonHireT != null}, fire={buttonFireT != null})");
            return;
        }

        string employeeId = entry.EmployeeId;

        // Toggle visibility based on hire state
        buttonHireT.gameObject.SetActive(!entry.IsHired);
        buttonFireT.gameObject.SetActive(entry.IsHired);

        // Set button labels
        TrySetTextOnTransform(buttonHireT.Find("TextHire"), "Hire");
        TrySetTextOnTransform(buttonFireT.Find("TextHire"), "Fire");

        // Wire up click handlers via ButtonExtended (NOT Button — ButtonExtended : Selectable)
        WireButtonExtendedClick(buttonHireT, () =>
        {
            CrashLog.Log($"CustomEmployee: Hire button clicked for '{employeeId}'");
            int result = Hire(employeeId);
            if (result == 1)
                RefreshAllCards();
            else if (result == -1)
                CrashLog.Log($"CustomEmployee: Hire rejected — insufficient reputation");
            else if (result == -2)
                CrashLog.Log($"CustomEmployee: Already hired");
        });

        WireButtonExtendedClick(buttonFireT, () =>
        {
            CrashLog.Log($"CustomEmployee: Fire button clicked for '{employeeId}'");
            int result = Fire(employeeId);
            if (result == 1)
                RefreshAllCards();
        });

        CrashLog.Log($"CustomEmployee: Buttons configured for '{entry.EmployeeId}' (hired={entry.IsHired})");
    }

    /// <summary>
    /// Wire an onClick listener to a ButtonExtended component.
    /// ButtonExtended inherits from Selectable (NOT Button!), so we must
    /// access it directly via GetComponent&lt;ButtonExtended&gt;().
    /// Its onClick property returns a ButtonExtended.ButtonClickedEvent (: UnityEvent).
    /// </summary>
    private static void WireButtonExtendedClick(Transform buttonTransform, System.Action callback)
    {
        if (buttonTransform == null) return;

        // Strategy 1: Direct ButtonExtended access (this is what the game uses)
        try
        {
            var btnExt = buttonTransform.GetComponent<ButtonExtended>();
            if (btnExt != null)
            {
                var clickEvent = btnExt.onClick;
                if (clickEvent != null)
                {
                    clickEvent.RemoveAllListeners();
                    UnityAction action = callback;
                    _liveCallbacks.Add(action); // prevent GC
                    clickEvent.AddListener(action);
                    CrashLog.Log($"CustomEmployee: Wired ButtonExtended.onClick on '{buttonTransform.name}'");
                    return;
                }
                else
                {
                    CrashLog.Log($"CustomEmployee: ButtonExtended.onClick was null on '{buttonTransform.name}'");
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException($"WireButtonExtendedClick on '{buttonTransform.name}'", ex);
        }

        // Strategy 2: Fallback to UnityEngine.UI.Button (unlikely but safe)
        try
        {
            var button = buttonTransform.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                UnityAction action = callback;
                _liveCallbacks.Add(action);
                button.onClick.AddListener(action);
                CrashLog.Log($"CustomEmployee: Wired Button.onClick fallback on '{buttonTransform.name}'");
                return;
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException($"WireButtonClick Button fallback on '{buttonTransform.name}'", ex);
        }

        CrashLog.Log($"CustomEmployee: Could not wire button on '{buttonTransform.name}' — no ButtonExtended or Button found");
    }

    // ── Portrait ────────────────────────────────────────────────────────────

    /// <summary>
    /// Replace the portrait image with a solid teal colour to visually
    /// distinguish custom/mod employees from vanilla ones.
    /// The portrait is the "Image" child directly under the EmployeeCard.
    /// </summary>
    private static void SetPortraitToSolidColor(Transform card)
    {
        try
        {
            var portraitTransform = card.Find("Image");
            if (portraitTransform == null)
            {
                CrashLog.Log("CustomEmployee: 'Image' (portrait) not found on card");
                return;
            }

            var image = portraitTransform.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = null;
                image.color = new Color(0.0f, 0.6f, 0.7f, 1f); // teal
                CrashLog.Log("CustomEmployee: Portrait set to teal");
            }

            // Also try RawImage (some UIs use it for portraits)
            var rawImageComp = portraitTransform.GetComponent<RawImage>();
            if (rawImageComp != null)
            {
                rawImageComp.texture = null;
                rawImageComp.color = new Color(0.0f, 0.6f, 0.7f, 1f);
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException("SetPortraitToSolidColor", ex);
        }
    }

    // ── Card refresh ────────────────────────────────────────────────────────

    /// <summary>Refresh all custom employee cards in any active HRSystem.</summary>
    private static void RefreshAllCards()
    {
        try
        {
            var hrSystems = UnityEngine.Object.FindObjectsOfType<HRSystem>();
            if (hrSystems == null) return;

            for (int h = 0; h < hrSystems.Count; h++)
            {
                var hr = hrSystems[h];
                if (hr == null) continue;

                var grid = hr.transform.Find("Grid");
                if (grid == null) continue;

                foreach (var entry in _employees)
                {
                    string cardName = "CustomEmployee_" + entry.EmployeeId;
                    var cardTransform = grid.Find(cardName);
                    if (cardTransform != null)
                    {
                        UpdateCard(cardTransform, entry);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException("RefreshAllCards", ex);
        }
    }

    // ── Debug helper ────────────────────────────────────────────────────────

    private static bool _hierarchyLogged = false;

    /// <summary>Log the full Unity transform hierarchy for debugging (once per session).</summary>
    private static void LogHierarchy(Transform t, int depth)
    {
        if (_hierarchyLogged) return;
        if (depth == 0)
        {
            CrashLog.Log("=== HRSystem hierarchy dump ===");
            _hierarchyLogged = true; // only dump once
        }

        try
        {
            string indent = new string(' ', depth * 2);
            string activeFlag = t.gameObject.activeSelf ? "" : " [INACTIVE]";

            var components = t.gameObject.GetComponents<Component>();
            var compNames = new List<string>();
            if (components != null)
            {
                for (int i = 0; i < components.Count; i++)
                {
                    try
                    {
                        var comp = components[i];
                        if (comp != null)
                            compNames.Add(comp.GetIl2CppType().Name);
                    }
                    catch { }
                }
            }

            string compsStr = compNames.Count > 0 ? " [" + string.Join(", ", compNames) + "]" : "";
            CrashLog.Log($"{indent}{t.name}{activeFlag}{compsStr}");

            for (int i = 0; i < t.childCount; i++)
            {
                try { LogHierarchy(t.GetChild(i), depth + 1); }
                catch { }
            }
        }
        catch { }

        if (depth == 0)
            CrashLog.Log("=== end hierarchy dump ===");
    }
}

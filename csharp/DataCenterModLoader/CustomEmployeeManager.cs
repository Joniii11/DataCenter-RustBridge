using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DataCenterModLoader;

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
/// Manages mod-registered custom employees: registration, state, and HR System UI injection.
/// </summary>
public static class CustomEmployeeManager
{
    private static readonly List<CustomEmployeeEntry> _employees = new();
    private static readonly Dictionary<string, int> _employeeIndex = new();
    private static readonly List<UnityAction> _liveCallbacks = new(); // prevent GC of Il2Cpp delegates

    public static IReadOnlyList<CustomEmployeeEntry> Employees => _employees;

    // Registration API (called from FFI)

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
        Core.Instance?.LoggerInstance.Msg($"[CustomEmployee] Registered: {name} (id={id}, salary={salary}/h, rep={reputation})");

        return 1;
    }

    public static bool IsHired(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        if (_employeeIndex.TryGetValue(id, out int idx))
            return _employees[idx].IsHired;
        return false;
    }

    /// <summary>Returns: 1 = hired, 0 = not found, -1 = insufficient reputation, -2 = already hired</summary>
    public static int Hire(string id)
    {
        if (!_employeeIndex.TryGetValue(id, out int idx)) return 0;
        var entry = _employees[idx];

        if (entry.IsHired) return -2;

        float playerRep = 0f;
        try { playerRep = PlayerManager.instance?.playerClass?.reputation ?? 0f; } catch { }

        if (playerRep < entry.RequiredReputation)
        {
            CrashLog.Log($"CustomEmployee hire rejected: {id} requires rep {entry.RequiredReputation}, player has {playerRep}");
            Core.Instance?.LoggerInstance.Warning($"[CustomEmployee] Cannot hire {entry.Name}: need reputation {entry.RequiredReputation} (you have {playerRep:F0})");
            return -1;
        }

        entry.IsHired = true;
        CrashLog.Log($"CustomEmployee hired: {id} ({entry.Name})");
        Core.Instance?.LoggerInstance.Msg($"[CustomEmployee] Hired: {entry.Name}");

        EventDispatcher.FireCustomEmployeeHired(id);
        return 1;
    }

    /// <summary>Returns: 1 = fired, 0 = not found or not currently hired</summary>
    public static int Fire(string id)
    {
        if (!_employeeIndex.TryGetValue(id, out int idx)) return 0;
        var entry = _employees[idx];

        if (!entry.IsHired) return 0;

        entry.IsHired = false;
        CrashLog.Log($"CustomEmployee fired: {id} ({entry.Name})");
        Core.Instance?.LoggerInstance.Msg($"[CustomEmployee] Fired: {entry.Name}");

        EventDispatcher.FireCustomEmployeeFired(id);
        return 1;
    }

    // UI Injection

    public static void InjectIntoHRSystem(HRSystem hrSystem)
    {
        if (_employees.Count == 0) return;

        try
        {
            var hrTransform = hrSystem.gameObject.transform;
            LogHierarchy(hrTransform, 0);

            var grid = hrTransform.Find("Grid");
            if (grid == null)
            {
                CrashLog.Log("CustomEmployee: 'Grid' not found in HRSystem");
                return;
            }

            CrashLog.Log($"CustomEmployee: Found Grid with {grid.childCount} children");

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

            foreach (var entry in _employees)
            {
                string cardName = "CustomEmployee_" + entry.EmployeeId;

                var existing = grid.Find(cardName);
                if (existing != null)
                {
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

    // Card creation

    private static void CreateCard(Transform grid, Transform template, CustomEmployeeEntry entry, string cardName)
    {
        var newCardObj = UnityEngine.Object.Instantiate(template.gameObject, grid);
        newCardObj.name = cardName;
        newCardObj.SetActive(true);

        var card = newCardObj.transform;

        SetTextAtPath(card, "VL/text_employeeName", entry.Name);
        SetTextAtPath(card, "VL/text_employeeSalary", $"Salary: {entry.SalaryPerHour:F0} / h");
        SetTextAtPath(card, "VL/text_requiredReputation", $"Required Reputation: {entry.RequiredReputation:F0}");

        SetupButtons(card, entry);
        SetPortraitToSolidColor(card);

        CrashLog.Log($"CustomEmployee: Card created for '{entry.Name}' (id={entry.EmployeeId}, hired={entry.IsHired})");
    }

    private static void UpdateCard(Transform card, CustomEmployeeEntry entry)
    {
        SetTextAtPath(card, "VL/text_employeeName", entry.Name);
        SetTextAtPath(card, "VL/text_employeeSalary", $"Salary: {entry.SalaryPerHour:F0} / h");
        SetTextAtPath(card, "VL/text_requiredReputation", $"Required Reputation: {entry.RequiredReputation:F0}");
        SetupButtons(card, entry);
    }

    // Text helpers

    private static void SetTextAtPath(Transform root, string path, string text)
    {
        var target = root.Find(path);
        if (target == null)
        {
            CrashLog.Log($"CustomEmployee: Path '{path}' not found under '{root.name}'");
            return;
        }

        if (!TrySetTextOnTransform(target, text))
            CrashLog.Log($"CustomEmployee: No text component at '{path}'");
    }

    private static bool TrySetTextOnTransform(Transform t, string text)
    {
        if (t == null) return false;

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
            CrashLog.LogException("TrySetText TMP", ex);
        }

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
            CrashLog.LogException("TrySetText legacy", ex);
        }

        return false;
    }

    // Button setup

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

        buttonHireT.gameObject.SetActive(!entry.IsHired);
        buttonFireT.gameObject.SetActive(entry.IsHired);

        TrySetTextOnTransform(buttonHireT.Find("TextHire"), "Hire");
        TrySetTextOnTransform(buttonFireT.Find("TextHire"), "Fire");

        WireButtonExtendedClick(buttonHireT, () =>
        {
            CrashLog.Log($"CustomEmployee: Hire clicked for '{employeeId}'");
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
            CrashLog.Log($"CustomEmployee: Fire clicked for '{employeeId}'");
            if (Fire(employeeId) == 1)
                RefreshAllCards();
        });

        CrashLog.Log($"CustomEmployee: Buttons configured for '{entry.EmployeeId}' (hired={entry.IsHired})");
    }

    /// <summary>ButtonExtended : Selectable (NOT Button!), has its own onClick : ButtonClickedEvent.</summary>
    private static void WireButtonExtendedClick(Transform buttonTransform, System.Action callback)
    {
        if (buttonTransform == null) return;

        try
        {
            var btnExt = buttonTransform.GetComponent<ButtonExtended>();
            if (btnExt != null)
            {
                // Replace entire event to nuke persistent listeners from cloned template
                var freshEvent = new ButtonExtended.ButtonClickedEvent();
                btnExt.m_OnClick = freshEvent;
                UnityAction action = callback;
                _liveCallbacks.Add(action);
                freshEvent.AddListener(action);
                CrashLog.Log($"CustomEmployee: Wired ButtonExtended.onClick on '{buttonTransform.name}'");
                return;
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException($"WireButtonExtendedClick on '{buttonTransform.name}'", ex);
        }

        // fallback
        try
        {
            var button = buttonTransform.GetComponent<Button>();
            if (button != null)
            {
                button.onClick = new Button.ButtonClickedEvent();
                UnityAction action = callback;
                _liveCallbacks.Add(action);
                button.onClick.AddListener(action);
                CrashLog.Log($"CustomEmployee: Wired Button.onClick fallback on '{buttonTransform.name}'");
                return;
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException($"WireButtonClick fallback on '{buttonTransform.name}'", ex);
        }

        CrashLog.Log($"CustomEmployee: No ButtonExtended or Button on '{buttonTransform.name}'");
    }

    // Portrait

    private static void SetPortraitToSolidColor(Transform card)
    {
        try
        {
            var portraitTransform = card.Find("Image");
            if (portraitTransform == null) return;

            var image = portraitTransform.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = null;
                image.color = new Color(0.0f, 0.6f, 0.7f, 1f);
            }

            var rawImage = portraitTransform.GetComponent<RawImage>();
            if (rawImage != null)
            {
                rawImage.texture = null;
                rawImage.color = new Color(0.0f, 0.6f, 0.7f, 1f);
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException("SetPortraitToSolidColor", ex);
        }
    }

    // Card refresh

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
                    var cardTransform = grid.Find("CustomEmployee_" + entry.EmployeeId);
                    if (cardTransform != null)
                        UpdateCard(cardTransform, entry);
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException("RefreshAllCards", ex);
        }
    }

    // debug

    private static bool _hierarchyLogged = false;

    private static void LogHierarchy(Transform t, int depth)
    {
        if (_hierarchyLogged) return;
        if (depth == 0)
        {
            CrashLog.Log("=== HRSystem hierarchy dump ===");
            _hierarchyLogged = true;
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

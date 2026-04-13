using System;
using System.Collections.Generic;
using System.IO;
using Il2Cpp;
using MelonLoader.Utils;
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
    public bool RequiresConfirmation;
}

public static class CustomEmployeeManager
{
    private static readonly List<CustomEmployeeEntry> _employees = new();
    private static readonly Dictionary<string, int> _employeeIndex = new();
    private static readonly List<UnityAction> _liveCallbacks = new();

    private static readonly string _statePath =
        Path.Combine(MelonEnvironment.UserDataDirectory, "custom_employees_hired.txt");

    private static string _pendingEmployeeId;
    private static bool _pendingIsHire;
    private static bool _salariesNeedReregistration;

    public static IReadOnlyList<CustomEmployeeEntry> Employees => _employees;
    public static bool HasPendingAction => _pendingEmployeeId != null;

    public static int Register(string id, string name, string description, float salary, float reputation, bool requiresConfirmation = false)
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
            RequiresConfirmation = requiresConfirmation,
        };

        _employeeIndex[id] = _employees.Count;
        _employees.Add(entry);

        CrashLog.Log($"CustomEmployee registered: id={id}, name={name}, salary={salary}/h, requiredRep={reputation}");
        Core.Instance?.LoggerInstance.Msg($"[CustomEmployee] Registered: {name} (id={id}, salary={salary}/h, rep={reputation})");

        LoadState();
        return 1;
    }

    public static bool IsHired(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        if (_employeeIndex.TryGetValue(id, out int idx))
            return _employees[idx].IsHired;
        return false;
    }

    // Returns: 1 = hired, 0 = not found, -1 = insufficient reputation, -2 = already hired
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

        try { BalanceSheet.instance?.RegisterSalary((int)entry.SalaryPerHour); } catch { }

        EventDispatcher.FireCustomEmployeeHired(id);
        SaveState();
        return 1;
    }

    // Returns: 1 = fired, 0 = not found or not currently hired
    public static int Fire(string id)
    {
        if (!_employeeIndex.TryGetValue(id, out int idx)) return 0;
        var entry = _employees[idx];

        if (!entry.IsHired) return 0;

        entry.IsHired = false;
        CrashLog.Log($"CustomEmployee.Fire: step 1 - set IsHired=false for '{id}' ({entry.Name})");

        try
        {
            Core.Instance?.LoggerInstance.Msg($"[CustomEmployee] Fired: {entry.Name}");
        }
        catch (Exception ex) { CrashLog.LogException("Fire: LoggerInstance.Msg", ex); }

        CrashLog.Log($"CustomEmployee.Fire: step 2 - about to unregister salary ({-(int)entry.SalaryPerHour})");
        try
        {
            var bs = BalanceSheet.instance;
            if (bs != null)
            {
                bs.RegisterSalary(-(int)entry.SalaryPerHour);
                CrashLog.Log("CustomEmployee.Fire: step 2 - salary unregistered OK");
            }
            else
            {
                CrashLog.Log("CustomEmployee.Fire: step 2 - BalanceSheet.instance is null, skipping salary");
            }
        }
        catch (Exception ex) { CrashLog.LogException("Fire: RegisterSalary", ex); }

        CrashLog.Log($"CustomEmployee.Fire: step 3 - dispatching CustomEmployeeFired event for '{id}'");
        try
        {
            EventDispatcher.FireCustomEmployeeFired(id);
            CrashLog.Log("CustomEmployee.Fire: step 3 - event dispatched OK");
        }
        catch (Exception ex) { CrashLog.LogException("Fire: FireCustomEmployeeFired", ex); }

        CrashLog.Log("CustomEmployee.Fire: step 4 - saving state");
        SaveState();
        CrashLog.Log("CustomEmployee.Fire: step 5 - complete");
        return 1;
    }

#pragma warning disable CS0414
    private static bool _scrollViewInjected = false;
    private static Transform _injectedContent = null;
#pragma warning restore CS0414

    private static Transform EnsureScrollView(Transform hrTransform, Transform grid)
    {
        var existingScroll = hrTransform.Find("ModScrollView");
        if (existingScroll != null)
        {
            var existingContent = existingScroll.Find("Viewport/Content");
            if (existingContent != null)
            {
                CrashLog.Log("CustomEmployee: ScrollView already exists, reusing");
                return existingContent;
            }
        }

        CrashLog.Log("CustomEmployee: Creating ScrollView wrapper for Grid");

        var gridRect = grid.GetComponent<RectTransform>();
        var gridParent = grid.parent;

        var anchorMin = gridRect.anchorMin;
        var anchorMax = gridRect.anchorMax;
        var offsetMin = gridRect.offsetMin;
        var offsetMax = gridRect.offsetMax;
        var pivot = gridRect.pivot;
        var sizeDelta = gridRect.sizeDelta;
        var anchoredPos = gridRect.anchoredPosition;
        int siblingIndex = grid.GetSiblingIndex();

        var scrollGO = new GameObject("ModScrollView");
        scrollGO.AddComponent<RectTransform>();
        scrollGO.transform.SetParent(gridParent, false);
        scrollGO.transform.SetSiblingIndex(siblingIndex);

        var scrollRect_rt = scrollGO.GetComponent<RectTransform>();
        scrollRect_rt.anchorMin = anchorMin;
        scrollRect_rt.anchorMax = anchorMax;
        scrollRect_rt.offsetMin = offsetMin;
        scrollRect_rt.offsetMax = offsetMax;
        scrollRect_rt.pivot = pivot;
        scrollRect_rt.sizeDelta = sizeDelta;
        scrollRect_rt.anchoredPosition = anchoredPos;

        var viewportGO = new GameObject("Viewport");
        viewportGO.AddComponent<RectTransform>();
        viewportGO.AddComponent<RectMask2D>();
        viewportGO.AddComponent<Image>();
        viewportGO.transform.SetParent(scrollGO.transform, false);

        var viewportRect = viewportGO.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        viewportRect.pivot = new Vector2(0.5f, 1f);

        // Transparent image needed for RectMask2D / raycasting
        var viewportImage = viewportGO.GetComponent<Image>();
        viewportImage.color = new Color(0, 0, 0, 0);
        viewportImage.raycastTarget = true;

        var contentGO = new GameObject("Content");
        contentGO.AddComponent<RectTransform>();
        contentGO.AddComponent<ContentSizeFitter>();
        contentGO.transform.SetParent(viewportGO.transform, false);

        var contentRect = contentGO.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = new Vector2(0, 0);
        contentRect.offsetMax = new Vector2(0, 0);
        contentRect.sizeDelta = new Vector2(0, 0);

        var fitter = contentGO.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var srcLayout = grid.GetComponent<GridLayoutGroup>();
        if (srcLayout != null)
        {
            var dstLayout = contentGO.AddComponent<GridLayoutGroup>();
            dstLayout.cellSize = srcLayout.cellSize;
            dstLayout.spacing = srcLayout.spacing;
            dstLayout.startCorner = srcLayout.startCorner;
            dstLayout.startAxis = srcLayout.startAxis;
            dstLayout.childAlignment = srcLayout.childAlignment;
            dstLayout.constraint = srcLayout.constraint;
            dstLayout.constraintCount = srcLayout.constraintCount;
            dstLayout.padding = srcLayout.padding;
            CrashLog.Log($"CustomEmployee: Copied GridLayoutGroup (cellSize={dstLayout.cellSize}, spacing={dstLayout.spacing}, constraint={dstLayout.constraint}, count={dstLayout.constraintCount})");
        }
        else
        {
            CrashLog.Log("CustomEmployee: Grid has no GridLayoutGroup, checking for other layouts...");
            var hLayout = grid.GetComponent<HorizontalLayoutGroup>();
            if (hLayout != null)
            {
                var dst = contentGO.AddComponent<HorizontalLayoutGroup>();
                dst.spacing = hLayout.spacing;
                dst.childAlignment = hLayout.childAlignment;
                dst.padding = hLayout.padding;
            }
            var vLayout = grid.GetComponent<VerticalLayoutGroup>();
            if (vLayout != null)
            {
                var dst = contentGO.AddComponent<VerticalLayoutGroup>();
                dst.spacing = vLayout.spacing;
                dst.childAlignment = vLayout.childAlignment;
                dst.padding = vLayout.padding;
            }
        }

        var childrenToMove = new System.Collections.Generic.List<Transform>();
        for (int i = 0; i < grid.childCount; i++)
            childrenToMove.Add(grid.GetChild(i));

        foreach (var child in childrenToMove)
            child.SetParent(contentGO.transform, false);

        CrashLog.Log($"CustomEmployee: Moved {childrenToMove.Count} children from Grid to Content");

        var scrollComp = scrollGO.AddComponent<ScrollRect>();
        scrollComp.content = contentRect;
        scrollComp.viewport = viewportRect;
        scrollComp.horizontal = false;
        scrollComp.vertical = true;
        scrollComp.movementType = ScrollRect.MovementType.Clamped;
        scrollComp.scrollSensitivity = 30f;
        scrollComp.inertia = true;
        scrollComp.decelerationRate = 0.1f;

        grid.gameObject.SetActive(false);

        _injectedContent = contentGO.transform;
        _scrollViewInjected = true;
        CrashLog.Log("CustomEmployee: ScrollView injection complete");

        return contentGO.transform;
    }

    public static void InjectIntoHRSystem(HRSystem hrSystem)
    {
        if (_employees.Count == 0) return;

        try
        {
            var hrTransform = hrSystem.gameObject.transform;
            LogHierarchy(hrTransform, 0);


            Transform contentGrid;
            if (_injectedContent != null)
            {
                contentGrid = _injectedContent;
                CrashLog.Log($"CustomEmployee: Reusing cached content '{contentGrid.name}' ({contentGrid.childCount} children)");
            }
            else
            {
                Transform grid = hrTransform.Find("Grid");
                if (grid != null)
                    CrashLog.Log("CustomEmployee: Found container via legacy 'Grid' child");

                if (grid == null)
                {
                    var hireButtons = hrSystem.buttonsHireEmployees;
                    var fireButtons = hrSystem.buttonsFireEmployees;

                    Transform btn0 = hireButtons?.Length > 0 ? hireButtons[0]?.transform : null;
                    Transform btn1 = hireButtons?.Length > 1 ? hireButtons[1]?.transform
                                   : fireButtons?.Length > 0 ? fireButtons[0]?.transform : null;

                    if (btn0 != null && btn1 != null)
                    {
                        grid = FindLowestCommonAncestor(btn0, btn1);
                        if (grid != null)
                            CrashLog.Log($"CustomEmployee: Found container via button LCA: '{grid.name}'");
                    }
                    else if (btn0 != null)
                    {
                        grid = btn0.parent?.parent?.parent ?? btn0.parent?.parent;
                        if (grid != null)
                            CrashLog.Log($"CustomEmployee: Found container via button walk-up: '{grid.name}'");
                    }
                }

                if (grid == null)
                {
                    int maxChildren = 0;
                    for (int i = 0; i < hrTransform.childCount; i++)
                    {
                        var c = hrTransform.GetChild(i);
                        if (c.childCount > maxChildren) { maxChildren = c.childCount; grid = c; }
                    }
                    if (grid != null && maxChildren > 0)
                        CrashLog.Log($"CustomEmployee: Found container by scan: '{grid.name}' ({maxChildren} children)");
                }

                if (grid == null)
                {
                    CrashLog.Log("CustomEmployee: Could not find employee card container in HRSystem");
                    return;
                }

                CrashLog.Log($"CustomEmployee: Found container '{grid.name}' with {grid.childCount} children");
                contentGrid = EnsureScrollView(hrTransform, grid);
            }

            CrashLog.Log($"CustomEmployee: Using content grid '{contentGrid.name}' with {contentGrid.childCount} children");

            // Prefer cards that start with "EmployeeCard" but fall back to any active
            // non-custom child, since the new game version may use different card names
            Transform templateCard = null;
            for (int i = contentGrid.childCount - 1; i >= 0; i--)
            {
                var child = contentGrid.GetChild(i);
                if (child.gameObject.activeSelf && !child.name.StartsWith("CustomEmployee_"))
                {
                    if (child.name.StartsWith("EmployeeCard"))
                    {
                        templateCard = child;
                        break;  // best match — stop immediately
                    }
                    if (templateCard == null)
                        templateCard = child;  // fallback: first active non-custom card
                }
            }

            if (templateCard == null)
            {
                CrashLog.Log("CustomEmployee: No EmployeeCard template found in content grid");
                return;
            }

            CrashLog.Log($"CustomEmployee: Using template '{templateCard.name}'");

            foreach (var entry in _employees)
            {
                string cardName = "CustomEmployee_" + entry.EmployeeId;

                var existing = contentGrid.Find(cardName);
                if (existing != null)
                {
                    UpdateCard(existing, entry);
                    continue;
                }

                try
                {
                    CreateCard(contentGrid, templateCard, entry, cardName);
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

    public static void ResetInjectionState()
    {
        _injectedContent = null;
        _scrollViewInjected = false;
        _hierarchyLogged = false;
        CrashLog.Log("CustomEmployee: injection state reset");
    }

    private static Transform FindLowestCommonAncestor(Transform a, Transform b)
    {
        if (a == null || b == null) return null;
        var ancestors = new HashSet<Transform>();
        for (var t = a.parent; t != null; t = t.parent)
            ancestors.Add(t);
        for (var t = b.parent; t != null; t = t.parent)
            if (ancestors.Contains(t)) return t;
        return null;
    }

    private static void CreateCard(Transform grid, Transform template, CustomEmployeeEntry entry, string cardName)
    {
        var newCardObj = UnityEngine.Object.Instantiate(template.gameObject, grid);
        newCardObj.name = cardName;
        newCardObj.SetActive(true);

        var card = newCardObj.transform;

        AssignCardTexts(card, entry);
        SetupButtons(card, entry);
        SetPortrait(card, entry.EmployeeId);

        CrashLog.Log($"CustomEmployee: Card created for '{entry.Name}' (id={entry.EmployeeId}, hired={entry.IsHired})");
    }

    private static void UpdateCard(Transform card, CustomEmployeeEntry entry)
    {
        AssignCardTexts(card, entry);
        SetupButtons(card, entry);
    }

    /// <summary>
    /// Sets the three visible text fields on an employee card.
    ///
    /// Strategy (most-to-least specific):
    ///   1. Try the legacy hard-coded paths from the old UI ("VL/text_employeeName", etc.)
    ///   2. If a path is missing, scan ALL TextMeshProUGUI / Text children of the card and
    ///      match by name-hint (case-insensitive contains: "name", "salary"/"pay"/"cost", "rep").
    ///   3. If name-hints fail, fall back to positional assignment: first visible text = name,
    ///      second = salary, third = reputation.
    /// This makes the card injection survive UI restructuring in game updates.
    /// </summary>
    private static void AssignCardTexts(Transform card, CustomEmployeeEntry entry)
    {
        string wantName = entry.Name;
        string wantSalary = $"Salary: {entry.SalaryPerHour:F0} / h";
        string wantRep = $"Required Reputation: {entry.RequiredReputation:F0}";

        // --- attempt 1: legacy exact paths ---
        bool nameSet = TrySetByPath(card, "VL/text_employeeName", wantName);
        bool salarySet = TrySetByPath(card, "VL/text_employeeSalary", wantSalary);
        bool repSet = TrySetByPath(card, "VL/text_requiredReputation", wantRep);

        if (nameSet && salarySet && repSet) return;

        // --- attempt 2: scan children by name hint ---
        try
        {
            // Collect all text transforms in depth-first order, skipping button sub-trees
            var textTransforms = new System.Collections.Generic.List<Transform>();
            CollectTextTransforms(card, textTransforms);

            CrashLog.Log($"AssignCardTexts: found {textTransforms.Count} text component(s) in card '{card.name}'");

            foreach (var t in textTransforms)
            {
                string n = t.name.ToLowerInvariant();

                if (!nameSet && (n.Contains("name") || n.Contains("employee")))
                {
                    nameSet = TrySetTextOnTransform(t, wantName);
                    continue;
                }
                if (!salarySet && (n.Contains("salary") || n.Contains("pay") || n.Contains("cost") || n.Contains("wage")))
                {
                    salarySet = TrySetTextOnTransform(t, wantSalary);
                    continue;
                }
                if (!repSet && (n.Contains("rep") || n.Contains("reputation") || n.Contains("prestige")))
                {
                    repSet = TrySetTextOnTransform(t, wantRep);
                    continue;
                }
            }

            // --- attempt 3: positional fallback for anything still unset ---
            if (!nameSet || !salarySet || !repSet)
            {
                int pos = 0;
                foreach (var t in textTransforms)
                {
                    if (pos == 0 && !nameSet) { TrySetTextOnTransform(t, wantName); nameSet = true; }
                    else if (pos == 1 && !salarySet) { TrySetTextOnTransform(t, wantSalary); salarySet = true; }
                    else if (pos == 2 && !repSet) { TrySetTextOnTransform(t, wantRep); repSet = true; }
                    pos++;
                    if (nameSet && salarySet && repSet) break;
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException("AssignCardTexts fallback scan", ex);
        }

        CrashLog.Log($"AssignCardTexts: name={nameSet}, salary={salarySet}, rep={repSet} for '{entry.EmployeeId}'");
    }

    private static bool TrySetByPath(Transform card, string path, string text)
    {
        var t = card.Find(path);
        if (t == null) return false;
        return TrySetTextOnTransform(t, text);
    }

    /// Collect all transforms that have a TextMeshProUGUI or legacy Text component,
    /// skipping sub-trees that are buttons (to avoid overwriting button labels).
    private static void CollectTextTransforms(Transform parent, System.Collections.Generic.List<Transform> result)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);

            // Skip button sub-trees
            if (child.GetComponent<UnityEngine.UI.Button>() != null) continue;
            try { if (child.GetComponent<ButtonExtended>() != null) continue; } catch { }

            bool hasText = false;
            try { if (child.GetComponent<TextMeshProUGUI>() != null) hasText = true; } catch { }
            if (!hasText)
            {
                try { if (child.GetComponent<UnityEngine.UI.Text>() != null) hasText = true; } catch { }
            }

            if (hasText) result.Add(child);

            CollectTextTransforms(child, result);
        }
    }

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

    /// <summary>
    /// Finds a button transform inside <paramref name="card"/> using a prioritised search:
    /// 1. Exact path (e.g. "VL/ButtonHire")
    /// 2. Any direct or deep child whose name contains <paramref name="nameHint"/>
    /// 3. Returns null if nothing matched.
    /// </summary>
    private static Transform FindButton(Transform card, string exactPath, string nameHint)
    {
        // 1. exact path
        var t = card.Find(exactPath);
        if (t != null) return t;

        // 2. deep-child name search (case-insensitive contains)
        return FindChildContaining(card, nameHint);
    }

    private static Transform FindChildContaining(Transform parent, string nameHint)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name.IndexOf(nameHint, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return child;
            var deep = FindChildContaining(child, nameHint);
            if (deep != null) return deep;
        }
        return null;
    }

    private static void SetupButtons(Transform card, CustomEmployeeEntry entry)
    {
        // Try legacy exact paths first, then fall back to name-hint search,
        // then fall back to ButtonExtended components in card order.
        Transform buttonHireT = FindButton(card, "VL/ButtonHire", "Hire");
        Transform buttonFireT = FindButton(card, "VL/ButtonFire", "Fire");

        if (buttonHireT == null || buttonFireT == null)
        {
            // Last resort: grab the first two ButtonExtended components in the card.
            // Index 0 → Hire, Index 1 → Fire (matches the game's own ordering).
            try
            {
                var allBtns = card.GetComponentsInChildren<ButtonExtended>(includeInactive: true);
                if (allBtns != null)
                {
                    if (buttonHireT == null && allBtns.Length >= 1)
                        buttonHireT = allBtns[0].transform;
                    if (buttonFireT == null && allBtns.Length >= 2)
                        buttonFireT = allBtns[1].transform;
                }
            }
            catch (Exception ex)
            {
                CrashLog.LogException("SetupButtons: ButtonExtended fallback", ex);
            }
        }

        if (buttonHireT == null || buttonFireT == null)
        {
            CrashLog.Log($"CustomEmployee: SetupButtons — could not find hire/fire buttons for '{entry.EmployeeId}' (hire={buttonHireT != null}, fire={buttonFireT != null})");
            return;
        }

        CrashLog.Log($"CustomEmployee: SetupButtons — using hire='{buttonHireT.name}', fire='{buttonFireT.name}' for '{entry.EmployeeId}'");

        string employeeId = entry.EmployeeId;

        buttonHireT.gameObject.SetActive(!entry.IsHired);
        buttonFireT.gameObject.SetActive(entry.IsHired);

        // Try the known text child name first; if absent scan for any text component.
        bool hireTextSet = TrySetTextOnTransform(buttonHireT.Find("TextHire"), "Hire");
        if (!hireTextSet) TrySetTextOnTransform(buttonHireT, "Hire");

        bool fireTextSet = TrySetTextOnTransform(buttonFireT.Find("TextHire"), "Fire");
        if (!fireTextSet) TrySetTextOnTransform(buttonFireT, "Fire");

        WireButtonExtendedClick(buttonHireT, () =>
        {
            CrashLog.Log($"CustomEmployee: Hire clicked for '{employeeId}'");
            if (entry.RequiresConfirmation)
            {
                _pendingEmployeeId = employeeId;
                _pendingIsHire = true;
                ShowOverlay(hire: true);
            }
            else
            {
                if (Hire(employeeId) == 1) RefreshAllCards();
            }
        });

        WireButtonExtendedClick(buttonFireT, () =>
        {
            CrashLog.Log($"CustomEmployee: Fire clicked for '{employeeId}'");
            if (entry.RequiresConfirmation)
            {
                _pendingEmployeeId = employeeId;
                _pendingIsHire = false;
                ShowOverlay(hire: false);
            }
            else
            {
                if (Fire(employeeId) == 1) RefreshAllCards();
            }
        });

        CrashLog.Log($"CustomEmployee: Buttons configured for '{entry.EmployeeId}' (hired={entry.IsHired})");
    }

    private static void ShowOverlay(bool hire)
    {
        var hrSystems = UnityEngine.Object.FindObjectsOfType<HRSystem>();
        if (hrSystems == null) return;
        for (int i = 0; i < hrSystems.Count; i++)
        {
            var hr = hrSystems[i];
            if (hr == null || !hr.gameObject.activeInHierarchy) continue;
            var overlay = hire ? hr.confirmHireOverlay : hr.confirmFireOverlay;
            overlay?.SetActive(true);
            return;
        }
    }

    // Called from Harmony prefix on ButtonConfirmHire. Returns true if handled (skip vanilla).
    public static bool HandleConfirmHire(HRSystem hr)
    {
        if (_pendingEmployeeId == null || !_pendingIsHire) return false;
        string id = _pendingEmployeeId;
        _pendingEmployeeId = null;
        CrashLog.Log($"HandleConfirmHire: confirming hire for '{id}'");
        Hire(id);
        hr.confirmHireOverlay?.SetActive(false);
        RefreshAllCards();
        return true;
    }

    // Called from Harmony prefix on ButtonConfirmFireEmployee. Returns true if handled.
    public static bool HandleConfirmFire(HRSystem hr)
    {
        if (_pendingEmployeeId == null || _pendingIsHire) return false;
        string id = _pendingEmployeeId;
        _pendingEmployeeId = null;
        CrashLog.Log($"HandleConfirmFire: confirming fire for '{id}'");
        Fire(id);
        hr.confirmFireOverlay?.SetActive(false);
        RefreshAllCards();
        return true;
    }

    public static void ClearPending() => _pendingEmployeeId = null;

    public static void SaveState()
    {
        try
        {
            var lines = new List<string>();
            foreach (var e in _employees)
                if (e.IsHired)
                    lines.Add(e.EmployeeId);
            File.WriteAllLines(_statePath, lines);
        }
        catch (Exception ex) { CrashLog.LogException("SaveState", ex); }
    }

    public static void LoadState()
    {
        try
        {
            if (!File.Exists(_statePath)) return;
            var hired = new HashSet<string>(File.ReadAllLines(_statePath));
            bool anyRestored = false;
            foreach (var e in _employees)
            {
                if (hired.Contains(e.EmployeeId))
                {
                    e.IsHired = true;
                    anyRestored = true;
                    CrashLog.Log($"LoadState: restored IsHired for '{e.EmployeeId}' ({e.Name}), salary={e.SalaryPerHour}");
                }
            }
            if (anyRestored)
            {
                _salariesNeedReregistration = true;
                CrashLog.Log("LoadState: salaries need re-registration (deferred until BalanceSheet is ready)");
            }
        }
        catch (Exception ex) { CrashLog.LogException("LoadState", ex); }
    }

    // Deferred until BalanceSheet is available. Only acts once after LoadState sets the flag.
    public static void ReregisterSalariesIfNeeded()
    {
        if (!_salariesNeedReregistration) return;

        try
        {
            if (BalanceSheet.instance == null) return;

            int count = 0;
            foreach (var e in _employees)
            {
                if (e.IsHired)
                {
                    BalanceSheet.instance.RegisterSalary((int)e.SalaryPerHour);
                    count++;
                    CrashLog.Log($"ReregisterSalariesIfNeeded: registered salary {e.SalaryPerHour} for '{e.EmployeeId}' ({e.Name})");
                }
            }

            _salariesNeedReregistration = false;
            CrashLog.Log($"ReregisterSalariesIfNeeded: done, re-registered {count} salary entries");
        }
        catch (Exception ex) { CrashLog.LogException("ReregisterSalariesIfNeeded", ex); }
    }

    // ButtonExtended is a Selectable subclass with its own onClick; falls back to standard Button.
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

        // Fallback to standard Button
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

    private static void SetPortrait(Transform card, string employeeId)
    {
        try
        {
            var portraitTransform = card.Find("Image");
            if (portraitTransform == null) return;

            string assetsDir = Path.Combine(MelonEnvironment.UserDataDirectory, "ModAssets");
            string imagePath = null;
            foreach (var ext in new[] { ".jpg", ".png" })
            {
                string candidate = Path.Combine(assetsDir, employeeId + ext);
                if (File.Exists(candidate)) { imagePath = candidate; break; }
            }

            if (imagePath != null)
            {
                try
                {
                    byte[] imageData = File.ReadAllBytes(imagePath);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (ImageConversion.LoadImage(tex, imageData))
                    {
                        var sprite = Sprite.Create(
                            tex,
                            new Rect(0, 0, tex.width, tex.height),
                            new Vector2(0.5f, 0.5f),
                            100f
                        );

                        var image = portraitTransform.GetComponent<Image>();
                        if (image != null)
                        {
                            image.sprite = sprite;
                            image.color = Color.white;
                            image.preserveAspect = true;
                        }

                        var rawImage = portraitTransform.GetComponent<RawImage>();
                        if (rawImage != null)
                        {
                            rawImage.texture = tex;
                            rawImage.color = Color.white;
                        }

                        CrashLog.Log($"CustomEmployee: Loaded portrait from '{imagePath}' for '{employeeId}'");
                        return;
                    }
                    else
                    {
                        CrashLog.Log($"CustomEmployee: Failed to decode image at '{imagePath}' for '{employeeId}'");
                    }
                }
                catch (Exception ex)
                {
                    CrashLog.LogException($"SetPortrait: load image for '{employeeId}'", ex);
                }
            }

            // Fallback: solid teal color
            var fallbackImage = portraitTransform.GetComponent<Image>();
            if (fallbackImage != null)
            {
                fallbackImage.sprite = null;
                fallbackImage.color = new Color(0.0f, 0.6f, 0.7f, 1f);
            }

            var fallbackRaw = portraitTransform.GetComponent<RawImage>();
            if (fallbackRaw != null)
            {
                fallbackRaw.texture = null;
                fallbackRaw.color = new Color(0.0f, 0.6f, 0.7f, 1f);
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException("SetPortrait", ex);
        }
    }

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

                // Try scroll view content first, fall back to Grid
                Transform contentGrid = null;
                var scrollView = hr.transform.Find("ModScrollView");
                if (scrollView != null)
                    contentGrid = scrollView.Find("Viewport/Content");
                if (contentGrid == null)
                    contentGrid = hr.transform.Find("Grid");
                if (contentGrid == null) continue;

                foreach (var entry in _employees)
                {
                    var cardTransform = contentGrid.Find("CustomEmployee_" + entry.EmployeeId);
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

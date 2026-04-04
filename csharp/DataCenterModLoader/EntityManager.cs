using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.AI;
using Il2Cpp;
using Il2CppUMA;
using Il2CppUMA.CharacterSystem;
using Il2CppTMPro;

namespace DataCenterModLoader;

public static class EntityManager
{
    private class ManagedEntity
    {
        public uint Id;
        public GameObject GO;
        public Animator Animator;
        public NavMeshAgent NavAgent;
        public bool WaitingForUMA;
        public float UMAWaitStart;
        public int SpeedParamHash;
        public int WalkingParamHash;
        public bool HasSpeedParam;
        public bool HasWalkingParam;
        public bool AnimParamsDiscovered;
        public int CrouchParamHash;
        public int SittingParamHash;
        public int CarryingParamHash;
        public bool HasCrouchParam;
        public bool HasSittingParam;
        public bool HasCarryingParam;
        public GameObject NameTagGO;
        public Vector3 LastPos;
        public GameObject CarryProxyGO;
        public Transform HandBone;
        public bool HandBoneSearched;
        public bool ColliderAdded;
        public float ColliderEligibleTime;
    }

    private static readonly Dictionary<uint, ManagedEntity> _entities = new();
    private static uint _nextId = 1;
    private static float _lastRoofCheckTime = 0f;

    public static uint SpawnCharacter(uint prefabIdx, float x, float y, float z, float rotY, string name)
    {
        try
        {
            GameObject go = null;
            var mgr = MainGameManager.instance;
            if (mgr != null && mgr.techniciansPrefabs != null && mgr.techniciansPrefabs.Length > 0)
            {
                int idx = (int)(prefabIdx % (uint)mgr.techniciansPrefabs.Length);
                var prefab = mgr.techniciansPrefabs[idx];

                bool prefabWasActive = prefab.activeSelf;
                prefab.SetActive(false);
                go = UnityEngine.Object.Instantiate(prefab);
                if (prefabWasActive) prefab.SetActive(true);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.transform.position = new Vector3(x, y, z);
                var col = go.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.Destroy(col);

                uint capsuleId = _nextId++;
                var capsuleEntity = new ManagedEntity
                {
                    Id = capsuleId,
                    GO = go,
                    WaitingForUMA = false,
                };
                go.name = $"Entity_{capsuleId}";
                AddNameTag(go, name, capsuleEntity);
                _entities[capsuleId] = capsuleEntity;
                CrashLog.Log($"[EntityManager] Spawned capsule fallback entity {capsuleId} '{name}'");
                return capsuleId;
            }

            go.SetActive(false);

            var spawnPos = new Vector3(x, y, z);

            go.transform.position = spawnPos;
            go.transform.eulerAngles = new Vector3(0, rotY, 0);


            var navCheck = go.GetComponent<NavMeshAgent>();

            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                string typeName = mb.GetIl2CppType().Name;
                if (typeName.Contains("UMA") || typeName.Contains("DynamicCharacter") ||
                    typeName.Contains("Avatar") || typeName.Contains("Generator") ||
                    typeName == "Animator" || typeName.Contains("Renderer"))
                    continue;
                try { mb.enabled = false; } catch { }
            }


            if (navCheck != null)
                try { UnityEngine.Object.DestroyImmediate(navCheck); } catch { }
            foreach (var cc in go.GetComponentsInChildren<CharacterController>(true))
                try { UnityEngine.Object.DestroyImmediate(cc); } catch { }
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                try { UnityEngine.Object.DestroyImmediate(c); } catch { }
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
                try { UnityEngine.Object.DestroyImmediate(rb); } catch { }
            foreach (var nav in go.GetComponentsInChildren<NavMeshAgent>(true))
                try { UnityEngine.Object.DestroyImmediate(nav); } catch { }

            go.SetActive(true);

            Animator animator = go.GetComponentInChildren<Animator>();
            if (animator != null)
                animator.applyRootMotion = false;

            uint id = _nextId++;
            go.name = $"Entity_{id}";

            var entity = new ManagedEntity
            {
                Id = id,
                GO = go,
                Animator = animator,
                NavAgent = null, // NavMeshAgent destroyed — remote entities don't need pathfinding
                WaitingForUMA = true,
                UMAWaitStart = Time.time,
                LastPos = spawnPos,
            };

            AddNameTag(go, name, entity);
            _entities[id] = entity;

            CrashLog.Log($"[EntityManager] Spawned entity {id} '{name}' at ({spawnPos.x:F1},{spawnPos.y:F1},{spawnPos.z:F1}) anim={animator != null}");
            return id;
        }
        catch (Exception ex)
        {
            CrashLog.LogException("EntityManager.SpawnCharacter", ex);
            return 0;
        }
    }

    public static void DestroyEntity(uint entityId)
    {
        if (!_entities.TryGetValue(entityId, out var entity)) return;
        if (entity.CarryProxyGO != null) UnityEngine.Object.Destroy(entity.CarryProxyGO);
        if (entity.NameTagGO != null) UnityEngine.Object.Destroy(entity.NameTagGO);
        if (entity.GO != null) UnityEngine.Object.Destroy(entity.GO);
        _entities.Remove(entityId);
    }

    public static void SetPosition(uint entityId, float x, float y, float z, float rotY)
    {
        if (!_entities.TryGetValue(entityId, out var entity)) return;
        if (entity.GO == null) { _entities.Remove(entityId); return; }

        // Direct transform — no NavMeshAgent involvement for remote entities
        entity.GO.transform.position = new Vector3(x, y, z);
        entity.GO.transform.eulerAngles = new Vector3(0f, rotY, 0f);
    }

    public static bool IsEntityReady(uint entityId)
    {
        if (!_entities.TryGetValue(entityId, out var entity)) return false;
        return !entity.WaitingForUMA;
    }

    public static void SetAnimation(uint entityId, float speed, bool isWalking)
    {
        if (!_entities.TryGetValue(entityId, out var entity)) return;
        if (entity.Animator == null) return;
        try
        {
            if (entity.HasSpeedParam)
            {
                // Smooth the speed to avoid jittery animation blending
                float current = entity.Animator.GetFloat(entity.SpeedParamHash);
                float smoothed = Mathf.Lerp(current, speed, Time.deltaTime * 8f);
                entity.Animator.SetFloat(entity.SpeedParamHash, smoothed);
            }
            if (entity.HasWalkingParam)
                entity.Animator.SetBool(entity.WalkingParamHash, isWalking);
        }
        catch { }
    }

    /// <summary>Set just the carry animator bool (cheap, can be called every frame)</summary>
    public static void SetCarryAnim(uint entityId, bool isCarrying)
    {
        if (!_entities.TryGetValue(entityId, out var entity)) return;
        if (entity.Animator == null || !entity.HasCarryingParam) return;
        try { entity.Animator.SetBool(entity.CarryingParamHash, isCarrying); }
        catch { }
    }

    /// <summary>Create a visual proxy from real game prefab, parented to hand bone</summary>
    public static void CreateCarryVisual(uint entityId, uint objectInHandType)
    {
        if (!_entities.TryGetValue(entityId, out var entity)) return;
        try
        {
            // Destroy existing proxy if any
            if (entity.CarryProxyGO != null)
            {
                UnityEngine.Object.Destroy(entity.CarryProxyGO);
                entity.CarryProxyGO = null;
            }

            // Find hand bone if not searched yet
            if (!entity.HandBoneSearched && entity.GO != null)
            {
                entity.HandBoneSearched = true;
                entity.HandBone = FindHandBone(entity.GO.transform);
                if (entity.HandBone != null)
                    CrashLog.Log($"[EntityManager] Found hand bone '{entity.HandBone.name}' for entity {entity.Id}");
                else
                    CrashLog.Log($"[EntityManager] No hand bone found for entity {entity.Id}");
            }

            // Try real game prefab first, fall back to primitive
            GameObject proxy = TryCreateFromGamePrefab(objectInHandType);
            if (proxy == null)
                proxy = CreateFallbackProxy(objectInHandType);

            if (proxy != null)
            {
                Transform parent = entity.HandBone ?? entity.GO?.transform;
                if (parent != null)
                {
                    proxy.transform.SetParent(parent, false);
                    var (pos, rot) = GetCarryOffsets(objectInHandType, entity.HandBone != null);
                    proxy.transform.localPosition = pos;
                    proxy.transform.localRotation = rot;
                }
                entity.CarryProxyGO = proxy;
            }

            CrashLog.Log($"[EntityManager] Created carry visual type={objectInHandType} prefab={proxy != null} for entity {entity.Id} bone={entity.HandBone?.name ?? "none"} parent={proxy?.transform.parent?.name ?? "none"}");
        }
        catch (Exception ex) { CrashLog.LogException("EntityManager.CreateCarryVisual", ex); }
    }

    private static (Vector3 position, Quaternion rotation) GetCarryOffsets(uint objectInHandType, bool hasHandBone)
    {
        if (!hasHandBone)
        {
            return (new Vector3(0.3f, 0.8f, 0.3f), Quaternion.identity);
        }

        switch (objectInHandType)
        {
            case 1: // Server1U — flat server unit
                return (new Vector3(0.0f, 0.0f, 0.0f), Quaternion.Euler(0f, 90f, 0f));
            case 2: // Server2U — taller server
                return (new Vector3(0.0f, 0.0f, 0.0f), Quaternion.Euler(0f, 90f, 0f));
            case 3: // Server3U — tallest server
                return (new Vector3(0.0f, 0.0f, 0.0f), Quaternion.Euler(0f, 90f, 0f));
            case 4: // Switch — flat network switch
                return (new Vector3(0.0f, 0.0f, 0.0f), Quaternion.Euler(0f, 90f, 0f));
            case 5: // Rack — large item
                return (new Vector3(0.0f, 0.0f, 0.0f), Quaternion.Euler(0f, 90f, 0f));
            case 6: // CableSpinner — round spool
                return (new Vector3(0.0f, 0.0f, 0.0f), Quaternion.Euler(0f, 0f, 0f));
            case 7: // PatchPanel — flat panel
                return (new Vector3(0.0f, 0.0f, 0.0f), Quaternion.Euler(0f, 90f, 0f));
            case 8: // SFPModule — tiny module
                return (new Vector3(0.0f, 0.0f, 0.0f), Quaternion.Euler(0f, 0f, 0f));
            case 9: // SFPBox — small box
                return (new Vector3(0.0f, 0.0f, 0.0f), Quaternion.Euler(0f, 0f, 0f));
            default:
                return (new Vector3(0.0f, 0.0f, 0.0f), Quaternion.Euler(0f, 0f, 0f));
        }
    }

    /// <summary>Destroy the carry visual proxy</summary>
    public static void DestroyCarryVisual(uint entityId)
    {
        if (!_entities.TryGetValue(entityId, out var entity)) return;
        if (entity.CarryProxyGO != null)
        {
            UnityEngine.Object.Destroy(entity.CarryProxyGO);
            entity.CarryProxyGO = null;
        }
    }

    /// <summary>Find the right hand bone in a humanoid UMA rig</summary>
    private static Transform FindHandBone(Transform root)
    {
        // UMA humanoid rigs use standard naming; search for right hand
        string[] handNames = { "Right Hand", "RightHand", "Hand_R", "hand_r", "R_Hand", "Bip01 R Hand" };
        foreach (var name in handNames)
        {
            var bone = FindChildRecursive(root, name);
            if (bone != null) return bone;
        }

        // Fallback: search for any transform containing "hand" and "r" (case insensitive)
        return FindChildByPattern(root, t =>
        {
            string n = t.name.ToLower();
            return n.Contains("hand") && (n.Contains("right") || (n.Contains("_r") || n.Contains(".r") || n.StartsWith("r_") || n.EndsWith(" r")));
        });
    }

    private static Transform FindChildRecursive(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            var found = FindChildRecursive(parent.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    private static Transform FindChildByPattern(Transform parent, Func<Transform, bool> predicate)
    {
        if (predicate(parent)) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            var found = FindChildByPattern(parent.GetChild(i), predicate);
            if (found != null) return found;
        }
        return null;
    }


    /// <summary>Cached prefab templates per ObjectInHand type (stripped visual clones)</summary>
    private static readonly Dictionary<uint, GameObject> _carryPrefabCache = new();
    private static bool _prefabCacheAttempted = false;

    /// <summary>Try to clone a real game prefab for the carried item type</summary>
    private static GameObject TryCreateFromGamePrefab(uint objectInHandType)
    {
        try
        {
            // Check cache first
            if (_carryPrefabCache.TryGetValue(objectInHandType, out var cachedTemplate))
            {
                if (cachedTemplate != null)
                {
                    var clone = UnityEngine.Object.Instantiate(cachedTemplate);
                    clone.SetActive(true);
                    clone.name = $"CarryVisual_{objectInHandType}";
                    return clone;
                }
                return null;
            }

            var shop = UnityEngine.Object.FindObjectOfType<ComputerShop>();
            if (shop == null || shop.shopItems == null)
            {
                CrashLog.Log("[EntityManager] ComputerShop not found, using fallback proxy");
                return null;
            }

            PlayerManager.ObjectInHand targetType = (PlayerManager.ObjectInHand)(int)objectInHandType;

            foreach (var shopItem in shop.shopItems)
            {
                if (shopItem == null || shopItem.shopItemSO == null) continue;
                if (shopItem.shopItemSO.itemType != targetType) continue;

                int itemID = shopItem.shopItemSO.itemID;
                var prefab = shop.GetPrefabForItem(itemID, targetType);
                if (prefab == null) continue;

                var template = UnityEngine.Object.Instantiate(prefab);
                StripToVisualOnly(template);
                template.SetActive(false);
                template.name = $"CarryTemplate_{objectInHandType}";
                UnityEngine.Object.DontDestroyOnLoad(template);
                _carryPrefabCache[objectInHandType] = template;

                CrashLog.Log($"[EntityManager] Cached carry prefab for type {objectInHandType} (itemID={itemID})");

                var instance = UnityEngine.Object.Instantiate(template);
                instance.SetActive(true);
                instance.name = $"CarryVisual_{objectInHandType}";
                return instance;
            }

            // No matching shop item found, cache null to avoid retrying
            CrashLog.Log($"[EntityManager] No shop prefab found for type {objectInHandType}");
            _carryPrefabCache[objectInHandType] = null;
            return null;
        }
        catch (Exception ex)
        {
            CrashLog.LogException("EntityManager.TryCreateFromGamePrefab", ex);
            _carryPrefabCache[objectInHandType] = null;
            return null;
        }
    }

    /// <summary>Strip all non-visual components from a GameObject (physics, scripts, nav)</summary>
    private static void StripToVisualOnly(GameObject go)
    {
        // Remove all colliders
        foreach (var col in go.GetComponentsInChildren<Collider>(true))
            try { UnityEngine.Object.DestroyImmediate(col); } catch { }

        // Remove all rigidbodies
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
            try { UnityEngine.Object.DestroyImmediate(rb); } catch { }

        // Remove NavMeshAgents
        foreach (var nav in go.GetComponentsInChildren<NavMeshAgent>(true))
            try { UnityEngine.Object.DestroyImmediate(nav); } catch { }

        // Remove CharacterControllers
        foreach (var cc in go.GetComponentsInChildren<CharacterController>(true))
            try { UnityEngine.Object.DestroyImmediate(cc); } catch { }

        // Remove all game scripts (MonoBehaviours) — keeps Transform, MeshFilter, MeshRenderer, etc.
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            try { UnityEngine.Object.DestroyImmediate(mb); } catch { }

        // Disable animators (don't want independent animation)
        foreach (var anim in go.GetComponentsInChildren<Animator>(true))
            try { anim.enabled = false; } catch { }
    }

    /// <summary>Create a primitive fallback when real prefab isn't available</summary>
    private static GameObject CreateFallbackProxy(uint objectInHandType)
    {
        try
        {
            var proxy = new GameObject($"CarryFallback_{objectInHandType}");
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);

            // Remove collider
            var col = visual.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);

            visual.transform.SetParent(proxy.transform, false);

            Vector3 scale;
            Color color;
            switch (objectInHandType)
            {
                case 1: // Server1U
                    scale = new Vector3(0.43f, 0.045f, 0.5f);
                    color = new Color(0.2f, 0.2f, 0.25f);
                    break;
                case 2: // Server2U
                    scale = new Vector3(0.43f, 0.09f, 0.5f);
                    color = new Color(0.2f, 0.2f, 0.25f);
                    break;
                case 3: // Server3U
                    scale = new Vector3(0.43f, 0.135f, 0.5f);
                    color = new Color(0.2f, 0.2f, 0.25f);
                    break;
                case 4: // Switch
                    scale = new Vector3(0.43f, 0.045f, 0.3f);
                    color = new Color(0.15f, 0.3f, 0.15f);
                    break;
                case 5: // Rack
                    scale = new Vector3(0.6f, 1.2f, 0.8f);
                    color = new Color(0.3f, 0.3f, 0.3f);
                    break;
                case 6: // CableSpinner
                    scale = new Vector3(0.15f, 0.15f, 0.15f);
                    color = new Color(0.4f, 0.3f, 0.1f);
                    break;
                case 7: // PatchPanel
                    scale = new Vector3(0.43f, 0.045f, 0.3f);
                    color = new Color(0.25f, 0.25f, 0.3f);
                    break;
                case 8: // SFPModule
                    scale = new Vector3(0.02f, 0.01f, 0.06f);
                    color = new Color(0.6f, 0.6f, 0.6f);
                    break;
                case 9: // SFPBox
                    scale = new Vector3(0.1f, 0.06f, 0.08f);
                    color = new Color(0.35f, 0.35f, 0.4f);
                    break;
                default:
                    scale = new Vector3(0.3f, 0.15f, 0.4f);
                    color = new Color(0.25f, 0.25f, 0.3f);
                    break;
            }

            visual.transform.localScale = scale;
            var renderer = visual.GetComponent<Renderer>();
            if (renderer != null)
            {
                try
                {
                    var mat = new Material(Shader.Find("Standard"));
                    mat.color = color;
                    renderer.material = mat;
                }
                catch { }
            }

            return proxy;
        }
        catch (Exception ex)
        {
            CrashLog.LogException("EntityManager.CreateFallbackProxy", ex);
            return null;
        }
    }

    public static void SetCrouching(uint entityId, bool isCrouching)
    {
        if (!_entities.TryGetValue(entityId, out var entity)) return;
        if (entity.Animator == null) return;
        try
        {
            if (entity.HasCrouchParam)
                entity.Animator.SetBool(entity.CrouchParamHash, isCrouching);
        }
        catch { }
    }

    public static void SetSitting(uint entityId, bool isSitting)
    {
        if (!_entities.TryGetValue(entityId, out var entity)) return;
        if (entity.Animator == null) return;
        try
        {
            if (entity.HasSittingParam)
                entity.Animator.SetBool(entity.SittingParamHash, isSitting);
        }
        catch { }
    }

    public static uint GetPrefabCount()
    {
        try
        {
            var mgr = MainGameManager.instance;
            if (mgr != null && mgr.techniciansPrefabs != null)
                return (uint)mgr.techniciansPrefabs.Length;
        }
        catch { }
        return 0;
    }

    public static void SetEntityName(uint entityId, string name)
    {
        if (!_entities.TryGetValue(entityId, out var entity)) return;
        if (entity.NameTagGO == null) return;
        try
        {
            var tmp = entity.NameTagGO.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null) tmp.text = name;
        }
        catch { }
    }

    public static void Update()
    {
        try
        {
            if (_entities.Count > 0 && Time.time - _lastRoofCheckTime >= 2.0f)
            {
                _lastRoofCheckTime = Time.time;
                var pm = PlayerManager.instance;
                if (pm != null && pm.playerGO != null && pm.playerClass != null)
                {
                    float playerY = pm.playerGO.transform.position.y;
                    if (playerY > 3.5f)
                    {
                        CrashLog.Log($"[EntityManager] Roof safety net triggered — player Y={playerY:F2}, warping to origin.");
                        pm.playerClass.WarpPlayer(new Vector3(0f, 1f, 0f), pm.playerGO.transform.rotation);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogException("[EntityManager] Roof safety net error", ex);
        }

        // Check UMA mesh status for all waiting entities
        var toRetry = new List<uint>();
        foreach (var kvp in _entities)
        {
            var entity = kvp.Value;
            if (!entity.WaitingForUMA) continue;
            if (entity.GO == null) continue;

            var umaData = entity.GO.GetComponentInChildren<UMAData>(true);
            bool meshReady = false;
            int rendererCount = 0;

            if (umaData != null && umaData.isOfficiallyCreated)
            {
                try
                {
                    var rends = umaData.GetRenderers();
                    if (rends != null)
                    {
                        for (int r = 0; r < rends.Length; r++)
                        {
                            var smr = rends[r];
                            if (smr != null && smr.sharedMesh != null)
                                rendererCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    CrashLog.LogException($"[EntityManager] UMAData.GetRenderers() error: {ex.Message}", ex);
                }
                meshReady = rendererCount > 0;
            }

            if (meshReady)
            {
                CrashLog.Log($"[EntityManager] UMA mesh ready for entity {entity.Id} {rendererCount} renderer");

                foreach (var mb in entity.GO.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null) continue;
                    string typeName = mb.GetIl2CppType().Name;
                    if (typeName == "Animator" || typeName.Contains("Renderer")) continue;
                    try { mb.enabled = false; } catch { }
                }
                entity.WaitingForUMA = false;
                entity.ColliderEligibleTime = Time.time + 3.0f; // wait 3s for position to settle

                // Disable NavMeshAgent — remote entities don't need pathfinding
                if (entity.NavAgent != null && entity.NavAgent.enabled)
                    entity.NavAgent.enabled = false;



                if (!entity.AnimParamsDiscovered)
                {
                    if (entity.Animator == null)
                        entity.Animator = entity.GO.GetComponentInChildren<Animator>();
                    if (entity.Animator != null)
                    {
                        entity.Animator.applyRootMotion = false;
                        try
                        {
                            foreach (var param in entity.Animator.parameters)
                            {
                                string lower = param.name.ToLower();
                                if (!entity.HasSpeedParam && param.type == AnimatorControllerParameterType.Float &&
                                    (lower.Contains("speed") || lower.Contains("velocity") || lower.Contains("move") || lower.Contains("forward")))
                                {
                                    entity.SpeedParamHash = param.nameHash;
                                    entity.HasSpeedParam = true;
                                }
                                if (!entity.HasWalkingParam && param.type == AnimatorControllerParameterType.Bool &&
                                    (lower.Contains("walk") || lower.Contains("moving") || lower.Contains("run")))
                                {
                                    entity.WalkingParamHash = param.nameHash;
                                    entity.HasWalkingParam = true;
                                }
                                if (!entity.HasCrouchParam && param.type == AnimatorControllerParameterType.Bool &&
                                    lower.Contains("crouch"))
                                {
                                    entity.CrouchParamHash = param.nameHash;
                                    entity.HasCrouchParam = true;
                                }
                                if (!entity.HasSittingParam && param.type == AnimatorControllerParameterType.Bool &&
                                    (lower == "issitting" || lower.Contains("sitting")))
                                {
                                    entity.SittingParamHash = param.nameHash;
                                    entity.HasSittingParam = true;
                                }
                                if (!entity.HasCarryingParam && param.type == AnimatorControllerParameterType.Bool &&
                                    (lower.Contains("carry") || lower.Contains("carrying")))
                                {
                                    entity.CarryingParamHash = param.nameHash;
                                    entity.HasCarryingParam = true;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            CrashLog.LogException($"[EntityManager] Animator param discovery error: {ex.Message}", ex);
                        }
                    }
                    entity.AnimParamsDiscovered = true;
                }
            }
            else if (Time.time - entity.UMAWaitStart > 15f)
            {
                // retry
                toRetry.Add(entity.Id);
            }

            if (!entity.ColliderAdded && !entity.WaitingForUMA && entity.GO != null && Time.time >= entity.ColliderEligibleTime)
            {
                try
                {
                    var ep = entity.GO.transform.position;

                    // Don't add collider until entity has moved away from default spawn (0,1,0)
                    float dx = ep.x;
                    float dz = ep.z;
                    bool farEnough = (dx * dx + dz * dz) >= 4.0f; // >= 2m from origin

                    if (farEnough)
                    {
                        var capsule = entity.GO.AddComponent<CapsuleCollider>();
                        capsule.center = new Vector3(0f, 0.9f, 0f);
                        capsule.radius = 0.3f;
                        capsule.height = 1.8f;
                        entity.ColliderAdded = true;
                        CrashLog.Log($"[EntityManager] Added collision capsule to entity {entity.Id}");
                    }
                }
                catch (Exception ex)
                {
                    CrashLog.LogException($"[EntityManager] Failed to add collision capsule to entity {entity.Id}", ex);
                }
            }
        }

        // Handle retries
        foreach (var id in toRetry)
        {
            if (_entities.TryGetValue(id, out var entity))
            {
                CrashLog.Log($"[EntityManager] UMA timeout for entity {id}, retrying");
                var pos = entity.GO != null ? entity.GO.transform.position : Vector3.zero;
                var rotY = entity.GO != null ? entity.GO.transform.eulerAngles.y : 0f;
                string entityName = "Entity";
                if (entity.NameTagGO != null)
                {
                    var tmp = entity.NameTagGO.GetComponentInChildren<TextMeshProUGUI>();
                    if (tmp != null) entityName = tmp.text;
                }

                if (entity.NameTagGO != null) UnityEngine.Object.Destroy(entity.NameTagGO);
                if (entity.GO != null) UnityEngine.Object.Destroy(entity.GO);
                _entities.Remove(id);

                CrashLog.Log($"[EntityManager] Entity {id} destroyed due to UMA timeout");
            }
        }
    }

    public static void DestroyAll()
    {
        foreach (var kvp in _entities)
        {
            if (kvp.Value.CarryProxyGO != null) UnityEngine.Object.Destroy(kvp.Value.CarryProxyGO);
            if (kvp.Value.NameTagGO != null) UnityEngine.Object.Destroy(kvp.Value.NameTagGO);
            if (kvp.Value.GO != null) UnityEngine.Object.Destroy(kvp.Value.GO);
        }
        _entities.Clear();
        foreach (var kvp in _carryPrefabCache)
            if (kvp.Value != null) UnityEngine.Object.Destroy(kvp.Value);
        _carryPrefabCache.Clear();
        _nextId = 1;
    }

    private static void AddNameTag(GameObject parent, string name, ManagedEntity entity)
    {
        try
        {
            var parentScale = parent.transform.lossyScale;
            float scale = 0.01f;
            float fontSize = 5f;
            float rectW = 70f;
            float rectH = 10f;

            if (parentScale.x > 0.001f)
            {
                float compensate = 1f / parentScale.x;
                scale *= compensate;
            }

            var canvasGO = new GameObject($"NameTag_Entity_{entity.Id}");
            canvasGO.transform.position = parent.transform.position + new Vector3(0, 1.75f, 0);

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var canvasRect = canvasGO.GetComponent<RectTransform>();
            if (canvasRect != null)
                canvasRect.sizeDelta = new Vector2(rectW, rectH);

            canvasGO.transform.localScale = new Vector3(scale, scale, scale);

            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(canvasGO.transform, false);

            var bgImage = bgGO.AddComponent<UnityEngine.UI.Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.45f);

            var bgRect = bgGO.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0f);
            bgRect.anchorMax = new Vector2(1f, 1f);
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(canvasGO.transform, false);

            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = name;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.outlineWidth = 0.2f;
            tmp.outlineColor = new Color32(0, 0, 0, 200);

            var rect = textGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var bb = canvasGO.AddComponent<BillboardNameTag>();
            bb.followTarget = parent.transform;
            bb.offsetY = 1.85f;

            entity.NameTagGO = canvasGO;
        }
        catch (Exception ex)
        {
            CrashLog.LogException("EntityManager.AddNameTag", ex);
        }
    }
}

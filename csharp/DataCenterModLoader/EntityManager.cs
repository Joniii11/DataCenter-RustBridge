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
        public GameObject NameTagGO;
        public Vector3 LastPos;
    }

    private static readonly Dictionary<uint, ManagedEntity> _entities = new();
    private static uint _nextId = 1;

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
                go = UnityEngine.Object.Instantiate(prefab);
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

            NavMeshAgent nav = null;
            var navCheck = go.GetComponent<NavMeshAgent>();
            if (navCheck != null)
            {
                nav = navCheck;
                nav.updatePosition = false;
                nav.updateRotation = false;
                nav.updateUpAxis = false;
                nav.isStopped = true;
                nav.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
                nav.autoTraverseOffMeshLink = false;
                nav.autoBraking = false;
            }

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

            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                try { UnityEngine.Object.Destroy(c); } catch { }
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
                try { UnityEngine.Object.Destroy(rb); } catch { }

            // Disable nav before activation so it doesnt snap
            if (nav != null) nav.enabled = false;
            go.SetActive(true);

            if (nav != null)
            {
                nav.enabled = true;
                nav.Warp(spawnPos);
                nav.enabled = false; // Disable permanently — remote entities don't need pathfinding
            }

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
                NavAgent = nav,
                WaitingForUMA = true,
                UMAWaitStart = Time.time,
                LastPos = spawnPos,
            };

            AddNameTag(go, name, entity);
            _entities[id] = entity;

            CrashLog.Log($"[EntityManager] Spawned entity {id} '{name}' at ({x:F1},{y:F1},{z:F1}) nav={nav?.isOnNavMesh} anim={animator != null}");
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

    /// <summary>Called once per frame from Core.cs to process UMA ready</summary>
    public static void Update()
    {
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
            if (kvp.Value.NameTagGO != null) UnityEngine.Object.Destroy(kvp.Value.NameTagGO);
            if (kvp.Value.GO != null) UnityEngine.Object.Destroy(kvp.Value.GO);
        }
        _entities.Clear();
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

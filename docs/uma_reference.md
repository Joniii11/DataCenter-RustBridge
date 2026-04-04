# UMA (Unity Multipurpose Avatar) — Reference for Data Center Modding

All types live in the `Il2CppUMA` / `Il2CppUMA.CharacterSystem` namespaces.
Assembly: `Il2CppUMA_Core.dll` (396 types total).

Full type dump available at: `inspect_tool/uma_dump.txt`

---

## Overview

Data Center uses UMA to generate its technician characters at runtime. Unlike static
meshes, UMA characters have **no SkinnedMeshRenderer at prefab time** — the mesh,
materials, and bone bindings are assembled on the fly by the UMA generation pipeline.

### Why This Matters for Multiplayer

When cloning a technician prefab (`MainGameManager.instance.techniciansPrefabs[0]`),
the resulting GameObject has **0 Renderers**. The mesh only exists after UMA's
`DynamicCharacterAvatar.Start()` fires and the generator processes the character.

**Two strategies for getting a visible remote player:**

| Strategy | How | Pros | Cons |
|----------|-----|------|------|
| Clone a **live** technician | `Instantiate(TechnicianManager.instance.technicians[i].gameObject)` | Instant mesh — UMA already ran | Requires ≥1 hired technician |
| Instantiate the **prefab** and let UMA run | `Instantiate(prefab)`, keep UMA scripts alive for a few frames | Works with 0 hired technicians | Need to wait 1–3 frames for mesh; must selectively disable scripts |

---

## Type Hierarchy

```
UMAContextBase (MonoBehaviour)
  └── UMAContext                         — singleton, holds asset indices

UMAAvatarBase (MonoBehaviour)
  └── DynamicCharacterAvatar             — the main character component
  └── UMADynamicAvatar                   — simpler non-wardrobe variant

UMAGeneratorBase (MonoBehaviour)
  └── UMAGeneratorBuiltin
      └── UMAGenerator                   — the scene singleton that builds meshes
  └── UMAGeneratorGLIB

UMAData (MonoBehaviour)                  — per-character runtime data + dirty flags
UMASkeleton                              — bone manipulation API
SlotData / OverlayData                   — mesh slots and texture overlays
RaceData                                 — race definition (DNA, wardrobe slots, TPose)
```

---

## 1. `DynamicCharacterAvatar`

**Namespace:** `Il2CppUMA.CharacterSystem`
**Inherits:** `UMAAvatarBase → MonoBehaviour`

The primary component on every technician prefab (e.g. `UMA_Greg`, `UMA_Elsa`).
Controls the full character: race, wardrobe, DNA, colors, and triggers builds.

### Key Properties

| Type | Name | Notes |
|------|------|-------|
| `RaceSetter` | `activeRace` | Current race configuration |
| `RaceData` | `previousRace` | Race before last change |
| `Boolean` | `hide` | Show/hide the character |
| `Boolean` | `BuildCharacterEnabled` | Master switch for builds |
| `Dictionary<String, UMATextRecipe>` | `WardrobeRecipes` | Equipped wardrobe items |
| `ColorValueList` | `characterColors` | Character color settings |
| `UMAPredefinedDNA` | `predefinedDNA` | DNA preset values |
| `Single` | `AtlasResolutionScale` | Texture atlas quality scale |
| `Boolean` | `loadBlendShapes` | Whether to load blendshapes |
| `Boolean` | `rebuildSkeleton` | Force skeleton rebuild |
| `Boolean` | `alwaysRebuildSkeleton` | Always rebuild skeleton |
| `Vector3` | `BoundsOffset` | Character bounds offset |
| `List<String>` | `crossCompatibleRaces` | Cross-compatible race list |

### Key Events (UnityEvent)

| Name | Type | When |
|------|------|------|
| `CharacterStart` | `UMACharacterEvent` | Character starts up |
| `BuildCharacterBegun` | `UMADataEvent` | Build pipeline begins |
| `RecipeUpdated` | `UMADataEvent` | Recipe was changed |
| `WardrobeAdded` | `UMADataWardrobeEvent` | Wardrobe slot equipped |
| `WardrobeRemoved` | `UMADataWardrobeEvent` | Wardrobe slot removed |
| `SlotsHidden` | `UMASlotsEvent` | Slots were hidden |

### Key Methods

#### Lifecycle

| Signature | Notes |
|-----------|-------|
| `void Awake()` | Unity Awake — early init |
| `void Start()` | Virtual — triggers `InitialStartup()` |
| `void InitialStartup()` | Sets up context, loads recipe, queues first build |
| `void InitializeAvatar()` | Creates UMAData, wires generator reference |
| `void BuildFromComponentSettings()` | Builds from inspector-set settings |
| `void BuildFromStartingFileOrRecipe()` | Builds from a saved recipe string |

#### Building (Core)

| Signature | Notes |
|-----------|-------|
| `void BuildCharacter(bool RestoreDNA, bool skipBundleCheck, bool useBundleParameter)` | **THE main build entry point** — assembles recipe from wardrobe/DNA/colors, then marks dirty |
| `void ForceUpdate(bool DnaDirty, bool TextureDirty, bool MeshDirty)` | Force specific dirty flags and rebuild |
| `void GenerateNow()` | Immediately generate (bypass queue) |
| `void UpdateUMA()` | Trigger an update cycle |
| `void AvatarCreated(UMAData umaData)` | Callback after generation completes |
| `void ApplyBounds()` | Apply bounds offset to renderers |
| `bool UpdatePending()` | Check if a build is queued |

#### Cleanup

| Signature | Notes |
|-----------|-------|
| `void UnloadAvatar()` | Tear down the character |
| `void Cleanup()` | Full cleanup |

#### Race

| Signature | Notes |
|-----------|-------|
| `bool SetActiveRace()` | Apply the current `activeRace` |
| `void ChangeRace(string racename, bool force)` | Switch to a different race |
| `bool ChangeRace(string racename, ChangeRaceOptions opts, bool ForceChange)` | Change with options |
| `void ChangeRaceData(string raceName)` | Update race data reference |
| `void PerformRaceChange(RaceData race, ChangeRaceOptions opts)` | Execute the race swap |

#### Wardrobe (Slots / Clothing)

| Signature | Notes |
|-----------|-------|
| `bool SetSlot(UMATextRecipe utr)` | Equip a wardrobe recipe |
| `void SetSlot(string Slotname)` | Equip by slot name |
| `void SetSlot(string Slotname, string Recipename)` | Equip by slot + recipe name |
| `void ClearSlot(string ws)` | Remove a wardrobe slot |
| `void ClearSlots()` | Remove all wardrobe |
| `void LoadDefaultWardrobe()` | Load the default wardrobe set |
| `void LoadWardrobeSet(List<WardrobeSet> wardrobeSet, bool clearExisting)` | Bulk-load wardrobe |
| `void LoadWardrobeCollection(string collectionName)` | Load a named collection |

#### Colors

| Signature | Notes |
|-----------|-------|
| `OverlayColorData GetColor(string Name)` | Get a shared color by name |
| `void SetColor(string SharedColorName, Color AlbedoColor, Color MetallicRGB, float Gloss, bool UpdateTexture)` | Set full color |
| `void SetColorValue(string SharedColorName, Color AlbedoColor)` | Set albedo only |
| `void UpdateColors(bool triggerDirty)` | Apply color changes |

#### DNA

| Signature | Notes |
|-----------|-------|
| `void SetDNA(string DNAName, float value, bool rebuild)` | Set a DNA slider |
| `UMADnaBase[] GetAllDNA()` | Get all DNA arrays |
| `Dictionary<string,float> GetDNA(UMARecipe recipe)` | Get DNA as name→value map |
| `void ApplyPredefinedDNA()` | Apply the `predefinedDNA` preset |
| `void ApplyDNAToModifiers()` | Push DNA to modifier plugins |
| `void TryImportDNAValues(UMADnaBase[] prevDna)` | Import DNA from another character |

#### Load / Save

| Signature | Notes |
|-----------|-------|
| `void Load(UMARecipeBase umaRecipe, UMARecipeBase[] additional)` | Load from recipe assets |
| `void LoadCharacter(UMARecipeBase umaRecipe, List replaces, List additional, ...)` | Full character load |
| `void LoadFromRecipeString(string settingsToLoad, LoadOptions customLoadOptions, bool ClearWardrobe)` | Load from JSON string |
| `void DoLoad()` | Execute pending load |
| `void DoSave(bool saveAsAsset, string filePath, SaveOptions customSaveOptions)` | Save character |
| `string GetCurrentRecipe(bool backwardsCompatible)` | Serialize current recipe to string |
| `void Preload(string Recipe)` | Preload a recipe for later use |

#### Animation / Expression

| Signature | Notes |
|-----------|-------|
| `void SetExpressionSet(bool addExpressionPlayer)` | Configure expression system |
| `void InitializeExpressionPlayer(UMAData umaData)` | Init expression after build |
| `void SetAnimatorController(bool addAnimator)` | Wire up Animator controller |

### Nested Types

| Type | Notes |
|------|-------|
| `ChangeRaceOptions` | Enum: `useDefaults`, `none`, `keepDNA`, `keepWardrobe`, `keepBodyColors` |
| `LoadOptions` | Recipe load options |
| `SaveOptions` | Recipe save options |
| `RaceSetter` | Race configuration wrapper |
| `ColorValue` | Named color entry |
| `WardrobeRecipeList` | List of wardrobe recipes |

---

## 2. `UMAData`

**Namespace:** `Il2CppUMA`
**Inherits:** `MonoBehaviour`

Per-character runtime data. Holds the recipe, skeleton, dirty flags, and generated
mesh references. Every live UMA character has one of these.

### Key Properties

| Type | Name | Notes |
|------|------|-------|
| `UMARecipe` | `umaRecipe` / `_umaRecipe` | The character's assembled recipe |
| `GameObject` | `umaRoot` | Root game object |
| `UMAGeneratorBase` | `umaGenerator` | Reference to the scene generator |
| `UMASkeleton` | `skeleton` | The character's bone hierarchy |
| `Animator` | `animator` | Unity Animator component |
| `Boolean` | `isMeshDirty` | Mesh needs rebuild |
| `Boolean` | `isTextureDirty` | Textures need rebuild |
| `Boolean` | `isShapeDirty` | Shape (DNA) needs reapply |
| `Boolean` | `isAtlasDirty` | Atlas needs rebuild |
| `Boolean` | `dirty` | Any dirty flag set |
| `Boolean` | `isOfficiallyCreated` | Fully created and ready |
| `Boolean` | `firstBake` | First-time generation |
| `Boolean` | `RebuildSkeleton` | Skeleton rebuild requested |
| `Single` | `characterHeight` | Calculated character height |
| `Single` | `characterRadius` | Calculated character radius |
| `Single` | `characterMass` | Calculated character mass |
| `GeneratedMaterials` | `generatedMaterials` | Atlas/material results |
| `BlendShapeSettings` | `blendShapeSettings` | Blendshape configuration |
| `SkinnedMeshRenderer[]` | `renderers` | **The generated mesh renderers** |
| `UMARendererAsset[]` | `rendererAssets` | Renderer configuration assets |

### Key Events (Callbacks)

| Name | Type | When |
|------|------|------|
| `CharacterCreated` | `UMADataEvent` | **First build completed** — subscribe to know when mesh is ready |
| `CharacterUpdated` | `UMADataEvent` | Subsequent rebuilds completed |
| `CharacterDestroyed` | `UMADataEvent` | Character being destroyed |
| `CharacterBegun` | `UMADataEvent` | Build pipeline starting |
| `CharacterBeforeUpdated` | `UMADataEvent` | Before a rebuild |
| `CharacterBeforeDnaUpdated` | `UMADataEvent` | Before DNA is reapplied |
| `CharacterDnaUpdated` | `UMADataEvent` | After DNA applied |
| `PreUpdateUMABody` | `UMADataEvent` | Before body update pass |
| `AnimatorStateSaved` | `UMADataEvent` | Animator state captured |
| `AnimatorStateRestored` | `UMADataEvent` | Animator state restored |

### Key Methods

| Signature | Notes |
|-----------|-------|
| `void Initialize(UMAGeneratorBase generator)` | Wire up the generator reference |
| `void ApplyDNA()` | Apply all DNA converters |
| `void PreApplyDNA()` / `void PostApplyDNA()` | DNA hooks |
| `void Dirty()` | Mark everything dirty (triggers rebuild) |
| `void Dirty(bool dnaDirty, bool textureDirty, bool meshDirty)` | Mark specific flags |
| `void SetSlot(int index, SlotData slot)` | Set a mesh slot |
| `void SetSlots(SlotData[] slots)` | Replace all slots |
| `SlotData GetSlot(int index)` | Get slot at index |
| `int GetSlotArraySize()` | Number of slots |
| `UMASkeleton GetSkeleton()` | Get skeleton |
| `UMADnaBase[] GetAllDna()` | Get all DNA arrays |
| `UMADnaBase GetDna(int dnaTypeNameHash)` | Get DNA by hash |
| `SkinnedMeshRenderer GetRenderer(int idx)` | Get renderer at index |
| `SkinnedMeshRenderer[] GetRenderers()` | **Get all generated renderers** |
| `void SetRenderers(SkinnedMeshRenderer[] renderers)` | Set renderers |
| `void SetupSkeleton()` | Initialize skeleton from bones |
| `Transform GetGlobalTransform()` | Get the root transform |
| `void Show()` / `void Hide()` | Toggle character visibility |
| `void FireCharacterBegunEvents()` | Trigger begun callbacks |
| `void FireDNAAppliedEvents()` | Trigger DNA-applied callbacks |
| `void FireCharacterCompletedEvents(bool fireEvents)` | Trigger completed callbacks |
| `void SetBlendShape(string name, float weight, bool allowRebuild)` | Set a blendshape |

### Nested Types

| Type | Notes |
|------|-------|
| `UMARecipe` | Contains slots, DNA, race — the full character definition |
| `GeneratedMaterials` | Result of atlas generation (materials list) |
| `GeneratedMaterial` | Single generated material |
| `MaterialFragment` | Fragment of a material during atlas build |
| `BoneData` | Bone transform data |

---

## 3. `UMAGeneratorBase` / `UMAGeneratorBuiltin` / `UMAGenerator`

**Namespace:** `Il2CppUMA`
**Inherits:** `MonoBehaviour`

The scene-wide singleton that processes the dirty queue and generates character meshes.
Find it with `UMAGeneratorBase.FindInstance()`.

### Key Properties (UMAGeneratorBase)

| Type | Name | Notes |
|------|------|-------|
| `TextureMerge` | `textureMerge` | Texture atlas merger |
| `Int32` | `atlasResolution` | Default atlas resolution |
| `UMARendererAsset` | `defaultRendererAsset` | Default renderer settings |
| `OverlayDataAsset` | `defaultOverlayAsset` | Default overlay |
| `Boolean` | `fitAtlas` | Fit atlas to content |
| `HashSet<UMAData>` | `CreatedAvatars` | All avatars this generator built |

### Key Properties (UMAGeneratorBuiltin)

| Type | Name | Notes |
|------|------|-------|
| `UMAData` | `umaData` | Currently processing character |
| `List<UMAData>` | `umaDirtyList` | Queue of characters needing rebuild |
| `UMAMeshCombiner` | `meshCombiner` | The mesh combiner implementation |
| `Single` | `InitialScaleFactor` | Base scale factor |
| `Boolean` | `processAllPending` | Process entire queue per frame |
| `Boolean` | `autoSetRaceBlendshapes` | Auto-apply race blendshapes |

### Key Methods

| Signature | Class | Notes |
|-----------|-------|-------|
| `static UMAGeneratorBase FindInstance()` | `UMAGeneratorBase` | **Find the scene singleton** |
| `bool IsIdle()` | `UMAGeneratorBase` | Check if nothing is queued |
| `int QueueSize()` | `UMAGeneratorBase` | Number of pending builds |
| `void addDirtyUMA(UMAData umaToAdd)` | `UMAGeneratorBase` | **Queue a character for rebuild** |
| `void removeUMA(UMAData umaToRemove)` | `UMAGeneratorBase` | Remove from queue |
| `bool updatePending(UMAData umaToCheck)` | `UMAGeneratorBase` | Check if character is queued |
| `bool updateProcessing(UMAData umaToCheck)` | `UMAGeneratorBase` | Check if currently processing |
| `void Work()` | `UMAGeneratorBase` | Process one frame of work |
| `void UpdateAvatar(UMAData umaData)` | `UMAGeneratorBase` | Update a single avatar |
| `bool GenerateSingleUMA(UMAData data, bool fireEvents)` | `UMAGeneratorBuiltin` | **THE core single-character generation call** |
| `void UpdateSlots(UMAData data)` | `UMAGeneratorBuiltin` | Process slot data |
| `void UpdateUMAMesh(bool updatedAtlas)` | `UMAGeneratorBuiltin` | Rebuild combined mesh |
| `void UpdateUMABody(UMAData umaData)` | `UMAGeneratorBuiltin` | Apply skeleton, create Avatar, bind Animator |
| `void PreApply(UMAData umaData)` | `UMAGeneratorBuiltin` | Pre-apply DNA pass |
| `void UMAReady(bool fireEvents)` | `UMAGeneratorBuiltin` | Finalize and fire events |
| `void OnDirtyUpdate()` | `UMAGeneratorBuiltin` | Main frame update — processes dirty queue |
| `static Avatar CreateAvatar(UMAData, UmaTPose)` | `UMAGeneratorBase` | Create a Mecanim Avatar |
| `static Avatar CreateGenericAvatar(UMAData)` | `UMAGeneratorBase` | Create a generic Avatar |

---

## 4. `UMAAvatarBase`

**Namespace:** `Il2CppUMA`
**Inherits:** `MonoBehaviour`

Base class for `DynamicCharacterAvatar` and `UMADynamicAvatar`.

### Key Properties

| Type | Name | Notes |
|------|------|-------|
| `UMAContext` | `context` | The UMA context singleton |
| `UMAData` | `umaData` | This character's runtime data |
| `UMARecipe` | `umaRecipe` | The recipe |
| `UMAGeneratorBase` | `umaGenerator` | Generator reference |
| `RaceData` | `umaRace` | The race |
| `RuntimeAnimatorController` | `animationController` | Animator controller |

### Key Events

| Name | Type |
|------|------|
| `CharacterCreated` | `UMADataEvent` |
| `CharacterUpdated` | `UMADataEvent` |
| `CharacterDestroyed` | `UMADataEvent` |
| `CharacterBegun` | `UMADataEvent` |

### Key Methods

| Signature | Notes |
|-----------|-------|
| `void Initialize()` | Set up references |
| `void Load(UMARecipeBase umaRecipe)` | Load a recipe |
| `void Load(UMARecipeBase umaRecipe, UMARecipeBase[] additional)` | Load with additionals |
| `void UpdateSameRace()` | Rebuild without race change |
| `void UpdateNewRace()` | Rebuild with a new race |
| `void Hide()` / `void Show()` | Toggle visibility |

---

## 5. `UMASkeleton`

**Namespace:** `Il2CppUMA`

Provides bone manipulation for UMA characters. All bone access is by **name hash**
(use `UMASkeleton.StringToHash(boneName)` to convert).

### Key Methods

| Signature | Notes |
|-----------|-------|
| `static int StringToHash(string name)` | Convert bone name → hash |
| `void BeginSkeletonUpdate()` / `void EndSkeletonUpdate()` | Frame skeleton editing |
| `void Set(int nameHash, Vector3 position, Vector3 scale, Quaternion rotation)` | Set full transform |
| `void SetPosition(int nameHash, Vector3 position)` | Set bone position |
| `void SetPositionRelative(int nameHash, Vector3 delta, float weight)` | Relative offset |
| `void SetScale(int nameHash, Vector3 scale)` | Set bone scale |
| `void SetScaleRelative(int nameHash, Vector3 scale, float weight)` | Relative scale |
| `void SetRotation(int nameHash, Quaternion rotation)` | Set bone rotation |
| `void SetRotationRelative(int nameHash, Quaternion rotation, float weight)` | Relative rotation |
| `void Lerp(int nameHash, Vector3 pos, Vector3 scale, Quaternion rot, float weight)` | Lerp towards target |
| `void Morph(int nameHash, Vector3 pos, Vector3 scale, Quaternion rot, float weight)` | Morph towards target |
| `bool Reset(int nameHash)` | Reset bone to bind pose |
| `void ResetAll()` | Reset all bones |
| `Vector3 GetPosition(int nameHash)` | Get bone position |
| `Vector3 GetScale(int nameHash)` | Get bone scale |
| `Quaternion GetRotation(int nameHash)` | Get bone rotation |
| `Transform GetBoneTransform(int nameHash)` | Get Transform by hash |
| `Transform GetBoneTransform(string boneName)` | Get Transform by name |
| `bool HasBone(int nameHash)` | Check bone exists |

---

## 6. `SlotData`

**Namespace:** `Il2CppUMA`

Represents a single mesh slot (e.g. body, hair, shirt, pants).

### Key Properties

| Type | Name | Notes |
|------|------|-------|
| `SlotDataAsset` | `asset` | The slot's asset definition |
| `List<OverlayData>` | `overlayList` | Texture overlays applied to this slot |
| `String` | `slotName` | Human-readable name |
| `Int32` | `skinnedMeshRenderer` | Renderer index |
| `List<String>` | `tags` | Tags for filtering |
| `UInt32` | `meshHideMask` | Bit mask for hiding parts |
| `UMARendererAsset` | `rendererAsset` | Renderer config |

### Key Methods

| Signature | Notes |
|-----------|-------|
| `SlotData Copy()` | Deep copy |
| `void AddOverlay(OverlayData overlay)` | Add a texture overlay |
| `List<OverlayData> GetOverlayList()` | Get all overlays |
| `void RemoveOverlay(string overlayName)` | Remove by name |
| `bool HasTag(string tag)` | Check tag |

---

## 7. `OverlayData`

**Namespace:** `Il2CppUMA`

Texture overlay applied to a mesh slot (e.g. skin texture, tattoo, clothing detail).

### Key Properties

| Type | Name | Notes |
|------|------|-------|
| `OverlayDataAsset` | `asset` | The overlay's asset definition |
| `OverlayColorData` | `colorData` | Color/tint |
| `String` | `overlayName` | Name |
| `Texture2D[]` | `textureArray` | Textures per channel |
| `Rect` | `rect` | UV rect in atlas |

### Key Methods

| Signature | Notes |
|-----------|-------|
| `OverlayData Duplicate()` | Deep copy |
| `void SetColor(int channel, Color32 color)` | Set color on channel |
| `Color32 GetColor(int channel)` | Get color |
| `bool HasTag(string tag)` | Check tag |

---

## 8. `RaceData`

**Namespace:** `Il2CppUMA`

Defines a UMA race (e.g. HumanMale, HumanFemale).

### Key Properties

| Type | Name | Notes |
|------|------|-------|
| `String` | `raceName` | Name of the race |
| `UMARecipeBase` | `baseRaceRecipe` | Default recipe |
| `String[]` | `wardrobeSlots` | Available wardrobe slot names |
| `UmaTPose` | `TPose` | T-pose definition |
| `ExpressionSet` | `expressionSet` | Facial expression set |
| `Single` | `raceHeight` / `raceRadius` / `raceMass` | Physical defaults |
| `DnaConverterBehaviour[]` | `dnaConverterList` | DNA converter pipeline |
| `List<String>` | `tags` | Race tags |

### Key Methods

| Signature | Notes |
|-----------|-------|
| `UMADnaBase[] GetDefaultDNA()` | Get default DNA values |
| `OverlayColorData[] GetDefaultColors()` | Get default colors |
| `bool IsCrossCompatibleWith(RaceData other)` | Cross-compatibility check |

---

## 9. Mesh Combination (Static Utilities)

### `SkinnedMeshCombiner`

| Signature | Notes |
|-----------|-------|
| `static void CombineMeshes(UMAMeshData target, CombineInstance[] sources, BlendShapeSettings, UMARecipe, int currentRenderer)` | Combine mesh slots into one SkinnedMesh |
| `static UMAMeshData ShallowInstanceMesh(UMAMeshData source, BitArray[] triangleMask)` | Shallow clone with triangle masking |
| `static bool BakeBlendShape(...)` | Bake blendshapes |

### `UMADefaultMeshCombiner`

| Signature | Notes |
|-----------|-------|
| `void UpdateUMAMesh(bool updatedAtlas, UMAData umaData, int atlasResolution)` | Full mesh update |
| `void BuildCombineInstances()` | Prepare combine instances |
| `void MakeRenderer(...)` | Create a SkinnedMeshRenderer |
| `void ApplyMeshModifiers()` | Apply post-processing |

---

## Character Generation Pipeline

The full flow from "I want a character" to "visible mesh in scene":

```
1. DynamicCharacterAvatar.Start()
   └── InitialStartup()
       └── BuildFromComponentSettings() or BuildFromStartingFileOrRecipe()

2. DynamicCharacterAvatar.BuildCharacter(restoreDNA, skipBundleCheck, useBundleParam)
   ├── Assembles recipe from wardrobe, DNA, colors
   └── Calls UMAData.Dirty() → marks isMeshDirty, isTextureDirty, etc.

3. UMAData.Dirty()
   └── UMAGeneratorBase.addDirtyUMA(this)
       └── Character added to umaDirtyList queue

4. UMAGeneratorBuiltin.OnDirtyUpdate()  (runs each frame)
   └── Picks next UMAData from umaDirtyList
       └── GenerateSingleUMA(data, fireEvents=true)

5. GenerateSingleUMA(data, fireEvents)
   ├── PreApply(data) — pre-DNA hooks
   ├── UpdateSlots(data) — process slot definitions
   ├── [Atlas generation — texture merging]
   ├── UpdateUMAMesh(updatedAtlas) — combine meshes
   │   └── UMADefaultMeshCombiner.UpdateUMAMesh()
   │       └── SkinnedMeshCombiner.CombineMeshes()
   │           → Creates/updates SkinnedMeshRenderer on "UMARenderer" child
   ├── UpdateUMABody(data) — skeleton, Avatar, Animator binding
   └── UMAReady(fireEvents)
       └── UMAData.FireCharacterCompletedEvents()
           → Fires CharacterCreated (first time) or CharacterUpdated

6. RESULT: "UMARenderer" child GameObject now has a SkinnedMeshRenderer
   with a combined mesh, materials, and proper bone bindings.
```

**Timing:** Steps 1–3 happen in the same frame. Steps 4–6 happen in subsequent
frames (typically 1–3 frames later, depending on queue size).

---

## Il2Cpp Gotchas

### SkinnedMeshRenderer Detection

In Il2Cpp, `GetComponentsInChildren<Renderer>(true)` often returns **0** for UMA
characters even when the mesh exists. This is because Il2Cpp's type casting doesn't
always recognize the runtime SkinnedMeshRenderer as a managed `Renderer` subclass.

**Workarounds:**

1. Check for a child named `"UMARenderer"` by Transform search
2. Try `GetComponentsInChildren<SkinnedMeshRenderer>(true)` directly
3. Use `component.GetIl2CppType().Name` to identify types by string
4. Access `UMAData.GetRenderers()` if you have a `UMAData` reference

### Script Disabling for Remote Players

When cloning a technician for multiplayer, you must carefully choose which
MonoBehaviours to disable:

**Disable immediately** (prevent AI/gameplay):
- `Technician` — AI state machine, tries to navigate
- `AICharacterControl` — NavMesh pathfinding
- `ThirdPersonCharacter` — Locomotion controller
- `NavMeshAgent` — Navigation component (not a MonoBehaviour — use `.enabled = false`)
- IK Rig components — `HeadAim`, `LeftHandIK`, `RightHandIK`

**Keep alive for prefab instantiation** (needed for mesh generation):
- Any component with `UMA`, `DynamicCharacter`, `Avatar`, `Generator` in type name
- `Animator` — needed for Avatar binding
- Any `Renderer` subclass

**Disable after mesh appears** (no longer needed):
- All UMA components — mesh is baked, don't need regeneration

### Type Name Detection

In Il2Cpp, `mb.GetType().Name` often returns `"Component"` for stripped types.
Use `mb.GetIl2CppType().Name` instead for accurate type names:

```csharp
foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
{
    string typeName = mb.GetIl2CppType().Name;  // ← use this
    // NOT: mb.GetType().Name                    // ← unreliable in Il2Cpp
}
```

---

## Adding UMA_Core to the Project

To reference UMA types directly in the C# mod:

Add to `DataCenterModLoader.csproj`:

```xml
<!-- UMA Core (needed for DynamicCharacterAvatar, UMAData, UMAGenerator) -->
<Reference Include="Il2CppUMA_Core">
  <HintPath>$(MelonLoaderDir)\Il2CppAssemblies\Il2CppUMA_Core.dll</HintPath>
  <Private>false</Private>
</Reference>
```

Then use:

```csharp
using Il2CppUMA;
using Il2CppUMA.CharacterSystem;

// Find the generator singleton
var generator = UMAGeneratorBase.FindInstance();

// Access UMAData on a character
var umaData = go.GetComponent<UMAData>();
if (umaData != null && umaData.isOfficiallyCreated)
{
    var renderers = umaData.GetRenderers();
    // renderers[0] is the SkinnedMeshRenderer
}

// Force a rebuild
var avatar = go.GetComponent<DynamicCharacterAvatar>();
avatar?.ForceUpdate(true, true, true);
```

---

## Related Game Types (Assembly-CSharp.dll)

| Type | Notes |
|------|-------|
| `MainGameManager.techniciansPrefabs` | Array of UMA technician prefab GameObjects |
| `TechnicianManager.instance` | Singleton — `.technicians` list has live characters |
| `TechnicianManager.AddTechnician(Technician)` | Register a new technician |
| `Technician` | MonoBehaviour on each technician — has `technicianID`, `technicianName`, `salary` |
| `HRSystem.ButtonConfirmHire()` | Instantiates a prefab and lets UMA generate |
| `LoadingScreen.LoadPlayerAndNPCDataWithDelay()` | Restores technicians from save |

---

## Available UMA Assemblies

| DLL | Contents |
|-----|----------|
| `Il2CppUMA_Core.dll` | Core types: DynamicCharacterAvatar, UMAData, UMAGenerator, SlotData, OverlayData, RaceData, UMASkeleton |
| `Il2CppUMA_Content.dll` | Content pipeline: importers, editors, asset processing |
| `Il2CppUMA_Examples.dll` | Example scripts and utilities |
# SULFUR Damage Numbers Mod

BepInEx 5 plugin for SULFUR. Displays damage numbers for player-executed attacks.

## v1.1.0 vanilla renderer

`Renderer = Vanilla` renders through SULFUR's own damage text instead of the mod's TextMeshPro
views. Implementation notes:

- The game keeps that damage text as a serialized prefab reference on
  `PerfectRandom.Sulfur.Gameplay.DamageTracker`, which only exists on the dummy unit prefabs. There
  is no code-reachable asset for it.
- `VanillaDamageText` therefore loads a dummy unit prefab through the game's own
  `UnitSO.FetchAndLoadUnitLoader()`, takes an extra Addressables reference so the asset survives the
  game's own releases, and reads `damageTextInstancePrefab` plus the ten damage-type colors from the
  `DamageTracker` on that prefab. `UnitIds.TargetDummy` is tried first because on 0.18.5 only the
  deployable target dummy (`Unit_Enemy_Target_Dummy`) carries a `DamageTracker`; the shooting-range
  `UnitIds.TestDummy` does not and is kept only as a fallback.
- `VanillaDamageNumberView` pools clones of that prefab. The clone stays at its parent's pivot and
  `DamageTextInstance.Show` is called without a start position, so the prefab's Animator is the only
  thing that moves or fades the text. The parent is pointed at the camera, matching what
  `DamageTracker` does with the player position.
- All references to `PerfectRandom.Sulfur.Gameplay` and to Addressables live in those two files.
  Every call into them from the manager is wrapped; a failure disables only this renderer and falls
  back to `ScreenSpace`.
- The damage pipeline is unchanged. The renderer still receives events from the existing
  `Unit.CreateDamageLog` postfix and still passes through the display mode, the filters, the
  per-frame spawn budget and the crowding limiter.
- Damage numbers are positioned at the target's overhead point like the other renderers, not at the
  hit point. `Unit.CreateDamageLog` does not carry a collision point.
- The vanilla pool is capped at 64 active numbers because every number is a clone with its own
  Animator and 3D TextMeshPro.

New settings:

```ini
[Visual]
Renderer = Vanilla
VanillaUseDamageTypeColors = true
VanillaScale = 1.0
```

## v1.1.0 status effect damage filter

Damage over time from player-applied status effects was never displayed. The game builds it in
`PeriodicModifier.CreateDamageSourceData`:

```csharp
DamageSourceData result = new DamageSourceData { name = "PeriodicModifier", sourceUnit = unit };
if (id != EntityAttributes.None) result.sourceUnit = unit.UnitThatAppliedStatus(id);
```

No `isPlayer`, no `sourceWeapon`, no `melee`. `IsPlayerExecutedAttack` rejected it on the very first
`sourceData.isPlayer` check, so only direct weapon damage was shown - and a weapon's own damage type
is usually `Normal`, which is why every number looked white while the target dummies showed colors.

`sourceUnit` is the unit that applied the status, so it is a reliable player marker here. The filter
now accepts an event when either `sourceData.isPlayer` is set or `sourceData.sourceUnit` is the
player, and gates the status-only case behind `[Filter] ShowStatusEffectDamage` (default `true`).
Note that when `PeriodicModifier` is created with `EntityAttributes.None`, `sourceUnit` is the unit
*taking* the damage rather than the applier; that case still resolves to a non-player unit for NPC
targets and is rejected as before.

## v1.0.8 - 1.0.12 performance and stability notes

These releases rebuild the renderer from the old `OnGUI` / per-hit `GameObject`
approach into a fixed-cap, pooled, low-allocation TextMeshPro system. Behavior to
keep in mind:

- Keeps the two existing display modes only: `PerTargetTotal` and `PerHit`.
- ScreenSpace and WorldTextMeshPro both use object pools; when a pool is full the
  oldest active number is reused instead of allocating a new one.
- Adds a spawn-time crowding limiter that can skip newly covered numbers. It is a
  visual filter only: it never merges damage or changes mode aggregation.
- Adds a per-frame spawn budget (`MaxNumbersPerFrame`) to protect Unity's GC
  during extreme hit spam.
- Uses one shared font material per renderer instead of one material per number.
- Screen fading uses `CanvasRenderer.SetAlpha`; world fading is quantized into a
  few steps so the TMP mesh is not rebuilt every frame.
- The damage-log Harmony postfix uses a strongly typed `Hitmesh.Data` parameter,
  reads `GetShapeMultiplier()` directly (no reflection on the hot path), and has a
  `[ThreadStatic]` reentrancy guard.
- Outline is shader-independent: each number is four same-size black copies at
  small diagonal offsets behind one colored face (the TMP SDF outline path does
  not display reliably with the game's font material).

Current performance defaults:

```ini
[Performance]
MaxActiveNumbers = 160
MaxNumbersPerFrame = 32
EnableCrowdingProtection = true
CrowdingRadius = 36
MaxNearbyNumbers = 5
```

Set `EnableCrowdingProtection = false` to restore the old visibility behavior.
Hard limits: `MaxActiveNumbers` / `MaxNumbersPerFrame` 1-256, `CrowdingRadius`
8-120, `MaxNearbyNumbers` 1-32.

## v1.0.6 notes

- Keeps the old-style MSBuild `.csproj` format.
- Keeps the Unity module references needed by Unity 6000 and the current SULFUR build:
  - `UnityEngine.TextRenderingModule`
  - `UnityEngine.IMGUIModule`
  - `UnityEngine.InputLegacyModule`
- Adds deterministic PerHit spread for same-frame multi-hit attacks such as shotguns.
- Keeps weakspot colors based on `Hitmesh.Data.GetShapeMultiplier()`:
  - red: `>= 1.5`
  - orange: `>= 1.0`
  - yellow: `>= 0.75`
  - white: no multiplier or normal damage fallback
- Adds `MaxActiveNumbers` to avoid UI spam in extreme PerHit cases.

## Recommended PerHit spread settings

```ini
[Visual]
ScreenJitter = 26
PerHitStackScreenSpread = 7.5
PerHitStackWorldSpread = 0.06
```

For slightly more separation:

```ini
PerHitStackScreenSpread = 9
```

For tighter display:

```ini
PerHitStackScreenSpread = 4
```

## Build

Open `SulfurDamageNumbersMod.csproj` in Visual Studio and build as a .NET Framework 4.7.2 class library.

Copy the compiled DLL to:

```text
BepInEx/plugins/SulfurDamageNumbersMod/SulfurDamageNumbersMod.dll
```

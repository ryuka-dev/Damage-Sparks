# SULFUR Damage Numbers Mod

BepInEx 5 plugin for SULFUR. Displays damage numbers for player-executed attacks.

## v1.1.1 damage pipeline

- `Unit.ReceiveDamage` calls `CreateDamageLog` before it decides whether the damage is applied:
  protected NPCs, damage disabled in the settings, healing ally bullets and same-faction hits are
  logged and then cancelled. `DamageLogPatch` therefore records a hit in the `CreateDamageLog` postfix
  and shows it only when the enclosing `ReceiveDamage` returns `true`, which it does when it applied
  the damage (also for `CannotDie` units, whose health is refilled right after, and for a 0-damage
  death bounce, which `MinDamageToShow` filters).
- `ReceiveDamage` nests (Crusader deals its Holy damage from `onDamageRecieved`, inside the outer
  call). Every call has its own slot on a stack, keyed by the prefix's `__state`; the postfix and a
  finalizer cut the stack back to that slot, so an exception cannot leave a stale slot. Hits are
  numbered in the order they are logged and shown in that order in `LateUpdate`, so the weapon hit
  always comes before the Holy hit it caused.
- Status damage over time is recognised by `DamageSourceData.states.isPeriodicModifier`, the flag
  `PeriodicModifier.CreateDamageSourceData` sets.
- In `PerTargetTotal` each number shown on a target in one flush takes the next row; row N is drawn N
  text lines higher. Rows count only numbers that were shown, and crowding protection tests the
  position the number will actually occupy. Line heights are measured from the first view of each
  renderer.
- The screen canvas is only shown while `GameManager.gameState` is `Running`. The pause menu, the
  inventory and dialogues use `Paused`, which stops time, so hidden numbers keep their remaining
  lifetime; cinematics leave time running. World and vanilla numbers are scene objects and are not
  hidden. While the state is `Loading`, every number is stopped and the queues are emptied on every
  frame.
- `VanillaDamageText` waits for a `GameManager` before it loads the dummy unit, because
  `UnitSO.FetchAndLoadUnitLoader` goes through `GameManager.currentLoadedUnits`. Treating that wait as a
  failed candidate used to disable the vanilla renderer for the whole session when it was selected at
  startup.
- Vanilla clones are switched off once the longest clip of their Animator has played (1.0 s on
  1.0.5); the text is fully transparent by then. Crowding uses the animated text position.
- Units that carry the game's own `DamageTracker` (the target dummies) are skipped, because the game
  already draws every hit on them.

## v1.1.0 vanilla renderer

`Renderer = Vanilla` renders through SULFUR's own damage text instead of the mod's TextMeshPro
views. Implementation notes:

- The game keeps that damage text as a serialized prefab reference on
  `PerfectRandom.Sulfur.Gameplay.DamageTracker`, which only exists on the dummy unit prefabs. There
  is no code-reachable asset for it.
- `VanillaDamageText` therefore loads a dummy unit prefab through the game's own
  `UnitSO.FetchAndLoadUnitLoader()`, takes an extra Addressables reference so the asset survives the
  game's own releases, and reads `damageTextInstancePrefab` plus the nine damage-type colors from the
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

## v1.1.0 weakpoint color for damage without a hitbox

`Hitmesh.Data.GetShapeMultiplier()` maps parts as `Eye 1.5`, `Head 1.0`, `Thorax 0.75`,
`Body 0.5`, `Groin 0.5`, `Arm 0.25`, `Leg 0.25`, `Block 0`, `Custom 1.0` - and `None 1.0`.

`Parts.None` means the damage carried no hitbox information; melee, explosions and status damage
over time all pass `Hitmesh.Data.Default`. Because it returns the same 1.0 as a head shot, those
events were shown in the head shot color. The postfix now reports `Parts.None` as `0`, which the
existing "no multiplier available" branch renders white.

Measured on 0.18.5 over 455 accepted weapon hits: `Head 54.9%`, `Thorax 14.1%`, `Arm 12.3%`,
`Eye 8.1%`, `Body 7.9%`, `Leg 2.6%`, and `None` never occurred. The game always resolves a real
body part for weapon hits, so this change only affects damage that has no hitbox to begin with.

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
PerHitStackScreenSpread = 12.8
PerHitStackWorldSpread = 0.06
```

These are the defaults. The spread is capped at 42 pixels and 0.36 world units per number.

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
Machine-specific paths live in `Directory.Build.props`; each can be overridden on the command line.

Building with `-p:DeployToSulfurProfile=true` copies the DLL, renamed to `DamageSparks.dll`, into
`Thunderstore/` and into the Gale profile's `plugins/ryuka_labs-DamageSparks/` folder together with
`lang/`. That is the folder the mod manager already owns, so the profile never holds two copies of the
same plugin GUID. When the mod manager has disabled that package (its files renamed to `*.old`), the
profile copy is skipped.

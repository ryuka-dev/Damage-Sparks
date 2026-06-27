# SULFUR Damage Numbers Mod

BepInEx 5 plugin for SULFUR. Displays damage numbers for player-executed attacks.

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

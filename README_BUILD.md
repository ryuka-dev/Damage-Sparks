# SULFUR Damage Numbers Mod

BepInEx 5 plugin for SULFUR. Displays damage numbers for player-executed attacks.

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

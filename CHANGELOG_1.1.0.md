# Damage Sparks 1.1.0

- Added `Renderer = Vanilla`, which draws damage numbers with SULFUR's own damage text, the one the target dummies use, including its original animation, font, fly-out direction and critical-hit variant.
- The vanilla renderer reuses the game's prefab instead of imitating it. The prefab and its damage-type colors are read from a dummy unit prefab loaded through the game's own unit loader, so no assets are shipped with this mod.
- Added `VanillaUseDamageTypeColors` (default `true`). `true` uses the game's damage-type colors exactly like the target dummies; `false` keeps this mod's weakpoint multiplier colors on the vanilla animation.
- Added `VanillaScale` (default `1.0`) to resize the vanilla damage text.
- The default configuration is unchanged: `Renderer = ScreenSpace` still produces the previous look.
- The vanilla renderer keeps every existing filter, display mode, per-frame spawn budget and crowding limit. It renders whole numbers only, because the game's damage text prefab does, so `ShowDecimals` has no effect in that mode.
- If the game's damage text prefab cannot be loaded, `Renderer = Vanilla` falls back to `ScreenSpace` and logs a warning instead of showing nothing.
## Status effect damage

- Fixed: damage over time from status effects the player applied, such as burning, poison and electrocution, was never shown. The game builds that damage in `PeriodicModifier` without its player flag and without a weapon, recording only the unit that applied the status, so this mod's player-source filter dropped it. These are exactly the colored numbers the target dummies show.
- **Intentional behavior change.** With the default configuration you will now see extra numbers for burning, poison and electrocution ticks in every renderer, not only in `Vanilla`. Set `ShowStatusEffectDamage = false` under `[Filter]` to restore the previous behavior.
- The new check uses `sourceData.sourceUnit`, which the game sets to the unit that applied the status. It does not match on source names.

- Verified against SULFUR 0.18.5.

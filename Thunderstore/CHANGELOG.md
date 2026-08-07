# Changelog

## 1.1.0

**Vanilla renderer**

- Added `Renderer = Vanilla`, which draws damage numbers with SULFUR's own damage text, the one the target dummies use, including its original animation, font, fly-out direction and critical-hit variant.
- The vanilla renderer reuses the game's prefab instead of imitating it, so nothing extra ships with this mod.
- Added `VanillaUseDamageTypeColors` (default `true`). `true` uses the game's damage-type colors exactly like the target dummies; `false` keeps this mod's weakpoint colors on the vanilla animation.
- Added `VanillaScale` (default `1.0`) to resize the vanilla damage text.
- The vanilla renderer keeps every existing filter, display mode, per-frame spawn budget and crowding limit. It shows whole numbers only, because the game's damage text does, so `ShowDecimals` has no effect there.
- If the game's damage text cannot be loaded, `Renderer = Vanilla` falls back to `ScreenSpace` and writes a warning to the log instead of showing nothing.
- The renderer default is unchanged. `Renderer = ScreenSpace` still produces the previous look.

**Status effect damage**

- Fixed damage over time from status effects the player applied, such as burning, poison and electrocution, never being shown. These are exactly the colored numbers the target dummies display.
- This is an intentional behavior change. With the default configuration you will now see extra numbers for burning, poison and electrocution ticks in every renderer, not only in `Vanilla`. Set `ShowStatusEffectDamage = false` under `[Filter]` to restore the previous behavior.

**Weakpoint colors**

- Fixed damage that carries no hitbox information being colored as a weakpoint hit. Status damage over time and explosion damage used the head shot color and are now white.
- Weapon hits are unaffected. The game always resolves a real body part for them.

**Compatibility**

- Verified against SULFUR 0.18.5.

## 1.0.12

- Corrected the fallback TextMeshPro outline introduced in 1.0.11.
- Removed the enlarged black backing glyph, which changed glyph size and character spacing instead of creating a real border.
- Screen-space numbers now use four same-size black copies with small diagonal pixel offsets behind one colored face.
- World-space numbers use the same same-size offset approach in local billboard space.
- All outline copies remain pooled and reuse the renderer's shared font material.
- Keeps the renderer-change optimization and all 1.0.9 performance limits.

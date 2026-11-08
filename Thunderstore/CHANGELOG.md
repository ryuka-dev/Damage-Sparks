# Changelog

## 1.1.1

**Stacked numbers**

- Fixed numbers drawn exactly on top of each other in `PerTargetTotal` mode when one hit deals more than one damage type to the same enemy. Scroll of Crusader is the common case: its Holy damage lands in the same frame as the weapon hit.
- The weapon hit stays where it was. Each additional number on that enemy in the same frame, such as another damage type or a second weapon, is now drawn one text line higher, in the `ScreenSpace`, `WorldTextMeshPro` and `Vanilla` renderers.
- Merging and colors are unchanged. Shotgun pellets of one damage type still add up to a single number, and each damage type keeps its own number and color.
- `PerHit` mode is unchanged.

**Damage that never happened**

- Fixed numbers appearing for damage the game cancels: hits on protected NPCs such as traders, healing ally bullets, same-faction hits and damage turned off in the game settings. A number now appears only when the game actually takes health off the target.

**Pause and level loading**

- Fixed screen-space numbers freezing on top of the pause menu. They now hide while the game is paused or the inventory or a dialogue is open, and finish once play resumes. `WorldTextMeshPro` and `Vanilla` numbers are part of the 3D scene and stay behind the menu, as before.
- Numbers on screen when a level starts loading are cleared instead of carrying over into the next level.

**Vanilla renderer**

- Fixed `Renderer = Vanilla` never working when it was already selected when the game started. It fell back to `ScreenSpace` for the whole session, so after the first launch it never appeared again.
- Vanilla numbers are switched off as soon as the game's animation has finished, and crowding protection now follows where the text is actually drawn.

**Target dummies**

- Target dummies already show the game's own damage text. Damage Sparks no longer adds a second set of numbers on them.

**Co-op (SULFUR Together)**

- A co-op client now sees numbers for its own hits on enemies the host owns. They show what the host actually applied, including critical hits, body-part colors and Crusader's Holy damage. This needs SULFUR Together 1.7.0 or newer. The adapter that connects the two is included in this package and only loads when SULFUR Together is installed.
- On the host, another player's weapon hits no longer appear as the host's own numbers.
- Damage over time on a client is not shown: the host runs those ticks and does not report them.

**Settings**

- Status damage over time is now recognised by the game's own marker. Other player-owned damage without a weapon, such as reflected thorns damage, now follows `ShowPlayerOwnedSecondaryEffects` instead of `ShowStatusEffectDamage`. With the default settings both are shown, as before.
- Corrected several setting descriptions: the color rules (head hits are orange, so `TreatOneMultiplierAsNormal` turns head hits white), which settings apply to which renderer and mode, the 64-number limit of the `Vanilla` renderer and the spread limits.
- Added Traditional Chinese (zh-TW) setting names for SULFUR Config.

**Compatibility**

- Verified against SULFUR 1.0.5.

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

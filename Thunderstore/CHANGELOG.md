# Changelog

## 1.1.1

**Stacked numbers**

- Fixed numbers drawn exactly on top of each other in `PerTargetTotal` mode when one hit deals more than one damage type to the same enemy. Scroll of Crusader was the usual cause: its Holy damage lands in the same frame as the weapon hit.
- The weapon hit stays where it was. Anything else on that enemy in the same frame (another damage type, a second weapon) goes one line higher. This works in every renderer.
- Shotgun pellets of one damage type still add up to a single number, and colors and `PerHit` mode haven't changed.

**Cancelled damage**

- Hits the game cancels no longer show a number. That covers protected NPCs such as traders, healing ally bullets, same-faction hits and damage turned off in the game settings.

**Pause and level loading**

- Fixed screen-space numbers freezing on top of the pause menu. They now hide while the game is paused or the inventory or a dialogue is open, and carry on when you get back. `WorldTextMeshPro` and `Vanilla` numbers are part of the 3D scene, so they stay behind the menu as before.
- Numbers on screen when a level starts loading are cleared instead of carrying over into the next level.

**Vanilla renderer**

- Fixed `Renderer = Vanilla` not working if it was already set when the game started. You got `ScreenSpace` for the whole session instead, so once it was saved in the config it never showed up again.
- Vanilla numbers now turn off when their animation ends, and crowding protection goes by where the text is on screen.

**Target dummies**

- Target dummies already show the game's own damage text, so Damage Sparks no longer adds a second set of numbers on them.

**Co-op (SULFUR Together)**

- Clients now see numbers for their own hits on enemies. The amounts come from the host, crits and Crusader's Holy damage included, and the colors work as usual. This needs SULFUR Together 1.7.0 or newer. The adapter that connects the two mods is in this package and only loads if you have SULFUR Together.
- The host no longer sees another player's weapon hits as its own numbers.
- Clients don't get numbers for damage over time, because the host runs those ticks and doesn't report them.

**Settings**

- Player-owned damage without a weapon, such as reflected thorns, now follows `ShowPlayerOwnedSecondaryEffects` instead of `ShowStatusEffectDamage`. Both are on by default, so nothing changes unless you turned one off.
- Setting descriptions now say which renderer and mode each one applies to, and list the limits that weren't documented. `TreatOneMultiplierAsNormal` was described wrongly: head hits are 1.0x, so it turns them white.
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

# Damage Sparks

Floating damage numbers for SULFUR.

Damage Sparks shows damage numbers when the player damages enemies with guns, shotguns, thrown weapons, melee weapons, or other player-driven attacks.

Numbers are colored by the body part you hit: eyes red, heads orange, the thorax yellow, the body and limbs white. The optional vanilla renderer uses the game's damage-type colors instead, unless you turn that off.

This mod does not change damage, weapon balance, enemy stats, loot, or progression. It only adds visual combat feedback.

## Features

- Floating damage numbers for player-caused damage
- Supports guns, shotguns, melee weapons, thrown weapons, and player-owned attack effects
- Hits the game cancels, like shooting a trader, show nothing
- Damage over time from player-applied burning, poison and electrocution
- Body-part color feedback
- Two damage display modes
- Optional vanilla renderer that uses SULFUR's own target-dummy damage text
- Configurable number spread
- Numbers stay out of the way of the pause menu, the inventory and level loads
- SULFUR Config support with localized settings

## Damage Display Modes

Damage Sparks has two display modes.

You can change the mode in:

```text
BepInEx/config/ryuka.sulfur.damagenumbers.cfg
```

or through SULFUR Config if installed.

### Per Target Total

This mode combines damage dealt to the same enemy within the same attack frame.

Shotgun example:

If one shotgun blast fires multiple pellets and 10 pellets hit the same enemy, the mod shows one damage number with the total damage dealt to that enemy.

If the same shotgun blast hits two enemies, the mod shows two damage numbers:

* one total number above the first enemy
* one total number above the second enemy

This mode is cleaner and easier to read, especially for shotguns and other multi-hit weapons.

There's no long delay window (like 200 ms), so separate hits from fast-firing weapons don't get merged.

Each damage type gets its own number. If a hit also deals a second type, like the Holy damage from Scroll of Crusader, that number goes one line above the weapon hit. A second weapon hitting in the same frame stacks the same way.

### Per Hit

This mode shows one number for each hit.

Shotgun example:

If 15 pellets from one shotgun blast hit enemies, the mod shows 15 damage numbers.

This mode gives more detailed feedback, but it can be visually busy with shotguns or high pellet-count weapons. Per Hit mode has extra number spread so shotgun pellet numbers do not overlap too tightly.

## Renderers

`Renderer` decides how a damage number is drawn. The display mode and the filters work the same way in
every renderer.

```ini
Renderer = ScreenSpace
```

* `ScreenSpace` - the default look of this mod: pooled screen-space text with a black outline.
* `WorldTextMeshPro` - the same numbers as 3D world-space text.
* `Both` - both of the above at once, mainly for testing.
* `Vanilla` - SULFUR's own damage text, the one the target dummies use.

### Vanilla renderer

`Renderer = Vanilla` does not imitate the game's damage text. It uses the game's actual damage text
prefab, so the animation, font, fly-out direction and the critical-hit variant are exactly what you
see when you shoot a target dummy.

```ini
Renderer = Vanilla
VanillaUseDamageTypeColors = true
VanillaScale = 1.0
```

* `VanillaUseDamageTypeColors = true` uses the game's damage-type colors, like the target dummies.
* `VanillaUseDamageTypeColors = false` keeps this mod's weakpoint colors on the vanilla animation.
* `VanillaScale` resizes the text. `1.0` is the size the target dummies use.

The game's animation decides how long a number stays and how it moves, so `Lifetime` and the font
size, float speed and screen jitter settings of the other renderers have no effect here. In Per Hit
mode, `RandomJitter` and `PerHitStackWorldSpread` still move the starting point. The game's damage text
shows whole numbers only, so `ShowDecimals` has no effect either. At most 64 vanilla numbers are on
screen at once, whatever `MaxActiveNumbers` says.

If the game does not provide the prefab, this renderer falls back to `ScreenSpace` and writes a
warning to the log.

## Weakpoint Colors

Damage number color is based on the multiplier of the body part you hit.

* Eyes (1.5x): red
* Heads (1.0x): orange
* Thorax (0.75x): yellow
* Body and limbs (0.5x and less): white
* Damage with no body part, such as status effects and explosions: white

`TreatOneMultiplierAsNormal = true` turns head hits white as well.

In Per Target Total mode, combined damage uses the highest multiplier color by default.
This makes it easier to notice when a shotgun blast includes a weakpoint hit.

## Configuration

Config file:

```text
BepInEx/config/ryuka.sulfur.damagenumbers.cfg
```

Important options:

```ini
DamageDisplayMode = PerTargetTotal
Renderer = ScreenSpace
MaxActiveNumbers = 160
```

Available renderers:

```ini
Renderer = ScreenSpace
Renderer = WorldTextMeshPro
Renderer = Both
Renderer = Vanilla
```

Available display modes:

```ini
DamageDisplayMode = PerTargetTotal
DamageDisplayMode = PerHit
```

Recommended setting for normal gameplay:

```ini
DamageDisplayMode = PerTargetTotal
Renderer = ScreenSpace
```

Recommended setting for detailed hit testing:

```ini
DamageDisplayMode = PerHit
ScreenJitter = 26
PerHitStackScreenSpread = 12.8
```

`ScreenJitter`, `RandomJitter` and the two `PerHitStack` spreads only apply in Per Hit mode.

## Installation

Install with a mod manager, or manually place both DLLs and the `lang` folder here:

```text
SULFUR/BepInEx/plugins/DamageSparks/
```

The `lang` folder holds the translated setting names that SULFUR Config shows.

Start the game once to generate the config file.

## Requirements

* BepInEx 5.4.2305 or compatible BepInEx 5 version
* SULFUR

## Compatibility

Damage Sparks reads the final damage SULFUR applies to a unit and displays it as a number.

Target dummies already show the game's own damage text, so Damage Sparks draws nothing on them.

The mod should be compatible with most gameplay mods unless another mod heavily changes how enemies receive damage.

If a future SULFUR update changes the internal damage methods, this mod may need an update.

### SULFUR Together

In co-op, each player sees numbers for their own hits. If you're the client, the amounts come from the host, so they show up a moment after the hit. You won't get numbers for damage over time, since the host runs those ticks. This needs SULFUR Together 1.7.0 or newer.

With SULFUR Together 1.8.0 or newer, a hit that gets the team mark bonus shows a small `x1.5` or `x1.75` after its number. The number already includes the bonus. SULFUR Together then stops showing the multiplier next to the crosshair, except with `Renderer = Vanilla`, where the game's own damage text can't carry it.

The adapter that connects the two mods is included. If you don't have SULFUR Together, BepInEx skips the adapter and logs this, which you can ignore:

```text
[Error  :   BepInEx] Could not load [Damage Sparks - SULFUR Together Adapter 1.2.0] because it has missing dependencies: com.ryuka.sulfur.together
```

## Source Code

The source code for this mod is available in my GitHub repository.

The repository only contains my original mod source code and packaging text. It does not include SULFUR game files, Unity assemblies, BepInEx binaries, paid assets, or decompiled game source.

If you are another modder, feel free to study how the mod works or use it as a reference for your own implementation.

GitHub repository:

[https://github.com/ryuka-dev/Damage-Sparks](https://github.com/ryuka-dev/Damage-Sparks)

## Feedback

Thunderstore does not provide a convenient feedback channel.

For bug reports or suggestions, please use NexusMods or GitHub.

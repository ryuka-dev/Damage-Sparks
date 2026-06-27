# Damage Sparks

Floating damage numbers for SULFUR.

Damage Sparks shows damage numbers when the player damages enemies with guns, shotguns, thrown weapons, melee weapons, or other player-driven attacks.

Weakpoint hits use colors based on the game's weakpoint multiplier. Normal damage is shown in white.

This mod does not change damage, weapon balance, enemy stats, loot, or progression. It only adds visual combat feedback.

## Features

- Floating damage numbers for player-caused damage
- Supports guns, shotguns, melee weapons, thrown weapons, and player-owned attack effects
- Uses the game's calculated damage log value
- Weakpoint color feedback
- Normal damage is white
- Two damage display modes
- Configurable number spread
- SULFUR Config support with localized settings
- Does not modify actual damage calculation

## Damage Display Modes

Damage Sparks has two display modes.

You can change the mode in:

```text
BepInEx/config/ryuka.sulfur.damagenumbers.cfg
````

or through SULFUR Config if installed.

### Per Target Total

This mode combines damage dealt to the same enemy within the same attack frame.

Shotgun example:

If one shotgun blast fires multiple pellets and 10 pellets hit the same enemy, the mod shows one damage number with the total damage dealt to that enemy.

If the same shotgun blast hits two enemies, the mod shows two damage numbers:

* one total number above the first enemy
* one total number above the second enemy

This mode is cleaner and easier to read, especially for shotguns and other multi-hit weapons.

It does not use a long delay window such as 200 ms, so it should not incorrectly merge separate hits from fast-firing weapons.

### Per Hit

This mode shows one number for each actual damage hit.

Shotgun example:

If one shotgun blast has 15 pellets that actually hit enemies, the mod shows 15 damage numbers.

This mode gives more detailed feedback, but it can be visually busy with shotguns or high pellet-count weapons. Per Hit mode has extra number spread so shotgun pellet numbers do not overlap too tightly.

## Weakpoint Colors

Damage number color is based on the game's weakpoint multiplier.

* Normal damage: white
* Weakpoint / higher multiplier damage: colored
* If the multiplier cannot be detected, the number falls back to white

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
ScreenJitter = 26
PerHitStackScreenSpread = 12.8
MaxActiveNumbers = 160
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
PerHitStackScreenSpread = 12.8
```

## Installation

Install with a mod manager, or manually place the DLL here:

```text
SULFUR/BepInEx/plugins/DamageSparks/
```

Start the game once to generate the config file.

## Requirements

* BepInEx 5.4.2305 or compatible BepInEx 5 version
* SULFUR

## Compatibility

Damage Sparks reads SULFUR's final damage log value and displays it as a number.

It does not modify the game's damage calculation.

The mod should be compatible with most gameplay mods unless another mod heavily changes enemy damage logging.

If a future SULFUR update changes the internal damage log method, this mod may need an update.

## Source Code

The source code for this mod is available in my GitHub repository.

It is shared for learning, reference, and transparency.

The repository only contains my original mod source code and packaging text. It does not include SULFUR game files, Unity assemblies, BepInEx binaries, paid assets, or decompiled game source.

If you are another modder, feel free to study how the mod works or use it as a reference for your own implementation.

GitHub repository:

[https://github.com/ryuka-dev/Damage-Sparks](https://github.com/ryuka-dev/Damage-Sparks)

## Feedback

Thunderstore does not provide a convenient feedback channel.

For bug reports or suggestions, please use NexusMods or GitHub.

## v1.0.12

Reworked the damage number rendering system to improve performance and stability. Damage numbers now use pooled TextMeshPro objects instead of `OnGUI`, excessive overlapping numbers are automatically suppressed, renderer updates have been optimized, and the text outline and default font size have been improved. These changes significantly reduce frame-rate drops during high-damage or rapid-fire situations and may also address the reported GC stack overflow crashes.

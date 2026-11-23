# The Chosen's Laser Vision

An experimental laser-vision spell for **Blade & Sorcery: Nomad / Quest 3**. Fire focused twin eye beams, sweep your gaze for directional mesh cuts, or charge a broader unstable Overdrive wave.

**Creator: ChiliCheeseDog (CurryLover99 on Nexus). Public release: 0.2.1.**

## Requirements

Install **[Zandatsu 0.7.0 — Slice at Any Angle, by StainlessHalo](https://www.nexusmods.com/bladeandsorcerynomad/mods/8249)** separately for dynamic mesh slicing. Its code/assets are not bundled. The beam can operate without it, but mesh cutting requires it. This is the Nomad release, not a PCVR release.

## Controls

| Action | Control |
| --- | --- |
| Equip | Select The Chosen's Laser Vision in either hand's spell wheel |
| Fire | Hold that hand's trigger |
| Aim | Look in the firing direction |
| Directional cut | Sweep your head while firing |
| Charge Overdrive | Hold trigger + grip on the casting hand for half a second |
| Return to normal | Release grip while keeping trigger held |
| Stop/cancel | Release trigger |

Normal beams continue during charge. Overdrive adds a broader unstable wave, animated rings, tendrils, stronger damage and audio. It can be disabled in mod options.

## Features

- Twin eye beams that stop at scenery, with optional enemy piercing.
- Directional Zandatsu mesh slicing and compatible detached-piece recuts.
- CrushingPressure, Knockback and DamageOnly contact modes.
- Configurable damage, burning, push, range, thickness and core/glow colors, including orange.
- Adjustable near-face clearance and fading stereo smear.
- Animated contact flares/sparks, joined ground scorch trails, residual heat and skin char on supported renderers.
- Heated cut edges with Burning and Zandatsu Fire Cuts Burn enabled.
- Custom stickman eye-beam spell icon and original code-synthesized sounds.

CrushingPressure is the default: localized blunt hits without added whole-body force. Choose Knockback to push enemies away. DamageOnly uses energy damage without pressure or push. The spell does not directly sculpt faces or excavate terrain. Carnage Reborn deformation compatibility is not verified.

## Installation

1. Install Zandatsu 0.7.0 separately.
2. Remove the old PrismLaserVision folder if upgrading. Do not run both versions.
3. Extract the Nomad ZIP and copy TheChosensLaserVision into `Android/data/com.Warpfrog.BladeAndSorcery/files/Mods/`.
4. Check for `Mods/TheChosensLaserVision/manifest.json` and restart Nomad.

Use the game's Mods options. Public 0.2.1 uses synthesized audio only; no user-supplied recordings or animation/video audio are redistributed.

## Creation and credits

**Created and directed by ChiliCheeseDog (CurryLover99 on Nexus):** concept, feature choices, visual/sound direction, iteration, gameplay tuning, testing and screenshots. **Implementation developed with AI assistance using OpenAI Codex.**

**StainlessHalo** created Zandatsu and its mesh-cutting engine. This project integrates with the separately installed mod. Inspired by laser vision in Alan Becker's animations; unofficial fan project, not affiliated with Alan Becker or Warpfrog.

## Status and support

Experimental 0.2.1. Screenshots show the mod in Nomad. Compatibility and performance may vary with other mods and Overdrive. Automated checks and compilation do not replace in-game testing. Report your game/mod versions, contact mode, other relevant mods, normal vs Overdrive behavior and [LaserVision] log messages.

Project source, build instructions and screenshots are being added here. Player downloads are intended for Nexus Mods and mod.io.

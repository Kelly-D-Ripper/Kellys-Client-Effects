# Client Effects & Weather 0.8.4 - candidate

More optional effects. Still one entry in the mod menu.

This candidate adds the approved client-side KROP effects to the complete 0.8.3 pack. Open FX on the map MFD or press Ctrl + F8. All 29 new switches start off, so updating does not suddenly turn everything up to eleven.

- Sound: Additional cockpit/distance filtering for 19 sound families, canopy/open-cabin acoustics, bullet-crack shaping, optional missile recordings and non-repeating aircraft booms.
- Battlefield: Extra fuel fireballs, smoke, flames and embers, optional cook-off audio, and delayed lifeboat beacons. The extra explosions are decorative and do not add damage.
- Ocean: Optional KROP fast-pass spray for fixed-wing aircraft, normally over 400 km/h and below 30 m. Requires Air wake effects and replaces all Boscali aircraft spray/foam wash while enabled. Keep it off for Boscali's rotorcraft and slower-flight effects. Switching off restores your existing Boscali spray preference.

The sound and visual additions are optional. Audio-listener and missile-warning protections are applied while the relevant module is active.

No server plugin is required by the new presentation features. They do not alter ammunition, damage, countermeasure physics or AI controls. NVG changes and general sound-propagation delay remain excluded. Weather/time commands retain their single-player restriction. Manual canopy/ramp controls and parked-AI changes are not part of this candidate.

## Install only when ready to test

Close Nuclear Option. Back up and move the old BepInEx/plugins/UnifiedEffects folder outside plugins, then extract the candidate into the game folder. Keep your existing configs and the separately installed NOAtmoFX folder. Remove duplicate old pack DLLs rather than leaving both versions to fight over the same effects.

The ZIP contains only BepInEx and this README. Its two runtime files are the main UnifiedEffects.dll and the existing small startup helper under BepInEx/patchers. DOORMAN still sees one pack entry.

[NOAtmoFX](https://github.com/paulleonard1314-lang/NoAtmoFX/releases) remains an optional separate download and is not included.

Checked in isolated missions at 3440x1440 and 1920x1080, including NOAtmoFX 1.4.0 and DOORMAN. This is a local candidate, not an installed or published update. Real multiplayer, longer performance runs and your flight/audio check are still outstanding. See VALIDATION-0.8.4.md in the source for the evidence. No FPS improvement is claimed.

[Credits and changes](CREDITS.md) | [Source notes](SOURCE-NOTES.md)

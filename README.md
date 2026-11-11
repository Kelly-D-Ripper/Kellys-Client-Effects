# Client Effects & Weather 0.8.4 - candidate

More optional effects. Still one entry in the mod menu.

This candidate adds the approved client-side KROP effects to the complete 0.8.3 pack. Open FX on the map MFD or press Ctrl + F8. All 29 new switches start off, so updating does not suddenly turn everything up to eleven.

- Sound: Additional cockpit/distance filtering for 19 sound families, canopy/open-cabin acoustics, bullet-crack shaping, optional missile recordings and non-repeating aircraft booms.
- Battlefield: Extra fuel fireballs, smoke, flames and embers, optional cook-off audio, and delayed lifeboat beacons. The extra explosions are decorative and do not add damage.
- Ocean: Optional KROP fast-pass spray for fixed-wing aircraft, normally over 400 km/h and below 30 m. Requires Air wake effects and replaces all Boscali aircraft spray/foam wash while enabled. Keep it off for Boscali's rotorcraft and slower-flight effects. Switching off restores your existing Boscali spray preference.

The sound and visual additions are optional. Audio-listener and missile-warning protections are applied while the relevant module is active.

No server plugin is required by the new presentation features. They do not alter ammunition, damage, countermeasure physics or AI controls. NVG changes and general sound-propagation delay remain excluded. Weather/time commands retain their single-player restriction. Manual canopy/ramp controls and parked-AI changes are not part of this candidate.

## Requirements

**Install BepInEx 5 for Windows x64/Mono first. It is required and is not bundled with this pack.** Use the official [BepInEx 5.4.23.6 release](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6). No separate .NET SDK is needed.

DOORMAN and Blueprinter are optional. The pack's menus work without either. NOAtmoFX is also optional and only needed for Volumetric mode.

## Install only when ready to test

1. Close Nuclear Option. Install the BepInEx loader beside `NuclearOption.exe`, then launch and close the game once so it creates its config and log folders.
2. Back up and move the old `BepInEx/plugins/UnifiedEffects` folder outside `plugins`. Remove duplicate older pack DLLs from other plugin folders too. Standalone KROP, Boscali Summer and Better Night conflict with their included versions; do not leave them loaded alongside the pack.
3. Extract the **client ZIP** into the folder containing `NuclearOption.exe`, merging its `BepInEx` folder. Keep both files: `BepInEx/plugins/UnifiedEffects/UnifiedEffects.dll` and `BepInEx/patchers/UnifiedEffectsStartup.dll`.
4. Keep your existing `BepInEx/config` files and any separately installed `NOAtmoFX` folder.

Do not put the whole ZIP's contents inside `BepInEx/plugins`, and do not use GitHub's source-code download as the playable pack. The ZIP contains only BepInEx and this README. DOORMAN still sees one pack entry.

The updated startup helper keeps the pack owner and its settings discoverable through startup even when no optional mods are installed. It does not require changing BepInEx's configuration.

Open the expanded map in a mission and look for **FX**, normally on the right-hand bezel, or press **Ctrl+F8** with either Ctrl key. Close the separate MET OFFICE console before using the keyboard panel. If the pack is missing, check `BepInEx/LogOutput.log` for `Loading [Client Effects & Weather`, startup errors or incompatible plugins. If DOORMAN is installed, check that it has not disabled the pack.

[NOAtmoFX](https://github.com/paulleonard1314-lang/NoAtmoFX/releases) remains an optional separate download and is not included.

The main DLL was checked in isolated missions at 3440x1440 and 1920x1080, including NOAtmoFX 1.4.0 and DOORMAN. The refreshed startup helper was also checked without companion mods and with DOORMAN's disabled-pack setting. This is a local candidate, not an installed or published update. Real multiplayer, longer performance runs, physical keyboard/visible Ctrl+F8 testing and your flight/audio check are still outstanding. See VALIDATION-0.8.4.md in the source for the evidence. No FPS improvement is claimed.

[Credits and changes](CREDITS.md) | [Source notes](SOURCE-NOTES.md)

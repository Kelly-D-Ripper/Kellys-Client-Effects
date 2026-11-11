# Client Effects & Weather 0.8.7 - candidate

Pick a storm. Get a storm. Preferably before the bloody flight ends.

Volumetric mode now has seven weather choices made for NOAtmoFX: Clear, Scattered, Thick Cloud, Low Cloud/Fog, Heavy Rain, Thunderstorm and Extreme Storm. Each sends actual cloud-model targets and selects matching Boscali precipitation. Enhanced mode keeps the full Boscali catalogue. Simple English and Technical names still work.

Changes take about **five seconds** by default in single player. The prominent transition button cycles 5 / 15 / 60 / 600 seconds. F1 and Cloud Settings also have a duration slider and a switch to restore NOAtmoFX's own timing. The native weather cap is retained; NOAtmoFX receives separate storm severity instead of the weakened native-sky value.

The fast transition adapter is checked against **NOAtmoFX 1.4.0**. It changes only the weather smoothing calculation for our single-player preset and a brief handoff back to mission weather. It does not replace the add-on DLL, save over its settings or change multiplayer timing. Cloud drift retains NOAtmoFX's own wind smoothing. Different add-on versions keep normal timing and disable unverified direct presets. An F11 weather override or another mod's active API weather is reported instead of silently fighting it.

O opens the supported weather page while Volumetric is selected. The old Boscali-only cloud-pattern button is hidden in that mode. Returning to mission weather or changing providers releases our own cloud override.

NOAtmoFX remains a **separate download**, not included here: [NOAtmoFX releases](https://github.com/paulleonard1314-lang/NoAtmoFX/releases).

All 0.8.6 features below are retained.

The retained 0.8.6 features build on 0.8.5, including the accessibility-colour repair, optional NVGs and sound travel. Open FX on the map MFD or press Ctrl + F8. If you use BepInEx Configuration Manager, its F1 menu finds all eight internal modules without adding extra DOORMAN entries.

Ground-roll / takeoff vibration is under **FX > EFFECTS > COCKPIT**. Enable Cockpit shake and Ground-roll / takeoff vibration, then set the ground-roll strength to taste. F1 has the same settings under the Cockpit Feel module. It runs in the cockpit while moving on the ground and stops after takeoff; Comfort motion reduces it.

This pass adds individual switches for recoil, landing and gear jolts, transonic buffet, the sonic-crossing jolt, breathing and cockpit sound layers, each extra voice warning, sun-disc colouring, aurora/sprites/jets, extreme-storm layers, rain streaks/cloud mist and the three ocean-wake layers. Existing master switches and settings still apply. New child switches start on to preserve the behaviour you already selected; optional NVG and sound-travel masters still start off.

Large-round camera shake now works with Bullet cracks sound switched off. NOAtmoFX gains MFD controls for reflected clouds, water lighting and daily cloud growth. Its ten numeric effects also get real F1 checkboxes alongside their existing tuning controls. NOAtmoFX remains a separate download.

The FX MFD panel follows the game's accessibility Primary colour, with Secondary for the weather readout and clock. Changes apply without reopening the panel, including its NOAtmoFX controls.

- Cockpit: optional green, white or amber NVG phosphor, bloom, grain, bright speckles, nuclear-flash speckles, automatic gain and raise/lower animation. These only change the local NVG presentation.
- Camera changes: automatic NVG selection is blocked by default while the KROP component is enabled. Manual NVG selection still works. "Allow game auto-NVG on camera change" restores the game's own behaviour, subject to its Camera Auto NVG preference. The pack does not overwrite that saved preference.
- Sound: optional travel time for supported world sounds and continuous engines, with separate switches and bounded queues/history. Gun-burst fade is optional. Cockpit warnings remain immediate. Existing native explosion propagation is not delayed a second time.

The 0.8.4 additions are retained:

- Sound: Additional cockpit/distance filtering for 19 sound families, canopy/open-cabin acoustics, bullet-crack shaping, optional missile recordings and non-repeating aircraft booms.
- Battlefield: Extra fuel fireballs, smoke, flames and embers, optional cook-off audio, and delayed lifeboat beacons. The extra explosions are decorative and do not add damage.
- Ocean: Optional KROP fast-pass spray for fixed-wing aircraft, normally over 400 km/h and below 30 m. Requires Air wake effects and replaces all Boscali aircraft spray/foam wash while enabled. Keep it off for Boscali's rotorcraft and slower-flight effects. Switching off restores your existing Boscali spray preference.

The sound and visual additions are optional. Audio-listener and missile-warning protections are applied while the relevant module is active.

No server plugin is required by the new presentation features. They do not alter ammunition, damage, countermeasure physics or AI controls. Boscali MET OFFICE weather presets and time controls remain single-player. NOAtmoFX controls follow its host/local multiplayer policy. Manual canopy/ramp controls and parked-AI changes are not part of this candidate.

## Requirements

**Install BepInEx 5 for Windows x64/Mono first. It is required and is not bundled with this pack.** Use the official [BepInEx 5.4.23.6 release](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6). No separate .NET SDK is needed.

DOORMAN and Blueprinter are optional. The pack's menus work without either. NOAtmoFX is also optional and only needed for Volumetric mode.

**F1 requires BepInEx Configuration Manager**, installed separately. The audited version is 19.1. It is not bundled. FX on the map MFD and Ctrl + F8 work without Configuration Manager.

## Install only when ready to test

1. Close Nuclear Option. Install the BepInEx loader beside `NuclearOption.exe`, then launch and close the game once so it creates its config and log folders.
2. Back up and move the old `BepInEx/plugins/UnifiedEffects` folder outside `plugins`. Remove duplicate older pack DLLs from other plugin folders too. Standalone KROP, Boscali Summer and Better Night conflict with their included versions; do not leave them loaded alongside the pack.
3. Extract the **client ZIP** into the folder containing `NuclearOption.exe`, merging its `BepInEx` folder. Keep both files: `BepInEx/plugins/UnifiedEffects/UnifiedEffects.dll` and `BepInEx/patchers/UnifiedEffectsStartup.dll`.
4. Keep your existing `BepInEx/config` files and any separately installed `NOAtmoFX` folder.

Do not put the whole ZIP's contents inside `BepInEx/plugins`, and do not use GitHub's source-code download as the playable pack. The ZIP contains only BepInEx and this README. DOORMAN still sees one pack entry.

The updated startup helper keeps the pack owner and its settings discoverable through startup even when no optional mods are installed. It does not require changing BepInEx's configuration.

Open the expanded map in a mission and look for **FX**, normally on the right-hand bezel, or press **Ctrl+F8** with either Ctrl key. Close the separate MET OFFICE console before using the keyboard panel. If the pack is missing, check `BepInEx/LogOutput.log` for `Loading [Client Effects & Weather`, startup errors or incompatible plugins. If DOORMAN is installed, check that it has not disabled the pack.

[NOAtmoFX](https://github.com/paulleonard1314-lang/NoAtmoFX/releases) remains an optional separate download and is not included.

This is a local candidate, not an installed or published update. See [the effect audit](EFFECT-TOGGLE-AUDIT.md) for the full control inventory and exact validation limits. Real multiplayer, longer performance runs, physical keyboard delivery and human flight/audio acceptance remain separate checks. No FPS improvement is claimed.

NVG auto-gain's real rendering sample and cleanup pass, but its ordinary visible-window operation still needs a flight check; the hidden test window skips normal rendering. It remains off by default.

[Credits and changes](CREDITS.md) | [Source notes](SOURCE-NOTES.md)

# Client Effects & Weather 0.8.3 - single-plugin candidate

One effects entry. The mod menu can stop looking like a shopping receipt.

The complete 0.8.2 effects pack, including grounded runway lighting and NOAtmoFX 1.4 support, now lives in one main DLL. Its eight supporting modules and night-lighting asset are embedded inside it. DOORMAN sees one Client Effects & Weather entry without needing special grouping code.

## Upgrade

1. Close Nuclear Option.
2. Move the old `BepInEx/plugins/UnifiedEffects` folder somewhere outside `BepInEx/plugins` as a backup. Remove any duplicate older copies of the pack from other plugin folders too.
3. Extract this ZIP into the game folder. Keep both its `plugins` and `patchers` files.
4. Keep your existing `BepInEx/config` files. The original effect settings and shortcuts still work.

Do not extract over the old multi-DLL pack and leave its components behind. The new plugin declares them incompatible to prevent duplicate effects.

The download contains:

- `BepInEx/plugins/UnifiedEffects/UnifiedEffects.dll` - the complete effects plugin.
- `BepInEx/patchers/UnifiedEffectsStartup.dll` - the small early loader helper for optional NOAtmoFX lifetime protection. It is not a DOORMAN menu item.
- This README, including credits and original licence notices below.

Open FX on the map MFD or press Ctrl + F8. The effects switches, MET OFFICE, Simple / Technical names and time controls are unchanged. Weather/time commands remain single-player only.

## NOAtmoFX is separate

[Download NOAtmoFX 1.4.0](https://github.com/paulleonard1314-lang/NoAtmoFX/releases/tag/1.4.0) separately and install its DLL and matching shader bundle together. It is not included here. Keep its folder when replacing Unified Effects.

The weather page still offers Vanilla / Enhanced / Volumetric and the add-on's quality and feature controls. NOAtmoFX remains its own DOORMAN entry. Its author documents a one-time defaults reset when upgrading the add-on to 1.4.

## Candidate limits

This is a packaging and startup change, not an FPS claim. Isolated mission tests exercise the effects, settings and the installed DOORMAN's actual inventory. Live multiplayer synchronization, sustained frame-time testing and human flight acceptance across every airfield remain open. The five-lamp approach chase and approximate PAPI are not a surveyed FAA installation.

The pack does not add NVG changes or the general speed-of-sound delay. Cockpit voice warnings remain optional and default off for fresh settings.

[Full credits and changes](CREDITS.md) | [Source notes](SOURCE-NOTES.md)

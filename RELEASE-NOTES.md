# Client Effects & Weather 0.8.3 - single-plugin candidate

One effects entry. The mod menu can stop looking like a shopping receipt.

This is the combined build: runway-grounding corrections and NOAtmoFX 1.4 support are part of the main pack, not extra Unified Effects add-ons.

## Packaging update

- One main effects DLL and one real DOORMAN entry. Eight supporting components and the night-lighting bundle are embedded.
- Tested with the actual installed DOORMAN build, without its earlier custom grouping code.
- Existing effect settings keep their original config files. NOAtmoFX remains separate.
- The small early loader helper stays under BepInEx/patchers and is not a plugin-menu item.

## Included fixes

- Retained native runway edges and removed overlapping added edges.
- Grounded added runway and approach fixtures, corrected runway-end placement from pavement boundaries, and omitted unsafe placements.
- Cleaned up markers, point lights and the temporary lighting budget on disable.
- Added early optional-plugin host protection so NOAtmoFX survives scene changes without a DOORMAN dependency.
- Fixed incomplete settings discovery with the optional add-on installed.
- Fixed cloud-mode preference ownership and prevented the menu's multiplayer preset changes from saving host cloud settings into local config.

## Included effects

- Weather director and fronts, MET OFFICE, plain-English or technical names, time controls and the extreme-storm preset.
- Rain, canopy droplets and runoff, wet terrain, rain atmosphere, lightning, thunder, sun shafts and sky effects. The original Boscali volumetric renderer is excluded.
- Ocean waves, aircraft ocean spray, foam and wakes.
- Pilot body and cockpit motion, kneeboard, visor reflections and glare, cockpit lighting, breathing and airframe sounds. Cockpit voice warnings are toggleable and default off for fresh settings.
- Aircraft and missile contrails, low-flight dust, cockpit/atmospheric muffling, near-miss cracks and sonic booms.
- Cosmetic lifeboats from sinking ships, with optional bobbing and beacons.
- Impact/scorch decals. These do not deform terrain.
- City windows, street and vehicle lighting; runway and approach rows, approximate PAPI, taxiway, hangar, helipad, industrial and port lighting.
- A unified FX / Weather map-MFD panel with category controls and Simple / Technical names. Ctrl + F8 remains available.

NVG changes and the general speed-of-sound delay remain excluded.

## NOAtmoFX - separate download

[NOAtmoFX 1.4.0](https://github.com/paulleonard1314-lang/NoAtmoFX/releases/tag/1.4.0) is not included. Install both its DLL and matching shader bundle. Our weather page detects it and enables Volumetric mode when its API is ready.

Cloud Settings provides quality presets, cloud/shadow/rain/lightning/fog/wet-glass controls, rays, nuclear cloud effects, rainbow, glory, valley fog and mountain clouds, plus cloud-shape controls and transition timing. Boscali following and rain/fog hand-off use NOAtmoFX's own integration API.

Weather/time commands remain single-player. NOAtmoFX owns its multiplayer cloud sync; host-owned settings are read-only while following the host. Our NOAtmoFX controls do not write its config while multiplayer is active; the pack still remembers your own cloud-mode choice.

## DOORMAN

The pack includes its card and stays one entry in compatible DOORMAN builds. NOAtmoFX is the separately installed bolt-on. The startup helper lives under BepInEx/patchers and is not an extra plugin card.

## Install and limits

Close the game and move the old BepInEx/plugins/UnifiedEffects folder outside the plugins directory before extracting this ZIP. Remove duplicate older pack DLLs from other plugin folders. Retain both new BepInEx/plugins and BepInEx/patchers files. Keep existing configs and the separate NOAtmoFX folder.

Pre-release. Live host/client sync, sustained performance and flight acceptance at every airfield are still open. Approach-row strobes are the requested five-lamp chase, not an exact FAA installation.

Credits: Boscali Summer - @awatarnieadekwatny; Better Night - Julmari; KROP - @kctries; optional NOAtmoFX - Paul Leonard. Full effect-by-effect credits and original licence notices accompany the project and download.

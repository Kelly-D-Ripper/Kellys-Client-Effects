# Client Effects & Weather 0.8.2

Nuclear Option still isn't wet or noisy enough.

Weather, cockpit immersion, ocean effects, lights, trails and other shiny distractions. **Pre-release:** if something looks cursed, tell us which aircraft, map and switches did it.

## Install

Close the game. Install BepInEx, then extract this ZIP into your Nuclear Option folder. Keep the included `BepInEx/patchers` file as well as `BepInEx/plugins`. Use one copy of the pack and preserve your existing configuration files.

Open **FX** on the map MFD, or **Ctrl + F8** for the effects panel. The weather page has a prominent Simple / Technical names selector. MET OFFICE weather and time changes are single-player only.

DOORMAN metadata is included; compatible DOORMAN builds show the pack as one entry. NOAtmoFX remains separate.

## What's changed

- Runway-grounding fixes are now included. Native runway edge lights stay; the extra overlapping edges are gone. Added markers follow the ground, and red/green ends use detected pavement boundaries. Unsafe or ambiguous placements are omitted.
- Lighting cleans up properly when disabled. Direction-changing approach rows, taxiways, hangars, helipads and site lighting remain toggleable.
- The optional NOAtmoFX 1.4 integration has Vanilla / Enhanced / Volumetric modes, quality presets, feature switches and cloud transition timing.
- The included startup helper keeps the optional add-on alive through scene changes, without needing DOORMAN to do it.
- Our NOAtmoFX controls do not write its config while multiplayer is active. Host-controlled cloud shape and weather settings are read-only while following the host. Fixed a preset-save path that could save host settings into the player's own config.

## Optional volumetric clouds

[Download NOAtmoFX 1.4.0 separately](https://github.com/paulleonard1314-lang/NoAtmoFX/releases/tag/1.4.0). It is **not included**. Install its matching DLL and `noatmofx` shader bundle together, then restart the game. Its author notes that upgrading to 1.4 resets its own settings to the new defaults once.

Choose **Volumetric** on our weather page. **Cloud Settings** exposes Low / Medium / High / Ultra quality, the individual effects and Boscali follow/yield settings. Clouds move gradually towards the selected weather; the default is ten minutes, adjustable under **Cloud transition minutes**. Fog and nearby rain yield to Boscali when the corresponding settings are enabled. Wet glass and lightning have their own switches if you want to avoid using both versions.

The pack still remembers your own cloud-mode choice. Unsaved add-on changes remain in memory until changed or the game closes. The add-on controls its multiplayer synchronization. This does not make Boscali's MET OFFICE a multiplayer weather controller.

## Still a pre-release

The merged pack has isolated mission and rendering checks. Live two-client weather sync, sustained frame-time testing and visual flight acceptance across every airfield remain open. The five-light approach chase is an artistic adaptation, not a claim of exact FAA ALSF-2 compliance.

The included startup helper protects the shared BepInEx host only when both the Unified weather plugin and NOAtmoFX are discovered. It does not filter plugins or change their settings, and it does not run on headless servers.

[Full credits and changes](CREDITS.md) | [Source notes](SOURCE-NOTES.md)

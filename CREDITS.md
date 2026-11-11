# Credits

This pack is built from the work of several modders, with changes made for Kelly's Client Effects. Thanks to everyone who made their work available.

## Boscali Summer - @awatarnieadekwatny

[Boscali Summer](https://github.com/GrabowMar/Boscali-Summer) provides much of the weather and cockpit immersion in this pack. The upstream MIT notice names GrabowMar as copyright holder.

- **Weather and MET OFFICE:** The weather director, fronts, rain, lightning and sky effects come from Boscali. We extracted and adapted them for the pack, redesigned MET OFFICE, and added plain-English names, time controls and an extreme storm. Weather controls are single-player only. The original Boscali volumetric cloud renderer is not included.
- **Rain:** Canopy droplets and runoff, wet terrain and rain haze come from Boscali. We adapted canopy detection, stopped precipitation above the cloud layer and adjusted the haze and colours. The canopy shader also uses toadstorm's work, credited below.
- **Ocean:** Waves, aircraft spray and foam wakes come from Boscali. We separated spray from the weather and replacement-wave switches and adjusted water restoration so the game's native water can be used.
- **Cockpit:** The pilot body, kneeboard, motion, visor reflections, glare, cockpit lighting, breathing and airframe sounds come from Boscali. We extracted them into individually switchable features and made compatibility and cleanup changes. Cockpit voice warnings can be switched off and default to off for new configurations.
- **Impact marks:** The scorch and impact decals come from Boscali, with added limits and cleanup. They are visual marks, not terrain-deforming craters.

The 0.8.6 control audit adds finer switches to these adapted effects, preserves their master switches, and exposes the private modules to F1 Configuration Manager. Ground-roll strength, individual cockpit sounds and warnings, storm layers, sky phenomena and ocean-wake layers now have their own controls. This changes the pack's controls and cleanup, not the original authorship of the effects.

## Better Night - Julmari

Better Night provides the base for the city windows, street lights, vehicle lights and other night lighting. We integrated its controls, disabled its NVG-specific changes and adjusted street-light size, brightness and visibility at altitude and above clouds.

The complete Better Night build source has not yet been assembled here. See [SOURCE-NOTES.md](SOURCE-NOTES.md).

## KROP - [@kctries](https://github.com/KcTries)

[KROP](https://github.com/KcTries/KROP) provides the following effects through a client-only extraction:

- **Aircraft and missile contrails:** Separate switches, emitter limits and cleanup fixes were added for the pack.
- **Low-flight dust and air wakes:** Extracted with their own controls. Boscali ocean spray remains the default; an optional KROP water-wake mode replaces it while enabled.
- **Lifeboats:** Cosmetic boats appear locally when ships sink. We added limits, cleanup and toggle handling, plus an optional initial beacon delay matching KROP's timing. They are not networked rescue units.
- **Sound:** Cockpit and atmospheric muffling, bullet near-miss cracks and aircraft sonic booms, with controls, cleanup and allocation fixes. The 0.8.4 candidate adds optional filtering for 19 more sound families, read-only canopy/open-cabin acoustics, and optional KROP bullet-crack shaping. Listener and missile-warning protections are adapted as safety fixes.
- **Varied fuel explosions:** The optional fireball, smoke, flame and ember presentation follows KROP's effect. Our implementation copies only allowlisted particle-rendering and audio properties into new local objects. It does not instantiate the original gameplay prefab or copy its damage, physics or network components.
- **Missile boom recordings:** Three KROP recordings are available as an optional sound choice. The detector remains Kelly's bounded local experiment, with the game's original recording available as fallback.

In 0.8.6, near-miss camera shake was separated from the bullet-crack sound switch, so disabling the sound no longer silently disables the shake.

The 0.8.5 candidate adds optional KROP-inspired NVG phosphors, bloom, grain/static, sensor speckles, nuclear-triggered speckles, rendered-brightness gain and raise/lower animation. Kelly's adaptation separates the switches, defaults them off, restores owned rendering state and bounds textures, readbacks and overlay resources. A separate camera-choice guard prevents the game's automatic NVG selection by default without changing the saved game preference.

Optional sound propagation uses Kelly's bounded local event snapshots and continuous-source history, based on KROP's sound-travel concept. It preserves native explosion propagation and excludes warnings and own-cockpit sounds. It is not a verbatim port of every upstream audio path. See FEATURES-0.8.5.md for coverage and differences.

KROP's ammunition, projectile, countermeasure-physics, AI-control and destructive canopy changes remain excluded. Optional non-repeating boom selection is our refinement; the original KROP selector can repeat samples.

## Kelly's additions and changes

- The unified FX and MET OFFICE interface, MFD integration, DOORMAN metadata and optional NOAtmoFX bridge.
- Runway, taxiway, hangar, helipad, industrial and port lighting, including five-light chasing approach rows and approximate PAPI indicators. The row-wide chase is an artistic adaptation of approach lighting, not the individual-flasher pattern of an FAA ALSF-2 installation.
- Street-light tuning, weather and water fixes, canopy compatibility work, effect controls and the integration and bug fixes described above.
- Ground placement and pavement-end detection for the added runway fixtures; native edge lights are retained, ambiguous placements omitted, and lighting resources released on disable.
- Single-plugin packaging, private module loading and embedded night-lighting asset loading, with the existing per-effect configuration identities retained.
- The client startup helper, including standalone plugin-host protection, and NOAtmoFX 1.4 API controls, multiplayer edits without writing the add-on config and protection against saving host settings into local configuration. The standalone add-on renderer remains Paul Leonard's work.

## Other authors and assets

- **toadstorm** - [RainyGlassShader](https://github.com/toadstorm/RainyGlassShader), adapted by Boscali Summer for canopy rain. The recovered shader source, masks, notice and LGPL-2.1 licence are in [third-party/RainyGlassShader](third-party/RainyGlassShader).
- **Paul Leonard** - [NOAtmoFX](https://github.com/paulleonard1314-lang/NoAtmoFX), the optional volumetric-cloud add-on. It is a separate download and is not included in this pack.
- **takareads, SuddenDice, Mediasaur and bart** - recordings used for pilot voice and breathing sounds. See the [pilot voice notices](third-party/notices/PilotVoice-LICENSES.txt).
- **Stereo Surgeon, aust_paul, IENBA, davidbain, greyfeather, ReadeOnly and JovianSounds** - recordings listed in the [cockpit Foley notices](third-party/notices/CockpitFoley-LICENSES.txt), which also document generated audio. The notice covers the wider upstream asset collection, so not every listed clip is necessarily in this pack.
- UI assets, fonts and other third-party components have their own notices in [third-party/notices](third-party/notices).

The original licence notices are included in the repository and the download. This credits file does not replace them. If we've missed or misattributed anyone, please let us know.

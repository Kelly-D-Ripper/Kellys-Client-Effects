# Credits

This pack is built from the work of several modders, with changes made for Kelly's Client Effects. Thanks to everyone who made their work available.

## Boscali Summer - @awatarnieadekwatny

[Boscali Summer](https://github.com/GrabowMar/Boscali-Summer) provides much of the weather and cockpit immersion in this pack. The upstream MIT notice names GrabowMar as copyright holder.

- **Weather and MET OFFICE:** The weather director, fronts, rain, lightning and sky effects come from Boscali. We extracted and adapted them for the pack, redesigned MET OFFICE, and added plain-English names, time controls and an extreme storm. Weather controls are single-player only. The original Boscali volumetric cloud renderer is not included.
- **Rain:** Canopy droplets and runoff, wet terrain and rain haze come from Boscali. We adapted canopy detection, stopped precipitation above the cloud layer and adjusted the haze and colours. The canopy shader also uses toadstorm's work, credited below.
- **Ocean:** Waves, aircraft spray and foam wakes come from Boscali. We separated spray from the weather and replacement-wave switches and adjusted water restoration so the game's native water can be used.
- **Cockpit:** The pilot body, kneeboard, motion, visor reflections, glare, cockpit lighting, breathing and airframe sounds come from Boscali. We extracted them into individually switchable features and made compatibility and cleanup changes. Cockpit voice warnings can be switched off and default to off for new configurations.
- **Impact marks:** The scorch and impact decals come from Boscali, with added limits and cleanup. They are visual marks, not terrain-deforming craters.

## Better Night - Julmari

Better Night provides the base for the city windows, street lights, vehicle lights and other night lighting. We integrated its controls, disabled its NVG-specific changes and adjusted street-light size, brightness and visibility at altitude and above clouds.

The complete Better Night build source has not yet been assembled here. See [SOURCE-NOTES.md](SOURCE-NOTES.md).

## KROP - [@kctries](https://github.com/KcTries)

[KROP](https://github.com/KcTries/KROP) provides the following effects through a client-only extraction:

- **Aircraft and missile contrails:** Separate switches, emitter limits and cleanup fixes were added for the pack.
- **Low-flight dust and air wakes:** Extracted with their own controls. Ocean spray is handled by the Boscali-derived effect.
- **Lifeboats:** Cosmetic boats appear locally when ships sink. We added limits, cleanup and toggle handling. They are not networked rescue units.
- **Sound:** Cockpit and atmospheric muffling, bullet near-miss cracks and aircraft sonic booms, with controls, cleanup and allocation fixes.

KROP's NVG effects, nuclear flash effects and added sound-propagation delay are not included. The optional missile sonic boom is a separate Kelly's experiment inspired by KROP's feature description; it does not use KROP's implementation or samples.

## Kelly's additions and changes

- The unified FX and MET OFFICE interface, MFD integration, DOORMAN metadata and optional NOAtmoFX bridge.
- Runway, taxiway, hangar, helipad, industrial and port lighting, including five-light chasing approach rows and approximate PAPI indicators. The row-wide chase is an artistic adaptation of approach lighting, not the individual-flasher pattern of an FAA ALSF-2 installation.
- Street-light tuning, weather and water fixes, canopy compatibility work, effect controls and the integration and bug fixes described above.

## Other authors and assets

- **toadstorm** - [RainyGlassShader](https://github.com/toadstorm/RainyGlassShader), adapted by Boscali Summer for canopy rain. The recovered shader source, masks, notice and LGPL-2.1 licence are in [third-party/RainyGlassShader](third-party/RainyGlassShader).
- **Paul Leonard** - [NOAtmoFX](https://github.com/paulleonard1314-lang/NoAtmoFX), the optional volumetric-cloud add-on. It is a separate download and is not included in this pack.
- **takareads, SuddenDice, Mediasaur and bart** - recordings used for pilot voice and breathing sounds. See the [pilot voice notices](third-party/notices/PilotVoice-LICENSES.txt).
- **Stereo Surgeon, aust_paul, IENBA, davidbain, greyfeather, ReadeOnly and JovianSounds** - recordings listed in the [cockpit Foley notices](third-party/notices/CockpitFoley-LICENSES.txt), which also document generated audio. The notice covers the wider upstream asset collection, so not every listed clip is necessarily in this pack.
- UI assets, fonts and other third-party components have their own notices in [third-party/notices](third-party/notices).

The original licence notices are included in the repository and the download. This credits file does not replace them. If we've missed or misattributed anyone, please let us know.

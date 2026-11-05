# Credits

This pack brings together other people's work with changes made for Kelly's Client Effects. Thank you to the original authors for letting us use it. The original licence notices are in [third-party/notices](third-party/notices) and in the download; this file does not replace them.

## Boscali Summer — @awatarnieadekwatny

[Boscali Summer](https://github.com/GrabowMar/Boscali-Summer) is the source for much of the pack. Its upstream MIT notice names GrabowMar as copyright holder.

| Effect | What is in this pack | Kelly's changes |
| --- | --- | --- |
| Weather and MET OFFICE | Weather director, fronts, presets, rain, lightning and sky effects | Extracted for client use; rebuilt the weather menu, added plain-English names, time controls and an extreme storm. Weather control is limited to single-player. The original Boscali volumetric renderer is not included. |
| Canopy rain, wet ground and haze | Canopy droplets/runoff, rain visuals, rain haze and terrain wetness | Adapted canopy detection for installed aircraft, stopped precipitation above the cloud layer, and adjusted the haze and colour treatment. The canopy shader also derives from toadstorm's work credited below. |
| Ocean | Waves, aircraft spray and foam wakes | Separated spray from the weather and replacement-wave switches; adjusted water ownership and restoration so native water can be used. |
| Cockpit immersion | First-person pilot, kneeboard, motion, visor reflections, glare, instrument and cockpit lighting, breathing, airframe and canopy sounds | Extracted into individually switchable client features, with lifecycle and compatibility changes. Cockpit voice warnings can be disabled and default off for a fresh configuration. |
| Impact marks | Scorch and impact decals | Added bounds and cleanup. These are visual marks, not terrain-deforming craters. |

## Better Night — Julmari

Better Night provides the base for city windows, street lights, vehicle lights and other night lighting. Kelly's pack adapts it to the unified controls, disables its NVG-specific changes, and adds street-light size, brightness and altitude/cloud attenuation adjustments. The complete Better Night build source is not yet assembled in this repository; see [SOURCE-NOTES.md](SOURCE-NOTES.md).

## KROP — [@kctries](https://github.com/KcTries)

[KROP](https://github.com/KcTries/KROP) is the source for the selected client effects below. The pack uses a client-only extraction, not the full mod.

| Effect | Kelly's changes |
| --- | --- |
| Aircraft and missile contrails | Separate switches and bounded emitters; disabled-aircraft and cleanup fixes. |
| Low-flight dust and air wakes | Client-only extraction and controls. Ocean spray is handled by the Boscali-derived effect instead. |
| Lifeboats from sinking ships | Local cosmetic boats with caps, cleanup and toggle handling; they are not networked rescue units. |
| Cockpit and atmospheric audio filtering, bullet near-misses and aircraft sonic booms | Extracted and made switchable, with cleanup and allocation fixes. |

KROP's NVG effects, nuclear flash effects and added sound-propagation delay are not included. The optional **missile** sonic boom is a separate Kelly's experiment informed by KROP's feature description; it does not use KROP's implementation or samples.

## Kelly's Client Effects additions and integration

- Unified FX/MET OFFICE controls, MFD integration, DOORMAN metadata and the optional NOAtmoFX bridge.
- Airfield and site lighting: runway, taxiway, hangar, helipad, industrial and port lights. The experimental approach build adds the five-light chasing rows and approximate PAPI indicators. The row-wide chase is an artistic adaptation, not the individual-flasher pattern of an FAA ALSF-2 installation.
- Street-light pool and visibility tuning, weather and water fixes, canopy compatibility work, effect toggles and the integration/performance and bug-fix passes described above.

## Other authors and assets

- **toadstorm** — [RainyGlassShader](https://github.com/toadstorm/RainyGlassShader), adapted by Boscali Summer for canopy rain. The recovered adapted source, masks, notice and LGPL-2.1 licence are in [third-party/RainyGlassShader](third-party/RainyGlassShader).
- **Paul Leonard** — [NOAtmoFX](https://github.com/paulleonard1314-lang/NoAtmoFX), the optional volumetric-cloud add-on. It is a separate download and is not included in this pack.
- **takareads, SuddenDice, Mediasaur and bart** — recordings used for pilot voice and breathing sounds. See the [pilot voice notices](third-party/notices/PilotVoice-LICENSES.txt).
- **Stereo Surgeon, aust_paul, IENBA, davidbain, greyfeather, ReadeOnly and JovianSounds** — recordings listed in the [cockpit Foley notices](third-party/notices/CockpitFoley-LICENSES.txt), which also document generated audio. The notice covers the broader upstream asset collection, not necessarily every clip in this pack.
- The UI assets, fonts and other third-party components retain their own notices in [third-party/notices](third-party/notices).

If we have missed or misattributed anyone, please tell us and we'll put it right.

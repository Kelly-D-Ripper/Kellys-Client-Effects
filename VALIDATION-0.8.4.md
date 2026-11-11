# 0.8.4 local candidate validation

Validated 11 November 2026. This is a client-only candidate based on public 0.8.3. This candidate was not installed, pushed or published. The existing public 0.8.3 release description was updated with loader requirements and a workaround; its assets, tag and publication status were unchanged. No server or database change was performed.

## Exact runtime

- Main DLL SHA256: `C67454DAEAB50BAA7FF45C3024CAD27F1AF559C20B7200A2BBEAA77ECB73227B`.
- Startup helper SHA256: `35CC24B43BA7B8837052958E0DA01C3483ABDB44C743C9A723FA57DA0C8DB79A`, version 0.8.3.1. It protects the standalone plugin host without requiring NOAtmoFX or DOORMAN and does not change loader settings.
- One public BepInEx plugin, eight private embedded modules, original per-component configuration identities.
- NOAtmoFX is external and absent from the candidate ZIP.

## Completed checks

| Check | Result | Local evidence |
| --- | --- | --- |
| Build all changed modules, prepare private payloads and build owner | Pass; zero errors, one existing System.IO.Compression reference conflict warning in Weather | `evidence/build-final.log` |
| Exact compiled assembly audit | 47 passed | `evidence/exact-pack-final.log` |
| Missile PCM decoding, invalid inputs and non-repeat selection | 225 passed | `evidence/missile-clip-tests.log` |
| Actual DOORMAN embedded-card parser | Pass | `evidence/doorman-parser.json` |
| Earlier main-DLL test, 3440x1440, DOORMAN, original startup helper | 855 passed | `evidence/single-plugin-20261111T084116802Z` |
| Earlier main-DLL test, 1920x1080, official NOAtmoFX 1.4.0, original startup helper | 923 passed | `evidence/single-plugin-20261111T084526668Z` |
| Earlier DOORMAN disabled-boot path, original startup helper | 12 passed; no private pack module instantiated | `evidence/single-plugin-20261111T084624466Z` |
| Current main DLL and repaired helper, 1920x1080, BepInEx 5.4.23.5, no optional mods, isolated default loader configuration | 865 passed | `evidence/single-plugin-20261111T101617562Z` |
| Current main DLL and repaired helper, DOORMAN disabled-boot path | 12 passed; no private pack module instantiated | `evidence/single-plugin-20261111T101945815Z` |

Each runtime row records the tested DLL hashes in launch.json, assertions/captures under BepInEx/plugins/Probe, and player-data-verification.json. All passing runs verified the same 49 player files unchanged and player preferences matching their baseline after restoring only Unity's known window/session entries. Tests used separate plugin/config trees. They did not edit the installed pack or its configs. Earlier rows use the same main DLL but the original helper and are historical coverage, not tests of the new helper bytes.

## Public-package menu investigation

The separate audit at `../Audits/FxMenus-20261111/REPORT.md` downloaded the exact public 0.8.3 ZIP and official BepInEx 5.4.23.6 Windows x64 loader. With fresh default loader configuration and no companion mods, the original helper did not protect the owner plugin host. The owner instance was lost after startup while private modules and UI roots survived. Native FX still registered and opened; therefore this finding does not explain every report of both menus missing. The fallback settings discovery was incomplete after losing the owner.

The exact public main DLL with the repaired helper passed 385 checks in `../Audits/FxMenus-20261111/evidence/fresh-loader-20261111T101135575Z`. The unmodified public ZIP with only `[Chainloader] HideManagerGameObject = true` passed the same 385 checks in `../Audits/FxMenus-20261111/evidence/fresh-loader-20261111T101215451Z`. Both runs preserved player data and preferences. The runtime keyboard checks simulate Unity input reads only inside the released panel Update method, including both Ctrl keys and an old saved shortcut. They do not prove physical keyboard delivery.

The native MFD panel was captured and inspected. Hidden-window tests received no independent IMGUI repaint events, so visible Ctrl+F8 rendering remains an explicit open gate rather than a claimed pass. BepInEx 5 Windows x64 / Unity Mono is required and not bundled. DOORMAN, Blueprinter, TALON, AIRLIFT and NOAtmoFX are not required. Duplicate original component plugins can cause the owner to be skipped through its declared incompatibility rules.

The first successful run covers all 29 optional MFD switches and three tuning controls: default-off state, setting lookup, pagination, enable/disable or increment/decrement, usable row geometry and label overflow. Controls were also added to the Ctrl+F8 panel; its assembly/config discovery was checked, but its whole IMGUI interaction flow was not separately driven by physical input.

Audio checks include the 19 source gates, registered native hooks, existing-filter restoration, AudioListener exclusion, explicitly protected and mixer-only missile-warning siblings, read-only canopy/open-cabin state, crack shaping and non-repeating samples. Runtime checks decode/play the three missile recordings, exercise native fallback, verify RNG isolation, and stop/release owned sounds on disable. This does not replace listening to every sound family in flight.

Visual checks create particle-only copies with no source scripts, physics, colliders, damage registration or network components; verify bounded systems/particles, null-sprite handling, subemitter remapping, supported native materials and floating-origin simulation; and exercise sound/effect cleanup and lifeboat beacon timing. The native AmmoCookoffVehicle visual copy rendered in the actual game. The NOAtmoFX run additionally exercised the local FuelIgnited entry point against a read-only tank anchor without invoking native ignition or damage.

The NOAtmoFX run also drove the real KROP water wake renderer using a controlled low/fast aircraft fixture. It verified water-coloured particles and a supported shader, suppression of Boscali spray/foam/wash, restoration when KROP or its master is disabled, and unchanged saved Boscali preference/gameplay RNG. KROP mode is intentionally a complete spray-provider replacement and only supports fast fixed-wing passes; Boscali remains the default for rotorcraft and slower flight.

Existing runway grounding, lighting, rain, canopy, weather, time, UI and ownership checks ran alongside the additions. The official NOAtmoFX bridge and its weather-follow/yield controls passed. Captures of the added MFD pages and native fuel visual were inspected. No managed pack exceptions or skipped audio hook registrations were found in the passing runs.

Earlier failures are retained as evidence: a null default sprite slot caused a particle-copy crash, now fixed and regression-tested; a capture helper incorrectly assumed the old weather console was initialized, now corrected to capture the MFD root. A review also found an unregistered missile-warning sibling guard gap, fixed and covered before the passing runs.

## Scope and remaining acceptance

The additions do not require a server plugin by design: they observe native state and change local rendering/audio. This is supported by source/IL inspection and isolated runtime tests, not by a real multiplayer session. Synthetic multiplayer-state guards do not prove synchronization or acceptance on an unmodified live server.

Live multiplayer with an unmodified peer/server, sustained frame-time comparisons, all-aircraft audible/visual acceptance and physical keyboard/UI interaction remain open. No FPS gain, universal compatibility or full upstream KROP equivalence is claimed.

NVG changes, sound-propagation delay, ammunition/projectile/damage changes, countermeasure trajectory/cooldown automation, AI controls and destructive canopy behavior remain excluded. Manual canopy/door/ramp controls and parked-aircraft rendering changes remain withheld until a separate client-only implementation is verified.

Configuration impact: additive settings in existing KROP/missile config files, 29 new feature switches default off, three visible fuel tuning values plus an advanced effect-count cap. Existing settings are preserved; no config is included in the ZIP. Database impact: none.

# 0.8.4 local candidate validation

Validated 11 November 2026. This is a client-only candidate based on public 0.8.3. No installation, GitHub push, release publication, server change or database change was performed.

## Exact runtime

- Main DLL SHA256: `C67454DAEAB50BAA7FF45C3024CAD27F1AF559C20B7200A2BBEAA77ECB73227B`.
- Startup helper SHA256: `C016C7C59C59C89BEC0834FF5C4A5028F37F1CABCD968C6FC6AADC30F8453C32`, unchanged from 0.8.3.
- One public BepInEx plugin, eight private embedded modules, original per-component configuration identities.
- NOAtmoFX is external and absent from the candidate ZIP.

## Completed checks

| Check | Result | Local evidence |
| --- | --- | --- |
| Build all changed modules, prepare private payloads and build owner | Pass; zero errors, one existing System.IO.Compression reference conflict warning in Weather | `evidence/build-final.log` |
| Exact compiled assembly audit | 47 passed | `evidence/exact-pack-final.log` |
| Missile PCM decoding, invalid inputs and non-repeat selection | 225 passed | `evidence/missile-clip-tests.log` |
| Actual DOORMAN embedded-card parser | Pass | `evidence/doorman-parser.json` |
| Offline mission, 3440x1440, installed-version DOORMAN, no NOAtmoFX | 855 passed | `evidence/single-plugin-20261111T084116802Z` |
| Offline mission, 1920x1080, official NOAtmoFX 1.4.0 | 923 passed | `evidence/single-plugin-20261111T084526668Z` |
| DOORMAN disabled-boot path | 12 passed; no private pack module instantiated | `evidence/single-plugin-20261111T084624466Z` |

Each runtime row records the tested DLL hashes in launch.json, assertions/captures under BepInEx/plugins/Probe, and player-data-verification.json. All three runs verified the same 49 player files unchanged and player preferences matching their baseline after restoring only Unity's known window/session entries. Tests used separate plugin/config trees. They did not edit the installed pack or its configs.

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

# 0.8.6 effect-control audit

Local candidate only. No installation, GitHub push or release publication was performed.

Tested main DLL SHA256: `2F28E55F2F98456F42EF72880E0E24C0BBA395A98C5B4F40D9AA227CC9C31A4A`.

| Exact isolated fixture | Assertions | Evidence directory |
|---|---:|---|
| NOAtmoFX 1.4.0 + DOORMAN + Configuration Manager 19.1 | 2,132 passed | `evidence/single-plugin-20261111T192051902Z` |
| Configuration Manager 19.1, no other optional mods | 1,881 passed | `evidence/single-plugin-20261111T192223248Z` |
| Pack alone | 1,417 passed | `evidence/single-plugin-20261111T192338457Z` |

Every fixture used the same main DLL and pinned startup helper. All preserved the 49 tracked player files and restored the exact player-preference baseline. The normal game was closed during testing; each test used a separate plugin/config tree and exited afterward.

The complete audit accounts for 180 bound Booleans: 159 effects/weather controls and 21 explicit non-effect/inactive classifications. It traverses all 219 MFD rows, clicks and restores all 176 checkbox rows, and checks their original config/API identities against the real F1 model. Some shared masters appear in multiple categories; those numbers are controls, not distinct effects. The seven hidden inactive entries include one numeric setting.

F1 discovery measured zero private modules without the bridge and all eight with it. DOORMAN still sees one public pack entry. Its actual embedded-preview parser accepts the card and standalone Boscali Summer/KROP incompatibilities. The exact-pack static audit passes 47 checks.

Runtime tests exercise ground-roll output on the actual cockpit camera pivot, amplitude scaling, ground/airborne/view gates, recoil and landing/gear/Mach envelope cleanup, independent sound voices, all added warning choices, and bullet-crack/shake independence. Weather tests check independent particle/material/fog/sky controls and restoration. Existing runway/hangar, canopy/ocean, NVG/audio and accessibility-colour regression checks remain included.

Native URP captures of the new cockpit and weather pages were visually inspected. They are in the full fixture as `ground-roll-controls.png` and `granular-weather-controls.png`.

## Open checks

- The hidden player provided zero OnGUI events for the ten custom F1 drawers. Their config identity, callbacks, metadata restoration and menu visibility passed, but visible F1 drawing remains OPEN, not a rendering pass. See `f1-render-status.json`.
- Physical keyboard delivery, human audio/flight acceptance on every aircraft, live multiplayer and sustained frame-time performance remain unverified by these fixtures.
- Ordinary visible-window NVG auto-gain sampling retains its previous open acceptance gate; explicit render-request sampling and cleanup pass. It remains optional and off by default.
- The reported other player's exact aircraft and settings were not supplied. Ground-roll vibration is verified in this fixture, but that report is not claimed reproduced.

## Impact

Database impact: none.

Configuration impact: 33 additive child-effect Booleans default true beneath the existing masters, plus a ground-roll strength slider defaulting to 1. Existing identities, values and optional NVG/sound-travel defaults are preserved. F1 metadata is transient and restored on shutdown. No config files are bundled or overwritten.

The installed 0.8.5 DLL remains SHA256 `09FAA6E9AC3706E24C0F717830E048CA4F6068CBCEFAA109E479588D10B3DF9E`. NOAtmoFX remains a separate download.

The candidate archive contains only `BepInEx` and `README.md`, including the full audit and original inline notices. Detailed rows are also available in [the audit](EFFECT-TOGGLE-AUDIT.md) and [CSV inventory](EFFECT-TOGGLE-INVENTORY.csv).

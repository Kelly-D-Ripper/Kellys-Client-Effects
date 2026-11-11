# Volumetric weather repair - 0.8.7 candidate

Built on the installed 0.8.6 source at `3a962783e31da3c587e62f2282f35f1a3fe76c98`. Candidate only; no installation or publication in this pass.

Main DLL SHA256: `C43AFDFC8ED07D6277B37CAD137DC36F6D5AF29290B3112F5D98D098968E4CA1`.

## Fault and repair

NOAtmoFX 1.4 initializes its first weather state immediately, then normally takes about ten minutes to settle later changes. The installed config used that default. Its automatic Boscali adapter also read our already capped native presentation value. Ordinary storms could therefore supply lower cloud conditions than overcast. Repeated Boscali selections did rebuild the field; there was no one-use latch in that handler.

- Volumetric mode now supplies seven supported cloud profiles through the add-on API, with matching Boscali rain/atmosphere selections. The full Boscali catalogue remains in Enhanced mode.
- The public automatic-follow export now reports canonical regime severity. Native sky/fog fields retain their protective cap.
- A guarded NOAtmoFX 1.4.0 compatibility adapter changes one weather-smoothing time constant. Five seconds is the default, with an off switch and duration controls in F1 and the MFD. The prominent button cycles 5/15/60/600 seconds.
- The adapter rejects an unexpected version or instruction shape. Multiplayer timing and wind drift timing remain native. A short bounded transition also accompanies handback to mission weather.
- Weather API ownership is checked before cleanup. Foreign API input and personal F11 overrides are respected. Scene and mode changes release only our unchanged input.
- O routes to the Volumetric weather page while that provider is selected; the unsupported Boscali reroll is hidden there.

## Exact-build runtime evidence

| Isolated mission | Result |
|---|---|
| `evidence/single-plugin-20261111T205852414Z` - NOAtmoFX 1.4.0, DOORMAN and Configuration Manager 19.1 | 2,240 assertions passed |
| `evidence/single-plugin-20261111T210051991Z` - Configuration Manager, NOAtmoFX absent | 1,884 assertions passed |

Each run used the main DLL hash above and preserved all 49 protected player files and the player's preferences. The add-on weather tests preserved saved configuration bytes and restored borrowed in-memory settings. The installed pack remained 0.8.6, hash `2F28E55F2F98456F42EF72880E0E24C0BBA395A98C5B4F40D9AA227CC9C31A4A`.

The actual MFD buttons drove four changes, with five simulation seconds allowed for each. No test wrote the add-on's smoothed simulation fields.

| Selection | Requested conditions | Actual conditions | Actual cloud coverage |
|---|---:|---:|---:|
| Clear | 0.00 | 0.00022 | 0.00000 |
| Thunderstorm | 0.92 | 0.91383 | 0.97099 |
| Clear again | 0.00 | 0.00613 | 0.00000 |
| Thunderstorm again | 0.92 | 0.91388 | 0.97100 |

The first frame of each repeated change was also checked to ensure a smooth transition. All seven profiles' full targets, matching rain selections, fast-transition off/on, foreign override preservation, automatic-follow storm severity, native cap, synthetic multiplayer refusal and changed-code rejection were tested. Menu checks cover names, O open/close routing and the shared duration control. Explicit URP cloud and menu captures were inspected.

The retained control audit was regenerated: 181 bound Booleans accounted for, 221 MFD controls, and all 177 checkbox rows clicked and restored. These are control counts, not distinct-effect counts.

## Impact and remaining acceptance

Database impact: **none**.

Configuration impact: adds `Weather display/Fast volumetric weather transitions` (true) and `Weather display/Volumetric transition seconds` (5). Existing keys and defaults remain. Direct preset choices are session state. The ZIP contains no configuration files and no NOAtmoFX DLL or bundle.

Visible flight testing, physical O/Ctrl+F8 input, F1 custom-drawer rendering in a visible window, sustained frame-time measurements and live multiplayer acceptance remain open. The multiplayer check exercises the authority guard without starting a network session. Five seconds is an approximate cloud-model settling time; wind drift keeps NOAtmoFX's smoothing, and personal feature/shape settings continue to apply.

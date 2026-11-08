# 0.8.2 pre-release validation

This candidate combines the grounded airfield lighting and the public NOAtmoFX 1.4 API integration. It was checked on 8 November 2026 in isolated Nuclear Option missions using Direct3D 11.

## What passed

- Full pack with the official NOAtmoFX 1.4.0 DLL and matching bundle, without DOORMAN: 403 checks.
- Full pack without NOAtmoFX: 345 checks.
- Full pack with NOAtmoFX, DOORMAN, AIRLIFT, TALON and MEDALS at 3440 x 1440: 410 checks.
- Follow-up rendering run using the native camera visibility mask: 404 checks.
- Exact packaged component audit: 25 checks. Embedded DOORMAN card parser: accepted.
- Early startup helper: 10 normal and 2 headless loader-contract checks.

These counts include repeated regression assertions; they are not counts of independent features. The packaged runtime files match all four successful mission runs byte for byte. Each run verified the same 41 protected player files and restored the native window/session preferences to their baseline. Installed plugins were not changed.

## Coverage

The mission checks exercise runway grounding, native-edge preservation, pavement-end detection, unsafe-placement rejection, approach direction and row flashes, PAPI colour transitions, hangar lighting, cleanup and floating-origin handling. PAPI physical placement remains approximate; this is not a surveyed FAA airfield installation.

They also exercise weather modes, official add-on readiness and feature controls, quality presets and Custom state, cloud transition controls, host-owned control gating, settings discovery, F10 state reconciliation, rain above/below clouds, ocean spray, extreme-storm cleanup, cockpit warning muting, effect limits and MFD layout. DOORMAN grouping and disabling/re-enabling the complete pack passed in the compatible test build.

The multiplayer config-write checks deliberately enter a synthetic multiplayer state and compare config bytes. They establish the menu's no-save behavior in that branch. They do not establish actual network synchronization between two players.

The magenta scenery patches seen in earlier automated captures disappeared when the temporary lighting camera copied the native camera's visibility mask. The runtime materials were unchanged. This observation does not diagnose every possible third-party shader conflict.

## Remaining limits

- No live two-client multiplayer acceptance. NOAtmoFX owns its own synchronization; MET OFFICE weather/time commands remain single-player only.
- No sustained gameplay FPS or frame-time benchmark. Resource bounds, cleanup and cache behavior were checked, but no gameplay performance improvement is claimed.
- No human flight acceptance at every airfield, aircraft and camera angle. Programmatic UI actions and offscreen native rendering do not establish physical-input or subjective audio acceptance.
- Existing third-party mod combinations beyond the named compatibility run are not certified.

## Data and configuration

Database impact: **none**. No server, bot or database changes.

Configuration impact: no config files are included in the client ZIP, so extracting it does not replace player settings. The pack retains its cloud-mode preference. While multiplayer is active, its NOAtmoFX controls avoid writing the add-on's config; unsaved values can remain in memory until changed or the game closes. NOAtmoFX 1.4 performs its own one-time settings reset when upgraded, as documented by its author.

Keep both BepInEx/plugins and BepInEx/patchers when installing. NOAtmoFX is an optional separate download and is not in the package.

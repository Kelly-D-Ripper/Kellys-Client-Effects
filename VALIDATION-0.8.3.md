# Single-plugin candidate 0.8.3

## Result

The effects pack is one discoverable BepInEx plugin, `Client Effects & Weather`, containing eight private component assemblies and the night-lighting asset. The unchanged 12 KiB startup helper remains in BepInEx/patchers for the optional cloud add-on; it is not a second menu item. NOAtmoFX remains external.

The old one-entry test used a modified DOORMAN with hardcoded Unified grouping. This candidate was tested against the actual installed DOORMAN DLL, SHA256 ED40543E88064E0444D152A506B6AE68CA5F3B1C2F7FF78691920ADF406C23FB, which has no such grouping. Both builds advertise version 1.4.0, so checking only the version had missed that difference.

## Final runtime checks - 9 November 2026

| Scenario | Passed checks |
| --- | ---: |
| NOAtmoFX 1.4, actual DOORMAN, AIRLIFT/TALON/MEDALS, 3440 x 1440 | 431 |
| Pack disabled by DOORMAN before loader startup; NOAtmoFX separately present | 13 |
| NOAtmoFX 1.4 without DOORMAN, existing custom config fixture | 424 |
| No NOAtmoFX, actual DOORMAN, existing custom config fixture | 373 |

Counts include repeated regression assertions, not independent features. All four runs used the exact runtime files in the sealed ZIP. The 30-check static audit and the actual DOORMAN metadata parser also passed. Build has zero errors and the existing System.IO.Compression reference warning.

Checks cover the single physical plugin entry, private modules absent from global PluginInfos, preserved config identities, embedded night shader loading, effects and weather controls, NOAtmoFX's real Boscali follow/yield behavior, grounding and lighting regressions, precipitation, ocean spray, cockpit warning mute and MFD layout. A non-default Home shortcut and an unbound config value survived startup and mission checks.

The disabled-boot run proves that disabling the owner prevents all embedded effects modules from loading and creating configs. DOORMAN still lists one recoverable pack entry and keeps NOAtmoFX separate. Menu re-enable/disable assertions exercise DOORMAN's actual in-memory preferences; they do not claim to perform an interactive restart.

Thirty-nine protected player files and native preferences matched their baseline after every final run. The installed pack and installed DOORMAN were not modified. The existing GitHub 0.8.2 draft is unchanged.

## Scope and limits

Only the Core/Controls private-registry calls and Better Night's asset loading needed binary adaptations. Five of the eight private modules remain byte-identical to 0.8.2, including grounded airfield lighting. The preparation tool and payload manifest record each change; original assembly namespaces, resources and configuration identities remain separate internally.

No live multiplayer pair, sustained FPS/frame-time benchmark or human visual flight acceptance across every airfield was performed. This packaging change is not a performance claim. The helper and effects are client-side; no server or database changes were made.

Configuration impact: existing per-effect config files retain their GUIDs and values; no config files are shipped or replaced. Upgrading requires moving the old UnifiedEffects plugin folder out of BepInEx/plugins before extracting the new package. Legacy loose components are declared incompatible to prevent duplicate effects. Keep the optional NOAtmoFX folder and the existing BepInEx/config directory.

Database impact: **none**.

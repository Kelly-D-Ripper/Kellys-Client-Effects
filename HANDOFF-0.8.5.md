# Optional NVG and audio candidate

Branch: codex/optional-nvg-audio-0.8.5. Based on the complete 0.8.4 draft at ab62a87a7cf4caa2b3c53e0b523896285043ca7d, including its standalone startup repair. The installed game and 0.8.4 GitHub draft are unchanged.

The Cockpit page now exposes local NVG phosphor, bloom, grain, speckles, nuclear speckles, auto-gain and raise/lower transition options. The Sound page exposes supported world/engine sound propagation and gun-burst fade. All new effect toggles default off. Both the native FX MFD and Ctrl+F8 panel share the existing KROP config.

The separate "Allow game auto-NVG on camera change" setting also defaults off. While KROP client effects are enabled, the native camera-switch auto-selection callback is blocked unless allowed. Manual NVG toggling is preserved. Allowing it restores the native behaviour subject to the game's Camera Auto NVG preference; it does not change that preference. Disabling the KROP master restores native camera behaviour.

Build using Build-Candidate.ps1. Package-Candidate.py seals only the exact bytes from passing isolated evidence. Keep both the main plugin and the small startup helper. No BepInEx loader, configs, probe, source folder or NOAtmoFX bundle belongs in the client ZIP; credits and notices remain inline in README.md.

No server plugin, database, damage, physics, AI, ammunition or network change is part of this candidate. Configuration impact: additive entries under the existing KROP config, new effects off by default and no saved game preference writes. Database impact: none. No installation or GitHub publication is authorized by this handoff. Close the game before any later approved installation and preserve the existing configs and rollback.

See FEATURES-0.8.5.md for precise coverage and VALIDATION-0.8.5.md for checks and remaining human-flight, multiplayer, physical-input and performance gates.

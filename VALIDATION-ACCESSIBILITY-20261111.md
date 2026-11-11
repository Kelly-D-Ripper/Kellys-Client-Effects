# 0.8.5 accessibility colour repair

The FX MFD page sampled whichever stock label was closest to green at construction time. It never refreshed that palette, so changing the accessibility Primary colour did not update the panel.

The panel now reads the same semantic colours as the game's basic accessibility pickers: `ThemeManager.Active.ColorTheme.AllClear` for Primary, and `HudUnitFriendly` for Secondary. Primary colours the titles, labels and selected outlines on Effects, Weather and NOAtmoFX pages. Secondary colours the weather readout and clock. Disabled controls retain their reduced opacity; neutral borders and the background remain neutral. Black is treated as a valid chosen colour.

Native theme notifications update cached graphics, including hidden pages. The existing 4 Hz visible-panel refresh also catches changes without a notification. Unchanged colours return immediately; colour updates do not scan the scene, recreate the panel or rebuild its controls. Scene reset unsubscribes and clears bindings. Inactive parent buttons are included when bindings are captured, so disabled labels remain dimmed even on pages built while closed.

## Exact validation

- Main DLL SHA256: `09FAA6E9AC3706E24C0F717830E048CA4F6068CBCEFAA109E479588D10B3DF9E`.
- Startup helper remains `35CC24B43BA7B8837052958E0DA01C3483ABDB44C743C9A723FA57DA0C8DB79A`.
- Build: zero errors, existing Weather reference warning retained.
- Exact assembly audit: 47 passed; actual DOORMAN card parser also passed, including the standalone Boscali Summer and KROP incompatibility notices.
- Isolated mission with official NOAtmoFX 1.4.0 and DOORMAN: 1,308 assertions passed. Evidence: `evidence/single-plugin-20261111T183122834Z`.
- Primary/secondary updates, selected outlines, disabled and inactive labels, page switching, map reopening, black primary, NOAtmoFX controls and event unsubscription were exercised. Values were changed only in memory and restored in `finally`; no accessibility save method was called.
- Three rendered panel captures were checked for their selected hue and visually inspected: amber Effects, cyan Weather with purple Secondary, and amber NOAtmoFX controls. These are captures of the actual panel graphics in a temporary render canvas, not a screenshot of a player clicking the accessibility menu.
- Player preferences and all 49 checked player files were preserved. No new managed error/fatal or exception marker appeared in the isolated log.
- Deployment policy, Windows and isolated Linux controller tests passed; exact evidence is in `evidence/deployment-validation-accessibility-20261111.json`.

## Scope

Only the pack's MFD palette and its refresh/binding lifecycle changed. Optional NVG/audio behaviour, weather simulation, runway lighting, the separate NOAtmoFX files and the startup helper are unchanged. The earlier sealed candidates remain available; the repaired archive is sealed separately under `dist/accessibility-colours`.

Database impact: none. Configuration impact: none; no config migration, added setting or saved accessibility value change. The requested local installation continuation uses a fresh one-DLL client-bundle plan and rollback. No GitHub publication is part of this repair. Human flight, physical input, full-loadout multiplayer and sustained FPS remain outside this focused check.

# 0.8.5 DOORMAN compatibility update

This rebuild adds the requested standalone Boscali Summer and KROP incompatibility notice to the pack's embedded DOORMAN card. The existing BepInEx incompatibility attributes enforce the original plugin GUIDs. NOAtmoFX is described as a compatible, separate optional download.

DOORMAN does not expose a structured incompatibility field. The notice uses its supported description field; no unsupported configuration keys were invented.

- Main DLL SHA256: `EC32FBCD120180345BAC87BAD3F18810800087ADB0659445A8ABA03635D94350`.
- Startup helper SHA256: `35CC24B43BA7B8837052958E0DA01C3483ABDB44C743C9A723FA57DA0C8DB79A`.
- Build: zero errors, existing Weather assembly-reference warning retained.
- Exact assembly audit: 47 passed, zero failed.
- Actual DOORMAN parser: passed, including both displayed incompatibilities and matching loader attributes.
- Isolated mission with official NOAtmoFX 1.4.0 and DOORMAN: 1,255 passed. Evidence: `evidence/single-plugin-20261111T180853601Z`.
- Player preferences and all 49 checked player files match the pre-test baseline.

This replaces only the metadata of the earlier 0.8.5 candidate. Its optional NVG/audio defaults, runway repairs and other runtime functionality are retained. The original sealed candidate is preserved. The rebuilt archive is sealed separately under `dist/doorman-conflicts`.

The official NOAtmoFX 1.4.0 release archive was verified against the current GitHub asset digest: `27DD78507829A9C365B304D751161807EA3E1D354261F85E37C358198D890893`. Its DLL and shader bundle must be updated together. Its upstream release notes announce a one-time settings reset to new defaults on first launch.

Installation is explicitly requested, using the supported client-bundle manifest workflow with rollback. Database impact: none. Installation configuration impact: none; existing files are retained. New optional pack settings are additive on next launch and start off, including permission for automatic NVG selection on camera changes. NOAtmoFX's own one-time defaults migration is upstream behaviour, separate from the install transaction.

The exact install manifest, reviewed plan, rollback path and post-install verification are recorded separately in local evidence. This source change does not publish or update a GitHub release. Human flight/audio acceptance, visible ordinary-frame NVG gain sampling, physical keyboard input, sustained FPS and live multiplayer remain the limitations listed in `VALIDATION-0.8.5.md`.

# Optional KROP effects candidate

Local branch: `codex/client-effects-0.8.4`. Baseline: public 0.8.3. This candidate is not installed or published.

Open FX on the map MFD or Ctrl+F8. New options are in Cockpit, Sound, Ocean and Battlefield. All 29 new switches default off; three fuel tuning controls are exposed alongside them. Sound-family filters use the existing muffling controls. Missile recordings also require missile sonic booms. KROP spray requires Air wake effects and replaces Boscali spray/wash while enabled.

Build with Build-Candidate.ps1. Seal using Package-Candidate.py with passing evidence directories for the current main and startup hashes listed in VALIDATION-0.8.4.md. The refreshed candidate is under dist/menu-startup-fix; the earlier sealed candidate remains unchanged. The distribution contains only README.md, BepInEx/plugins/UnifiedEffects/UnifiedEffects.dll and BepInEx/patchers/UnifiedEffectsStartup.dll. Credits and original notices are inline. BepInEx 5 Windows x64 / Unity Mono must be installed separately. DOORMAN is optional and NOAtmoFX remains a separate add-on.

The refreshed startup helper protects the standalone owner host automatically, without changing BepInEx.cfg. Both plugin and patcher folders must be extracted. This addresses a verified host-lifetime/settings-discovery failure; it is not proof that every missing-menu report has the same cause. Physical keyboard and visible Ctrl+F8 rendering remain open acceptance checks.

Before any approved installation, close the game and retain the existing pack/config rollback. Do not copy the isolated test configs or probe DLL. No server deployment is needed or authorized by this handoff.

Configuration impact: additive off-by-default feature switches under existing identities; existing values retained. Database impact: none. Remaining gates: real multiplayer, longer performance runs, and human flight/audio/UI acceptance.

# 0.8.4 candidate - optional KROP presentation features

Based on the complete public 0.8.3 pack, retaining runway grounding, optional NOAtmoFX support and the single DOORMAN entry.

- Added 29 opt-in switches and three fuel-effect sliders to both FX interfaces.
- Added optional filtering for turbine startup, engine fires, split airbrakes, scrapes, launchers, VLS boosters, lasers, ship engines, ambient sounds, escape capsules, lift intake doors, missile startup/motor/flight sounds, pylon release, warhead effects, canopy jettison, ejection seats, countermeasure sounds and fuel ignition.
- Added optional canopy-state and Ibis open-cabin muffling, without controlling or damaging aircraft parts.
- Added optional shortened/high-pass bullet cracks and aircraft boom non-repeat selection.
- Added optional KROP missile recordings to the existing local detector, with native fallback.
- Added bounded visual-only fuel explosions, moving embers, optional audio and live cleanup. Original prefab scripts, damage, colliders and network components are not copied.
- Added optional KROP fixed-wing fast-pass water spray with automatic Boscali hand-off, and delayed lifeboat beacon activation. KROP mode replaces all Boscali aircraft spray/wash and needs Air wake effects; leave it off for rotorcraft and slower-flight wash.
- Added explicit AudioListener exclusion and missile-warning filter protection.
- Fixed pack-owner lifetime and settings discovery with a clean BepInEx installation and no optional add-ons. The included startup helper now protects the owner without requiring NOAtmoFX or DOORMAN; it does not change loader configuration.
- Added explicit BepInEx 5 Windows x64/Mono requirements and extraction instructions. BepInEx is not bundled. DOORMAN, Blueprinter and NOAtmoFX remain optional.

All new feature switches default off. Existing configs and values are retained. No configuration files are shipped. New entries are created in the existing KROP and missile module configs when this candidate is first run.

No NVG or propagation-delay changes, projectile/ammunition changes, countermeasure automation, AI-control changes or destructive canopy logic. Manual doors/ramps and parked-aircraft rendering remain withheld pending a separately verified client-only implementation.

Configuration impact: additive optional entries only; no replacement of existing preferences. Database impact: none. Installation/publication: not performed.

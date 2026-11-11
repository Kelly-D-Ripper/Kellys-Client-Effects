# NVG and sound gap check

Compared with KROP v1.1.2, source commit d79b6f910bda8ef1ffba0336362da5a57773bd1e, and our complete 0.8.4 draft at ab62a87a7cf4caa2b3c53e0b523896285043ca7d. The earlier no-NVG/no-added-delay requirement has been changed for this optional candidate. It does not restore KROP's gameplay features.

| Feature | 0.8.4 draft | 0.8.5 candidate |
| --- | --- | --- |
| Green, white and amber NVG phosphor | Missing by the earlier request | Optional tint and colour selector, including KROP's saturation treatment. Restores the native saturation and override when disabled. Does not select NVG. |
| NVG bloom and grain/static | Missing | Separate switches and strength controls. |
| White sensor specks | Missing | Optional, adjustable count, one bounded overlay mesh. |
| Nuclear-triggered NVG specks | Missing | Separate option observing already-existing visible detonations; no explosion or weapon changes. |
| NVG rendered-brightness auto-gain | Missing | Optional 16x16 asynchronous metering at at most 2 Hz. Native ambient gain remains the baseline. Unsupported/failed readback falls back to native gain. |
| NVG raise/lower wipe | Missing | Optional manual-toggle transition, cancelled on camera/owner changes or disable. |
| Automatic NVG activation on camera changes | Native game behaviour remained | Blocked by default while the KROP module is enabled. The separate permission restores native behaviour only if the game's Camera Auto NVG preference also permits it. Saved game preferences are not changed. |
| Added world-sound travel time | Intentionally excluded | Optional for supported playback paths, using captured local clips/parameters and emission positions. |
| Continuous engine travel time | Intentionally excluded | Separate option with bounded histories of position, pitch and volume, interpolation and bounded Doppler treatment. |
| End-of-gun-burst fade | Missing with the old propagation exclusion | Optional 0.05-second fade on delayed gun loops, requiring world-sound delay. |
| 19 extra filter families, canopy/open-cabin acoustics, bullet shaping, KROP missile recordings and boom non-repeat | Already in 0.8.4 | Retained, with their existing independent controls. |

## Audio scope

World delay covers the selected native gun-shot/loop and particle-impact paths, the already intercepted release/ejection/countermeasure playback calls, aircraft boom replacement, and supported missile/VLS loops. Continuous engine observation covers supported turbine, turbojet/turbofan, propeller/fan/rotor, jet-nozzle, ship and ground-engine sources. Ground engines use the existing periodic observer, not a new GroundVehicle.Update detour.

Native ExplosionAudioManager propagation is retained and not delayed again. The separate experimental missile-boom detector keeps its own timing. Prefab-started fuel-fireball and independent warhead audio, and some prefab/startup one-shots, retain native playback where no safe pre-play interception exists. This is not a complete port of every upstream KROP playback path. Boscali cockpit sounds and weather thunder are unchanged.

Queues, sources and histories are capped; over-budget or unsupported sources fall back to native playback. Source parameters and positions are captured so pooled-object reuse cannot retarget an older sound. Native playback clocks remain running where the game uses isPlaying for sequencing; owned muting and delayed copies only change local output. Own-cockpit/nearby detached sounds, 2D UI audio, listener hosts and protected warnings are excluded. Delayed copies retain the original optional filtering group rather than enabling extra filters.

Default limits are 128 delayed event voices, 32 delayed engine sources, 32 other delayed loops and ten seconds of permitted travel time. Each tracked loop has a fixed 604-sample ring buffer, approximately 30 seconds at 20 Hz, to support the maximum selectable delay. UI controls expose the event/engine caps and delay limit. Optional continuous propagation adds work; no FPS gain is claimed.

## Controls and boundaries

Both FX interfaces expose 18 new controls. Eleven are default-off switches: seven NVG presentation switches, the camera auto-NVG permission, and three sound options. Seven are colour/strength/count/delay/cap controls. Their settings use the existing aaron.boscali.krop-unified config.

| Tuning control | Default | Range |
| --- | --- | --- |
| Phosphor colour | Green | Green, white, amber |
| NVG bloom strength | 35 | 0-80 |
| NVG grain strength | 0.55 | 0-1 |
| NVG sensor speck count | 12 | 0-100 |
| Maximum delay | 10 seconds | 1-30 seconds |
| Maximum delayed event voices | 128 | 16-512 |
| Maximum delayed engine sources | 32 | 4-64 |

The camera policy is independent of the visual switches. Turning off NVG tint does not re-enable camera auto-selection. Turning off the KROP master restores the native camera callback along with other native behaviour. A manual NVG choice is never cleared simply because a camera changes.

KROP supplies the NVG treatment being adapted. Boscali's alternative fused look and Better Night's NVG world-ambient changes are not stacked on top of it; normal lighting and weather controls remain separate.

NVG bloom and grain default to milder strengths than upstream (35 and 0.55 versus 70 and 1); the controls allow the upstream strengths. Phosphor mode retains KROP's colour values and +80 saturation because the game's native -80 saturation otherwise washes out the tint. Native colour filter, contrast and exposure are not changed by the phosphor switch.

No server component, network message, authoritative state, damage, ammunition, countermeasure, AI, canopy/door or physics change is added. Live multiplayer acceptance remains a separate check. See VALIDATION-0.8.5.md for tested behaviour and open gates.

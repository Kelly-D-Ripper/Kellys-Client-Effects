# 0.8.5 local candidate validation

Date: 11 November 2026. Based on the complete 0.8.4 draft. The installed game and GitHub releases were not changed.

## Exact runtime files

- Main plugin SHA256: `B36F8FCE76ED0B7C64DCFDE1BEB5AA84E707EB5113EF77C92A0B844F756B167F`.
- Unchanged startup helper SHA256: `35CC24B43BA7B8837052958E0DA01C3483ABDB44C743C9A723FA57DA0C8DB79A`.
- Build: zero errors. The Weather project retains its existing System.IO.Compression assembly-reference warning; KROP, controls and probe build without warnings.
- Exact-assembly audit: 47 passed, zero failed. One discoverable pack owner, eight embedded private modules, no bundled NOAtmoFX, no identified network-send or gameplay-mutation additions, and no direct writes to native NightVision fields.
- Actual DOORMAN embedded-preview parser accepts the candidate. No unsafe single-DLL automatic update is advertised because the startup helper remains required.

## Isolated runtime checks

| Configuration | Checks passed | Evidence directory |
| --- | ---: | --- |
| BepInEx 5.4.23.5, no optional companions | 1,179 | `evidence/single-plugin-20261111T150708816Z` |
| Official NOAtmoFX 1.4.0 plus installed DOORMAN | 1,245 | `evidence/single-plugin-20261111T150838550Z` |
| DOORMAN disables the complete pack at startup | 12 | `evidence/single-plugin-20261111T150946182Z` |

These launches use separate plugin/config folders and the actual installed game data and managed assemblies. Each run verifies 49 protected player files and restores only the known Unity window/session registry entries to the saved baseline. Existing shortcut and orphaned config preservation also pass. No live multiplayer session or normal player game was started.

No new managed error/fatal, skipped effect hook or NVG sampling-failure marker appears in the final enabled-run logs. Ordinary BepInEx/Steam/Rewired environment warnings are not treated as evidence of a new pack error. The previous 0.8.4 ZIP retains SHA256 `4E1F71176A2E6326CC81896F42EB2C74E381AB0C389DB08390602CBC3A61E2E0`.

The native camera callback was exercised at night/day, with manual NVG selection, with the game's own camera preference disabled, and with the KROP master disabled. Default behaviour blocks automatic selection without clearing a manual choice or changing the saved preference. The real native manual toggle was exercised through the optional transition, including repeated input and cancellation.

NVG checks cover existing and missing URP overrides, restoring original values and flags, grain-buffer reuse, no gameplay RNG changes, the bounded 400-speck mesh, visible/offscreen nuclear bursts, decay, transition cleanup, gain correction bounds and pending GPU readback cleanup. Real URP captures show the three phosphors, bloom/grain and native restoration. The vanilla and restored captures in the standalone run are byte-identical. Its actual async brightness readback measured 0.5381569, rather than the black buffer detected and fixed during development. Native saturation is -80; optional phosphor mode now applies upstream's +80 and restores both the original value and override flag, preventing washed-out colours.

Ordinary visible-window auto-gain sampling is an explicit open gate. Independent render callbacks recorded no camera renders while hundreds of Update frames advanced in the hidden test window, even with a temporary camera target texture. The candidate's real URP render-request sample and callback cleanup pass; this does not establish the normal visible backbuffer path. No absent renderer callback is counted as a passing auto-gain sample.

Audio checks cover the default-off gates, local warning/listener/UI exclusions, event caps, captured clip/pitch/position across pooled-source reuse, delayed arrival, unchanged native playback clocks, engine caps, source reuse, disable while paused, camera resets and master cleanup. Native explosion playback is not delayed again. These checks establish local playback/state behaviour, not subjective sound quality.

Both FX interfaces expose the same 18 new settings. The probe checks defaults, category placement, reachability, matching config identity, enable/disable actions, colour cycling, numeric limits and label bounds. The two new review captures also check image content, and were visually inspected without clipping. Capture canvases are temporary test objects; the native panel layout was not changed for the screenshots. Existing weather, time, lighting, rain, spray and 0.8.4 optional-effect regressions remain in the full mission run.

## Scope and remaining acceptance

Real multiplayer against an unmodified server, sustained frame-time measurements, physical keyboard delivery, visible Ctrl+F8 rendering and human flight/listening acceptance remain open. No FPS gain is claimed. The new options add local presentation work; their caps bound resource use, not guarantee a frame rate.

No server or database changes. Configuration impact is additive: new effects default off, and permission for native camera-triggered NVG selection also defaults off. Existing config identities and values are retained; no config files are shipped. NOAtmoFX remains a separate optional download.

Earlier failed runs are retained as diagnostic evidence, not packaging proof. They exposed two reflection-only fixture errors, an unordered/invalid NVG sample source, and hidden-window render suppression. Visual review also caught native desaturation and a review-canvas layer/cropping issue. Product fixes and capture-fixture fixes were rechecked before the final standalone run. Final packaging must bind only passing runs with the exact runtime hashes above.

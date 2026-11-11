# Effect toggle audit - 0.8.7 candidate

The exact loaded build was checked with Configuration Manager 19.1 and NOAtmoFX 1.4.0.

- 181 bound Boolean settings accounted for: 160 mapped to effects/weather controls and 21 explicitly classified as policy, diagnostics, quality or inactive legacy settings.
- 221 MFD controls inspected, including 177 effect/master/feature checkboxes. Every checkbox was reached through the actual MFD pages, clicked, checked against its original config entry or API, and restored.
- These are control counts, not a claim of that many distinct effects. Some shared master switches intentionally appear in more than one category.
- F1 resolves the original config entries. Eight private modules are discoverable without creating extra DOORMAN entries. Ten numeric NOAtmoFX features have API-backed checkboxes alongside their strength controls.
- Seven inactive legacy entries are hidden in F1; their saved values are retained.

## Takeoff vibration

Ground-roll camera vibration was already part of Cockpit shake, rather than a KROP feature. It now has its own switch and a 0-3 strength slider. Find it under FX > EFFECTS > COCKPIT or F1 > Cockpit Feel. Cockpit shake remains its master; Comfort motion reduces it. It requires the local cockpit view, movement above 2 m/s and near-ground radar altitude, then stops after takeoff.

The isolated mission verified nonzero motion at takeoff speed reaching the actual camera pivot, strength scaling, immediate disable, stationary/airborne/external-camera suppression and restoration. This is camera movement, not hardware force feedback. The other player's exact aircraft/configuration has not been reproduced.

## Other repairs

- Added optional fast volumetric weather transitions and a seconds control to MFD Cloud Settings and F1. The weather page also has a prominent duration button and seven direct NOAtmoFX weather presets.

The 0.8.6 controls below are retained and rechecked:

- Split cockpit recoil, touchdown/gear jolts and the two Mach effects into independent switches.
- Split breathing, G strain, suit hiss, heartbeat, recovery, structural creaks, harness, impacts, gear lever and canopy closing into independent sound switches.
- Added separate switches for all four added voice warnings. Disabling Pull Up no longer masks an enabled Terrain warning.
- Added sky-phenomenon, sun-disc, extreme-storm, rain/mist and ocean-wake child switches.
- Decoupled KROP large-round shake from bullet-crack audio.
- Exposed NOAtmoFX reflected clouds, ocean lighting and daily cloud growth on the MFD.

Existing master settings are preserved. New child switches default on beneath those masters, so this does not re-enable a disabled effect. Optional NVG and sound-travel defaults stay off.

## Scope and validation limits

This inventories independently controlled effects, not each shader parameter or particle. Wet-ground darkening/puddles/glints/ripples remain one wet-terrain material effect; ocean swell/whitecaps/streaks/surf remain one ocean-wave effect; wet-glass beads/runoff/condensation/frost remain one canopy-shader effect. Pilot arm/pedal/breathing/G animation remains one body-motion effect. View-dependent exterior rain rush and cockpit patter remain one rain-sound system. Their masters are in both menus.

NOAtmoFX is optional and not bundled. F1 needs the separately installed BepInEx Configuration Manager; the FX MFD and Ctrl+F8 do not. Boscali MET OFFICE weather presets and time controls remain single-player. NOAtmoFX controls follow its host/local multiplayer policy. Excluded gameplay changes are not introduced by this audit.

The hidden fixture verifies menu models, live callbacks, particle/audio cleanup and camera output. It does not establish physical keyboard delivery, every effect's appearance on every aircraft, live multiplayer acceptance or sustained performance.

F1 custom-drawer rendering status: **OPEN**. Hidden isolated window received no OnGUI events in 120 frames. Discovery, configuration identity and checkbox callbacks were tested; visible F1 drawing still needs an interactive check. Recorded OnGUI events: 0; drawer invocations: 0. An OPEN result is not counted as a rendering pass.

Tested main DLL SHA256: `C43AFDFC8ED07D6277B37CAD137DC36F6D5AF29290B3112F5D98D098968E4CA1`.

Runtime evidence: `single-plugin-20261111T205852414Z`. Raw control/callback evidence is `BepInEx/plugins/Probe/effect-menu-coverage.json` in that run.

Database impact: none. Configuration impact: adds the fast volumetric transition switch (on) and seconds control (5); retains existing child switches and strength settings. No bundled configs, renamed keys or forced master changes. This candidate is packaged separately, not installed or published by this audit.

## Complete control mapping

Numeric tuning and quality rows are included below as controls; they are not counted as separate visual effects. F1 section/key refers to the original owner listed in the CSV. The Simple/Technical weather-name toggle also exists on the weather page and in F1 under MET OFFICE.

### Weather

| MFD control | Type | F1 section / key | Check |
|---|---|---|---|
| O / Ctrl+O weather shortcut (disable if O conflicts) | Toggle | Weather / ConsoleKeyOFallback | Clicked and restored |
| Boscali weather system | Toggle | Weather / Enabled | Clicked and restored |
| Rain visuals | Toggle | Weather / RainVisualsEnabled | Clicked and restored |
| Rain density | Slider | Weather / RainDensity | Binding and reachable row |
| Rain sound | Toggle | Weather / RainAudioEnabled | Clicked and restored |
| Canopy rain | Toggle | Weather / CanopyRainEnabled | Clicked and restored |
| Canopy runoff shader | Toggle | Weather / CanopyShaderEnabled | Clicked and restored |
| Wet terrain | Toggle | Weather / TerrainRainEnabled | Clicked and restored |
| Rain haze and atmosphere | Toggle | Weather / RainAtmosphereEnabled | Clicked and restored |
| Sun shafts | Toggle | Weather / SunShaftsEnabled | Clicked and restored |
| Sky phenomena | Toggle | Weather / SkyPhenomenaEnabled | Clicked and restored |
| Boscali dark nights | Toggle | Weather / DarkNightsEnabled | Clicked and restored |
| Boscali storm lightning | Toggle | Weather / LightningEnabled | Clicked and restored |
| Reduce Boscali flashes (native flashes unchanged) | Toggle | Weather / ReducedFlashes | Clicked and restored |
| Cloud-passage camera shake | Toggle | Weather / CloudPassageShakeEnabled | Clicked and restored |
| Sun-disc colour and haze | Toggle | Weather / SunDiscEnabled | Clicked and restored |
| Aurora (sky phenomena master) | Toggle | Weather / AuroraEnabled | Clicked and restored |
| Red sprites (sky phenomena master) | Toggle | Weather / RedSpritesEnabled | Clicked and restored |
| Blue jets (sky phenomena master) | Toggle | Weather / BlueJetsEnabled | Clicked and restored |
| Falling rain streaks (rain visuals master) | Toggle | Weather / RainStreaksEnabled | Clicked and restored |
| Cloud-passage mist (rain visuals master) | Toggle | Weather / CloudPassageMistEnabled | Clicked and restored |
| Extreme-storm thunder (rain sound master) | Toggle | Weather / ThunderAudioEnabled | Clicked and restored |
| Extreme-storm thick cloud deck | Toggle | Weather / ExtremeStormCloudDeckEnabled | Clicked and restored |
| Extreme-storm ground mist (rain haze master) | Toggle | Weather / ExtremeStormGroundMistEnabled | Clicked and restored |
| Extreme-storm visibility fog (rain haze master) | Toggle | Weather / ExtremeStormFogEnabled | Clicked and restored |

### Ocean

| MFD control | Type | F1 section / key | Check |
|---|---|---|---|
| Ocean waves | Toggle | Weather / OceanWavesEnabled | Clicked and restored |
| Aircraft sea spray and foam wakes | Toggle | Weather / SeaSprayEnabled | Clicked and restored |
| KROP fast-pass spray (replaces Boscali) | Toggle | 08 Visual refinements / KROP water wakes | Clicked and restored |
| Airborne ocean spray (spray master) | Toggle | Weather / SeaSprayParticlesEnabled | Clicked and restored |
| Ocean foam wakes (spray master) | Toggle | Weather / SeaFoamWakeEnabled | Clicked and restored |
| Ocean surface wash (spray + ocean waves) | Toggle | Weather / SeaSurfaceWashEnabled | Clicked and restored |

### Cockpit

| MFD control | Type | F1 section / key | Check |
|---|---|---|---|
| Allow game auto-NVG on camera change | Toggle | 09 Night vision / Allow game auto NVG on camera change | Clicked and restored |
| NVG phosphor tint | Toggle | 09 Night vision / Phosphor tint | Clicked and restored |
| NVG phosphor colour | Phosphor | 09 Night vision / Phosphor colour | Binding and reachable row |
| NVG bloom | Toggle | 09 Night vision / NVG bloom | Clicked and restored |
| NVG bloom strength | Slider | 09 Night vision / NVG bloom strength | Binding and reachable row |
| NVG grain and static | Toggle | 09 Night vision / NVG grain | Clicked and restored |
| NVG grain strength | Slider | 09 Night vision / NVG grain strength | Binding and reachable row |
| NVG bright speckles | Toggle | 09 Night vision / NVG sparkles | Clicked and restored |
| NVG speckle count | IntSlider | 09 Night vision / NVG speck count | Binding and reachable row |
| Extra NVG speckles after nuclear flashes | Toggle | 09 Night vision / Nuclear NVG speckles | Clicked and restored |
| NVG automatic brightness adjustment | Toggle | 09 Night vision / NVG auto gain | Clicked and restored |
| NVG raise and lower animation | Toggle | 09 Night vision / NVG transition | Clicked and restored |
| First-person pilot body | Toggle | Immersion / PilotBodyEnabled | Clicked and restored |
| Pilot control and G-load motion | Toggle | Immersion / PilotControlMotionEnabled | Clicked and restored |
| Pilot kneeboard (requires body) | Toggle | Immersion / KneeboardEnabled | Clicked and restored |
| Head inertia | Toggle | Cockpit Feel / Head inertia | Clicked and restored |
| Head inertia strength | Slider | Cockpit Feel / Head inertia strength | Binding and reachable row |
| Cockpit vibration and shake | Toggle | Cockpit Feel / Cockpit shake | Clicked and restored |
| Cockpit shake strength | Slider | Cockpit Feel / Cockpit shake strength | Binding and reachable row |
| Mach buffet | Toggle | Cockpit Feel / Mach buffet | Clicked and restored |
| Comfort motion | Toggle | Cockpit Feel / Comfort motion | Clicked and restored |
| Instrument glow | Toggle | Cockpit Feel / Instrument glow | Clicked and restored |
| Cockpit night lighting | Toggle | Cockpit Feel / Cockpit night lighting | Clicked and restored |
| Cockpit damage shading | Toggle | Cockpit Feel / Cockpit damage shading | Clicked and restored |
| Helmet visor reflections | Toggle | Immersion / HelmetVisor | Clicked and restored |
| Visor reflection strength | Slider | Immersion / HelmetReflection | Binding and reachable row |
| Sun glare | Toggle | Immersion / SunGlare | Clicked and restored |
| Cockpit voice warnings (off by default) | Toggle | Audio / Cockpit voice warnings | Clicked and restored |
| Pilot breathing and G-suit sounds | Toggle | Cockpit Audio / PilotBreathing | Clicked and restored |
| Airframe creaks and stress sounds | Toggle | Cockpit Audio / AirframeCreaks | Clicked and restored |
| Canopy wind rush | Toggle | Cockpit Audio / CanopyWind | Clicked and restored |
| Gear and canopy cockpit cues | Toggle | Cockpit Audio / ControlFoley | Clicked and restored |
| Optional softer radar tones | Toggle | Cockpit Audio / SoftRadarTones | Clicked and restored |
| Ground-roll / takeoff vibration | Toggle | Cockpit Feel / Ground roll vibration | Clicked and restored |
| Ground-roll vibration strength | Slider | Cockpit Feel / Ground roll vibration strength | Binding and reachable row |
| Cockpit gun recoil | Toggle | Cockpit Feel / Gun recoil | Clicked and restored |
| Touchdown jolts | Toggle | Cockpit Feel / Touchdown jolts | Clicked and restored |
| Landing-gear lock jolts | Toggle | Cockpit Feel / Landing gear jolts | Clicked and restored |
| Transonic vibration (Mach buffet master) | Toggle | Cockpit Feel / Transonic vibration | Clicked and restored |
| Sonic-crossing jolt (Mach buffet master) | Toggle | Cockpit Feel / Sonic crossing jolt | Clicked and restored |
| Breathing cycles (breathing master) | Toggle | Cockpit Audio / Breathing cycles | Clicked and restored |
| G-strain voice (breathing master) | Toggle | Cockpit Audio / G strain | Clicked and restored |
| G-suit hiss (breathing master) | Toggle | Cockpit Audio / G suit hiss | Clicked and restored |
| Heartbeat (breathing master) | Toggle | Cockpit Audio / Heartbeat | Clicked and restored |
| Recovery gasps (breathing master) | Toggle | Cockpit Audio / Recovery gasps | Clicked and restored |
| Structural creaks (airframe master) | Toggle | Cockpit Audio / Structural creaks | Clicked and restored |
| Harness strain (airframe master) | Toggle | Cockpit Audio / Harness strain | Clicked and restored |
| Impact sounds (airframe master) | Toggle | Cockpit Audio / Impact foley | Clicked and restored |
| Gear-lever sound (control foley master) | Toggle | Cockpit Audio / Gear lever | Clicked and restored |
| Canopy-close sound (control foley master) | Toggle | Cockpit Audio / Canopy closing | Clicked and restored |
| Pull-up warning (voice master) | Toggle | Audio / Pull up warning | Clicked and restored |
| Terrain warning (voice master) | Toggle | Audio / Terrain warning | Clicked and restored |
| Over-G warning (voice master) | Toggle | Audio / Over G warning | Clicked and restored |
| Bingo warning (voice master) | Toggle | Audio / Bingo warning | Clicked and restored |

### Trails

| MFD control | Type | F1 section / key | Check |
|---|---|---|---|
| KROP client effects master | Toggle | 00 General / Enable pack | Clicked and restored |
| Aircraft contrails | Toggle | 05 Contrails / Aircraft contrails | Clicked and restored |
| Missile contrails | Toggle | 05 Contrails / Missile contrails | Clicked and restored |
| Low-flight air wakes | Toggle | 04 Low-flight wake / Air wake effects | Clicked and restored |
| Land dust | Toggle | 04 Low-flight wake / Land dust | Clicked and restored |

### Lights

| MFD control | Type | F1 section / key | Check |
|---|---|---|---|
| Better Night master | Toggle | 1. General / Enabled | Clicked and restored |
| Night ambient lighting | Toggle | 2. Night / Enabled | Clicked and restored |
| Night ambient strength | Slider | 2. Night / Ambient Boost | Binding and reachable row |
| Lit building windows | Toggle | 3. Lights / City Windows | Clicked and restored |
| Window brightness | Slider | 3. Lights / City Intensity | Binding and reachable row |
| Vehicle lights | Toggle | 3. Lights / Vehicle Lights | Clicked and restored |
| Other lights and beacons | Toggle | 3. Lights / Other Lights | Clicked and restored |
| Street lights | Toggle | 4. Street Lights / Enabled | Clicked and restored |
| Street lights in daylight | Toggle | 4. Street Lights / Always On (ignore day/night) | Clicked and restored |
| Street-light poles | Toggle | 4. Street Lights / Poles | Clicked and restored |
| Street-light appearance | EnumChoice | 4. Street Lights / Light Sources | Binding and reachable row |
| Compact street lighting | Toggle | Lighting / Compact street lighting | Clicked and restored |
| Ground light radius (m) | Slider | Lighting / Street pool radius | Binding and reachable row |
| Ground light strength | Slider | Lighting / Street pool brightness | Binding and reachable row |
| Reduce street glow at altitude | Toggle | Lighting / Reduce street glow at altitude | Clicked and restored |
| Reduce street glow above clouds | Toggle | Lighting / Reduce street glow above clouds | Clicked and restored |
| Airfield and site lighting | Toggle | Airfield lighting / Enabled | Clicked and restored |
| Runway lights | Toggle | Airfield lighting / Runways | Clicked and restored |
| Sequenced approach strobes | Toggle | Airfield lighting / Approach strobes | Clicked and restored |
| Flash runway ends (nonstandard) | Toggle | Airfield lighting / Legacy flashing runway ends | Clicked and restored |
| Taxiway lights | Toggle | Airfield lighting / Taxiways | Clicked and restored |
| Hangar illumination | Toggle | Airfield lighting / Hangars | Clicked and restored |
| Helipad lights | Toggle | Airfield lighting / Helipads | Clicked and restored |
| Industrial and port lights | Toggle | Airfield lighting / Industrial and ports | Clicked and restored |

### Sound

| MFD control | Type | F1 section / key | Check |
|---|---|---|---|
| World sound travel time | Toggle | 09 Sound propagation / World sound delay | Clicked and restored |
| Engine sound travel time | Toggle | 09 Sound propagation / Engine sound delay | Clicked and restored |
| Gun burst fade (requires world delay) | Toggle | 09 Sound propagation / Gun loop fade | Clicked and restored |
| Maximum sound delay (seconds) | Slider | 09 Sound propagation / Maximum delay seconds | Binding and reachable row |
| Maximum delayed sounds | IntSlider | 09 Sound propagation / Maximum delayed sounds | Binding and reachable row |
| Maximum delayed engines | IntSlider | 09 Sound propagation / Maximum engine sources | Binding and reachable row |
| Pilot breathing and G-suit sounds | Toggle | Cockpit Audio / PilotBreathing | Clicked and restored |
| Airframe creaks and stress sounds | Toggle | Cockpit Audio / AirframeCreaks | Clicked and restored |
| Canopy wind rush | Toggle | Cockpit Audio / CanopyWind | Clicked and restored |
| Gear and canopy cockpit cues | Toggle | Cockpit Audio / ControlFoley | Clicked and restored |
| Optional softer radar tones | Toggle | Cockpit Audio / SoftRadarTones | Clicked and restored |
| KROP client effects master | Toggle | 00 General / Enable pack | Clicked and restored |
| Distant sound muffling | Toggle | 01 Audio filters / Atmospheric muffling | Clicked and restored |
| Cockpit muffling | Toggle | 01 Audio filters / First-person cockpit muffling | Clicked and restored |
| Reduced mechanical muffling | Toggle | 01 Audio filters / Reduced mechanical muffling | Clicked and restored |
| Fade cockpit filter after ejection | Toggle | 01 Audio filters / Ejection filter release | Clicked and restored |
| Own-aircraft impact filtering | Toggle | 01 Audio filters / Own-aircraft impact filtering | Clicked and restored |
| Helicopter turbine treatment | Toggle | 01 Audio filters / Helicopter turbine treatment | Clicked and restored |
| Muffling follows canopy opening and loss | Toggle | 01 Audio filters / Canopy-aware muffling | Clicked and restored |
| Open-cabin muffling for Ibis door guns | Toggle | 01 Audio filters / Open-cabin muffling | Clicked and restored |
| Bullet near-miss cracks | Toggle | 02 Bullet near misses / Bullet cracks | Clicked and restored |
| Large-round camera shake | Toggle | 02 Bullet near misses / Large-round camera shake | Clicked and restored |
| KROP bullet crack sound shaping | Toggle | 02 Bullet near misses / KROP crack shaping | Clicked and restored |
| Aircraft sonic booms | Toggle | 03 Aircraft sonic booms / Replacement aircraft booms | Clicked and restored |
| Suppress repeated aircraft booms | Toggle | 03 Aircraft sonic booms / Retrigger suppression | Clicked and restored |
| Avoid repeating aircraft boom recordings | Toggle | 03 Aircraft sonic booms / Avoid repeated samples | Clicked and restored |
| Cockpit voice warnings | Toggle | Audio / Cockpit voice warnings | Clicked and restored |
| Experimental missile sonic booms | Toggle | Audio / Missile sonic boom | Clicked and restored |
| KROP recordings (requires missile booms) | Toggle | Audio / KROP missile recordings | Clicked and restored |
| Filter turbine startup sounds | Toggle | 08 Additional audio / Turbine startup | Clicked and restored |
| Filter engine fire sounds | Toggle | 08 Additional audio / Engine fires | Clicked and restored |
| Filter split airbrakes | Toggle | 08 Additional audio / Split airbrakes | Clicked and restored |
| Filter airframe scraping | Toggle | 08 Additional audio / Airframe scrapes | Clicked and restored |
| Filter ground and ship launchers | Toggle | 08 Additional audio / Ground and ship launchers | Clicked and restored |
| Filter VLS booster sounds | Toggle | 08 Additional audio / VLS boosters | Clicked and restored |
| Filter laser sounds | Toggle | 08 Additional audio / Lasers | Clicked and restored |
| Filter ship engines | Toggle | 08 Additional audio / Ship engines | Clicked and restored |
| Filter world ambience | Toggle | 08 Additional audio / World ambience | Clicked and restored |
| Filter escape capsules | Toggle | 08 Additional audio / Escape capsules | Clicked and restored |
| Filter lift intake doors | Toggle | 08 Additional audio / Lift intake doors | Clicked and restored |
| Filter missile startup sounds | Toggle | 08 Additional audio / Missile startup | Clicked and restored |
| Filter missile motors and fly-bys | Toggle | 08 Additional audio / Missile motors and flight | Clicked and restored |
| Filter weapon release sounds | Toggle | 08 Additional audio / Pylon releases | Clicked and restored |
| Filter warhead explosion sounds | Toggle | 08 Additional audio / Warhead effects | Clicked and restored |
| Filter canopy jettison sounds | Toggle | 08 Additional audio / Canopy jettison | Clicked and restored |
| Filter ejection-seat sounds | Toggle | 08 Additional audio / Ejection seats | Clicked and restored |
| Filter flare and chaff release sounds | Toggle | 08 Additional audio / Countermeasure ejection | Clicked and restored |
| Filter fuel ignition sounds | Toggle | 08 Additional audio / Fuel ignition | Clicked and restored |

### Battlefield

| MFD control | Type | F1 section / key | Check |
|---|---|---|---|
| Impact scorch decals | Toggle | Impact marks / Enabled | Clicked and restored |
| KROP client effects master | Toggle | 00 General / Enable pack | Clicked and restored |
| Lifeboats from sinking ships | Toggle | 07 Cosmetic lifeboats / Lifeboats | Clicked and restored |
| Lifeboat bobbing | Toggle | 07 Cosmetic lifeboats / Gentle bobbing | Clicked and restored |
| Lifeboat beacons | Toggle | 07 Cosmetic lifeboats / Blinking beacons | Clicked and restored |
| Stagger initial lifeboat beacons | Toggle | 08 Visual refinements / Delayed lifeboat beacons | Clicked and restored |
| Varied fuel explosions (visual only) | Toggle | 08 Visual refinements / Varied fuel explosions | Clicked and restored |
| Extra fuel explosion sound (requires varied) | Toggle | 08 Visual refinements / Fuel explosion audio | Clicked and restored |
| Longer-lived embers (requires varied) | Toggle | 08 Visual refinements / Extended burning embers | Clicked and restored |
| Extra fuel explosion chance | Slider | 08 Visual refinements / Fuel explosion chance | Binding and reachable row |
| Fuel effect lifetime (seconds) | Slider | 08 Visual refinements / Fuel effect lifetime seconds | Binding and reachable row |
| Fuel effect range (m) | Slider | 08 Visual refinements / Fuel effect maximum distance m | Binding and reachable row |

### NOAtmoFX

| MFD control | Type | F1 section / key | Check |
|---|---|---|---|
| Quality preset | Preset | Quality / Preset | Binding and reachable row |
| Volumetric clouds | Feature | General / Enabled | Clicked and restored |
| Cloud shadows | Feature | Shadows / Shadows | Clicked and restored |
| Distant rain curtains | Feature | Atmosphere / RainShafts | Clicked and restored |
| Rain near camera | Feature | Atmosphere / NearRain | Clicked and restored |
| NOAtmoFX lightning | Feature | Atmosphere / Lightning | Clicked and restored |
| NOAtmoFX turbulence | Feature | Atmosphere / Turbulence | Clicked and restored |
| Sun rays | Feature | Atmosphere / GodRays | Clicked and restored |
| Light shafts | Feature | Atmosphere / LightShafts | Clicked and restored |
| NOAtmoFX fog | Feature | Atmosphere / Fog | Clicked and restored |
| NOAtmoFX wet glass | Feature | Glass / WetGlass | Clicked and restored |
| Nuclear cloud effects | Feature | Nuclear / Enabled | Clicked and restored |
| Keep sun below clouds | Feature | Lighting / KeepSunBelowClouds | Clicked and restored |
| Rainbow | Feature | Atmosphere / Rainbow | Clicked and restored |
| Glory | Feature | Atmosphere / Glory | Clicked and restored |
| Valley fog | Feature | Atmosphere / ValleyFog | Clicked and restored |
| Mountain clouds | Feature | Atmosphere / MountainCloud | Clicked and restored |
| Follow Boscali weather | Toggle | Integration / FollowBoscaliWeather | Clicked and restored |
| Yield rain/fog to Boscali | Toggle | Integration / YieldToBoscali | Clicked and restored |
| Cloud transition minutes | Slider | Weather / ChangeTime | Binding and reachable row |
| NOAtmoFX cirrus | Toggle | Weather / OwnCirrus | Clicked and restored |
| Cirrus amount | Slider | Weather / Cirrus | Binding and reachable row |
| Mid-level clouds | Slider | Weather / MidLevel | Binding and reachable row |
| Overcast layer | Slider | Layer / OvercastDeck | Binding and reachable row |
| Cumulus coverage | Slider | Shape / CoverageScale | Binding and reachable row |
| Cloud density | Slider | Shape / Density | Binding and reachable row |
| Cumulus shape | Slider | Shape / Dome | Binding and reachable row |
| Billowing detail | Slider | Shape / Billow | Binding and reachable row |
| Cloud blur | Slider | Shape / Blur | Binding and reachable row |
| Humidity | Slider | Weather / HumidityBias | Binding and reachable row |
| Storm instability | Slider | Weather / InstabilityBias | Binding and reachable row |
| Weather front | Front | Weather / Front | Binding and reachable row |
| Front distance km | Slider | Weather / FrontDistanceKm | Binding and reachable row |
| Front width km | Slider | Weather / FrontWidthKm | Binding and reachable row |
| Storm cloud top m | Slider | Storm / CbTop | Binding and reachable row |
| Anvil width | Slider | Storm / Anvil | Binding and reachable row |
| Supercell chance | Slider | Storm / Supercells | Binding and reachable row |
| Rain cloud darkening | Slider | Lighting / WetDarkening | Binding and reachable row |
| NOAtmoFX lightning | Slider | Atmosphere / Lightning | Binding and reachable row |
| Sun rays | Slider | Atmosphere / GodRays | Binding and reachable row |
| Light shafts | Slider | Atmosphere / LightShafts | Binding and reachable row |
| Render downscale | Downscale | Quality / Downscale | Binding and reachable row |
| Quick volumetric weather transitions | Toggle | Weather display / Fast volumetric weather transitions | Clicked and restored |
| Volumetric transition seconds | Slider | Weather display / Volumetric transition seconds | Binding and reachable row |
| Clouds reflected in water and surfaces | Toggle | General / CloudsInReflections | Clicked and restored |
| NOAtmoFX ocean lighting | Toggle | General / WaterLighting | Clicked and restored |
| Cloud growth through the day | Toggle | Weather / DiurnalCycle | Clicked and restored |

## Non-effect and inactive Boolean settings

| Owner | Section / key | Reason |
|---|---|---|
| aaron.boscali.betternight | 1. General / Diagnostics Logging | Logging only. |
| aaron.boscali.betternight | 8. Street Lights - Range & Destruction / Destructible | Prepared StreetLights.get_Destruct returns false; pole physics is disabled. |
| aaron.boscali.krop-unified | 00 General / Diagnostic logging | Startup and unsupported-host logging only. |
| aaron.boscali.unified | Immersion / Contrails | Bundled KROP suppresses the Boscali duplicate through private-registry lookup; the KROP replacement has its own control. |
| aaron.boscali.unified | Immersion / DustWakes | Bundled KROP suppresses the Boscali duplicate through private-registry lookup; the KROP replacement has its own control. |
| aaron.boscali.weather-unified | Weather / CinematicCloudsEnabled | Old volumetric renderer is excluded from the compiled pack. |
| aaron.boscali.weather-unified | Weather / CloudHalfResolution | Old volumetric renderer is excluded from the compiled pack. |
| aaron.boscali.weather-unified | Weather / CloudTemporalUpdate | Old volumetric renderer is excluded from the compiled pack. |
| aaron.boscali.weather-unified | Weather / ConsoleKeyRequiresCtrl | Weather shortcut modifier preference. |
| aaron.boscali.weather-unified | Weather / TapeMoves | Weather evolution policy; not a separate cosmetic effect. |
| paul.no.clouds | General / AnimatedJitter | Raymarch noise sampling quality option. |
| paul.no.clouds | General / DebugView | Cloud-opacity debug display. |
| paul.no.clouds | General / FollowGameWeather | Chooses mission weather as an input. |
| paul.no.clouds | General / HideGameClouds | Prevents native-cloud duplication while volumetric rendering is active. |
| paul.no.clouds | General / UseSceneDepth | Depth clipping against terrain and aircraft. |
| paul.no.clouds | Multiplayer / FollowHostSettings | Host settings authority and local restoration policy. |
| paul.no.clouds | Multiplayer / SyncClouds | Shared mission-clock synchronization. |
| paul.no.clouds | Nuclear / UnlockNukes | Weapon-access bypass is outside the client effects UI. |
| paul.no.clouds | Quality / EmptySkip | Empty-space skipping optimization. |
| paul.no.clouds | Quality / MapSlicing | Weather-map update scheduling optimization. |
| paul.no.clouds | Quality / PerformanceMeter | GPU timing readout. |

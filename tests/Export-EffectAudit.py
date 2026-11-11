"""Export the verified runtime control inventory; never infer coverage from source names."""
import argparse
import csv
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CATEGORIES = ['Weather', 'Ocean', 'Cockpit', 'Trails', 'Lights', 'Sound', 'Battlefield', 'NOAtmoFX']


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('run', type=Path)
    args = parser.parse_args()
    probe = args.run / 'BepInEx/plugins/Probe'
    audit = json.loads((probe / 'effect-menu-coverage.json').read_text(encoding='utf-8-sig'))
    result = (probe / 'result.txt').read_text(encoding='utf-8-sig')
    f1_render = json.loads((probe / 'f1-render-status.json').read_text(encoding='utf-8-sig'))
    assert result.startswith('PASS') and not audit['failures'], 'Runtime audit must pass first'
    assert audit['f1Installed'] and audit['noAtmoInstalled'], 'Complete inventory requires both optional integrations'
    dll = ROOT / 'Weather/bin/Release/netstandard2.1/BoscaliUnifiedWeather.dll'
    digest = hashlib.sha256(dll.read_bytes()).hexdigest().upper()
    assert audit['rootSha256'].upper() == digest, 'Audit is from a different build'
    controls = audit['mfdRows']
    toggles = [row for row in controls if row['kind'] in ('Toggle', 'Feature')]
    assert all(row['toggleStatus'] == 'clicked-and-restored' for row in toggles), 'Every effect toggle must be exercised'
    output = ROOT / 'EFFECT-TOGGLE-INVENTORY.csv'
    fields = ['Category', 'Control', 'Kind', 'Owner', 'F1 section', 'F1 key', 'F1 visible', 'MFD reachable', 'MFD callback']
    with output.open('w', newline='', encoding='utf-8') as stream:
        writer = csv.writer(stream)
        writer.writerow(fields)
        for row in controls:
            writer.writerow([CATEGORIES[row['category']], row['label'], row['kind'], row['owner'],
                row['bindingSection'], row['bindingKey'], row['f1Visible'], row['reachable'], row['toggleStatus']])
    text = [
        '# Effect toggle audit - 0.8.6 candidate', '',
        f"The exact loaded build was checked with Configuration Manager {audit['f1Version']} and NOAtmoFX {audit['noAtmoVersion']}.", '',
        f"- {audit['booleanCount']} bound Boolean settings accounted for: {audit['mappedBooleanCount']} mapped to effects/weather controls and {audit['classifiedNonEffectCount']} explicitly classified as policy, diagnostics, quality or inactive legacy settings.",
        f'- {len(controls)} MFD controls inspected, including {len(toggles)} effect/master/feature checkboxes. Every checkbox was reached through the actual MFD pages, clicked, checked against its original config entry or API, and restored.',
        '- These are control counts, not a claim of that many distinct effects. Some shared master switches intentionally appear in more than one category.',
        '- F1 resolves the original config entries. Eight private modules are discoverable without creating extra DOORMAN entries. Ten numeric NOAtmoFX features have API-backed checkboxes alongside their strength controls.',
        '- Seven inactive legacy entries are hidden in F1; their saved values are retained.', '',
        '## Takeoff vibration', '',
        'Ground-roll camera vibration was already part of Cockpit shake, rather than a KROP feature. It now has its own switch and a 0-3 strength slider. Find it under FX > EFFECTS > COCKPIT or F1 > Cockpit Feel. Cockpit shake remains its master; Comfort motion reduces it. It requires the local cockpit view, movement above 2 m/s and near-ground radar altitude, then stops after takeoff.', '',
        'The isolated mission verified nonzero motion at takeoff speed reaching the actual camera pivot, strength scaling, immediate disable, stationary/airborne/external-camera suppression and restoration. This is camera movement, not hardware force feedback. The other player\'s exact aircraft/configuration has not been reproduced.', '',
        '## Other repairs', '',
        '- Split cockpit recoil, touchdown/gear jolts and the two Mach effects into independent switches.',
        '- Split breathing, G strain, suit hiss, heartbeat, recovery, structural creaks, harness, impacts, gear lever and canopy closing into independent sound switches.',
        '- Added separate switches for all four added voice warnings. Disabling Pull Up no longer masks an enabled Terrain warning.',
        '- Added sky-phenomenon, sun-disc, extreme-storm, rain/mist and ocean-wake child switches.',
        '- Decoupled KROP large-round shake from bullet-crack audio.',
        '- Exposed NOAtmoFX reflected clouds, ocean lighting and daily cloud growth on the MFD.', '',
        'Existing master settings are preserved. New child switches default on beneath those masters, so this does not re-enable a disabled effect. Optional NVG and sound-travel defaults stay off.', '',
        '## Scope and validation limits', '',
        'This inventories independently controlled effects, not each shader parameter or particle. Wet-ground darkening/puddles/glints/ripples remain one wet-terrain material effect; ocean swell/whitecaps/streaks/surf remain one ocean-wave effect; wet-glass beads/runoff/condensation/frost remain one canopy-shader effect. Pilot arm/pedal/breathing/G animation remains one body-motion effect. View-dependent exterior rain rush and cockpit patter remain one rain-sound system. Their masters are in both menus.', '',
        'NOAtmoFX is optional and not bundled. F1 needs the separately installed BepInEx Configuration Manager; the FX MFD and Ctrl+F8 do not. Boscali MET OFFICE weather presets and time controls remain single-player. NOAtmoFX controls follow its host/local multiplayer policy. Excluded gameplay changes are not introduced by this audit.', '',
        'The hidden fixture verifies menu models, live callbacks, particle/audio cleanup and camera output. It does not establish physical keyboard delivery, every effect\'s appearance on every aircraft, live multiplayer acceptance or sustained performance.', '',
        f"F1 custom-drawer rendering status: **{f1_render['status']}**. {f1_render.get('reason', '')} Recorded OnGUI events: {f1_render['onGuiEvents']}; drawer invocations: {f1_render['drawerInvocations']}. An OPEN result is not counted as a rendering pass.", '',
        f'Tested main DLL SHA256: `{digest}`.', '',
        f'Runtime evidence: `{args.run.name}`. Raw control/callback evidence is `BepInEx/plugins/Probe/effect-menu-coverage.json` in that run.', '',
        'Database impact: none. Configuration impact: additive child switches and one strength setting; no bundled configs, renamed keys or forced master changes. This candidate is packaged separately, not installed or published by this audit.', '',
        '## Complete control mapping', '',
        'Numeric tuning and quality rows are included below as controls; they are not counted as separate visual effects. F1 section/key refers to the original owner listed in the CSV. The Simple/Technical weather-name toggle also exists on the weather page and in F1 under MET OFFICE.', '',
    ]
    def clean(value):
        return str(value).replace('|', '/').replace('\n', ' ')
    for category, name in enumerate(CATEGORIES):
        text += [f'### {name}', '', '| MFD control | Type | F1 section / key | Check |', '|---|---|---|---|']
        for row in controls:
            if row['category'] != category:
                continue
            text.append('| ' + ' | '.join(map(clean, [row['label'], row['kind'], row['bindingSection'] + ' / ' + row['bindingKey'], 'Clicked and restored' if row['kind'] in ('Toggle', 'Feature') else 'Binding and reachable row'])) + ' |')
        text.append('')
    text += ['## Non-effect and inactive Boolean settings', '', '| Owner | Section / key | Reason |', '|---|---|---|']
    for row in audit['booleans']:
        if row['classification'] in ('effect-menu-control', 'dedicated-weather-ui-control'):
            continue
        text.append('| ' + ' | '.join(map(clean, [row['owner'], row['section'] + ' / ' + row['key'], row['reason']])) + ' |')
    (ROOT / 'EFFECT-TOGGLE-AUDIT.md').write_text('\n'.join(text) + '\n', encoding='utf-8')
    print(f'Exported {len(controls)} controls and {len(toggles)} exercised toggles')


if __name__ == '__main__':
    main()

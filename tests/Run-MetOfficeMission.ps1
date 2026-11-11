param([string]$GameDir='C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option', [switch]$LaunchOnly, [switch]$IncludeNoAtmo, [switch]$IncludeLegacyNoAtmo, [switch]$IncludeDoorman, [switch]$IncludeConfigManager, [switch]$IncludeOtherMfdMods, [switch]$OmitStartup, [switch]$DisableUnifiedOnBoot, [ValidateRange(800,3840)][int]$Width=1920, [ValidateRange(600,2160)][int]$Height=1080)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$candidate=Split-Path -Parent $PSScriptRoot
$project='C:\Users\Aaron\Documents\Nuclear Option mods\BoscaliUnified'
$workspace='C:\Users\Aaron\Documents\Nuclear Option mods'
function Hash([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
function ProfileFiles {
    $profile=Join-Path $env:USERPROFILE 'AppData\LocalLow\Shockfront\NuclearOption'
    @(Get-ChildItem -LiteralPath $profile -File -Recurse | Where-Object { $_.Name -notmatch '^Player(?:-prev)?\.log$' -and $_.FullName -notmatch '[\\/]com\.unity\.addressables[\\/]' } | ForEach-Object { [pscustomobject]@{path=$_.FullName;sha256=(Hash $_.FullName)} })
}
function RegistryEntries([string]$text) {
    $entries=@{}
    foreach($m in [regex]::Matches($text,'(?ms)^"(?<key>[^"]+)"=.*?(?=^"|^\[|\z)')) { $entries[$m.Groups['key'].Value]=$m.Value.TrimEnd() }
    return $entries
}
if($DisableUnifiedOnBoot -and !$IncludeDoorman) { throw 'Disabled-boot test needs the installed DOORMAN startup filter.' }
if (Get-Process NuclearOption -ErrorAction SilentlyContinue) { throw 'A game is running; isolated mission not launched.' }
& (Join-Path $workspace 'build-tools\dotnet-sdk\dotnet.exe') build (Join-Path $PSScriptRoot 'MetOfficeProbe\MetOfficeProbe.csproj') -c Release "-p:GameDir=$GameDir" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Probe build failed.' }
$stamp=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$root=Join-Path (Join-Path $candidate 'evidence') ('single-plugin-'+$stamp)
if(Test-Path -LiteralPath $root) { throw 'Evidence destination exists.' }
New-Item -ItemType Directory -Path $root | Out-Null
foreach($file in @('NuclearOption.exe','UnityPlayer.dll','winhttp.dll','.doorstop_version','doorstop_config.ini','steam_appid.txt')) { Copy-Item -LiteralPath (Join-Path $GameDir $file) -Destination $root }
foreach($dir in @('NuclearOption_Data','MonoBleedingEdge')) { New-Item -ItemType Junction -Path (Join-Path $root $dir) -Target (Join-Path $GameDir $dir) | Out-Null }
$bep=Join-Path $root 'BepInEx'; $plugins=Join-Path $bep 'plugins'
New-Item -ItemType Directory -Path (Join-Path $plugins 'Probe'),(Join-Path $bep 'config') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $GameDir 'BepInEx\core') -Destination (Join-Path $bep 'core') -Recurse
# Seed an old per-component setting in the isolated copy. Never copy or edit live configs.
if(!$DisableUnifiedOnBoot) {
    $legacyConfig="[Controls]`nOpen effects panel = Home`n`n[Preservation fixture]`nUnbound sentinel = keep-me`n"
    Set-Content -LiteralPath (Join-Path $bep 'config\aaron.boscali.control-unified.cfg') -Value $legacyConfig -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $plugins 'Probe\config-preservation.txt') -Value 'Controls/Open effects panel=Home; orphaned sentinel=keep-me' -Encoding ASCII
}
$hashes=@{}
$parts=@(
    @((Join-Path $candidate 'Weather\bin\Release\netstandard2.1\BoscaliUnifiedWeather.dll'),'UnifiedEffects/UnifiedEffects.dll'),
    @((Join-Path $PSScriptRoot 'MetOfficeProbe\bin\Release\net472\MetOfficeExtremeProbe.dll'),'Probe/MetOfficeExtremeProbe.dll')
)
foreach($part in $parts) {
    $destination=Join-Path $plugins $part[1]
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $part[0] -Destination $destination -Force
    $hashes['plugins/'+$part[1]]=Hash $part[0]
    if((Hash $destination) -ne $hashes['plugins/'+$part[1]]) { throw 'Candidate staging hash mismatch.' }
}
if(!$OmitStartup) {
    $startup=Join-Path $candidate 'Startup\bin\Release\net472\UnifiedEffectsStartup.dll'
    $destination=Join-Path $bep 'patchers\UnifiedEffectsStartup.dll'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $startup -Destination $destination
    $hashes['patchers/UnifiedEffectsStartup.dll']=Hash $startup
    if((Hash $destination) -ne $hashes['patchers/UnifiedEffectsStartup.dll']) { throw 'Candidate startup staging hash mismatch.' }
    Set-Content -LiteralPath (Join-Path $plugins 'Probe\unified-startup.txt') -Value 'enabled' -Encoding ASCII
}
if($IncludeDoorman) {
  $d=Join-Path $GameDir 'BepInEx\plugins\KellysJOINCHECK.dll'
  if((Hash $d) -ne 'FD9E3C052299259DB9972BC20157160E2A874A2079EC9F2DC53A8F1AF2AB4521') {throw 'Installed DOORMAN changed; review exact test target.'}
  Copy-Item -LiteralPath $d -Destination (Join-Path $plugins 'KellysJOINCHECK.dll');$hashes['plugins/KellysJOINCHECK.dll']=Hash $d
  $startup=Join-Path $GameDir 'BepInEx\patchers\KellysDOORMANStartup.dll'
  if((Hash $startup) -ne 'ABE18CDCB8FF544E62E5B1A96A1924DFA640F6A185C97D252D7FB241A6466BBE') {throw 'Installed DOORMAN startup changed; review exact test target.'}
  New-Item -ItemType Directory -Force -Path (Join-Path $bep 'patchers') | Out-Null
  Copy-Item -LiteralPath $startup -Destination (Join-Path $bep 'patchers\KellysDOORMANStartup.dll');$hashes['patchers/KellysDOORMANStartup.dll']=Hash $startup
  Set-Content -LiteralPath (Join-Path $plugins 'Probe\doorman.txt') -Value 'enabled' -Encoding ASCII
  if($DisableUnifiedOnBoot) {
      @{schema=1;autoMatchServers=$false;disabledContent=@();disabledPlugins=@('aaron.boscali.weather-unified')} | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $bep 'config\doorman-mods.json') -Encoding UTF8
      Set-Content -LiteralPath (Join-Path $plugins 'Probe\unified-disabled.txt') -Value 'enabled' -Encoding ASCII
  }
}
if($IncludeConfigManager) {
    $source=Join-Path $GameDir 'BepInEx\plugins\ConfigurationManager\ConfigurationManager.dll'
    if((Hash $source) -ne '143BB25E8F03ED8728224DE0B72752DE29C0962FF69D20EC685831EC7491B323') {throw 'Installed Configuration Manager changed; review exact F1 test target.'}
    $destination=Join-Path $plugins 'ConfigurationManager\ConfigurationManager.dll'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
    $hashes['plugins/ConfigurationManager/ConfigurationManager.dll']=Hash $destination
    # Deliberately seed isolated defaults, never copy or modify the player's F1 configuration.
    $config="[Filtering]`nShow advanced = false`nShow keybinds = true`nShow settings = true`n`n[General]`nShow config manager = F1`nPlugin collapsed default = true`n"
    Set-Content -LiteralPath (Join-Path $bep 'config\com.bepis.bepinex.configurationmanager.cfg') -Value $config -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $plugins 'Probe\configuration-manager.txt') -Value 'Configuration Manager 19.1 - F1 defaults' -Encoding ASCII
}
if($IncludeNoAtmo -or $IncludeLegacyNoAtmo) {
    $legacy='D:\NuclearOption-Codex-evidence\NoAtmoFX-test-only\BepInEx\plugins\NoAtmoFX'
    $dest=Join-Path $plugins 'NoAtmoFX'; New-Item -ItemType Directory -Path $dest -Force | Out-Null
    $release=Join-Path $project 'NoAtmoFX14Candidate\release-test\extracted\BepInEx\plugins\NoAtmoFX'
    $dll=if($IncludeNoAtmo) { Join-Path $release 'NoAtmoFX.dll' } else { Join-Path $legacy 'NoAtmoFX.dll' }
    Copy-Item -LiteralPath $dll -Destination (Join-Path $dest 'NoAtmoFX.dll')
    $bundle=if($IncludeNoAtmo) { Join-Path $release 'noatmofx' } else { Join-Path $legacy 'noatmofx' }
    Copy-Item -LiteralPath $bundle -Destination (Join-Path $dest 'noatmofx')
    foreach($name in @('NoAtmoFX.dll','noatmofx')) { $hashes['plugins/NoAtmoFX/'+$name]=Hash (Join-Path $dest $name) }
    if($IncludeNoAtmo -and ($hashes['plugins/NoAtmoFX/noatmofx'] -ne '1AC0ADA2655B390D48F78A631986161BB95133C2764BFA366A9650921D869BC2' -or $hashes['plugins/NoAtmoFX/NoAtmoFX.dll'] -ne 'E415694864304262B3F1800087412B175D8947D1113CF3D26D644EC533E77F0C')) {throw 'Unexpected release assets'}
    if($IncludeNoAtmo) { Set-Content -LiteralPath (Join-Path $plugins 'Probe\noatmo.txt') -Value 'official 1.4.0 DLL and 1.4.0 bundle' -Encoding ASCII }
}

if($IncludeOtherMfdMods) {
    foreach($name in @('KellysAIRLIFTPublicUI','KellysTALONPublicUI','KellysMEDALSPublicUI')) {
        $source=Join-Path $GameDir ('BepInEx\plugins\'+$name+'\'+$name+'.dll')
        $destination=Join-Path $plugins ($name+'\'+$name+'.dll')
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
        Copy-Item -LiteralPath $source -Destination $destination
        $hashes['plugins/'+$name+'/'+$name+'.dll']=Hash $source
        if((Hash $destination) -ne $hashes['plugins/'+$name+'/'+$name+'.dll']) { throw 'MFD compatibility copy mismatch.' }
    }
    Set-Content -LiteralPath (Join-Path $plugins 'Probe\other-mfd-mods.txt') -Value 'enabled' -Encoding ASCII
}
$fixture=Join-Path $project 'evidence\cockpitfeel-mission-20261101T211021923Z\BepInEx\plugins\Probe\input-mission.json'
Copy-Item -LiteralPath $fixture -Destination (Join-Path $plugins 'Probe\input-mission.json')
$profileBefore=ProfileFiles
$profileBackup=Join-Path $root 'player-data-backup'
New-Item -ItemType Directory -Path $profileBackup | Out-Null
$profileRoot=Join-Path $env:USERPROFILE 'AppData\LocalLow\Shockfront\NuclearOption'
foreach($file in $profileBefore) {
    $relative=$file.path.Substring($profileRoot.Length+1); $dest=Join-Path $profileBackup $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
    Copy-Item -LiteralPath $file.path -Destination $dest
    if((Hash $dest) -ne $file.sha256) { throw 'Player data backup mismatch.' }
}
$profileBefore | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'player-files-before.json') -Encoding UTF8
& reg.exe export 'HKCU\Software\Shockfront\NuclearOption' (Join-Path $root 'playerprefs-before.reg') /y | Out-Null
if($LASTEXITCODE -ne 0) { throw 'PlayerPrefs backup failed.' }
if(Get-Process NuclearOption -ErrorAction SilentlyContinue) { throw 'A game started while preparing; probe not launched.' }
$argsList=@('-screen-fullscreen','0','-screen-width',"$Width",'-screen-height',"$Height",'-force-d3d11','-logFile',('"'+(Join-Path $root 'player.log')+'"'))
$proc=Start-Process -FilePath (Join-Path $root 'NuclearOption.exe') -WorkingDirectory $root -ArgumentList $argsList -WindowStyle Hidden -PassThru
@{pid=$proc.Id;layout='single visible plugin with private embedded modules';disabledBoot=[bool]$DisableUnifiedOnBoot;componentSha256=$hashes;fixtureSha256=(Hash $fixture);dataAccess='game-data junctions, not filesystem-enforced read-only; native managed preference writes and recent mission save suppressed; player files and registry snapshotted';started=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'launch.json') -Encoding UTF8
Write-Output "MET OFFICE full-pack mission PID=$($proc.Id) evidence=$root"
if($LaunchOnly) { return }
$deadline=[DateTime]::UtcNow.AddSeconds(210)
while(!$proc.WaitForExit(1000)) { if([DateTime]::UtcNow -ge $deadline) { throw "Probe remains running PID=$($proc.Id); inspect $root" } }
& reg.exe export 'HKCU\Software\Shockfront\NuclearOption' (Join-Path $root 'playerprefs-after.reg') /y | Out-Null
if($LASTEXITCODE -ne 0) { throw 'PlayerPrefs verification export failed.' }
$prefsUnchanged=(Hash (Join-Path $root 'playerprefs-before.reg')) -eq (Hash (Join-Path $root 'playerprefs-after.reg'))
$restoredPrefs=0
if(!$prefsUnchanged) {
    # Unity itself persists window geometry/session counters outside managed PlayerPrefs.
    # Restore only those known entries after our own fixture has exited, with no other game running.
    if(Get-Process NuclearOption -ErrorAction SilentlyContinue) { throw 'Another game is running; preference restoration deferred.' }
    $before=RegistryEntries (Get-Content -Raw -LiteralPath (Join-Path $root 'playerprefs-before.reg'))
    $after=RegistryEntries (Get-Content -Raw -LiteralPath (Join-Path $root 'playerprefs-after.reg'))
    if($before.Count -ne $after.Count) { throw 'Unexpected added/removed registry key; review backup.' }
    $changedKeys=@($after.Keys | Where-Object { !$before.ContainsKey($_) -or $before[$_] -ne $after[$_] })
    foreach($key in $changedKeys) {
        if(!$before.ContainsKey($key) -or $key -notmatch '^(Screenmanager (Resolution (Width|Height|Use Native)|Fullscreen mode|Window Position (X|Y))_h[0-9]+|unity\.player_session(_count|id)_h[0-9]+)$') { throw 'Unexpected preference change; review backup.' }
    }
    $restore='Windows Registry Editor Version 5.00'+"`r`n`r`n"+'[HKEY_CURRENT_USER\Software\Shockfront\NuclearOption]'+"`r`n"+(($changedKeys | Sort-Object | ForEach-Object { $before[$_] }) -join "`r`n")+"`r`n"
    $restoreFile=Join-Path $root 'restore-native-playerprefs.reg'
    Set-Content -LiteralPath $restoreFile -Value $restore -Encoding Unicode
    & reg.exe import $restoreFile | Out-Null
    if($LASTEXITCODE -ne 0) { throw 'Native preference restore failed.' }
    & reg.exe export 'HKCU\Software\Shockfront\NuclearOption' (Join-Path $root 'playerprefs-restored.reg') /y | Out-Null
    if($LASTEXITCODE -ne 0) { throw 'Native preference restore verification failed.' }
    $prefsUnchanged=(Hash (Join-Path $root 'playerprefs-before.reg')) -eq (Hash (Join-Path $root 'playerprefs-restored.reg'))
    $restoredPrefs=$changedKeys.Count
}
$profileAfter=ProfileFiles; $changed=@(); $old=@{}
foreach($file in $profileBefore) { $old[$file.path]=$file.sha256 }
foreach($file in $profileAfter) { if(!$old.ContainsKey($file.path) -or $old[$file.path] -ne $file.sha256) { $changed+=$file.path }; $old.Remove($file.path) }
$changed+=@($old.Keys)
@{playerPrefsMatchBaseline=$prefsUnchanged;restoredNativeUnityEntries=$restoredPrefs;playerFilesUnchanged=$changed.Count -eq 0;changedPlayerFiles=$changed;checkedPlayerFiles=$profileBefore.Count} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'player-data-verification.json') -Encoding UTF8
if(!$prefsUnchanged -or $changed.Count -ne 0) { throw "Player data changed during isolated probe; backup retained in $root" }
$result=Get-Content -Raw -LiteralPath (Join-Path $plugins 'Probe\result.txt')
if($proc.ExitCode -ne 0 -or !$result.StartsWith('PASS')) { throw "MET OFFICE probe failed; inspect $root\BepInEx\plugins\Probe\result.txt" }
Write-Output ("PASS isolated mission assertions="+([regex]::Matches($result,'(?m)^PASS ').Count)+" evidence=$root")
Write-Output "Player preferences and $($profileBefore.Count) player files unchanged."


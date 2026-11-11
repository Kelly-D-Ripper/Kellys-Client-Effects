param([string]$GameDir='C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option')
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$sdk=Join-Path $workspace 'build-tools\dotnet-sdk\dotnet.exe'
$env:NUCLEAR_OPTION_DIR=$GameDir
$stamp=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$stage=Join-Path $PSScriptRoot ('staging\build-'+$stamp)
$rebuilt=Join-Path $stage 'rebuilt'
$prepared=Join-Path $stage 'prepared'
New-Item -ItemType Directory -Path $rebuilt,$prepared -Force | Out-Null
foreach($project in @('Core\BoscaliClientExperimental.csproj','Krop\KROP-Unified.csproj','CockpitAudio\CockpitAudio.csproj','CockpitFeel\CockpitFeel.csproj','MissileBoom\ExperimentalMissileBoom.csproj','Controls\ClientEffectsControlPanel.csproj')) {
    & $sdk build (Join-Path $PSScriptRoot $project) -c Release "-p:GameDir=$GameDir" --nologo
    if($LASTEXITCODE -ne 0){throw "Build failed: $project"}
}
foreach($pair in @(@('Core\bin\Release\net472\BoscaliUnified.dll','BoscaliUnified.dll'),@('Krop\bin\Release\net472\KROP.UnifiedEffects.dll','KROP.UnifiedEffects.dll'),@('CockpitAudio\bin\Release\net472\Boscali.CockpitAudio.dll','Boscali.CockpitAudio.dll'),@('CockpitFeel\bin\Release\net472\Boscali.CockpitFeel.dll','Boscali.CockpitFeel.dll'),@('MissileBoom\bin\Release\net472\Boscali.UnifiedMissileBoom.dll','Boscali.UnifiedMissileBoom.dll'),@('Controls\bin\Release\net472\ClientEffectsControlPanel.dll','ClientEffectsControlPanel.dll'))) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $pair[0]) -Destination (Join-Path $rebuilt $pair[1])
}
$baseline=Join-Path (Split-Path -Parent $PSScriptRoot) 'Release082Candidate\dist\KellysClientEffects-v0.8.2-CLIENT.zip'
& $sdk run --project (Join-Path $PSScriptRoot 'PayloadTools\PayloadTools.csproj') -c Release -- $baseline $prepared $rebuilt
if($LASTEXITCODE -ne 0){throw 'Private payload preparation failed.'}
Get-ChildItem -LiteralPath $prepared -File | Copy-Item -Destination (Join-Path $PSScriptRoot 'Payloads') -Force
& $sdk build (Join-Path $PSScriptRoot 'Weather\BoscaliUnifiedWeather.csproj') -c Release "-p:GameDir=$GameDir" --nologo
if($LASTEXITCODE -ne 0){throw 'Main effects build failed.'}
# Reuse the exact clean-loader-tested helper; its matching source is under Startup.
$startup=Join-Path $PSScriptRoot 'Startup\Pinned\UnifiedEffectsStartup.dll'
$startupSha='35CC24B43BA7B8837052958E0DA01C3483ABDB44C743C9A723FA57DA0C8DB79A'
if((Get-FileHash -LiteralPath $startup).Hash -ne $startupSha){throw 'Pinned startup helper mismatch.'}
$target=Join-Path $PSScriptRoot 'Startup\bin\Release\net472\UnifiedEffectsStartup.dll'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
Copy-Item -LiteralPath $startup -Destination $target -Force
if((Get-FileHash -LiteralPath $target).Hash -ne $startupSha){throw 'Startup helper staging mismatch.'}
Write-Output "Candidate build prepared locally: $stage"

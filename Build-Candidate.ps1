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
foreach($project in @('Krop\KROP-Unified.csproj','MissileBoom\ExperimentalMissileBoom.csproj','Controls\ClientEffectsControlPanel.csproj')) {
    & $sdk build (Join-Path $PSScriptRoot $project) -c Release "-p:GameDir=$GameDir" --nologo
    if($LASTEXITCODE -ne 0){throw "Build failed: $project"}
}
foreach($pair in @(@('Krop\bin\Release\net472\KROP.UnifiedEffects.dll','KROP.UnifiedEffects.dll'),@('MissileBoom\bin\Release\net472\Boscali.UnifiedMissileBoom.dll','Boscali.UnifiedMissileBoom.dll'),@('Controls\bin\Release\net472\ClientEffectsControlPanel.dll','ClientEffectsControlPanel.dll'))) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $pair[0]) -Destination (Join-Path $rebuilt $pair[1])
}
$baseline=Join-Path (Split-Path -Parent $PSScriptRoot) 'Release082Candidate\dist\KellysClientEffects-v0.8.2-CLIENT.zip'
& $sdk run --project (Join-Path $PSScriptRoot 'PayloadTools\PayloadTools.csproj') -c Release -- $baseline $prepared $rebuilt
if($LASTEXITCODE -ne 0){throw 'Private payload preparation failed.'}
Get-ChildItem -LiteralPath $prepared -File | Copy-Item -Destination (Join-Path $PSScriptRoot 'Payloads') -Force
& $sdk build (Join-Path $PSScriptRoot 'Weather\BoscaliUnifiedWeather.csproj') -c Release "-p:GameDir=$GameDir" --nologo
if($LASTEXITCODE -ne 0){throw 'Main effects build failed.'}
# Startup code is unchanged. Reuse its exact released bytes rather than introduce an unrelated rebuild.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$released=Join-Path (Split-Path -Parent $PSScriptRoot) 'SinglePluginCandidate\dist\KellysClientEffects-v0.8.3-CLIENT.zip'
if((Get-FileHash -LiteralPath $released).Hash -ne 'CAA9E4AA43AEBEBBD9010651F9DA9919E1CB35948A3B50B71CEDFD3BE151BB02'){throw 'Released baseline mismatch.'}
$zip=[IO.Compression.ZipFile]::OpenRead($released)
try {
    $entry=$zip.GetEntry('BepInEx/patchers/UnifiedEffectsStartup.dll')
    $target=Join-Path $PSScriptRoot 'Startup\bin\Release\net472\UnifiedEffectsStartup.dll'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$target,$true)
} finally {$zip.Dispose()}
Write-Output "Candidate build prepared locally: $stage"

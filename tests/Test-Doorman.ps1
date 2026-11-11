$ErrorActionPreference='Stop'
$candidate=Split-Path -Parent $PSScriptRoot
$workspace=Split-Path -Parent (Split-Path -Parent $candidate)
$game='C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option'
Add-Type -Path (Join-Path $game 'BepInEx\core\Mono.Cecil.dll')
$testPath=Join-Path $workspace 'KellysDOORMAN\tests\bin\Release\net8.0\Tests.dll'
$tests=[Reflection.Assembly]::LoadFrom($testPath)
$reader=$tests.GetType('KellysJOINCHECK.EmbeddedModPreview',$true).GetMethod('Read',[Reflection.BindingFlags]'Static,NonPublic')
$target=[string](Join-Path $candidate 'Weather\bin\Release\netstandard2.1\BoscaliUnifiedWeather.dll')
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($target)
try {
    $card=$reader.Invoke($null,@($assembly,$target))
    if($null -eq $card) {throw 'DOORMAN rejected the card'}
    function Value($name){$f=$card.GetType().GetField($name,[Reflection.BindingFlags]'Public,NonPublic,Instance');if($null -eq $f){throw 'Missing card property'};return $f.GetValue($card)}
    if((Value 'Plugin') -ne 'aaron.boscali.weather-unified' -or (Value 'Name') -ne 'Client Effects & Weather' -or !(Value 'Description')) {throw 'DOORMAN card identity mismatch'}
    if($null -ne (Value 'Update')) {throw 'Package still needs its startup helper; do not advertise single-DLL-only automatic updates'}
    @{result='PASS';plugin=(Value 'Plugin');name=(Value 'Name');description=(Value 'Description');automaticUpdater='none';weatherSha256=(Get-FileHash -LiteralPath $target).Hash;parserAssemblySha256=(Get-FileHash -LiteralPath $testPath).Hash} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $candidate 'evidence\doorman-parser.json') -Encoding UTF8
    'DOORMAN normal embedded-preview parser accepted the exact candidate; automatic update is absent.'
} finally {$assembly.Dispose()}

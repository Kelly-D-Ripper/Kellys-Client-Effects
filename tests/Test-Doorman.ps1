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
    $conflicts=@(
        @{name='Boscali Summer';plugin='com.marci.boscalisummer'},
        @{name='KROP';plugin='pavehog727.qolrealismfixes'}
    )
    $owner=@($assembly.MainModule.Types | Where-Object { @($_.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInPlugin' -and $_.ConstructorArguments[0].Value -eq (Value 'Plugin') }).Count -eq 1 })
    if($owner.Count -ne 1) {throw 'DOORMAN owner type is ambiguous'}
    $declared=@($owner[0].CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInIncompatibility' } | ForEach-Object { [string]$_.ConstructorArguments[0].Value })
    if((Value 'Description') -notmatch 'Incompatible with standalone Boscali Summer and KROP') {throw 'DOORMAN card must visibly list the standalone incompatibilities'}
    foreach($conflict in $conflicts) {
        if($declared -cnotcontains $conflict.plugin -or $conflict.plugin -ceq (Value 'Plugin')) {throw ('Missing or self-referential loader incompatibility: '+$conflict.name)}
    }
    if((Value 'Description') -notmatch 'NOAtmoFX is a compatible optional bolt-on') {throw 'DOORMAN must distinguish the supported optional bolt-on'}
    if($null -ne (Value 'Update')) {throw 'Package still needs its startup helper; do not advertise single-DLL-only automatic updates'}
    @{result='PASS';plugin=(Value 'Plugin');name=(Value 'Name');description=(Value 'Description');standaloneIncompatibilities=$conflicts;incompatibilityEnforcement='BepInEx plugin attributes; DOORMAN card lists the names';automaticUpdater='none';weatherSha256=(Get-FileHash -LiteralPath $target).Hash;parserAssemblySha256=(Get-FileHash -LiteralPath $testPath).Hash} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $candidate 'evidence\doorman-parser.json') -Encoding UTF8
    'DOORMAN normal embedded-preview parser accepted the exact candidate and both standalone incompatibility notices; loader attributes match and automatic update is absent.'
} finally {$assembly.Dispose()}

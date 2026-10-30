param([switch]$AllMods, [switch]$NpcRegressionOnly, [string]$TestRoot = '')
$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$reuse = [bool]$TestRoot
if (!$TestRoot) {
    $TestRoot = Join-Path $gameRoot ('artifacts\QuitToMenuRuntime-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,6))
} elseif (!(Test-Path -LiteralPath (Join-Path $TestRoot 'quit-to-menu-test.instance'))) {
    throw 'Only an explicitly marked test copy can be reused.'
}
$TestRoot = [IO.Path]::GetFullPath($TestRoot)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $gameRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (!$TestRoot.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Runtime test copies must be inside this game workspace artifacts directory.'
}
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
Set-Content -LiteralPath (Join-Path $testRoot 'quit-to-menu-test.instance') -Value 'Isolated runtime checks. No user saves or profile data.'
$regressionMarker = Join-Path $testRoot 'quit-to-menu-test.npc-regression'
if ($NpcRegressionOnly) { Set-Content -LiteralPath $regressionMarker -Value 'Run the repeated custom NPC spawn regression.' }
elseif (Test-Path -LiteralPath $regressionMarker) { Remove-Item -LiteralPath $regressionMarker }
if (!$reuse) {
foreach ($file in @('Silverpine.exe','UnityPlayer.dll','UnityCrashHandler64.exe','winhttp.dll','doorstop_config.ini')) {
    Copy-Item -LiteralPath (Join-Path $gameRoot $file) -Destination $testRoot
}
Copy-Item -LiteralPath (Join-Path $gameRoot 'MonoBleedingEdge') -Destination $testRoot -Recurse -Force
New-Item -ItemType Directory -Path (Join-Path $testRoot 'Silverpine_Data') -Force | Out-Null
foreach ($item in Get-ChildItem -LiteralPath (Join-Path $gameRoot 'Silverpine_Data')) {
    if ($item.Name -ne 'StreamingAssets') {
        Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $testRoot 'Silverpine_Data') -Recurse -Force
    }
}
New-Item -ItemType Directory -Path (Join-Path $testRoot 'Silverpine_Data\StreamingAssets') -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $gameRoot 'Silverpine_Data\StreamingAssets') -File | Copy-Item -Destination (Join-Path $testRoot 'Silverpine_Data\StreamingAssets') -Force
}
# Reset only the marked, validated test instance so repeat runs stay independent.
foreach ($relative in @('TestData', 'BepInEx\plugins', 'BepInEx\patchers')) {
    $targetPath = [IO.Path]::GetFullPath((Join-Path $testRoot $relative))
    if (!$targetPath.StartsWith($TestRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Test cleanup target escaped its test instance.'
    }
    if (Test-Path -LiteralPath $targetPath) { Remove-Item -LiteralPath $targetPath -Recurse -Force }
}
$bepinex = Join-Path $testRoot 'BepInEx'
New-Item -ItemType Directory -Path $bepinex -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $gameRoot 'BepInEx\core') -Destination $bepinex -Recurse -Force
New-Item -ItemType Directory -Path (Join-Path $bepinex 'plugins'),(Join-Path $bepinex 'patchers'),(Join-Path $bepinex 'config') -Force | Out-Null
if ($AllMods) {
    Get-ChildItem -LiteralPath (Join-Path $gameRoot 'BepInEx\plugins') -File -Filter '*.dll' |
        Where-Object { $_.Name -notin @('QuitToMainMenu.dll', 'ModUpdateChecker.dll') } |
        Copy-Item -Destination (Join-Path $bepinex 'plugins') -Force
    Get-ChildItem -LiteralPath (Join-Path $gameRoot 'BepInEx\patchers') -File -Filter '*.dll' |
        Copy-Item -Destination (Join-Path $bepinex 'patchers') -Force
    foreach ($folder in Get-ChildItem -LiteralPath (Join-Path $gameRoot 'BepInEx\plugins') -Directory) {
        if ($folder.Name -notin @('QuitToMainMenu','ModUpdateChecker')) {
            Copy-Item -LiteralPath $folder.FullName -Destination (Join-Path $bepinex 'plugins') -Recurse -Force
        }
    }
    $fixtureDirectory = Join-Path $bepinex 'config\StartingInventory'
    New-Item -ItemType Directory -Path $fixtureDirectory -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'loadout.json') -Destination $fixtureDirectory -Force
} else {
    Copy-Item -LiteralPath (Join-Path $gameRoot 'BepInEx\plugins\ModdingTools') -Destination (Join-Path $bepinex 'plugins') -Recurse -Force
}
New-Item -ItemType Directory -Path (Join-Path $bepinex 'plugins\QuitToMainMenu') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $gameRoot 'artifacts\ReturnMenuInvestigation-20261029\test-plugin\QuitToMainMenu.dll') -Destination (Join-Path $bepinex 'plugins\QuitToMainMenu')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Isolation\bin\Release\netstandard2.1\QuitToMenuTestIsolation.dll') -Destination (Join-Path $bepinex 'patchers')
$process = Start-Process -FilePath (Join-Path $testRoot 'Silverpine.exe') -ArgumentList '-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile runtime-player.log' -WorkingDirectory $testRoot -WindowStyle Hidden -PassThru
[pscustomobject]@{ TestRoot=$testRoot; ProcessId=$process.Id; AllMods=[bool]$AllMods } | ConvertTo-Json -Compress
$process.WaitForExit()
Write-Output ('Game test exit code: ' + $process.ExitCode)
exit $process.ExitCode

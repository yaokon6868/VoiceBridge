$ErrorActionPreference='Stop'
$checkRoot=Split-Path -Parent $PSScriptRoot
function Invoke-Check([string]$Program,[string[]]$Arguments) {
    & $Program @Arguments
    if($LASTEXITCODE -ne 0){throw "Check failed: $Program $($Arguments -join ' ')"}
}
Invoke-Check 'dotnet' @('build',(Join-Path $checkRoot 'VoiceBridge.csproj'),'-c','Release')
foreach($project in @('Transport','Pipeline','Bridge')) {
    Invoke-Check 'dotnet' @('run','--project',(Join-Path $checkRoot "tests\$project\$project.csproj"),'-c','Release')
}
Invoke-Check 'node' @((Join-Path $checkRoot 'tests\Extension\turn-tracker.cjs'),(Join-Path $checkRoot 'extension\turn-tracker.js'))
Invoke-Check 'node' @((Join-Path $checkRoot 'tests\Extension\content-lifecycle.cjs'),(Join-Path $checkRoot 'extension'))
Invoke-Check 'node' @((Join-Path $checkRoot 'tests\Extension\web-hook-test.cjs'),(Join-Path $checkRoot 'extension\page-hook.js'))
Invoke-Check 'node' @((Join-Path $checkRoot 'tests\Extension\web-mute-test.cjs'),(Join-Path $checkRoot 'extension\background.js'))
Invoke-Check 'node' @((Join-Path $checkRoot 'tests\Extension\bridge-client.cjs'),(Join-Path $checkRoot 'extension\background.js'))
foreach($file in Get-ChildItem -LiteralPath (Join-Path $checkRoot 'extension') -Filter '*.js') {
    Invoke-Check 'node' @('--check',$file.FullName)
}
Write-Output 'All offline checks passed. No Fish API requests were made.'

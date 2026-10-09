param([switch]$FrameworkDependent,[switch]$IncludeAec)
$ErrorActionPreference='Stop'
if(!(Get-Variable IncludeAec -ErrorAction SilentlyContinue)){$IncludeAec=$false}
$packageRepo=Split-Path -Parent $PSScriptRoot
& (Join-Path $packageRepo 'scripts\Check.ps1')
$artifactRoot=Join-Path $packageRepo 'artifacts'
$stageRoot=Join-Path $artifactRoot ('stage-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
$selfContained=if($FrameworkDependent){'false'}else{'true'}
& dotnet publish (Join-Path $packageRepo 'VoiceBridge.csproj') -c Release -r win-x64 --self-contained $selfContained -o (Join-Path $stageRoot 'app')
if($LASTEXITCODE -ne 0){throw 'Publish failed'}
if($IncludeAec){
    $aecBuild=Join-Path $artifactRoot 'aec\dist\VoiceBridge.Aec'
    if(!(Test-Path -LiteralPath (Join-Path $aecBuild 'VoiceBridge.Aec.exe'))){throw 'Run scripts/BuildAec.ps1 first'}
    Copy-Item -LiteralPath $aecBuild -Destination (Join-Path $stageRoot 'app\aec') -Recurse
}
Copy-Item -LiteralPath (Join-Path $packageRepo 'extension') -Destination $stageRoot -Recurse
Copy-Item -LiteralPath (Join-Path $packageRepo 'licenses') -Destination $stageRoot -Recurse
New-Item -ItemType Directory -Path (Join-Path $stageRoot 'docs') -Force | Out-Null
foreach($guide in @('USER_GUIDE.md','USE_CASES.md','AEC-031.md','ACCEPTANCE.md','RELEASE.md','REGRESSION_GATES.md')){
    Copy-Item -LiteralPath (Join-Path $packageRepo ('docs\'+$guide)) -Destination (Join-Path $stageRoot 'docs')
}
New-Item -ItemType Directory -Path (Join-Path $stageRoot 'scripts') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $packageRepo 'scripts\Install.ps1'),(Join-Path $packageRepo 'scripts\Uninstall.ps1') -Destination (Join-Path $stageRoot 'scripts')
if($IncludeAec){Copy-Item -LiteralPath (Join-Path $packageRepo 'scripts\StartCandidate.ps1'),(Join-Path $packageRepo 'scripts\StopCandidate.ps1'),(Join-Path $packageRepo 'scripts\CandidateStartupPolicy.ps1') -Destination (Join-Path $stageRoot 'scripts')}
foreach($file in @('README.md','CHANGELOG.md','ARCHITECTURE.md','CONTRIBUTING.md','SECURITY.md','LICENSE','THIRD_PARTY_NOTICES.md','Install.cmd','Uninstall.cmd')){
    Copy-Item -LiteralPath (Join-Path $packageRepo $file) -Destination $stageRoot
}
Get-ChildItem -LiteralPath (Join-Path $stageRoot 'app') -Filter '*.pdb' | Remove-Item
$zip=Join-Path $artifactRoot 'VoiceBridge-Windows-x64.zip'
Compress-Archive -Path (Join-Path $stageRoot '*') -DestinationPath $zip -Force
(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash | Set-Content -LiteralPath ($zip+'.sha256') -Encoding ascii
Write-Output $zip

param([switch]$FrameworkDependent,[switch]$IncludeAec)
$ErrorActionPreference='Stop'
if(!(Get-Variable IncludeAec -ErrorAction SilentlyContinue)){$IncludeAec=$false}
$packageRepo=Split-Path -Parent $PSScriptRoot
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
New-Item -ItemType Directory -Path (Join-Path $stageRoot 'scripts') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $packageRepo 'scripts\Install.ps1'),(Join-Path $packageRepo 'scripts\Uninstall.ps1') -Destination (Join-Path $stageRoot 'scripts')
if($IncludeAec){Copy-Item -LiteralPath (Join-Path $packageRepo 'scripts\StartCandidate.ps1') -Destination (Join-Path $stageRoot 'scripts')}
foreach($file in @('README.md','LICENSE','THIRD_PARTY_NOTICES.md','Install.cmd','Uninstall.cmd')){
    Copy-Item -LiteralPath (Join-Path $packageRepo $file) -Destination $stageRoot
}
Get-ChildItem -LiteralPath (Join-Path $stageRoot 'app') -Filter '*.pdb' | Remove-Item
$zip=Join-Path $artifactRoot 'VoiceBridge-Windows-x64.zip'
Compress-Archive -Path (Join-Path $stageRoot '*') -DestinationPath $zip -Force
(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash | Set-Content -LiteralPath ($zip+'.sha256') -Encoding ascii
Write-Output $zip

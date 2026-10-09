param([Parameter(Mandatory=$true)][string]$PackageZip)
$ErrorActionPreference='Stop'
$verifyRepo=Split-Path -Parent $PSScriptRoot
$verifyRoot=Join-Path $verifyRepo ('artifacts\install-test-'+[Guid]::NewGuid().ToString('N'))
$extractRoot=Join-Path $verifyRoot 'package'
Expand-Archive -LiteralPath $PackageZip -DestinationPath $extractRoot
foreach($guide in @('USER_GUIDE.md','USE_CASES.md','AEC-031.md')){
    if(!(Test-Path -LiteralPath (Join-Path $extractRoot ('docs\'+$guide)))){throw "Missing packaged guide: $guide"}
}
foreach($privatePath in @('docs\LOCAL_WORKING_STATE.md','settings.json','.git')){
    if(Test-Path -LiteralPath (Join-Path $extractRoot $privatePath)){throw "Private file in package: $privatePath"}
}
if(Test-Path -LiteralPath (Join-Path $extractRoot 'app\aec\VoiceBridge.Aec.exe')){
    foreach($required in @('scripts\StartCandidate.ps1','scripts\StopCandidate.ps1','scripts\CandidateStartupPolicy.ps1','app\aec\licenses')){
        if(!(Test-Path -LiteralPath (Join-Path $extractRoot $required))){throw "Missing AEC package file: $required"}
    }
}
$desktopLink=Join-Path ([Environment]::GetFolderPath('Desktop')) '启动换声工具.lnk'
$before=if(Test-Path -LiteralPath $desktopLink){(Get-FileHash -LiteralPath $desktopLink).Hash}else{''}
$installedRoot=Join-Path $verifyRoot 'installed'
& (Join-Path $extractRoot 'scripts\Install.ps1') -InstallRoot $installedRoot -NoLaunch -NoShortcuts
foreach($file in @('app\VoiceBridge.Next.exe','extension\manifest.json','scripts\Uninstall.ps1','licenses\NAudio.txt','LICENSE','docs\USER_GUIDE.md','docs\USE_CASES.md','docs\AEC-031.md')){
    if(!(Test-Path -LiteralPath (Join-Path $installedRoot $file))){throw "Missing installed file: $file"}
}
$after=if(Test-Path -LiteralPath $desktopLink){(Get-FileHash -LiteralPath $desktopLink).Hash}else{''}
if($before -ne $after){throw 'Installer changed the working desktop shortcut during isolated verification.'}
# Test upgrade of an isolated installation, preserving unrelated files.
'test' | Set-Content -LiteralPath (Join-Path $installedRoot 'user-marker.txt')
& (Join-Path $extractRoot 'scripts\Install.ps1') -InstallRoot $installedRoot -NoLaunch -NoShortcuts
if(!(Test-Path -LiteralPath (Join-Path $installedRoot 'user-marker.txt'))){throw 'Upgrade removed unrelated data.'}
# The public uninstaller must refuse a path outside its named installation root.
$refused=$false
try { & (Join-Path $installedRoot 'scripts\Uninstall.ps1') }catch{$refused=$true}
if(!$refused -or !(Test-Path -LiteralPath (Join-Path $installedRoot 'app\VoiceBridge.Next.exe'))){throw 'Unsafe uninstall scope.'}
Write-Output 'PASS: extracted release installs, upgrades, preserves the live shortcut, and refuses out-of-scope uninstall.'

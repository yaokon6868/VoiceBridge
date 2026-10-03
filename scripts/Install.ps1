param([switch]$StartWithWindows,[switch]$NoLaunch,[switch]$NoShortcuts,[string]$InstallRoot)
$ErrorActionPreference='Stop'
if(!$InstallRoot){$InstallRoot=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'VoiceBridge'}
$InstallRoot=[IO.Path]::GetFullPath($InstallRoot)
$packageRoot=Split-Path -Parent $PSScriptRoot
$appSource=Join-Path $packageRoot 'app'
if(!(Test-Path -LiteralPath (Join-Path $appSource 'VoiceBridge.Next.exe'))){throw 'Please extract the complete release ZIP before installing.'}
$installedExe=Join-Path $InstallRoot 'app\VoiceBridge.Next.exe'
Get-Process 'VoiceBridge.Next' -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $installedExe} | ForEach-Object {
    Stop-Process -Id $_.Id
    if(!$_.WaitForExit(5000)){throw 'The installed application did not exit.'}
}
New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
foreach($directory in @('app','extension','scripts','licenses')){
    New-Item -ItemType Directory -Path (Join-Path $InstallRoot $directory) -Force | Out-Null
    Copy-Item -Path (Join-Path $packageRoot "$directory\*") -Destination (Join-Path $InstallRoot $directory) -Recurse -Force
}
foreach($file in @('README.md','LICENSE','THIRD_PARTY_NOTICES.md','Uninstall.cmd')){
    Copy-Item -LiteralPath (Join-Path $packageRoot $file) -Destination $InstallRoot -Force
}
$shortcutShell=New-Object -ComObject WScript.Shell
function Write-Shortcut([string]$Path){
    $link=$shortcutShell.CreateShortcut($Path);$link.TargetPath=$installedExe
    $link.WorkingDirectory=Split-Path -Parent $installedExe
    $link.Description='VoiceBridge subtitles and voice replacement';$link.Save()
}
if(!$NoShortcuts){Write-Shortcut (Join-Path ([Environment]::GetFolderPath('Desktop')) '启动换声工具.lnk')}
$startupPath=Join-Path ([Environment]::GetFolderPath('Startup')) 'VoiceBridge.lnk'
if(!$NoShortcuts -and $StartWithWindows){Write-Shortcut $startupPath}
elseif(!$NoShortcuts -and (Test-Path -LiteralPath $startupPath)){
    $existing=$shortcutShell.CreateShortcut($startupPath)
    if($existing.TargetPath -eq $installedExe){Remove-Item -LiteralPath $startupPath}
}
Write-Output "Installed: $InstallRoot"
Write-Output "Chrome: open chrome://extensions, enable Developer mode, Load unpacked: $(Join-Path $InstallRoot 'extension')"
Write-Output 'Enable only one VoiceBridge extension. Then refresh ChatGPT and start Voice.'
if(!$NoLaunch){Start-Process -FilePath $installedExe -WorkingDirectory (Split-Path -Parent $installedExe) -WindowStyle Hidden}

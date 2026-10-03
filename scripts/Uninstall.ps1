$ErrorActionPreference='Stop'
$uninstallRoot=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$expectedRoot=[IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'VoiceBridge'))
if($uninstallRoot -ne $expectedRoot){throw 'Run this uninstaller from the installed VoiceBridge directory.'}
$installedExe=Join-Path $uninstallRoot 'app\VoiceBridge.Next.exe'
Get-Process 'VoiceBridge.Next' -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $installedExe} | ForEach-Object {
    Stop-Process -Id $_.Id
    if(!$_.WaitForExit(5000)){throw 'Application did not exit.'}
}
$shortcutShell=New-Object -ComObject WScript.Shell
foreach($linkPath in @((Join-Path ([Environment]::GetFolderPath('Desktop')) '启动换声工具.lnk'),(Join-Path ([Environment]::GetFolderPath('Startup')) 'VoiceBridge.lnk'))){
    if(Test-Path -LiteralPath $linkPath){$link=$shortcutShell.CreateShortcut($linkPath);if($link.TargetPath -eq $installedExe){Remove-Item -LiteralPath $linkPath}}
}
# Both absolute paths above have been checked before recursive removal.
Remove-Item -LiteralPath $uninstallRoot -Recurse -Force
Write-Output 'Uninstalled. Your settings remain in %USERPROFILE%\.voicebridge-next.'
Write-Output 'Remove the VoiceBridge extension from Chrome separately.'

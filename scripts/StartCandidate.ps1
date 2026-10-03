param([switch]$EchoCancellation)
$ErrorActionPreference='Stop'
$candidateRoot=Split-Path -Parent $PSScriptRoot
$candidateExe=Join-Path $candidateRoot 'app\VoiceBridge.Next.exe'
if(!(Test-Path -LiteralPath $candidateExe)){throw 'Candidate application is missing. Extract the full package first.'}
$stableSettings=Join-Path ([Environment]::GetFolderPath('UserProfile')) '.voicebridge-next\settings.json'
$candidateSettings=Join-Path ([Environment]::GetFolderPath('UserProfile')) '.voicebridge-next-candidate\settings.json'
if(!(Test-Path -LiteralPath $candidateSettings) -and (Test-Path -LiteralPath $stableSettings)){
    New-Item -ItemType Directory -Path (Split-Path -Parent $candidateSettings) -Force | Out-Null
    Copy-Item -LiteralPath $stableSettings -Destination $candidateSettings
}
# An explicit candidate launch swaps the running application, not its files or shortcuts.
Get-Process 'VoiceBridge.Next' -ErrorAction SilentlyContinue | ForEach-Object {
    if([IO.Path]::GetFileName($_.Path) -eq 'VoiceBridge.Next.exe'){
        Stop-Process -Id $_.Id
        if(!$_.WaitForExit(5000)){throw 'The previous application did not exit.'}
    }
}
$candidateArgs=@('--candidate')
if($EchoCancellation){$candidateArgs+='--echo-cancel'}
Start-Process -FilePath $candidateExe -ArgumentList $candidateArgs -WorkingDirectory (Split-Path -Parent $candidateExe) -WindowStyle Hidden

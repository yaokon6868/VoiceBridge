param([switch]$EchoCancellation,[switch]$Background)
$ErrorActionPreference='Stop'
$launcherLock=New-Object System.Threading.Mutex($false,'Local\VoiceBridge.CandidateLauncher')
$ownsLauncherLock=$false
$launcherLog=$null
$failureTitle='VoiceBridge 启动失败'
$failureMessage='换声工具启动未完成。请把这条提示告诉我；诊断已保存在 launcher.log。'
try {
try {$ownsLauncherLock=$launcherLock.WaitOne(0)} catch [System.Threading.AbandonedMutexException] {$ownsLauncherLock=$true}
if(!$ownsLauncherLock){return}
$launcherLog=Join-Path ([Environment]::GetFolderPath('UserProfile')) '.voicebridge-next-candidate\launcher.log'
New-Item -ItemType Directory -Path (Split-Path -Parent $launcherLog) -Force | Out-Null
Add-Content -LiteralPath $launcherLog -Encoding UTF8 -Value ((Get-Date -Format o)+' launch requested')
. (Join-Path $PSScriptRoot 'CandidateStartupPolicy.ps1')
$candidateRoot=Split-Path -Parent $PSScriptRoot
$candidateExe=Join-Path $candidateRoot 'app\VoiceBridge.Next.exe'
if(!(Test-Path -LiteralPath $candidateExe)){throw 'Candidate application is missing. Extract the full package first.'}
$stableSettings=Join-Path ([Environment]::GetFolderPath('UserProfile')) '.voicebridge-next\settings.json'
$candidateSettings=Join-Path ([Environment]::GetFolderPath('UserProfile')) '.voicebridge-next-candidate\settings.json'
if(!(Test-Path -LiteralPath $candidateSettings) -and (Test-Path -LiteralPath $stableSettings)){
    New-Item -ItemType Directory -Path (Split-Path -Parent $candidateSettings) -Force | Out-Null
    Copy-Item -LiteralPath $stableSettings -Destination $candidateSettings
}
# Ask verified application instances to exit normally. A failed shutdown must
# never look like a successful launch or leave the user on an empty virtual mic.
$running=Get-Process 'VoiceBridge.Next' -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $candidateExe}
if($running){
    $alreadyReady=$false
    try {
        $session=Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:17892/session' -Headers @{'X-VoiceBridge-Client'='extension'} -TimeoutSec 2
        $headers=@{Authorization=('Bearer '+$session.token);'X-VoiceBridge-Client'='launcher'}
        $health=Invoke-RestMethod -Uri 'http://127.0.0.1:17892/health' -Headers $headers -TimeoutSec 2
        $startupStatus=Get-CandidateStartupStatus -Health $health
        if($startupStatus.NeedsDiagnostics){
            $diagnostics=Invoke-RestMethod -Uri 'http://127.0.0.1:17892/diagnostics' -Headers $headers -TimeoutSec 2
            $startupStatus=Get-CandidateStartupStatus -Health $health -Diagnostics $diagnostics
        }
        $alreadyReady=$startupStatus.Ready
    } catch {}
    if($alreadyReady){
        if(!$Background){Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:17892/show-settings' -Headers $headers -TimeoutSec 3 | Out-Null}
        Add-Content -LiteralPath $launcherLog -Encoding UTF8 -Value ((Get-Date -Format o)+' existing instance reused; background='+$Background)
        return
    }
}
Get-Process 'VoiceBridge.Next' -ErrorAction SilentlyContinue | ForEach-Object {
    $previousInstance=$_
    if([IO.Path]::GetFileName($previousInstance.Path) -eq 'VoiceBridge.Next.exe'){
        $normalExitRequested=$false
        try {
            $bridgeOwner=Get-NetTCPConnection -State Listen -LocalPort 17892 -ErrorAction Stop | Where-Object {$_.OwningProcess -eq $previousInstance.Id}
            if($bridgeOwner){
                $nativeSession=Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:17892/session' -Headers @{'X-VoiceBridge-Client'='extension'} -TimeoutSec 1
                Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:17892/shutdown' -Headers @{Authorization=('Bearer '+$nativeSession.token);'X-VoiceBridge-Client'='launcher'} -TimeoutSec 2 | Out-Null
                $normalExitRequested=$previousInstance.WaitForExit(8000)
            }
        } catch {}
        if($normalExitRequested){return}
        $shutdownArguments=@('--request-exit',$previousInstance.Id.ToString(),('"'+$previousInstance.Path+'"'))
        $shutdown=Start-Process -FilePath $candidateExe -ArgumentList $shutdownArguments -WorkingDirectory (Split-Path -Parent $candidateExe) -WindowStyle Hidden -PassThru
        if(!$shutdown.WaitForExit(45000) -or $shutdown.ExitCode -ne 0 -or !$previousInstance.WaitForExit(2000)){
            $failureTitle='VoiceBridge 启动未完成'
            $failureMessage='旧的 VoiceBridge 无法正常退出。请在任务管理器结束 VoiceBridge.Next.exe 后，再点这个启动入口。新版尚未启动；请先保留实际麦克风，不要选择 CABLE Output。'
            throw 'Previous VoiceBridge did not exit; candidate was not launched.'
        }
    }
}
$candidateArgs=@('--candidate','--desktop')
if($EchoCancellation){$candidateArgs+='--echo-cancel'}
if($Background){$candidateArgs+='--background'}
$launched=Start-Process -FilePath $candidateExe -ArgumentList $candidateArgs -WorkingDirectory (Split-Path -Parent $candidateExe) -WindowStyle Hidden -PassThru
$deadline=[DateTime]::UtcNow.AddSeconds(20)
$startupVerified=$false
$connectionToken=$null
do {
    if($launched.HasExited){break}
    try {
        if(!$connectionToken){
            $session=Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:17892/session' -Headers @{'X-VoiceBridge-Client'='extension'} -TimeoutSec 1
            $connectionToken=$session.token
        }
        $startupHeaders=@{Authorization=('Bearer '+$connectionToken);'X-VoiceBridge-Client'='launcher'}
        $health=Invoke-RestMethod -Uri 'http://127.0.0.1:17892/health' -Headers $startupHeaders -TimeoutSec 1
        $startupStatus=Get-CandidateStartupStatus -Health $health
        if($startupStatus.NeedsDiagnostics){
            $diagnostics=Invoke-RestMethod -Uri 'http://127.0.0.1:17892/diagnostics' -Headers $startupHeaders -TimeoutSec 1
            $startupStatus=Get-CandidateStartupStatus -Health $health -Diagnostics $diagnostics
        }
        if($startupStatus.Ready){
            $startupVerified=$true
            break
        }
    } catch {$connectionToken=$null}
    Start-Sleep -Milliseconds 300
} while([DateTime]::UtcNow -lt $deadline)
if(!$startupVerified){
    $failureTitle='VoiceBridge 启动检查失败'
    $failureMessage='VoiceBridge 的连接服务或回声处理组件没有就绪。请先保留实际麦克风，不要选择 CABLE Output。程序尚未通过启动检查。'
    throw 'Candidate startup health check failed.'
}
if(!$Background){Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:17892/show-settings' -Headers @{Authorization=('Bearer '+$connectionToken);'X-VoiceBridge-Client'='launcher'} -TimeoutSec 3 | Out-Null}
Add-Content -LiteralPath $launcherLog -Encoding UTF8 -Value ((Get-Date -Format o)+' startup verified; background='+$Background)
} catch {
    if($launcherLog){Add-Content -LiteralPath $launcherLog -Encoding UTF8 -Value ((Get-Date -Format o)+' failure '+$_.Exception.GetType().Name+' line '+$_.InvocationInfo.ScriptLineNumber)}
    if(!$Background){
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show($failureMessage,$failureTitle) | Out-Null
    }
    throw
} finally {
    if($ownsLauncherLock){$launcherLock.ReleaseMutex()}
    $launcherLock.Dispose()
}

param([switch]$Quiet)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$expected=Join-Path $root 'app\VoiceBridge.Next.exe'
$gate=New-Object System.Threading.Mutex($false,'Local\VoiceBridge.CandidateLauncher')
$held=$false
try {
    try {$held=$gate.WaitOne(20000)} catch [System.Threading.AbandonedMutexException] {$held=$true}
    if(!$held){throw '启动操作仍未结束，暂未关闭程序。'}
    $main=@(Get-Process 'VoiceBridge.Next' -ErrorAction SilentlyContinue)
    if($main.Count -eq 0){Write-Output 'VoiceBridge already stopped.';return}
    if($main.Count -ne 1 -or $main[0].Path -ne $expected){throw '检测到其他版本，未关闭未知程序。'}
    $owner=Get-NetTCPConnection -State Listen -LocalPort 17892 -ErrorAction Stop | Where-Object {$_.OwningProcess -eq $main[0].Id}
    if(!$owner){throw '连接服务未响应，正常退出尚未完成。'}
    $session=Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:17892/session' -Headers @{'X-VoiceBridge-Client'='extension'} -TimeoutSec 2
    $headers=@{Authorization=('Bearer '+$session.token);'X-VoiceBridge-Client'='launcher'}
    $helper=@(Get-Process 'VoiceBridge.Aec' -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq (Join-Path $root 'app\aec\VoiceBridge.Aec.exe')})
    Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:17892/shutdown' -Headers $headers -TimeoutSec 3 | Out-Null
    if(!$main[0].WaitForExit(8000)){throw '主程序尚未完成正常退出。'}
    foreach($child in $helper){if(!$child.WaitForExit(8000)){throw '回声处理组件尚未退出。'}}
    Write-Output 'VoiceBridge and its AEC helper exited normally.'
} catch {
    if(!$Quiet){
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show($_.Exception.Message,'VoiceBridge 关闭未完成') | Out-Null
    }
    throw
} finally {
    if($held){$gate.ReleaseMutex()}
    $gate.Dispose()
}

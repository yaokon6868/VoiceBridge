$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$policyPath = Join-Path $repoRoot 'scripts\CandidateStartupPolicy.ps1'
$launcherPath = Join-Path $repoRoot 'scripts\StartCandidate.ps1'
$failures = New-Object 'System.Collections.Generic.List[string]'
$checks = 0

if (Test-Path -LiteralPath $policyPath) { . $policyPath }

function Check([bool]$Pass, [string]$Name) {
    $script:checks++
    Write-Output ((@('FAIL', 'PASS')[[int]$Pass]) + ': ' + $Name)
    if (!$Pass) { $script:failures.Add($Name) }
}

function Health([bool]$Enabled = $true, [bool]$Ready = $true) {
    return [pscustomobject]@{
        ok = $true
        service = 'VoiceBridge Next'
        version = '0.3.1-candidate'
        instanceId = 'offline-policy-instance'
        ttsReady = $Ready
        aecReady = $Ready
        aecEnabled = $Enabled
        startup = [pscustomobject]@{ profile = 'candidate'; desktopRequested = $true; background = $false }
    }
}

function Check-Policy($Health, $Diagnostics, [bool]$Ready, [bool]$NeedsDiagnostics, [string]$Name) {
    if (!(Get-Command Get-CandidateStartupStatus -ErrorAction SilentlyContinue)) {
        Check $false ($Name + ' (startup policy is missing)')
        return
    }
    $result = Get-CandidateStartupStatus -Health $Health -Diagnostics $Diagnostics
    Check ($result.Ready -eq $Ready -and $result.NeedsDiagnostics -eq $NeedsDiagnostics) $Name
}

Check-Policy (Health) $null $true $false 'normal candidate startup is ready'
$wrongVersion = Health
$wrongVersion.version = '0.3.0-preview'
Check-Policy $wrongVersion $null $false $false 'wrong application version cannot be reused'
$wrongProfile = Health
$wrongProfile.startup.profile = 'standard'
Check-Policy $wrongProfile $null $false $false 'wrong configuration profile cannot be reused'
$noDesktop = Health
$noDesktop.startup.desktopRequested = $false
Check-Policy $noDesktop $null $false $false 'missing desktop startup request cannot be reused'
$failedService = Health
$failedService.ok = $false
Check-Policy $failedService $null $false $false 'failed bridge health cannot be reused'
Check-Policy (Health $false $false) $null $true $false 'saved disabled AEC remains a reusable healthy instance'
Check-Policy (Health $true $true) $null $true $false 'enabled AEC reported ready needs no additional request'
$withoutKey = Health $true $false
Check-Policy $withoutKey $null $false $true 'enabled AEC without TTS readiness requires component diagnostics'
$readyComponent = [pscustomobject]@{ echoCancellation = [pscustomobject]@{ ready = $true; error = ''; data = $null } }
Check-Policy $withoutKey $readyComponent $true $false 'no Key does not block a genuinely ready AEC component'
$unreadyComponent = [pscustomobject]@{ echoCancellation = [pscustomobject]@{ ready = $false; error = ''; data = $null } }
Check-Policy $withoutKey $unreadyComponent $false $false 'unready AEC component is never accepted'
Check-Policy $withoutKey ([pscustomobject]@{}) $false $false 'missing component readiness is never accepted'
$missingPreference = Health
$missingPreference.PSObject.Properties.Remove('aecEnabled')
Check-Policy $missingPreference $readyComponent $false $false 'missing saved AEC state cannot bypass readiness checks'
Check-Policy $wrongVersion $readyComponent $false $false 'ready component cannot override a wrong application identity'

# Run the real launcher with controlled process and HTTP responses. All external
# boundaries are replaced before invoking it: no user files, real mutex, network,
# application process or UI can be touched by these checks.
function Invoke-LauncherProbe([string]$Mode) {
    $probe = [pscustomobject]@{
        UiAttempts = 0; Logs = New-Object 'System.Collections.Generic.List[string]'; Threw = $false
        Starts = 0; Diagnostics = 0; NativeDiagnostics = $true; SettingsRequests = 0; PassedBackground = $false
    }
    $testExe = Join-Path $repoRoot 'app\VoiceBridge.Next.exe'
    $existing = [pscustomobject]@{ Path = $testExe; Id = 910001 }
    $existing | Add-Member -MemberType ScriptMethod -Name WaitForExit -Value { param($timeout) return $false }
    function New-Object {
        param([string]$TypeName, [object[]]$ArgumentList)
        if ($TypeName -ne 'System.Threading.Mutex') { throw 'Unexpected object construction in isolated launcher test.' }
        $mutex = [pscustomobject]@{}
        $mutex | Add-Member -MemberType ScriptMethod -Name WaitOne -Value { param($timeout) return $true }
        $mutex | Add-Member -MemberType ScriptMethod -Name ReleaseMutex -Value { }
        $mutex | Add-Member -MemberType ScriptMethod -Name Dispose -Value { }
        return $mutex
    }
    function Test-Path { param([string]$LiteralPath) return $LiteralPath -eq $testExe -and $Mode -ne 'package-failure' }
    function New-Item { param([string]$ItemType, [string]$Path, [switch]$Force) }
    function Add-Content { param([string]$LiteralPath, [string]$Encoding, [string]$Value) $probe.Logs.Add($Value) }
    function Add-Type { param([string]$AssemblyName) $probe.UiAttempts++; throw 'UI is forbidden in the offline launcher test.' }
    function Get-Process {
        param([string]$Name, [string]$ErrorAction)
        if ($Mode -in @('shutdown-failure', 'disabled-reuse', 'component-reuse')) { return $existing }
    }
    function Get-NetTCPConnection { throw 'No real listener is used by the isolated launcher test.' }
    function Invoke-RestMethod {
        param([string]$Method, [string]$Uri, $Headers, [int]$TimeoutSec)
        switch ($Uri) {
            'http://127.0.0.1:17892/session' { return [pscustomobject]@{ token = 'offline-policy-token' } }
            'http://127.0.0.1:17892/health' {
                if ($Mode -eq 'disabled-reuse') { return Health $false $false }
                if ($Mode -eq 'shutdown-failure') {
                    $outdated = Health
                    $outdated.version = '0.3.0-preview'
                    return $outdated
                }
                return Health $true $false
            }
            'http://127.0.0.1:17892/diagnostics' {
                $probe.Diagnostics++
                $probe.NativeDiagnostics = $probe.NativeDiagnostics -and
                    $Headers.Authorization -ceq 'Bearer offline-policy-token' -and
                    $Headers['X-VoiceBridge-Client'] -ceq 'launcher'
                return [pscustomobject]@{ echoCancellation = [pscustomobject]@{ ready = $true; error = ''; data = $null } }
            }
            'http://127.0.0.1:17892/show-settings' { $probe.SettingsRequests++; return }
            default { throw ('Unexpected HTTP request in isolated launcher test: ' + $Uri) }
        }
    }
    function Start-Process {
        param([string]$FilePath, [object[]]$ArgumentList, [string]$WorkingDirectory, [string]$WindowStyle, [switch]$PassThru)
        $probe.Starts++
        $probe.PassedBackground = $ArgumentList -contains '--background'
        $process = [pscustomobject]@{ HasExited = $Mode -ne 'component-startup'; ExitCode = 1 }
        $process | Add-Member -MemberType ScriptMethod -Name WaitForExit -Value { param($timeout) return $false }
        return $process
    }
    function Start-Sleep { param([int]$Milliseconds) }
    try { & $launcherPath -Background } catch { $probe.Threw = $true }
    return $probe
}
$failureProbe = Invoke-LauncherProbe 'package-failure'
Check ($failureProbe.Threw -and $failureProbe.Logs.Count -ge 2 -and $failureProbe.UiAttempts -eq 0) 'background package failure is logged and thrown without attempting UI'
$shutdownProbe = Invoke-LauncherProbe 'shutdown-failure'
Check ($shutdownProbe.Threw -and $shutdownProbe.Logs.Count -ge 2 -and $shutdownProbe.Starts -eq 1 -and $shutdownProbe.UiAttempts -eq 0) 'background normal-exit failure is logged and thrown without attempting UI'
$startupProbe = Invoke-LauncherProbe 'startup-failure'
Check ($startupProbe.Threw -and $startupProbe.Logs.Count -ge 2 -and $startupProbe.Starts -eq 1 -and $startupProbe.UiAttempts -eq 0) 'background startup-check failure is logged and thrown without attempting UI'
$disabledProbe = Invoke-LauncherProbe 'disabled-reuse'
Check (!$disabledProbe.Threw -and $disabledProbe.Starts -eq 0 -and $disabledProbe.Diagnostics -eq 0 -and $disabledProbe.SettingsRequests -eq 0 -and $disabledProbe.UiAttempts -eq 0) 'real launcher reuses disabled AEC without restarting or opening settings'
$componentReuseProbe = Invoke-LauncherProbe 'component-reuse'
Check (!$componentReuseProbe.Threw -and $componentReuseProbe.Starts -eq 0 -and $componentReuseProbe.Diagnostics -eq 1 -and $componentReuseProbe.NativeDiagnostics -and $componentReuseProbe.SettingsRequests -eq 0) 'real launcher authenticates component diagnostics before reusing an instance without a Key'
$componentStartupProbe = Invoke-LauncherProbe 'component-startup'
Check (!$componentStartupProbe.Threw -and $componentStartupProbe.Starts -eq 1 -and $componentStartupProbe.Diagnostics -eq 1 -and $componentStartupProbe.NativeDiagnostics -and $componentStartupProbe.PassedBackground -and $componentStartupProbe.SettingsRequests -eq 0) 'new background startup authenticates component diagnostics and forwards background mode'

Write-Output ('Launcher policy checks: ' + ($checks - $failures.Count) + '/' + $checks + ' passed. No user settings, audio engines, UI or Fish API were used.')
if ($failures.Count -gt 0) { throw ('Launcher policy checks failed: ' + ($failures -join '; ')) }

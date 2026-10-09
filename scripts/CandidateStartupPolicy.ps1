# This policy only evaluates supplied responses. Pairing, HTTP requests, process
# lifecycle and user notification remain the launcher's responsibility.
function Get-CandidateStartupStatus {
    param($Health, $Diagnostics = $null)

    $result = [pscustomobject]@{ Ready = $false; NeedsDiagnostics = $false }
    if ($null -eq $Health -or $Health.ok -isnot [bool] -or !$Health.ok -or
        $Health.version -cne '0.3.1-candidate' -or
        $Health.startup.profile -cne 'candidate' -or
        $Health.startup.desktopRequested -isnot [bool] -or !$Health.startup.desktopRequested -or
        $Health.aecEnabled -isnot [bool]) {
        return $result
    }

    # The runtime preference is authoritative, including a user who deliberately
    # disabled AEC. The launcher's compatibility switch must not override it.
    if (!$Health.aecEnabled -or ($Health.aecReady -is [bool] -and $Health.aecReady)) {
        $result.Ready = $true
    } elseif ($null -eq $Diagnostics) {
        # health.aecReady also depends on TTS settings. Check the actual engine
        # when a missing Key/voice prevents that combined readiness flag.
        $result.NeedsDiagnostics = $true
    } elseif ($Diagnostics.echoCancellation.ready -is [bool] -and $Diagnostics.echoCancellation.ready) {
        $result.Ready = $true
    }
    return $result
}

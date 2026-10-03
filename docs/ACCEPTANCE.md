# Release acceptance

Never mark real-device checks complete based only on offline tests.

| Scenario | Offline coverage | Real-device status |
|---|---|---|
| Web text, captions and Fish output | Pipeline/transport | User confirmed previous build |
| Authenticated extension/app pairing | Bridge/client | Pending current candidate |
| Comma phrases and revised snapshots | Pipeline | User preferred working behavior |
| DOM ID changes do not replay replies | Tracker/lifecycle | Repeat candidate check |
| Fish faults unlock official voice | Transport/mute | Pending |
| Spoken interruption clears old audio | Router/epochs | Pending |
| Normal and isolated Codex | Source isolation | Pending both end-to-end |
| No white popup after exit | Cleanup code | Pending 10 exit/relaunch cycles |
| Wrong/removed input or output device | Partial metadata | Pending |
| 30-minute session and restarts | Partial reconnect | Pending |
| Speaker barge-in with no self-transcription | No accepted AEC | Not stable-supported |
| Fresh checkout build and packaging | Check.ps1, Package.ps1 | Passed on maintainer Windows PC, 2026-10-03 |

Before stable release test on a second Windows 11 PC. Record first text, first
Fish byte and first audible audio separately; network latency is not audible latency.

Publication-preparation checks on 2026-10-03: clean source export build, complete
offline suite, self-contained x64 ZIP, isolated installation and upgrade, unchanged
working desktop shortcut, and out-of-scope uninstall rejection passed. The suite
made no Fish API calls. The initial GitHub CI run passed. The 0.3.1 candidate
adds AEC signal, microphone fallback and backpressure checks; actual speaker
acceptance remains pending, tracked in [AEC-031.md](AEC-031.md).

The previously running preview also reported receiver:InvalidOperationException
with its fallback circuit open during this review. The exception's original stack
was not recorded, so its cause is unconfirmed. Real extended-session recovery is
still a release-candidate acceptance requirement; do not describe it as resolved.

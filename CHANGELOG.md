# Changelog

## 0.3.1 candidate — duplex echo architecture

- Optional bundled physical playback/microphone engine with adaptive echo reference.
- Acoustic delay estimation when device timestamps are insufficient.
- Speech interruption without muting the microphone; old audio epochs discarded.
- Bounded asynchronous playback with cancellation independent of queue backpressure.
- Stable web microphone graph with physical fallback on helper loss/restart.
- Device selection, isolated candidate settings, offline microphone and signal checks.

Not accepted as a speaker-ready release; see docs/AEC-031.md for measured limits.

## 0.3.0 Beta — publication preparation

- Self-contained x64 packaging, per-user installer/uninstaller and stable shortcut.
- Repository-contained offline tests and Windows CI.
- Local API tokens, website-origin rejection and event validation.
- Extension automatic session renewal after application restart.
- Settings/tray styling, icon, popup close ordering and debounced position saving.
- DOM committed-identity deduplication, with later identical replies allowed.
- Documentation, MIT license, third-party notices and issue template.

Limitations: speaker AEC and broad Codex compatibility are experimental; manual
extension loading/reloading remains necessary. No signed installer or store release.

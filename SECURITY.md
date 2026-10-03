# Security and privacy

Use GitHub private vulnerability reporting if the maintainer enables it. Otherwise
open a minimal issue asking for a private contact, without credentials or exploit details.

Assistant text goes to Fish Audio. The bridge does not record the microphone or
capture Windows system audio. The web adapter observes assistant text and track
metadata; Codex uses Accessibility. The key is current-user DPAPI ciphertext outside Git.

The API is loopback-only and requires a random per-process session token. Website
origins and cross-site bootstrap are rejected; no CORS is granted. The bootstrap
requires a custom header a normal website cannot send through a successful preflight.

This does not isolate the app from malicious programs running as the same Windows
user or installed extensions with loopback permission. They are trusted native/extension
clients and can bootstrap. Page-message text is trusted as coming from ChatGPT;
a compromised page could supply misleading text. No independent comprehensive
security audit has been completed.

Never upload configuration, DPAPI ciphertext, .env, private keys, conversations or
personal screenshots. Diagnostics may disclose device labels, time and session IDs;
review and redact before sharing.

The opt-in 0.3.1 duplex engine captures only the selected physical microphone
and renders only VoiceBridge's Fish PCM. Microphone frames stay in volatile
memory and flow to the virtual input; no microphone recording or upload is
implemented by the engine. The user-selected ChatGPT/Codex voice application
still transmits its input to its own service. The helper IPC on 17896 binds
loopback, requires a random token and rejects browser Origins. The helper exits
when its parent process exits. Enabling the helper keeps the selected microphone
open until disabled or VoiceBridge exits; Windows defaults are not changed.

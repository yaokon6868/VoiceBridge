# Publishing a Beta

1. Build the optional bundled engine with scripts/BuildAec.ps1. For v0.3.1-beta,
   run scripts/Package.ps1 -IncludeAec (which runs Check.ps1), then
   scripts/VerifyPackage.ps1 against the resulting ZIP.
2. Inspect the Git file list and the source ZIP for settings, private paths and secrets.
3. Test the candidate app and extension together on a real ChatGPT Voice session.
   Do not replace a working install before this test; exit it for the candidate test.
4. Create the repository with LICENSE, source, tests and docs; do not add artifacts or binaries to Git.
5. Confirm the Windows workflow passes on GitHub. This has not been established by local checks.
6. Create a **pre-release**, attach VoiceBridge-Windows-x64.zip and its SHA-256 file,
   and describe remaining AEC/Codex limitations. Do not label it stable.

Release v0.3.1-beta keeps the tested internal 0.3.1-candidate identity. Changing
that string alone changes the settings and desktop-startup profile. Do not
rename it without a separate migration design and verification.

Package only the explicitly listed public guides; exclude LOCAL_WORKING_STATE.md,
maintainer artifacts, user settings, credentials and diagnostic logs.

The local source ZIP is a clean source export for review, not a Git repository with
history. The install ZIP is intended for end users. Browser installation is still
manual. No credentials or Windows user settings belong in either ZIP.

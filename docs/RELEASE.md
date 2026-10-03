# Publishing a Beta

1. Run scripts/Check.ps1 on Windows, then scripts/Package.ps1 and scripts/VerifyPackage.ps1.
2. Inspect the Git file list and the source ZIP for settings, private paths and secrets.
3. Test the candidate app and extension together on a real ChatGPT Voice session.
   Do not replace a working install before this test; exit it for the candidate test.
4. Create the repository with LICENSE, source, tests and docs; do not add artifacts or binaries to Git.
5. Confirm the Windows workflow passes on GitHub. This has not been established by local checks.
6. Create a **pre-release**, attach VoiceBridge-Windows-x64.zip and its SHA-256 file,
   and describe remaining AEC/Codex limitations. Do not label it stable.

The local source ZIP is a clean source export for review, not a Git repository with
history. The install ZIP is intended for end users. Browser installation is still
manual. No credentials or Windows user settings belong in either ZIP.

# ScreenFast Local Release Notes

## Local Build Prerequisites
- Windows 10 2004 (build 19041) or later.
- .NET 8 SDK installed.
- WinUI 3 / Windows App SDK restore working locally.
- Media Foundation available on the machine.

## First Local Release Assumptions
- This repo currently targets an unpackaged WinUI 3 desktop build.
- Publish profile: `ScreenFast.App/Properties/PublishProfiles/LocalFolder.pubxml`.
- App data, logs, settings, recovery state, and history live under `%LocalAppData%\ScreenFast`.

## Known Limitations For V1
- The overlay is lightweight and may still be visible in some full-display captures.
- Source resize during capture fails fast instead of adapting mid-recording.
- Disk-space checks are conservative heuristics, not duration-aware estimates.
- System audio and microphone capture prioritize stable playable MP4 output over studio-grade sync.

## First Manual Smoke Test
- Launch the app and confirm startup completes without crashes.
- Review the startup readiness summary in the main window.
- Select a source and output folder.
- Record video-only, then stop and confirm the MP4 plays.
- Record with system audio enabled.
- Record with microphone enabled.
- Record with pause/resume and confirm the MP4 remains playable.
- Verify tray, hotkeys, countdown, diagnostics export, and recovery notice behavior.

## Logs And Diagnostics
- Logs: `%LocalAppData%\ScreenFast\Logs`
- Settings: `%LocalAppData%\ScreenFast\settings.json`
- History: `%LocalAppData%\ScreenFast\recording-history.json`
- Recovery marker: `%LocalAppData%\ScreenFast\active-session.json`
- Diagnostics export: use the in-app `Export Diagnostics` action.

# ScreenFast Repo Skill

## Purpose

This file gives Codex repo-specific operating guidance for ScreenFast. It helps choose the right working mode for tasks in this repository and keeps future work aligned with the product direction in `PLAN.md`.

ScreenFast is a native Windows recorder evolving into a recorder plus render/presentation engine. The default bias is to keep raw capture stable while building future zoom, styling, and polished export features through metadata and a second-pass render pipeline.

## Skill Trigger Map

### A. Build / Startup / Config Skill

Trigger when the task mentions:

- Visual Studio build errors.
- `.sln`, `.slnx`, `.csproj`, props, targets, or configuration mapping issues.
- Runtime identifier problems.
- Startup project problems.
- Windows SDK, Windows App SDK, packaging, publish, or local release issues.

How to work:

- Inspect the solution, project files, `Directory.Build.props`, `Directory.Build.targets`, publish profiles, startup config, and local release notes first.
- Prefer narrow fixes that restore build/startup reliability.
- Do not guess blindly at SDK, platform, or packaging settings.
- Keep generated `bin` and `obj` output out of intentional source changes.
- Report the exact command used for validation and whether it ran successfully.

### B. Native Capture / Audio / Encoding Skill

Trigger when the task mentions:

- Screen capture.
- Source picker or capture item resolution.
- WASAPI, system audio, microphone, or audio sync.
- Media Foundation or MP4 finalization.
- WinUI interop related to capture.
- Hotkeys, tray behavior, overlay, countdown, or recorder lifecycle.

How to work:

- Preserve reliability first.
- Change the smallest adjacent code needed to fix the issue.
- Call out lifecycle, disposal, threading, and finalization risks.
- Protect MP4 finalization and avoid changes that can leave corrupted output.
- Verify video-only, system-audio, microphone, pause/resume, stop, and playback behavior when possible.

### C. Product Architecture / Planning Skill

Trigger when the task mentions:

- New major features.
- Roadmap decisions.
- Architecture direction.
- Render pipeline discussions.
- Auto-zoom, styling, presets, presentation output, or creator workflow.

How to work:

- Consult `PLAN.md` first.
- Decide whether the work belongs in the Capture Engine or the Render / Presentation Engine.
- Avoid collapsing capture, metadata, planning, rendering, and UI customization into one brittle subsystem.
- Keep docs current when the product direction changes materially.
- Prefer a design pass before implementation if the change affects persistence, metadata, encoding, rendering, or app workflow.

### D. Zoom / Styling / Render Skill

Trigger when the task mentions:

- Cursor tracking.
- Cursor telemetry.
- Auto-zoom.
- Cursor-follow behavior.
- Click emphasis, click rings, spotlight, or interaction highlighting.
- Background colors, gradients, images, padding, shadows, rounded frames, or composition.
- Export presets or polished render output.

How to work:

- Prefer metadata-first design.
- Prefer a second-pass render/export pipeline.
- Define camera path, easing, safe margins, coordinate spaces, and scene composition explicitly.
- Keep cursor samples and click events as first-class timeline artifacts.
- Do not stuff advanced rendering into the live recorder path without a strong documented reason.
- Keep raw recordings recoverable even if planning or styled export fails.

### E. Diagnostics / Reliability Skill

Trigger when the task mentions:

- Logging.
- Diagnostics export.
- Recovery.
- Recording history.
- Preflight validation.
- Smoke checks.
- Startup readiness.
- Disk-space checks or failure reporting.

How to work:

- Preserve observability.
- Keep failure states user-friendly and actionable.
- Keep logs useful for root-cause analysis without becoming noisy.
- Preserve recoverability after interrupted sessions.
- Avoid swallowing exceptions without diagnostics.
- Validate that diagnostics and recovery artifacts still point users toward the next useful action.

## Decision Rules

- When in doubt, keep the recorder stable.
- When a task changes product direction, update `PLAN.md`.
- When a task touches auto-zoom or styling, think in terms of metadata, render planning, and second-pass export.
- When a task touches capture, avoid unnecessary coupling to visual presentation features.
- When a task touches encoding, protect MP4 finalization and output playability.
- When a task touches persistence, preserve existing settings, history, recovery, and diagnostics behavior.
- When the environment cannot compile or run the app, be explicit and provide manual verification guidance.
- When generated or unrelated files are already dirty, do not revert them unless explicitly asked.

## Anti-Patterns

Avoid:

- Bolting advanced render features directly into fragile capture code without design.
- Broad refactors with no product gain.
- Undocumented architecture drift.
- Fake "done" states when compile or runtime validation did not happen.
- Adding complexity that weakens the current recorder foundation.
- Introducing web-stack detours for native recorder responsibilities.
- Mixing cleanup, formatting churn, and behavior changes in one patch.
- Claiming aspirational render features are implemented before they exist.
- Hiding important decisions only in code comments.

## Preferred Output Shape

For future ScreenFast tasks, responses should include:

- What changed.
- Why it changed.
- Risks or affected systems.
- Validation status.
- Next best step.

For reviews, lead with concrete findings and file references. For implementation work, keep the summary concise and honest about what was and was not verified.

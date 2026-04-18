# ScreenFast Agent Operating Manual

## Mission

This repository is building ScreenFast: a native Windows screen recorder that is evolving into a recorder plus polished export engine.

Agents working here should protect the existing recorder foundation while moving the product toward cursor-aware smart zoom, styled composition, and presentation-ready exports.

## Mandatory Working Rules

- Read `PLAN.md` before implementing major features or changing product direction.
- Respect the existing native Windows architecture. This is a WinUI/.NET Windows app with capture, audio, encoding, infrastructure, and app-shell projects.
- Do not introduce web-stack detours for the core recorder or render engine.
- Do not destabilize the current recorder when adding future render features.
- Prefer small, surgical changes over broad refactors.
- Preserve diagnostics, recovery, structured logging, history, startup checks, and user-friendly failure states.
- Prefer root-cause fixes over surface patches.
- Do not invent hidden architecture. Document major new subsystems and update direction docs when product or architecture changes materially.
- Keep implemented reality separate from aspiration. Docs and final reports must be honest about what exists and what is still planned.

## Repo Understanding Rules

Before coding:

- Inspect the relevant files and current implementation.
- Do not assume a feature is missing without checking the repo.
- Identify whether the task touches capture, audio, encoding, shell, persistence, diagnostics, or planned rendering.
- Understand lifecycle effects before editing start, stop, pause, resume, shutdown, tray, hotkey, overlay, recovery, or encoder paths.
- Check existing abstractions and service boundaries before adding new ones.

The current project shape is intentionally native and modular:

- App shell and orchestration live in the app project.
- Core models, interfaces, results, and recorder state live in the core project.
- Screen capture lives in the capture project.
- Audio capture lives in the audio project.
- Encoding lives in the encoding project.
- Settings, logging, diagnostics, history, recovery, filenames, and preflight behavior live in infrastructure.

Use that shape unless there is a documented reason to change it.

## Change Strategy

For bug fixes:

- Make the smallest safe diff that fixes the root cause.
- Preserve adjacent behavior unless the bug requires changing it.
- Explain the failure mode and why the fix addresses it.

For new features:

- Do a design pass first if the feature affects architecture, capture lifecycle, encoding, metadata, rendering, persistence, or user workflow.
- Keep feature slices narrow and shippable.
- Add or update docs when a new subsystem or product direction is introduced.

For cross-cutting changes:

- State the risk areas explicitly.
- Verify startup, recording, pause/resume, stop/finalization, diagnostics, recovery, hotkeys, tray, and history behavior when adjacent systems are touched.
- Avoid broad cleanup mixed with behavior changes.

For major product-direction work:

- Update `PLAN.md` first or alongside the implementation.
- Keep roadmap and architecture language aligned with actual code.
- Do not let aspirational docs claim features are shipped before they exist.

## Critical Architectural Guardrails

- The Capture Engine and Render / Presentation Engine should remain conceptually distinct.
- Capture is responsible for reliable raw recording and capture-adjacent metadata.
- Render/presentation is responsible for auto-zoom, camera paths, styled composition, backgrounds, frame treatments, and polished exports.
- Future auto-zoom and styling work should prefer metadata plus render composition over brittle direct-capture hacks.
- Native recorder stability takes priority over ambitious visual features in a single unsafe step.
- Cursor telemetry should become a first-class timeline artifact before full auto-zoom.
- Styled export should be a second-pass workflow unless a live feature has a clear, documented product and technical reason.

## Testing and Validation Expectations

- Always try to verify builds locally when possible.
- If the environment cannot build, say so clearly and provide manual verification steps.
- For documentation-only changes, confirm the changed-file list and skip unnecessary builds.
- For capture, audio, or encoding changes, validate at least video-only recording, system audio, microphone, pause/resume, stop, and MP4 playback when the environment allows.
- For shell changes, preserve startup, shutdown, hotkeys, tray behavior, overlay behavior, onboarding, and post-record actions.
- For persistence changes, preserve settings, history, recovery markers, diagnostics export, and log readability.
- For render-planning work, keep tests or diagnostics focused on deterministic metadata, camera paths, and composition settings before attempting polished output.

## Documentation Discipline

- Update `PLAN.md` when roadmap, product positioning, architecture direction, or major feature sequencing changes.
- Add docs for major new subsystems.
- Keep docs honest about what is implemented versus aspirational.
- Document new metadata formats, render pipeline contracts, export settings, or persistence artifacts when introduced.
- Do not bury major architecture decisions only in code comments or PR text.

## Commit and PR Expectations

Use clear commit messages and PR descriptions.

A good final report or PR should explain:

- Root cause or product reason.
- What changed.
- Why the chosen approach fits the ScreenFast architecture.
- Validation performed.
- Risks or areas not covered.
- Remaining gaps and next best step.

Do not claim full validation when only static inspection or partial checks were possible.

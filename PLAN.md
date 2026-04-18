# ScreenFast Product and Engineering Plan

## Product Vision

ScreenFast is a native Windows screen recorder for creators, builders, teachers, and demo makers who need recordings that look intentional without spending hours in a video editor.

The product is not just a basic recorder. ScreenFast should evolve into a recorder plus render engine: the current recorder captures reliable raw source, and a later presentation pipeline turns that source into modern, polished output.

ScreenFast matters because most screen recordings are raw, flat, and visually forgettable. A useful demo, lesson, launch video, or product walkthrough often needs clear focus, smooth framing, attractive backgrounds, readable spacing, and presentation-ready exports. ScreenFast should make that quality feel native, fast, and repeatable on Windows.

What makes ScreenFast different from plain recorders:

- Native Windows-first capture instead of a generic cross-platform wrapper.
- Reliable screen, system audio, and microphone capture as the foundation.
- Cursor-aware storytelling through future cursor telemetry, click emphasis, and smart zoom.
- Creator-friendly rendering with backgrounds, padding, rounded frames, shadows, gradients, and export presets.
- A staged architecture that protects raw capture while adding presentation features.

## Product Positioning

ScreenFast should be positioned as a Windows-first recorder for polished software demos, tutorials, lessons, and builder updates.

The long-term product promise is:

- Native capture that users can trust.
- Smart zoom that follows intent, not random motion.
- Cursor-aware visual emphasis for clicks and interactions.
- Styled exports that look designed: clean backgrounds, safe margins, frame treatments, and presentation layouts.
- Fast creator workflows that turn a raw recording into a shareable video without a full editing timeline.

ScreenFast should compete on reliability and output quality. Recording must be boringly dependable; exported videos should feel modern and deliberate.

## Current State

The repository already has a strong native recorder foundation. The implementation is currently stronger on recording, shell behavior, diagnostics, and local reliability than on rendering, styling, or presentation-aware export.

Implemented or largely present:

- WinUI desktop shell.
- Source selection.
- Output folder selection.
- Video recording pipeline.
- Optional system audio.
- Optional microphone.
- Start, stop, pause, and resume.
- Timer.
- Post-record actions.
- Quality presets.
- Hotkeys.
- Tray behavior.
- Onboarding.
- Preflight validation.
- Recording history.
- Diagnostics export.
- Structured logging.
- Interrupted-session recovery.
- Countdown before recording.
- Overlay indicator.
- Disk-space checks.
- Safer filename generation.
- Local release notes and startup smoke checks.
- Build and source-picker stabilization work.

Not yet implemented in the real product direction:

- Cursor telemetry as a first-class timeline artifact.
- Click and interaction event capture.
- Auto-zoom planning.
- Cursor-follow framing.
- Click emphasis or spotlight logic.
- Presentation-aware camera path generation.
- Background replacement, color, gradient, or image styling.
- Rounded recorded-content frame, shadows, padding, spacing, and safe-margin layout.
- Export renderer for polished outputs.
- Creator-style scene composition presets.
- Robust styled export workflow on top of raw capture.

The honest state: ScreenFast is currently a recorder with meaningful reliability work. The true north star is a recorder plus render/presentation engine.

## Product Principles

- Reliability first for raw capture. A beautiful export is worthless if the raw recording fails, loses audio, corrupts MP4 finalization, or destabilizes the app.
- Presentation quality is the competitive edge. Beautiful exports are core product value, not optional polish.
- Protect the capture pipeline. New render features should not be hacked into fragile live recording paths unless there is a clear, documented reason.
- Prefer staged architecture over monolithic shortcuts. Capture, metadata, zoom planning, composition, and export should be separable enough to test and evolve.
- Metadata should become a first-class artifact. Cursor path, click events, source geometry, timing, and capture settings should be persisted in a form the renderer can consume.
- Keep the product Windows-native. Do not detour into a web-stack implementation for the core recorder.
- Build for creator workflow speed. Users should get a polished result with presets and light customization, not a heavy nonlinear editor.

## Architecture Direction

ScreenFast should evolve as two major systems.

### Capture Engine

The Capture Engine records reliable raw source:

- Screen/window/display capture.
- System audio and microphone capture.
- Encoding and MP4 finalization.
- Pause/resume lifecycle.
- Hotkeys, tray, overlay, countdown, preflight, recovery, history, logging, and diagnostics.
- Capture-adjacent metadata such as source dimensions, timing, selected quality preset, and eventually cursor/click timeline data.

The Capture Engine should stay stable and conservative. It should not own complex styling, scene composition, or post-processing decisions.

### Render / Presentation Engine

The Render / Presentation Engine turns raw capture plus metadata into polished output:

- Cursor-aware zoom planning.
- Camera path generation with easing and safe margins.
- Click emphasis, spotlight, and focus effects.
- Background color, gradient, and image composition.
- Padding, spacing, rounded frame, shadow, and layout treatments.
- Export presets for tutorial, social, product demo, and clean documentation modes.

The recommended evolution is staged:

1. Stage 1: Raw capture remains reliable and recoverable.
2. Stage 2: Cursor telemetry and click events are captured and persisted as metadata.
3. Stage 3: Zoom planning generates a camera path from cursor and interaction data.
4. Stage 4: Styled composition applies backgrounds, padding, frame treatments, and layout rules.
5. Stage 5: Polished export presets package those choices into fast creator workflows.

The key architectural rule: smart zoom and styling should first be modeled as metadata-driven render/export work, not as live-capture hacks.

## Roadmap

### Phase A: Stabilize Existing Native Recorder and Local Build Reliability

Goal: make the current recorder dependable enough to serve as the foundation for presentation features.

Key workstreams:

- Keep source picking, capture, audio, encoding, MP4 finalization, and pause/resume reliable.
- Preserve startup smoke checks, diagnostics export, structured logs, history, recovery, hotkeys, tray, countdown, and overlay behavior.
- Keep solution, runtime identifier, Windows SDK, packaging, and publish configuration understandable.

Deliverables:

- Repeatable local Debug and Release builds.
- Clear manual smoke test path.
- Stable video-only, system-audio, microphone, and pause/resume recordings.
- Actionable diagnostics for common failure states.

Success criteria:

- A user can select a source, record, stop, and play the MP4 reliably.
- Audio options work without corrupting output.
- App startup, shutdown, recovery, and diagnostics remain understandable.

Do not do yet:

- Do not add complex render features directly into the recording loop.
- Do not refactor broad native interop or encoder code without a concrete reliability goal.

### Phase B: Cursor Telemetry and Interaction Timeline

Goal: capture user intent as metadata while preserving raw recording reliability.

Key workstreams:

- Record cursor position over time relative to the captured source.
- Record click events and basic interaction markers.
- Persist metadata beside the raw recording with stable timing references.
- Define coordinate spaces, source bounds, timestamps, and pause/resume behavior.

Deliverables:

- A metadata artifact for each recording.
- Cursor timeline samples.
- Click event timeline.
- Diagnostics or debug inspection path for metadata quality.

Success criteria:

- Metadata can be matched to video time.
- Cursor positions are accurate enough to drive zoom planning.
- Metadata capture does not destabilize recording or MP4 finalization.

Do not do yet:

- Do not build the full auto-zoom engine in this phase.
- Do not require styled export before metadata correctness is proven.

### Phase C: Auto-Zoom Planning Engine

Goal: turn cursor and interaction metadata into a smooth camera plan.

Key workstreams:

- Define zoom segments, focus points, easing, dwell time, and safe margins.
- Generate camera paths from cursor movement and click events.
- Avoid jitter, over-zooming, and motion that makes tutorials hard to watch.
- Support deterministic preview/debug output for camera plans.

Deliverables:

- Camera path model.
- Zoom planning service.
- Testable planner inputs and outputs.
- Basic preview or diagnostic representation of planned zooms.

Success criteria:

- Zoom decisions feel intentional and readable.
- The planner can be tested without a live recording session.
- Raw capture remains independent from zoom planning.

Do not do yet:

- Do not combine planner, renderer, and capture lifecycle into one subsystem.
- Do not chase advanced AI behavior before deterministic rules are solid.

### Phase D: Styled Export Renderer

Goal: produce a polished video from raw capture, metadata, and composition settings.

Key workstreams:

- Build a second-pass render/export pipeline.
- Apply camera path transforms to recorded content.
- Compose backgrounds, padding, rounded frames, shadows, and safe margins.
- Preserve output quality and predictable export timing.

Deliverables:

- Styled export workflow.
- Composition settings model.
- Rendered output with at least background color/gradient, padding, rounded frame, and shadow support.
- Export failure handling and diagnostics.

Success criteria:

- Users can export a raw recording into a visibly polished video.
- Render failures do not erase or corrupt raw recordings.
- The renderer can evolve independently from capture.

Do not do yet:

- Do not replace the stable raw recorder with an unproven render pipeline.
- Do not build a full nonlinear editor.

### Phase E: Presets, Branding, Layout Customization, and Export Polish

Goal: make polished output fast and repeatable for creators.

Key workstreams:

- Add presets for tutorial, social, product demo, and clean documentation formats.
- Allow background colors, gradients, images, padding, spacing, corner radius, and shadows.
- Add branding-light controls without turning the app into a design tool.
- Improve export naming, history, and post-export actions.

Deliverables:

- Preset library.
- Customization UI for common styling controls.
- Reliable preset persistence.
- Improved export workflow around polished outputs.

Success criteria:

- A user can get a good-looking export quickly with minimal tweaking.
- Presets feel opinionated but not restrictive.
- Styling controls do not compromise recorder reliability.

Do not do yet:

- Do not build deep timeline editing.
- Do not add cloud sync or team asset libraries.

### Phase F: Advanced Creator Features Later

Goal: expand creator power after the core recorder plus render engine is stable.

Possible workstreams:

- Webcam compositing.
- Advanced captions or callouts.
- Brand kits.
- Template sharing.
- More export aspect ratios and platform-specific packaging.
- Smarter interaction detection.

Deliverables:

- Only define after Phases B-E prove the render architecture.

Success criteria:

- Advanced features enhance, rather than destabilize, the core workflow.

Do not do yet:

- Do not broaden into a full NLE.
- Do not pursue multi-platform capture before Windows-native quality is strong.

## Feature Definitions

- Auto-zoom: a render-time camera planning feature that zooms into important regions based on cursor motion, clicks, dwell time, and safe-margin rules.
- Cursor-follow: a camera behavior that keeps the cursor or active interaction region visible without making the viewer seasick or overreacting to small movements.
- Click emphasis: visual treatment around clicks, such as rings, pulse, spotlight, or momentary focus, driven by click event metadata.
- Background color and gradient styling: composition controls that place recorded content on a designed canvas instead of exporting only raw capture pixels.
- Background image replacement: the ability to place recording content over an image background while preserving readability and safe margins.
- Rounded frame, shadow, and safe-margin layout: recorded-content treatment that makes the capture look like a polished presentation object.
- Export presets: named bundles of aspect ratio, background, padding, frame, shadow, zoom, and output settings for common creator workflows.
- Social and tutorial presentation modes: export modes optimized for specific delivery contexts, such as vertical social clips, product demos, lessons, and documentation videos.

## Technical Strategy

The raw recorder should stay stable. Capture, audio, encoding, lifecycle, recovery, and diagnostics are foundational systems.

Auto-zoom and styling should not be hacked directly into the live capture pipeline first. A second-pass render/export architecture is preferred because it keeps raw capture recoverable, makes visual decisions testable, and allows multiple exports from the same source recording.

Metadata should become a first-class artifact. At minimum, future metadata should describe cursor samples, click events, source bounds, timing, recording segments, quality settings, and enough session context for render planning.

The target data flow should be:

1. Capture Engine records raw video/audio.
2. Capture-adjacent telemetry records cursor and interaction metadata.
3. Planner generates a camera path from metadata.
4. Presentation renderer composes the final styled video.
5. Export workflow saves polished output and records history/diagnostics.

This strategy allows ScreenFast to become visually ambitious without making the recorder fragile.

## Non-Goals for Now

- No webcam compositing until explicitly planned after the core render pipeline is stable.
- No full nonlinear timeline editor.
- No cloud sync.
- No broad multi-platform ambition before Windows-native capture and export quality are strong.
- No unstable visual hacks that degrade recording reliability.
- No large architecture rewrites without a concrete product or reliability payoff.
- No hidden architecture drift without docs.

## Definition of Success

A successful v1.5/v2 ScreenFast feels like this:

- A user can record a Windows screen or app with system audio and microphone confidence.
- The raw MP4 is reliable, playable, and recoverable even if later export work fails.
- Cursor movement and clicks are captured as useful timeline metadata.
- The app can generate smooth, readable zoom plans that make demos easier to follow.
- The user can choose a preset, adjust background and frame styling, and export a modern video quickly.
- The final output looks intentional enough for product demos, tutorials, course clips, and launch posts without a separate editor.

## Immediate Next Build Slice

The next smart implementation slice is not random feature sprawl. It is:

1. Cursor telemetry capture.
2. Metadata persistence beside recordings.
3. Render pipeline planning for future camera path and styled export work.

That slice creates the bridge from reliable recorder to polished presentation engine while protecting the existing capture foundation.

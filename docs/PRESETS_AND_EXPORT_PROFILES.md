# Presets and Export Profiles

ScreenFast now persists a creator workflow library in `AppSettings` instead of relying on magic strings or one-off UI choices.

## Implemented preset types

- **Recording presets**: Quick Demo, Tutorial, Meeting Clip, and Product Walkthrough. These map to quality, audio, countdown, overlay, and default export intent.
- **Zoom presets**: Subtle Zoom, Standard Zoom, and Strong Zoom. These are deterministic render-planning inputs for future cursor-aware camera paths.
- **Styling presets**: Clean, Gradient, and Branded Frame. These describe background, padding, corner, shadow, and frame intent without touching live capture.
- **Export presets**: Raw Recorder Output, Tutorial Polished Output, Social Clip Output, and Demo Presentation Output. These connect zoom, styling, and export profile choices.

The main window exposes all preset types so users can select a recording workflow before capture. Recording presets apply safe recorder settings immediately; render-oriented presets are carried into post-record processing and history.

## Export profiles

Export profiles are persisted alongside presets and support:

- output mode: raw only, styled only, or both;
- background style and value;
- canvas mode and optional canvas dimensions;
- padding and margin;
- frame, corner radius, shadow, and border styling;
- linked zoom preset;
- output naming behavior.

## Current renderer boundary

The raw MP4 remains the source of truth. Styled export profiles are fully modeled and wired into post-record processing, but the current build uses an explicit unsupported styled-export service that records a skipped stage instead of pretending to render a polished video. The next renderer pass can replace that service without modifying capture or MP4 finalization.

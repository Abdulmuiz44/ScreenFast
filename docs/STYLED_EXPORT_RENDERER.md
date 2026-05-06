# Styled Export Renderer

ScreenFast now has a first native-app renderer slice for auto-zoom exports.

## Current Behavior

- Raw recordings remain the source of truth and are not modified.
- Recording stop can create:
  - `.screenfast.json` recording metadata.
  - `.zoomplan.json` auto-zoom camera plan.
  - `.styled-export.json` composition plan.
  - `.styled.mp4` rendered auto-zoom export when `ffmpeg` is available.
- Existing recordings can be exported through the app's `Export Auto Zoom MP4` command.

## Renderer Implementation

The first renderer implementation is `FfmpegStyledVideoExportService` in `ScreenFast.Infrastructure`.

It reads a `StyledExportPlan`, builds an ffmpeg filter graph, and writes a separate MP4. The renderer applies segment-level source viewports from the plan, scales the cropped source into the planned output content rectangle, and composites it over the configured primary background color.

The service resolves ffmpeg from:

1. `SCREENFAST_FFMPEG_PATH`
2. `ffmpeg.exe` on `PATH`

If ffmpeg is unavailable, ScreenFast keeps the raw recording and render-plan artifacts, then reports a user-friendly export failure.

## Current Limitations

- Segment-level zoom is implemented; sub-segment eased camera interpolation is not yet rendered.
- Gradient backgrounds currently render as the primary background color.
- Rounded frame masks and frame shadows are planned but not yet rendered.
- The renderer depends on ffmpeg being installed or configured.

## Next Renderer Slice

The next slice should improve visual fidelity:

- Interpolate crop rectangles over transition segments.
- Render rounded frame masks.
- Render soft shadows.
- Add deterministic render validation using a short fixture MP4 and known zoom plan.

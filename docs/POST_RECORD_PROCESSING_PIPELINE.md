# Post-Record Processing Pipeline

ScreenFast now has an explicit `IPostRecordingProcessingPipeline` boundary for work that happens after the encoder finalizes a raw MP4.

## Stage order

1. **Raw finalized**: the MP4 from the encoder is recorded as the stable primary artifact.
2. **Metadata sidecar**: capture-adjacent metadata and cursor telemetry are saved beside the MP4 when session context is available.
3. **Zoom plan**: a deterministic `.zoomplan.json` artifact is generated when metadata exists. If telemetry is unavailable, the plan safely falls back to a full-frame hold and records a warning.
4. **Styled export**: selected export profiles are evaluated. Styled output is skipped with an observable stage result until the renderer is implemented.
5. **History**: the history entry stores an asset graph for raw, metadata, zoom-plan, and styled-export artifacts.
6. **Post-record file action**: open-file/open-folder behavior runs last and cannot invalidate the MP4.

## Failure policy

A post-finalization stage failure never invalidates a good raw MP4. Stage failures are logged and reported as partial success in history. This keeps capture and rendering/presentation concerns separated.

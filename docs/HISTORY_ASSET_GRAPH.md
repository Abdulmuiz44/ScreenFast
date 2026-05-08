# History Asset Graph

Recording history entries now keep the legacy MP4-focused fields and add an optional asset graph.

## Tracked assets

- raw MP4 path;
- metadata sidecar path;
- zoom plan path;
- styled export path;
- selected recording, zoom, styling, export preset, and export profile names;
- processing state: success, partial success, or failure;
- warnings from metadata, zoom planning, styled export, and post-record actions.

Existing history remains usable. Missing secondary assets are shown as absent rather than causing the main recording entry to fail. The main UI still opens and copies the raw MP4 path by default.

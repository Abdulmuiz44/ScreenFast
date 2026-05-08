# Settings Backup, Import, and Export

ScreenFast can export and import a versioned local settings bundle.

## Bundle contents

The bundle includes:

- hotkeys;
- recording and UI behavior preferences;
- preset selection;
- full preset library;
- full export profile library.

Bundles are written under the local ScreenFast settings area in `SettingsBackups` with a timestamped file name. Import reads the latest ScreenFast bundle, validates the shape, normalizes presets/profiles, avoids restoring stale capture-source handles, persists the result, and re-registers hotkeys through the shell service.

## Safety policy

Import is not a blind destructive overwrite. The bundle version and app name are validated, presets and profiles are normalized with defaults, missing source handles are cleared, and hotkey registration is attempted explicitly after restore.

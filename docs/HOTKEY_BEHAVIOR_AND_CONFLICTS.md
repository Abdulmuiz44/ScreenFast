# Hotkey Behavior and Conflicts

ScreenFast validates global hotkeys with a centralized `IHotkeyValidator` before registration.

## Validation rules

- Start, stop, and pause/resume gestures must include at least one modifier.
- Function keys are limited to F1 through F24.
- Each ScreenFast command must use a unique gesture.
- Warnings are logged for combinations that Windows or foreground apps may intercept.

## Registration behavior

Runtime updates unregister old hotkeys, attempt the new set, and fall back to the previous registered set if a new shortcut is already owned by another app. Failed registration is surfaced to the UI and logged with the conflicting gesture.

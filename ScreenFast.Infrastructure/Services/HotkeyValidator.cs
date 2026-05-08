using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;

namespace ScreenFast.Infrastructure.Services;

public sealed class HotkeyValidator : IHotkeyValidator
{
    public HotkeyValidationResult Validate(HotkeySettings hotkeys)
    {
        var issues = new List<HotkeyValidationIssue>();
        var gestures = new[]
        {
            (HotkeyCommandKind.StartRecording, hotkeys.StartRecording),
            (HotkeyCommandKind.StopRecording, hotkeys.StopRecording),
            (HotkeyCommandKind.PauseResumeRecording, hotkeys.PauseResumeRecording)
        };

        foreach (var (command, gesture) in gestures)
        {
            if (!gesture.HasAnyModifier)
            {
                issues.Add(new HotkeyValidationIssue(HotkeyValidationSeverity.Error, command, $"{command} must include Ctrl, Shift, or Alt.", gesture));
            }

            if (gesture.VirtualKey is < 0x70 or > 0x87)
            {
                issues.Add(new HotkeyValidationIssue(HotkeyValidationSeverity.Error, command, $"{command} must use a function key from F1 through F24.", gesture));
            }
        }

        foreach (var group in gestures.GroupBy(x => x.Item2).Where(group => group.Count() > 1))
        {
            var names = string.Join(", ", group.Select(x => x.Item1));
            issues.Add(new HotkeyValidationIssue(HotkeyValidationSeverity.Error, null, $"Hotkey conflict: {group.Key.DisplayText} is assigned to {names}.", group.Key));
        }

        if (issues.Count == 0 && gestures.Any(x => x.Item2.Alt && x.Item2.VirtualKey == 0x73))
        {
            issues.Add(new HotkeyValidationIssue(HotkeyValidationSeverity.Warning, null, "Alt+F4-style shortcuts can be intercepted by Windows or apps; ScreenFast will still try to register them.", null));
        }

        return new HotkeyValidationResult(issues);
    }
}

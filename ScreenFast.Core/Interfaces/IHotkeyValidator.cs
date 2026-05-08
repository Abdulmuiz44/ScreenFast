using ScreenFast.Core.Models;

namespace ScreenFast.Core.Interfaces;

public interface IHotkeyValidator
{
    HotkeyValidationResult Validate(HotkeySettings hotkeys);
}

namespace ScreenFast.Core.Models;

public sealed record HotkeyGesture(bool Control, bool Shift, bool Alt, int VirtualKey)
{
    public bool HasAnyModifier => Control || Shift || Alt;

    public string KeyDisplayText => VirtualKey is >= 0x70 and <= 0x87
        ? $"F{VirtualKey - 0x6F}"
        : $"0x{VirtualKey:X2}";

    public string DisplayText
    {
        get
        {
            var parts = new List<string>();
            if (Control)
            {
                parts.Add("Ctrl");
            }

            if (Shift)
            {
                parts.Add("Shift");
            }

            if (Alt)
            {
                parts.Add("Alt");
            }

            parts.Add(KeyDisplayText);
            return string.Join(" + ", parts);
        }
    }
}

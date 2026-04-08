using System.Text;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;

namespace ScreenFast.Infrastructure.Services;

public sealed class RecordingFileNameService : IRecordingFileNameService
{
    public string CreateOutputFilePath(string outputFolder, CaptureSourceModel source)
    {
        var safePrefix = "ScreenFast";
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var sourceHint = SanitizeSourceHint(source.DisplayName);
        var baseName = string.IsNullOrWhiteSpace(sourceHint)
            ? $"{safePrefix}_{timestamp}"
            : $"{safePrefix}_{timestamp}_{sourceHint}";

        var path = Path.Combine(outputFolder, baseName + ".mp4");
        if (!File.Exists(path))
        {
            return path;
        }

        for (var index = 2; index <= 999; index++)
        {
            var candidate = Path.Combine(outputFolder, $"{baseName}-{index}.mp4");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(outputFolder, $"{safePrefix}_{timestamp}_{Guid.NewGuid():N}.mp4");
    }

    private static string SanitizeSourceHint(string? sourceDisplayName)
    {
        if (string.IsNullOrWhiteSpace(sourceDisplayName))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(sourceDisplayName.Length);
        foreach (var character in sourceDisplayName)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (character is ' ' or '-' or '_')
            {
                builder.Append('-');
            }
        }

        var sanitized = builder.ToString().Trim('-');
        while (sanitized.Contains("--", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("--", "-", StringComparison.Ordinal);
        }

        return sanitized.Length <= 24 ? sanitized : sanitized[..24].TrimEnd('-');
    }
}

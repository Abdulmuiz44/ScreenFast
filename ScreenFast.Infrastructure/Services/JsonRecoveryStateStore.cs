using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;

namespace ScreenFast.Infrastructure.Services;

public sealed class JsonRecoveryStateStore : IRecoveryStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _recoveryPath;

    public JsonRecoveryStateStore()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScreenFast");
        Directory.CreateDirectory(root);
        _recoveryPath = Path.Combine(root, "active-session.json");
    }

    public async Task<RecoverySessionMarker?> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_recoveryPath))
            {
                return null;
            }

            await using var stream = File.OpenRead(_recoveryPath);
            return await JsonSerializer.DeserializeAsync<RecoverySessionMarker>(stream, SerializerOptions, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveAsync(RecoverySessionMarker marker, CancellationToken cancellationToken = default)
    {
        var tempPath = _recoveryPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, marker, SerializerOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(tempPath, _recoveryPath, true);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(_recoveryPath))
        {
            File.Delete(_recoveryPath);
        }

        return Task.CompletedTask;
    }
}

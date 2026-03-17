using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;

namespace ScreenFast.Infrastructure.Services;

public sealed class FileScreenFastLogService : IScreenFastLogService, IDisposable
{
    private const long MaxFileSizeBytes = 2 * 1024 * 1024;
    private const int MaxFileCount = 10;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Channel<StructuredLogEntry> _channel = Channel.CreateUnbounded<StructuredLogEntry>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _processorTask;
    private readonly ConcurrentQueue<Exception> _internalFailures = new();
    private int _queuedCount;
    private string? _currentFilePath;

    public FileScreenFastLogService()
    {
        LogsFolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScreenFast",
            "Logs");
        Directory.CreateDirectory(LogsFolderPath);
        _processorTask = Task.Run(ProcessAsync);
    }

    public string LogsFolderPath { get; }

    public void Info(string eventName, string message, IReadOnlyDictionary<string, object?>? properties = null)
        => Enqueue(ScreenFastLogLevel.Info, eventName, message, properties);

    public void Warning(string eventName, string message, IReadOnlyDictionary<string, object?>? properties = null)
        => Enqueue(ScreenFastLogLevel.Warning, eventName, message, properties);

    public void Error(string eventName, string message, IReadOnlyDictionary<string, object?>? properties = null)
        => Enqueue(ScreenFastLogLevel.Error, eventName, message, properties);

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        while (Volatile.Read(ref _queuedCount) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(50, cancellationToken);
        }
    }

    public Task<IReadOnlyList<string>> GetRecentLogFilesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<string> files;
        try
        {
            files = Directory.Exists(LogsFolderPath)
                ? Directory.GetFiles(LogsFolderPath, "*.jsonl")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ToList()
                : [];
        }
        catch
        {
            files = [];
        }

        return Task.FromResult(files);
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _shutdown.Cancel();
        try
        {
            _processorTask.GetAwaiter().GetResult();
        }
        catch
        {
        }

        _shutdown.Dispose();
    }

    private void Enqueue(ScreenFastLogLevel level, string eventName, string message, IReadOnlyDictionary<string, object?>? properties)
    {
        try
        {
            Interlocked.Increment(ref _queuedCount);
            var entry = new StructuredLogEntry(
                DateTimeOffset.UtcNow,
                level,
                string.IsNullOrWhiteSpace(eventName) ? "unknown" : eventName,
                string.IsNullOrWhiteSpace(message) ? string.Empty : message,
                ConvertProperties(properties));

            if (!_channel.Writer.TryWrite(entry))
            {
                Interlocked.Decrement(ref _queuedCount);
            }
        }
        catch (Exception ex)
        {
            _internalFailures.Enqueue(ex);
            Interlocked.Exchange(ref _queuedCount, Math.Max(0, Volatile.Read(ref _queuedCount) - 1));
        }
    }

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (var entry in _channel.Reader.ReadAllAsync(_shutdown.Token))
            {
                try
                {
                    await WriteEntryAsync(entry, _shutdown.Token);
                }
                catch (Exception ex)
                {
                    _internalFailures.Enqueue(ex);
                }
                finally
                {
                    Interlocked.Decrement(ref _queuedCount);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task WriteEntryAsync(StructuredLogEntry entry, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(LogsFolderPath);
        var path = EnsureCurrentFilePath();
        var json = JsonSerializer.Serialize(entry, SerializerOptions);
        var payload = Encoding.UTF8.GetBytes(json + Environment.NewLine);

        if (File.Exists(path))
        {
            var fileInfo = new FileInfo(path);
            if (fileInfo.Length + payload.Length > MaxFileSizeBytes)
            {
                path = RollToNewFile();
            }
        }

        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        TrimRetention();
    }

    private string EnsureCurrentFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_currentFilePath) && File.Exists(_currentFilePath))
        {
            return _currentFilePath;
        }

        return RollToNewFile();
    }

    private string RollToNewFile()
    {
        Directory.CreateDirectory(LogsFolderPath);
        _currentFilePath = Path.Combine(LogsFolderPath, $"screenfast-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.jsonl");
        return _currentFilePath;
    }

    private void TrimRetention()
    {
        try
        {
            var files = Directory.GetFiles(LogsFolderPath, "*.jsonl")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();

            foreach (var file in files.Skip(MaxFileCount))
            {
                File.Delete(file);
            }
        }
        catch
        {
        }
    }

    private static IReadOnlyDictionary<string, string?>? ConvertProperties(IReadOnlyDictionary<string, object?>? properties)
    {
        if (properties is null || properties.Count == 0)
        {
            return null;
        }

        return properties.ToDictionary(
            pair => pair.Key,
            pair => pair.Value switch
            {
                null => null,
                DateTimeOffset dto => dto.ToString("O"),
                DateTime dt => dt.ToUniversalTime().ToString("O"),
                bool boolean => boolean ? "true" : "false",
                _ => pair.Value.ToString()
            });
    }
}

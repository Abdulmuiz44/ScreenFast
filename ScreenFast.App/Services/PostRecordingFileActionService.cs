using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.App.Services;

public sealed class PostRecordingFileActionService : IPostRecordingFileActionService
{
    private readonly IFileLauncherService _fileLauncherService;
    private readonly IScreenFastLogService _logService;

    public PostRecordingFileActionService(IFileLauncherService fileLauncherService, IScreenFastLogService logService)
    {
        _fileLauncherService = fileLauncherService;
        _logService = logService;
    }

    public async Task<OperationResult> RunAsync(PostRecordingOpenBehavior behavior, string rawVideoPath, CancellationToken cancellationToken = default)
    {
        OperationResult result = behavior switch
        {
            PostRecordingOpenBehavior.OpenFile => await _fileLauncherService.OpenFileAsync(rawVideoPath),
            PostRecordingOpenBehavior.OpenContainingFolder => await _fileLauncherService.OpenContainingFolderAsync(rawVideoPath),
            _ => OperationResult.Success()
        };

        if (behavior != PostRecordingOpenBehavior.None)
        {
            _logService.Info("post_record.file_action", "ScreenFast ran a post-record file action.", new Dictionary<string, object?> { ["behavior"] = behavior, ["path"] = rawVideoPath, ["success"] = result.IsSuccess });
        }

        return result;
    }
}

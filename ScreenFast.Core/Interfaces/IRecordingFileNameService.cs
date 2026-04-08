using ScreenFast.Core.Models;

namespace ScreenFast.Core.Interfaces;

public interface IRecordingFileNameService
{
    string CreateOutputFilePath(string outputFolder, CaptureSourceModel source);
}

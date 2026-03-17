using System.Runtime.InteropServices;

namespace ScreenFast.Encoding.Interop;

internal static class MediaFoundationNative
{
    public const int MFVersion = 0x00020070;
    public const int MFStartupFull = 0;
    public const int ProgressiveInterlaceMode = 2;

    public static readonly Guid MFMediaTypeVideo = new("73646976-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFMediaTypeAudio = new("73647561-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFVideoFormatH264 = new("34363248-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFVideoFormatArgb32 = new("00000015-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFAudioFormatAac = new("00001610-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFAudioFormatPcm = new("00000001-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFMtMajorType = new("48EBA18E-F8C9-4687-BF11-0A74C9F96A8F");
    public static readonly Guid MFMtSubtype = new("F7E34C9A-42E8-4714-B74B-CB29D72C35E5");
    public static readonly Guid MFMtAvgBitrate = new("20332624-FB0D-4D9E-BD0D-CBF6786C102E");
    public static readonly Guid MFMtInterlaceMode = new("E2724BB8-E676-4806-B4B2-A8D6EFB44CCD");
    public static readonly Guid MFMtFrameSize = new("1652C33D-D6B2-4012-B834-72030849A37D");
    public static readonly Guid MFMtFrameRate = new("C459A2E8-3D2C-4E44-B132-FEE5156C7BB0");
    public static readonly Guid MFMtPixelAspectRatio = new("C6376A1E-8D0A-4027-BE45-6D9A0AD39BB6");
    public static readonly Guid MFMtAudioNumChannels = new("37E48BF5-645E-4C5B-89DE-ADA9E29B696A");
    public static readonly Guid MFMtAudioSamplesPerSecond = new("5FAEEAE7-0290-4C31-9E8A-C534F68D9DBA");
    public static readonly Guid MFMtAudioAvgBytesPerSecond = new("1AAB75C8-CF4D-19E4-6CA9-4B4C2D66C3CF");
    public static readonly Guid MFMtAudioBlockAlignment = new("322DE230-9Eeb-43bd-AB7A-FF412251541D");
    public static readonly Guid MFMtAudioBitsPerSample = new("F2DEB57F-40FA-4764-AA33-ED4F2D1FF669");
    public static readonly Guid MFMtAudioValidBitsPerSample = new("F2DEB57F-40FA-4764-AA33-ED4F2D1FF669");
    public static readonly Guid MFMtAacPayloadType = new("BFBABE79-7434-4d1c-94F0-72A3B9E17188");
    public static readonly Guid MFMtAacAudioProfileLevelIndication = new("7632F0E6-9538-4d61-ACDA-EA29C8C14456");
    public static readonly Guid MFReadwriteEnableHardwareTransforms = new("A634A91C-822B-41B9-A494-4DE4643612B0");
    public static readonly Guid MFSinkWriterD3DManager = new("EC822DA2-E1E9-4B29-A0D8-563C719F5269");

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFStartup(int version, int flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateAttributes(out IMFAttributes attributes, uint initialSize);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateMediaType(out IMFMediaType mediaType);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateSample(out IMFSample sample);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateDXGIDeviceManager(out uint resetToken, out IMFDXGIDeviceManager deviceManager);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateMemoryBuffer(uint maxLength, out IMFMediaBuffer buffer);

    [DllImport("mfapi.dll", ExactSpelling = true)]
    public static extern int MFSetAttributeSize(IMFAttributes attributes, in Guid key, uint width, uint height);

    [DllImport("mfapi.dll", ExactSpelling = true)]
    public static extern int MFSetAttributeRatio(IMFAttributes attributes, in Guid key, uint numerator, uint denominator);

    [DllImport("mf.dll", ExactSpelling = true)]
    public static extern int MFCreateDXGISurfaceBuffer(in Guid riid, nint surface, uint subresourceIndex, [MarshalAs(UnmanagedType.Bool)] bool bottomUpWhenLinear, out IMFMediaBuffer buffer);

    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern int MFCreateSinkWriterFromURL(string outputPath, nint byteStream, IMFAttributes? attributes, out IMFSinkWriter sinkWriter);

    public static void ThrowIfFailed(int hr)
    {
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }
    }

    [ComImport]
    [Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFAttributes
    {
        int GetItem(in Guid guidKey, nint value);
        int GetItemType(in Guid guidKey, out int itemType);
        int CompareItem(in Guid guidKey, nint value, [MarshalAs(UnmanagedType.Bool)] out bool result);
        int Compare(IMFAttributes theirs, int matchType, [MarshalAs(UnmanagedType.Bool)] out bool result);
        int GetUINT32(in Guid guidKey, out uint value);
        int GetUINT64(in Guid guidKey, out ulong value);
        int GetDouble(in Guid guidKey, out double value);
        int GetGUID(in Guid guidKey, out Guid value);
        int GetStringLength(in Guid guidKey, out uint length);
        int GetString(in Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder value, uint size, out uint length);
        int GetAllocatedString(in Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string value, out uint length);
        int GetBlobSize(in Guid guidKey, out uint size);
        int GetBlob(in Guid guidKey, nint buffer, uint size, out uint length);
        int GetAllocatedBlob(in Guid guidKey, out nint buffer, out uint size);
        int GetUnknown(in Guid guidKey, in Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object value);
        int SetItem(in Guid guidKey, nint value);
        int DeleteItem(in Guid guidKey);
        int DeleteAllItems();
        int SetUINT32(in Guid guidKey, uint value);
        int SetUINT64(in Guid guidKey, ulong value);
        int SetDouble(in Guid guidKey, double value);
        int SetGUID(in Guid guidKey, in Guid value);
        int SetString(in Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string value);
        int SetBlob(in Guid guidKey, nint buffer, uint size);
        int SetUnknown(in Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object value);
        int LockStore();
        int UnlockStore();
        int GetCount(out uint count);
        int GetItemByIndex(uint index, out Guid guidKey, nint value);
        int CopyAllItems(IMFAttributes destination);
    }

    [ComImport]
    [Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFMediaType : IMFAttributes
    {
    }

    [ComImport]
    [Guid("45BC0AAB-ABF9-43EE-BC8D-526CBF620B88")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSample : IMFAttributes
    {
        int GetSampleFlags(out uint flags);
        int SetSampleFlags(uint flags);
        int GetSampleTime(out long sampleTime);
        int SetSampleTime(long sampleTime);
        int GetSampleDuration(out long sampleDuration);
        int SetSampleDuration(long sampleDuration);
        int GetBufferCount(out uint count);
        int GetBufferByIndex(uint index, out IMFMediaBuffer buffer);
        int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
        int AddBuffer(IMFMediaBuffer buffer);
        int RemoveBufferByIndex(uint index);
        int RemoveAllBuffers();
        int GetTotalLength(out uint totalLength);
        int CopyToBuffer(IMFMediaBuffer buffer);
    }

    [ComImport]
    [Guid("045FA593-8799-42B8-BC8D-8968C6453507")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFMediaBuffer
    {
        int Lock(out nint buffer, out uint maxLength, out uint currentLength);
        int Unlock();
        int GetCurrentLength(out uint currentLength);
        int SetCurrentLength(uint currentLength);
        int GetMaxLength(out uint maxLength);
    }

    [ComImport]
    [Guid("EB533D5D-2DB6-40F8-97A9-494692014F07")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFDXGIDeviceManager
    {
        int CloseDeviceHandle(nint hDevice);
        int GetVideoService(nint hDevice, in Guid riid, out nint service);
        int LockDevice(nint hDevice, in Guid riid, [MarshalAs(UnmanagedType.Bool)] bool block, out nint device);
        int OpenDeviceHandle(out nint hDevice);
        int ResetDevice(nint device, uint resetToken);
        int TestDevice(nint hDevice);
        int UnlockDevice(nint hDevice, [MarshalAs(UnmanagedType.Bool)] bool saveState);
    }

    [ComImport]
    [Guid("3137F1CD-FE5E-4805-A5D8-FB477448CB3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSinkWriter
    {
        int AddStream(IMFMediaType targetMediaType, out uint streamIndex);
        int SetInputMediaType(uint streamIndex, IMFMediaType inputMediaType, IMFAttributes? encodingParameters);
        int BeginWriting();
        int WriteSample(uint streamIndex, IMFSample sample);
        int SendStreamTick(uint streamIndex, long timestamp);
        int PlaceMarker(uint streamIndex, nint context);
        int NotifyEndOfSegment(uint streamIndex);
        int Flush(uint streamIndex);
        int Finalize_();
    }
}

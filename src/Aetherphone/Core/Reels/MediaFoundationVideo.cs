using System.Runtime.InteropServices;
using System.Text;
using NAudio.MediaFoundation;
using TransformEnumFlags = NAudio.MediaFoundation._MFT_ENUM_FLAG;

namespace Aetherphone.Core.Reels;

[ComImport]
[Guid("7DC9D5F9-9ED9-44ec-9BBF-0600BB589FBB")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMF2DBuffer
{
    void Lock2D(out IntPtr scanline0, out int pitch);

    void Unlock2D();

    void GetScanline0AndPitch(out IntPtr scanline0, out int pitch);

    void IsContiguousFormat([MarshalAs(UnmanagedType.Bool)] out bool contiguous);

    void GetContiguousLength(out int length);

    void ContiguousCopyTo(IntPtr destination, int destinationLength);

    void ContiguousCopyFrom(IntPtr source, int sourceLength);
}

[ComImport]
[Guid("7b981cf0-560e-4116-9875-b099895f23d7")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSourceReaderEx
{
    void GetStreamSelection(int streamIndex, [MarshalAs(UnmanagedType.Bool)] out bool selected);

    void SetStreamSelection(int streamIndex, [MarshalAs(UnmanagedType.Bool)] bool selected);

    void GetNativeMediaType(int streamIndex, int mediaTypeIndex, out IMFMediaType mediaType);

    void GetCurrentMediaType(int streamIndex, out IMFMediaType mediaType);

    void SetCurrentMediaType(int streamIndex, IntPtr reserved, IMFMediaType mediaType);

    void SetCurrentPosition([MarshalAs(UnmanagedType.LPStruct)] Guid timeFormat, IntPtr position);

    void ReadSample(int streamIndex, int controlFlags, out int actualStreamIndex, out int streamFlags,
        out long timestamp, out IMFSample sample);

    void Flush(int streamIndex);

    void GetServiceForStream(int streamIndex, [MarshalAs(UnmanagedType.LPStruct)] Guid service,
        [MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId, out IntPtr result);

    [PreserveSig]
    int GetPresentationAttribute(int streamIndex, [MarshalAs(UnmanagedType.LPStruct)] Guid attribute,
        IntPtr value);

    void SetNativeMediaType(int streamIndex, IMFMediaType mediaType, out int streamFlags);

    void AddTransformForStream(int streamIndex, [MarshalAs(UnmanagedType.IUnknown)] object transformOrActivate);

    void RemoveAllTransformsForStream(int streamIndex);

    void GetTransformForStream(int streamIndex, int transformIndex, out Guid category, out IMFTransform transform);
}

[ComImport]
[Guid("588d72ab-5bc1-496a-8714-b70617141b25")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSinkWriterEx
{
    void AddStream(IMFMediaType targetMediaType, out int streamIndex);

    void SetInputMediaType(int streamIndex, IMFMediaType inputMediaType, IMFAttributes? encodingParameters);

    void BeginWriting();

    void WriteSample(int streamIndex, IMFSample sample);

    void SendStreamTick(int streamIndex, long timestamp);

    void PlaceMarker(int streamIndex, IntPtr context);

    void NotifyEndOfSegment(int streamIndex);

    void Flush(int streamIndex);

    void DoFinalize();

    void GetServiceForStream(int streamIndex, [MarshalAs(UnmanagedType.LPStruct)] Guid service,
        [MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId, out IntPtr result);

    void GetStatistics(int streamIndex, IntPtr statistics);

    void GetTransformForStream(int streamIndex, int transformIndex, out Guid category, out IMFTransform transform);
}

internal readonly record struct MediaTransformInfo(string Name, bool Hardware);

internal readonly ref struct MediaFoundationSession
{
    private readonly bool started;

    private MediaFoundationSession(bool started) => this.started = started;

    internal static MediaFoundationSession Start()
    {
        MediaFoundationInterop.MFStartup(MediaFoundationInterop.MF_VERSION);
        return new MediaFoundationSession(true);
    }

    public void Dispose()
    {
        if (started)
        {
            MediaFoundationInterop.MFShutdown();
        }
    }
}

internal static class MediaFoundationVideo
{
    internal const int MediaSourceStream = -1;
    internal const int AnyStream = -2;

    internal const int ReaderError = 0x1;
    internal const int ReaderEndOfStream = 0x2;
    internal const int ReaderCurrentTypeChanged = 0x20;

    internal const int ProgressiveInterlace = 2;
    internal const int H264HighProfile = 100;

    internal const int InvalidStreamNumber = unchecked((int)0xC00D36B3);
    internal const int AttributeNotFound = unchecked((int)0xC00D36E6);

    internal static readonly Guid VideoFormatNv12 = new("3231564E-0000-0010-8000-00AA00389B71");
    internal static readonly Guid VideoFormatH264 = new("34363248-0000-0010-8000-00AA00389B71");

    internal static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    internal static readonly Guid FrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    internal static readonly Guid PixelAspectRatio = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    internal static readonly Guid InterlaceMode = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    internal static readonly Guid Mpeg2Profile = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
    internal static readonly Guid DefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    internal static readonly Guid MinimumDisplayAperture = new("d7388766-18fe-48c6-a177-ee894867c8c4");
    internal static readonly Guid GeometricAperture = new("66758743-7e5f-400d-980a-aa8596c85696");
    internal static readonly Guid VideoRotation = new("c380465d-2271-428c-9b83-ecea3b4a85c1");
    internal static readonly Guid AdvancedVideoProcessing = new("0f81da2c-b537-4672-a8b2-a681b17307a3");

    private const short VariantInt64 = 20;
    private const short VariantUInt64 = 21;
    private const short VariantWideString = 31;
    private const int VariantSize = 24;
    private const int VariantValueOffset = 8;
    private const int ApertureBlobSize = 16;
    private const TransformEnumFlags TransformSearch = TransformEnumFlags.MFT_ENUM_FLAG_SYNCMFT
        | TransformEnumFlags.MFT_ENUM_FLAG_ASYNCMFT | TransformEnumFlags.MFT_ENUM_FLAG_HARDWARE
        | TransformEnumFlags.MFT_ENUM_FLAG_LOCALMFT | TransformEnumFlags.MFT_ENUM_FLAG_SORTANDFILTER;

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(IntPtr variant);

    internal static long Pack(int high, int low) => ((long)high << 32) | (uint)low;

    internal static (int High, int Low) Unpack(long value) => ((int)(value >> 32), (int)(value & 0xFFFFFFFF));

    internal static void Release(object? comObject)
    {
        if (comObject is not null)
        {
            Marshal.ReleaseComObject(comObject);
        }
    }

    internal static string Hex(int hresult) => "0x" + hresult.ToString("X8");

    internal static bool TryGetUInt32(IMFAttributes attributes, Guid key, out int value)
    {
        try
        {
            attributes.GetUINT32(key, out value);
            return true;
        }
        catch (COMException)
        {
            value = 0;
            return false;
        }
    }

    internal static bool TryGetUInt64(IMFAttributes attributes, Guid key, out long value)
    {
        try
        {
            attributes.GetUINT64(key, out value);
            return true;
        }
        catch (COMException)
        {
            value = 0;
            return false;
        }
    }

    internal static Guid GetGuidOrEmpty(IMFAttributes attributes, Guid key)
    {
        try
        {
            attributes.GetGUID(key, out var value);
            return value;
        }
        catch (COMException)
        {
            return Guid.Empty;
        }
    }

    internal static string? TryGetString(IMFAttributes attributes, Guid key)
    {
        try
        {
            attributes.GetAllocatedString(key, out var value, out _);
            return value;
        }
        catch (COMException)
        {
            return null;
        }
    }

    internal static bool TryGetAperture(IMFAttributes attributes, out PixelRect aperture)
    {
        if (TryGetApertureBlob(attributes, MinimumDisplayAperture, out aperture))
        {
            return true;
        }

        return TryGetApertureBlob(attributes, GeometricAperture, out aperture);
    }

    private static bool TryGetApertureBlob(IMFAttributes attributes, Guid key, out PixelRect aperture)
    {
        var blob = new byte[ApertureBlobSize];
        try
        {
            attributes.GetBlob(key, blob, blob.Length, out var size);
            if (size < ApertureBlobSize)
            {
                aperture = default;
                return false;
            }
        }
        catch (COMException)
        {
            aperture = default;
            return false;
        }

        aperture = new PixelRect(BitConverter.ToInt16(blob, 2), BitConverter.ToInt16(blob, 6),
            BitConverter.ToInt32(blob, 8), BitConverter.ToInt32(blob, 12));
        return aperture.Width > 0 && aperture.Height > 0;
    }

    internal static void Seek(IMFSourceReader reader, long ticks)
    {
        var variant = Marshal.AllocHGlobal(VariantSize);
        try
        {
            Marshal.WriteInt64(variant, 0);
            Marshal.WriteInt64(variant + VariantValueOffset, ticks);
            Marshal.WriteInt64(variant + 16, 0);
            Marshal.WriteInt16(variant, VariantInt64);
            reader.SetCurrentPosition(Guid.Empty, variant);
        }
        finally
        {
            Marshal.FreeHGlobal(variant);
        }
    }

    internal static long ReadDurationTicks(IMFSourceReader reader)
    {
        var variant = Marshal.AllocHGlobal(VariantSize);
        try
        {
            ZeroVariant(variant);
            var result = reader.GetPresentationAttribute(MediaSourceStream, MediaFoundationAttributes.MF_PD_DURATION,
                variant);
            var type = Marshal.ReadInt16(variant);
            var ticks = result >= 0 && type == VariantUInt64 ? Marshal.ReadInt64(variant + VariantValueOffset) : 0L;
            _ = PropVariantClear(variant);
            return ticks;
        }
        finally
        {
            Marshal.FreeHGlobal(variant);
        }
    }

    internal static string? ReadMimeType(IMFSourceReader reader)
    {
        var variant = Marshal.AllocHGlobal(VariantSize);
        try
        {
            ZeroVariant(variant);
            var result = reader.GetPresentationAttribute(MediaSourceStream, MediaFoundationAttributes.MF_PD_MIME_TYPE,
                variant);
            var type = Marshal.ReadInt16(variant);
            var text = result >= 0 && type == VariantWideString
                ? Marshal.PtrToStringUni(Marshal.ReadIntPtr(variant + VariantValueOffset))
                : null;
            _ = PropVariantClear(variant);
            return text;
        }
        finally
        {
            Marshal.FreeHGlobal(variant);
        }
    }

    private static void ZeroVariant(IntPtr variant)
    {
        Marshal.WriteInt64(variant, 0);
        Marshal.WriteInt64(variant + 8, 0);
        Marshal.WriteInt64(variant + 16, 0);
    }

    internal static MediaTransformInfo[] FindTransforms(Guid category, Guid majorType, Guid outputSubtype)
    {
        var output = new MFT_REGISTER_TYPE_INFO { guidMajorType = majorType, guidSubtype = outputSubtype };
        MediaFoundationInterop.MFTEnumEx(category, TransformSearch, null, output, out var activates, out var count);
        var found = new MediaTransformInfo[count];
        try
        {
            for (var index = 0; index < count; index++)
            {
                var pointer = Marshal.ReadIntPtr(activates, index * IntPtr.Size);
                var activate = (IMFActivate)Marshal.GetObjectForIUnknown(pointer);
                _ = Marshal.Release(pointer);
                try
                {
                    found[index] = new MediaTransformInfo(
                        TryGetString(activate, MediaFoundationAttributes.MFT_FRIENDLY_NAME_Attribute) ?? "unnamed",
                        TryGetString(activate, MediaFoundationAttributes.MFT_ENUM_HARDWARE_URL_Attribute) is not null);
                }
                finally
                {
                    Release(activate);
                }
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(activates);
        }

        return found;
    }

    internal static string Describe(MediaTransformInfo[] transforms)
    {
        if (transforms.Length == 0)
        {
            return "none";
        }

        var text = new StringBuilder();
        for (var index = 0; index < transforms.Length; index++)
        {
            if (index > 0)
            {
                text.Append(", ");
            }

            text.Append(transforms[index].Name).Append(transforms[index].Hardware ? " (hardware)" : " (software)");
        }

        return text.ToString();
    }

    internal static string DescribeTransform(IMFTransform transform)
    {
        IMFAttributes? attributes = null;
        try
        {
            transform.GetAttributes(out attributes);
        }
        catch (COMException exception)
        {
            return $"unnamed (no attributes, {Hex(exception.HResult)})";
        }

        if (attributes is null)
        {
            return "unnamed (no attributes)";
        }

        try
        {
            var name = TryGetString(attributes, MediaFoundationAttributes.MFT_FRIENDLY_NAME_Attribute) ?? "unnamed";
            var hardware = TryGetString(attributes, MediaFoundationAttributes.MFT_ENUM_HARDWARE_URL_Attribute) is not null;
            var asynchronous = TryGetUInt32(attributes, MediaFoundationAttributes.MF_TRANSFORM_ASYNC, out var flag)
                && flag != 0;
            return $"{name} ({(hardware ? "hardware" : "software")}{(asynchronous ? ", async" : string.Empty)})";
        }
        finally
        {
            Release(attributes);
        }
    }

    internal static string CodecName(Guid subtype)
    {
        Span<byte> bytes = stackalloc byte[16];
        _ = subtype.TryWriteBytes(bytes);
        var tag = BitConverter.ToUInt32(bytes);
        var isFourCcFamily = bytes[4] == 0x00 && bytes[5] == 0x00 && bytes[6] == 0x10 && bytes[7] == 0x00
            && bytes[8] == 0x80 && bytes[9] == 0x00 && bytes[10] == 0x00 && bytes[11] == 0xAA;
        if (!isFourCcFamily)
        {
            return subtype.ToString();
        }

        switch (tag)
        {
            case 0x0001:
                return "PCM";
            case 0x0003:
                return "Float";
            case 0x0055:
                return "MP3";
            case 0x1610:
                return "AAC";
            case 0x1600:
                return "AAC (ADTS)";
            case 0xF1AC:
                return "FLAC";
        }

        if (tag <= 0xFFFF)
        {
            return "audio tag 0x" + tag.ToString("X4");
        }

        Span<char> fourCc = stackalloc char[4];
        for (var index = 0; index < 4; index++)
        {
            var character = (char)bytes[index];
            fourCc[index] = character is >= ' ' and <= '~' ? character : '?';
        }

        return new string(fourCc);
    }
}

using System.Runtime.InteropServices;
using NAudio.MediaFoundation;

namespace Aetherphone.Core.Reels;

internal readonly record struct ReelVideoGeometry(
    int CodedWidth,
    int CodedHeight,
    int Stride,
    PixelRect Visible,
    int ClockwiseRotation)
{
    internal int DisplayWidth => ClockwiseRotation is 90 or 270 ? Visible.Height : Visible.Width;
    internal int DisplayHeight => ClockwiseRotation is 90 or 270 ? Visible.Width : Visible.Height;

    internal static ReelVideoGeometry Read(IMFSourceReader reader, int stream, in ReelMediaInfo native,
        out string rotationNote)
    {
        reader.GetCurrentMediaType(stream, out var type);
        try
        {
            _ = MediaFoundationVideo.TryGetUInt64(type, MediaFoundationVideo.FrameSize, out var packed);
            var (width, height) = MediaFoundationVideo.Unpack(packed);
            var visible = MediaFoundationVideo.TryGetAperture(type, out var aperture)
                ? aperture
                : new PixelRect(0, 0, width, height);
            var stride = MediaFoundationVideo.TryGetUInt32(type, MediaFoundationVideo.DefaultStride, out var found)
                && found > 0
                    ? found
                    : width;
            var hasRotation = MediaFoundationVideo.TryGetUInt32(type, MediaFoundationVideo.VideoRotation,
                out var outputRotation);
            var nativeRotation = native.RotationAttribute;
            var readerRotated = !hasRotation && (nativeRotation is 90 or 270) && width == native.CodedHeight
                && height == native.CodedWidth;
            var attribute = hasRotation ? outputRotation : readerRotated ? 0 : nativeRotation;
            var clockwise = (360 - attribute) % 360;
            rotationNote =
                $"native attribute {nativeRotation}, output attribute {(hasRotation ? outputRotation.ToString() : "absent")}, applied {clockwise} clockwise";
            return new ReelVideoGeometry(width, height, stride, visible, clockwise);
        }
        finally
        {
            MediaFoundationVideo.Release(type);
        }
    }

    internal static void RequestNv12(IMFSourceReader reader, int stream, string codec)
    {
        var type = MediaFoundationApi.CreateMediaType();
        try
        {
            type.SetGUID(MediaFoundationAttributes.MF_MT_MAJOR_TYPE, MediaTypes.MFMediaType_Video);
            type.SetGUID(MediaFoundationAttributes.MF_MT_SUBTYPE, MediaFoundationVideo.VideoFormatNv12);
            reader.SetCurrentMediaType(stream, IntPtr.Zero, type);
        }
        catch (COMException exception)
        {
            throw new ReelTranscodeException(ReelTranscodeFailure.DecoderMissing,
                $"Windows cannot decode {codec} video to NV12 ({MediaFoundationVideo.Hex(exception.HResult)}).");
        }
        finally
        {
            MediaFoundationVideo.Release(type);
        }
    }
}

internal readonly ref struct ReelLockedFrame
{
    internal readonly IntPtr Scan0;
    internal readonly int Pitch;
    internal readonly long Length;
    private readonly IMFMediaBuffer buffer;
    private readonly IMF2DBuffer? surface;

    internal ReelLockedFrame(IMFMediaBuffer buffer, in ReelVideoGeometry geometry)
    {
        this.buffer = buffer;
        surface = buffer as IMF2DBuffer;
        if (surface is not null)
        {
            surface.Lock2D(out Scan0, out Pitch);
            Length = (long)Pitch * geometry.CodedHeight * 3 / 2;
            return;
        }

        buffer.Lock(out Scan0, out _, out var current);
        Pitch = geometry.Stride;
        Length = current;
    }

    internal Nv12Planes Planes(in ReelVideoGeometry geometry)
    {
        var lumaBytes = Pitch * geometry.CodedHeight;
        if (Pitch <= 0 || Length < lumaBytes * 3L / 2)
        {
            throw new ReelTranscodeException(ReelTranscodeFailure.EncodeFailed,
                $"A decoded frame holds {Length} bytes at pitch {Pitch}, less than {geometry.CodedWidth}x{geometry.CodedHeight} NV12 needs.");
        }

        return new Nv12Planes(Scan0, lumaBytes, Scan0 + lumaBytes, lumaBytes / 2, Pitch);
    }

    public void Dispose()
    {
        if (surface is not null)
        {
            surface.Unlock2D();
            return;
        }

        buffer.Unlock();
    }
}

using Aetherphone.Core.Net;

namespace Aetherphone.Core.Reels;

internal static class ReelErrorCodes
{
    public const string MissingVideo = "reel_missing_video";
    public const string TooBig = "reel_too_big";
    public const string NotMp4 = "reel_not_mp4";
    public const string NotFaststart = "reel_not_faststart";
    public const string HeaderTooLarge = "reel_header_too_large";
    public const string Fragmented = "reel_fragmented";
    public const string NoVideoTrack = "reel_no_video_track";
    public const string VideoCodec = "reel_video_codec";
    public const string AudioCodec = "reel_audio_codec";
    public const string TooLong = "reel_too_long";
    public const string NoDuration = "reel_no_duration";
    public const string Resolution = "reel_resolution";
    public const string Aspect = "reel_aspect";
    public const string MediaInUse = "reel_media_in_use";
    public const string PosterInvalid = "reel_poster_invalid";
    public const string PostCooldown = "post_cooldown";
    public const string RateLimited = "rate_limited";
}

internal enum ReelShareStep : byte
{
    None,
    Convert,
    Cover,
    Upload,
    Create,
}

internal readonly record struct ReelShareFailure(ReelShareStep Step, ReelTranscodeFailure Transcode, AepFailure Network)
{
    private const int FirstServerErrorStatus = 500;
    private const int TooManyRequestsStatus = 429;

    // Checked before any upload, worded like the server's own refusal of the same file.
    internal static readonly ReelShareFailure TooBig = new(ReelShareStep.Create, default,
        new AepFailure(AepFailureKind.Server, 0, ReelErrorCodes.TooBig, null, null, null));

    internal static ReelShareFailure Converting(ReelTranscodeFailure failure) => new(ReelShareStep.Convert, failure, default);

    internal static ReelShareFailure Covering(ReelTranscodeFailure failure) => new(ReelShareStep.Cover, failure, default);

    internal static ReelShareFailure Uploading(AepFailure failure) => new(ReelShareStep.Upload, default, failure);

    internal static ReelShareFailure Creating(AepFailure failure) => new(ReelShareStep.Create, default, failure);

    internal bool Retryable => Step switch
    {
        ReelShareStep.Convert => Transcode == ReelTranscodeFailure.EncodeFailed,
        ReelShareStep.Cover => Transcode is ReelTranscodeFailure.EncodeFailed or ReelTranscodeFailure.UnreadableInput,
        ReelShareStep.Upload => Transient(Network),
        ReelShareStep.Create => Transient(Network) || IsCode(ReelErrorCodes.MissingVideo)
            || IsCode(ReelErrorCodes.PostCooldown),
        _ => false,
    };

    // The server deletes a refused video and never saw the upload of a missing one, so only a create that failed in
    // transit or on cooldown keeps its upload for the retry.
    internal bool KeepsUpload => Step == ReelShareStep.Create && (Transient(Network) || IsCode(ReelErrorCodes.PostCooldown));

    private bool IsCode(string code) => string.Equals(Network.Code, code, StringComparison.Ordinal);

    private static bool Transient(AepFailure failure) => failure.Kind switch
    {
        AepFailureKind.Offline or AepFailureKind.Timeout or AepFailureKind.RateLimitPaused
            or AepFailureKind.BadResponse => true,
        AepFailureKind.Server => failure.StatusCode is >= FirstServerErrorStatus or TooManyRequestsStatus
            || string.Equals(failure.Code, ReelErrorCodes.RateLimited, StringComparison.Ordinal),
        _ => false,
    };
}

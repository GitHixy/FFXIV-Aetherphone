namespace Aetherphone.Core.Reels;

// Cards offer themselves while the list draws, so the winner is only known once the frame's list is done.
internal struct FeedReelFocus
{
    internal const float MostlyVisible = 0.6f;

    private string? candidateId;
    private float candidateShare;
    private int candidateFrame;

    internal bool Offer(string postId, float visibleShare, int frame)
    {
        if (visibleShare < MostlyVisible)
        {
            return false;
        }

        if (candidateFrame == frame && candidateId is not null && visibleShare <= candidateShare)
        {
            return false;
        }

        candidateId = postId;
        candidateShare = visibleShare;
        candidateFrame = frame;
        return true;
    }

    internal readonly string? Winner(int frame) => candidateFrame == frame ? candidateId : null;

    internal static float VisibleShare(float top, float bottom, float viewportTop, float viewportBottom)
    {
        var height = bottom - top;
        if (height <= 0f)
        {
            return 0f;
        }

        var visible = MathF.Min(bottom, viewportBottom) - MathF.Max(top, viewportTop);
        return Math.Clamp(visible / height, 0f, 1f);
    }
}

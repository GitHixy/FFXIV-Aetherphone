using Aetherphone.Core.Aethernet;
using Aetherphone.Core.Aethernet.Contracts;

namespace Aetherphone.Core.Social;

internal abstract partial class SocialFeedStore
{
    private FeedLane<PostDto>[] attachedLanes = Array.Empty<FeedLane<PostDto>>();

    protected string? FeedRegions => feedRegions;

    protected bool FeedIncludesSensitive => feedIncludesSensitive;

    // Lanes a store keeps beside the shared feeds still follow every like, save, follow, comment count and removal.
    protected void AttachLanes(params FeedLane<PostDto>[] lanes)
    {
        attachedLanes = lanes;
    }

    private void MapAttachedLanes(Func<PostDto[], PostDto[]> map)
    {
        var lanes = attachedLanes;
        for (var index = 0; index < lanes.Length; index++)
        {
            lanes[index].Items = map(lanes[index].Items);
        }
    }
}

namespace Aetherphone.Core.Social;

// Where a post was on screen when its impression was recorded. The numbers are the wire values the
// server's ViewSources table uses, so a value is never renumbered, only appended.
internal enum PostViewSource : byte
{
    Other = 0,
    ForYou = 1,
    Following = 2,
    Latest = 3,
    Explore = 4,
    Profile = 5,
    Hashtag = 6,
    Search = 7,
    Story = 8,
    Detail = 9,
}

internal static class PostViewSources
{
    public static PostViewSource For(SocialFeedScope scope) => scope switch
    {
        SocialFeedScope.ForYou => PostViewSource.ForYou,
        SocialFeedScope.Following => PostViewSource.Following,
        _ => PostViewSource.Latest,
    };
}

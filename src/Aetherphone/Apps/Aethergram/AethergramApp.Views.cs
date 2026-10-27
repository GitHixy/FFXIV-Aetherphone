using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Social;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Aethergram;

internal sealed partial class AethergramApp
{
    private readonly PostViewsSheet postViewsSheet = new();
    private string? storyOpenedPostId;

    private static bool ViewSourceFor(PostSource source, out PostViewSource viewSource)
    {
        viewSource = source switch
        {
            PostSource.Explore => PostViewSource.Explore,
            PostSource.Profile or PostSource.Tagged => PostViewSource.Profile,
            PostSource.Hashtag => PostViewSource.Hashtag,
            _ => PostViewSource.Other,
        };
        return viewSource != PostViewSource.Other;
    }

    private PostViewSource DetailViewSource(PostDto post) =>
        string.Equals(post.Id, storyOpenedPostId, StringComparison.Ordinal) ? PostViewSource.Story : PostViewSource.Detail;

    private void BeginImpressionPass(PostViewSource source)
    {
        var viewportTop = ImGui.GetWindowPos().Y;
        store.BeginImpressions(viewportTop, viewportTop + ImGui.GetWindowSize().Y, ImGui.GetIO().DeltaTime, source);
    }

    private void OpenDetailFromStory(StorySharedPostDto shared)
    {
        if (shared.IsChirp)
        {
            ChirpChatBridge.Open(shared.PostId);
            return;
        }

        storyOpenedPostId = shared.PostId;
        OpenDetailFromLink(shared.PostId);
    }

    private void OpenPostViews(PostDto post)
    {
        store.LoadPostViews(post.Id);
        postViewsSheet.Open();
    }

    private void DrawPostViewsSheet(Rect screen)
    {
        if (!postViewsSheet.CapturesPointer)
        {
            return;
        }

        postViewsSheet.Draw(screen, Ink, store.PostViews, store.PostViewsLoading);
    }
}

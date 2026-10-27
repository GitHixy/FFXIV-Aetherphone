using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Social;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Chirper;

internal sealed partial class ChirperApp
{
    private readonly PostViewsSheet postViewsSheet = new();

    private void BeginImpressionPass(PostViewSource source)
    {
        var viewportTop = ImGui.GetWindowPos().Y;
        store.BeginImpressions(viewportTop, viewportTop + ImGui.GetWindowSize().Y, ImGui.GetIO().DeltaTime, source);
    }

    private void DrawObservedPost(PostDto post, bool showPinned = false)
    {
        var rowTop = ImGui.GetCursorScreenPos().Y;
        DrawPost(post, showPinned: showPinned);
        store.ObserveImpression(post.RepostOfId ?? post.Id, rowTop, ImGui.GetCursorScreenPos().Y);
    }

    private bool IsOwnPost(PostDto post) => string.Equals(store.Me?.Id, post.AuthorId, StringComparison.Ordinal);

    private void OpenPostViews(PostDto post)
    {
        actions.Reset();
        store.LoadPostViews(post.Id);
        postViewsSheet.Open();
    }

    private void DrawPostViewsSheet(Rect screen)
    {
        if (!postViewsSheet.CapturesPointer)
        {
            return;
        }

        postViewsSheet.Draw(screen, ChirperInk.Shared, store.PostViews, store.PostViewsLoading);
    }
}

using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Animation;
using Aetherphone.Core.Social;
using Aetherphone.Core.Theme;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Windows.Components;

internal sealed partial class StoryViewerOverlay
{
    private const float LikeSlotWidth = 44f;
    private const float LikeIconSize = 24f;
    private const float LikeHitHalf = 18f;
    private const float LikePopGrowth = 0.35f;
    private const float ViewerHeartSize = 13f;
    private const float ViewerHeartGap = 8f;

    private static readonly Vector4 LikeIdleInk = new(1f, 1f, 1f, 0.95f);

    private readonly DoubleTapLike likeBurst = new();
    private readonly Action<string>? openPost;
    private Action<StoryDto>? onToggleLike;
    private Rect sharedCardBounds;
    private string likePopStoryId = string.Empty;
    private double likePopStartedAt;

    private bool StageReady(StoryDto story) =>
        story.SharedPost is { Available: false } || images.Get(story.MediaUrl) is not null;

    private bool PressOnSharedCard() => UiInteract.Hover(sharedCardBounds.Min, sharedCardBounds.Max);

    private void DrawStage(ImDrawListPtr drawList, Rect stage, PhoneTheme theme, StoryDto story, float scale,
        bool interactive)
    {
        if (story.SharedPost is { } shared)
        {
            DrawSharedPost(drawList, stage, theme, shared, scale, interactive);
        }
        else
        {
            sharedCardBounds = default;
            DrawImage(drawList, stage, story, scale);
        }

        likeBurst.DrawBurst(drawList, stage, story.Id);
    }

    private void DrawSharedPost(ImDrawListPtr drawList, Rect stage, PhoneTheme theme, StorySharedPostDto shared,
        float scale, bool interactive)
    {
        var hovered = interactive && openPost is not null && PressOnSharedCard();
        var content = new SharedPostCardContent(SocialIdentity.Name(shared.AuthorDisplayName, shared.AuthorHandle),
            shared.AuthorAvatarUrl, shared.MediaUrl, shared.MediaWidth, shared.MediaHeight, shared.Available);
        var bounds = SharedPostCard.Draw(drawList, stage, theme, content, images, lodestone, scale, hovered);
        sharedCardBounds = openPost is null ? default : bounds;
        if (!interactive || openPost is not { } openSharedPost)
        {
            return;
        }

        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if (UiInteract.Click(bounds.Min, bounds.Max, hovered))
        {
            openSharedPost(shared.PostId);
        }
    }

    private float LikeReserve(float scale) => onToggleLike is null ? 0f : LikeSlotWidth * scale;

    private void DrawLikeButton(ImDrawListPtr drawList, Vector2 center, StoryDto story, float scale)
    {
        if (onToggleLike is not { } toggle)
        {
            return;
        }

        var hit = new Vector2(LikeHitHalf * scale, LikeHitHalf * scale);
        var hovered = UiInteract.Hover(center - hit, center + hit);
        var pop = 1f;
        if (story.Id == likePopStoryId)
        {
            pop += LikePopGrowth * (1f - Spring.Settle((float)(ImGui.GetTime() - likePopStartedAt), Motion.Release));
        }

        PhoneIcon.Draw(drawList, center, story.Liked ? PhoneIcons.HeartFilled : PhoneIcons.Heart,
            story.Liked ? CommentHeart.LikeRed : LikeIdleInk, LikeIconSize * scale * pop);
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if (!UiInteract.Click(center - hit, center + hit, hovered))
        {
            return;
        }

        if (!story.Liked)
        {
            likeBurst.Burst(story.Id);
            likePopStoryId = story.Id;
            likePopStartedAt = ImGui.GetTime();
        }

        toggle(story);
    }

    private static float ViewerHeartReserve(StoryViewerDto viewer, float scale) =>
        viewer.Liked ? (ViewerHeartSize + ViewerHeartGap) * scale : 0f;

    private static void DrawViewerHeart(ImDrawListPtr drawList, StoryViewerDto viewer, float stampLeft, float centerY,
        float scale)
    {
        if (!viewer.Liked)
        {
            return;
        }

        PhoneIcon.Draw(drawList,
            new Vector2(stampLeft - (ViewerHeartGap + ViewerHeartSize * 0.5f) * scale, centerY),
            PhoneIcons.HeartFilled, CommentHeart.LikeRed, ViewerHeartSize * scale);
    }
}

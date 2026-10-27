using Aetherphone.Core;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Lodestone;
using Aetherphone.Core.Media;
using Aetherphone.Core.Theme;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace Aetherphone.Windows.Components;

internal readonly record struct SharedPostCardContent(string AuthorName, string? AvatarUrl, string? PhotoUrl,
    int PhotoWidth, int PhotoHeight, bool Available);

internal static class SharedPostCard
{
    private const float CardWidthFraction = 0.76f;
    private const float CardMaxHeightFraction = 0.7f;
    private const float CardRaise = 0.04f;
    private const float HeaderHeight = 40f;
    private const float AvatarRadius = 12f;
    private const float HeaderInset = 10f;
    private const float NameGap = 8f;
    private const float MinPhotoAspect = 0.56f;
    private const float MaxPhotoAspect = 1.25f;
    private const float UnavailableHeight = 110f;
    private const float UnavailableTextInset = 18f;

    private static readonly Vector4 BackdropTop = new(0.36f, 0.18f, 0.46f, 1f);
    private static readonly Vector4 BackdropBottom = new(0.86f, 0.36f, 0.32f, 1f);
    private static readonly Vector4 CardFill = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 CardHoverFill = new(0.94f, 0.94f, 0.96f, 1f);
    private static readonly Vector4 CardInk = new(0.07f, 0.07f, 0.09f, 1f);
    private static readonly Vector4 CardMutedInk = new(0.38f, 0.38f, 0.42f, 1f);
    private static readonly Vector4 PhotoPlaceholder = new(0.88f, 0.88f, 0.9f, 1f);

    // Returns the card's bounds so the caller can make it tappable; an unavailable post has nothing to open.
    public static Rect Draw(ImDrawListPtr drawList, Rect stage, PhoneTheme theme, in SharedPostCardContent content,
        RemoteImageCache images, LodestoneService lodestone, float scale, bool hovered)
    {
        var stageRounding = Metrics.Radius.Md * scale;
        Squircle.FillVerticalGradient(drawList, stage.Min, stage.Max, stageRounding, ImGui.GetColorU32(BackdropTop),
            ImGui.GetColorU32(BackdropBottom));
        var cardRounding = Metrics.Radius.Md * scale;
        var width = stage.Width * CardWidthFraction;
        if (!content.Available)
        {
            DrawUnavailable(drawList, stage, width, cardRounding, scale);
            return default;
        }

        var aspect = content.PhotoWidth > 0 && content.PhotoHeight > 0
            ? Math.Clamp((float)content.PhotoHeight / content.PhotoWidth, MinPhotoAspect, MaxPhotoAspect)
            : 1f;
        var headerHeight = HeaderHeight * scale;
        var maxHeight = stage.Height * CardMaxHeightFraction;
        if (headerHeight + width * aspect > maxHeight)
        {
            width = MathF.Max(1f, (maxHeight - headerHeight) / aspect);
        }

        var photoHeight = width * aspect;
        var cardHeight = headerHeight + photoHeight;
        var center = new Vector2(stage.Center.X, stage.Center.Y - stage.Height * CardRaise);
        var cardMin = new Vector2(center.X - width * 0.5f, center.Y - cardHeight * 0.5f);
        var cardMax = new Vector2(center.X + width * 0.5f, center.Y + cardHeight * 0.5f);
        Squircle.Fill(drawList, cardMin, cardMax, cardRounding, ImGui.GetColorU32(hovered ? CardHoverFill : CardFill));

        var avatarRadius = AvatarRadius * scale;
        var avatarCenter = new Vector2(cardMin.X + HeaderInset * scale + avatarRadius, cardMin.Y + headerHeight * 0.5f);
        AvatarView.DrawRemote(drawList, avatarCenter, avatarRadius, theme, content.AuthorName, string.Empty,
            content.AvatarUrl, images, lodestone, 0.8f, 24);
        var nameLeft = avatarCenter.X + avatarRadius + NameGap * scale;
        var nameMaxWidth = MathF.Max(1f, cardMax.X - HeaderInset * scale - nameLeft);
        var name = Typography.FitText(content.AuthorName, nameMaxWidth, TextStyles.SubheadlineEmphasized);
        var nameSize = Typography.Measure(name, TextStyles.SubheadlineEmphasized);
        Typography.Draw(drawList, new Vector2(nameLeft, avatarCenter.Y - nameSize.Y * 0.5f), name, CardInk,
            TextStyles.SubheadlineEmphasized);

        var photoMin = new Vector2(cardMin.X, cardMin.Y + headerHeight);
        var texture = images.Get(content.PhotoUrl);
        if (texture is null)
        {
            drawList.AddRectFilled(photoMin, cardMax, ImGui.GetColorU32(PhotoPlaceholder), cardRounding,
                ImDrawFlags.RoundCornersBottom);
            Typography.DrawCentered(drawList, new Vector2(center.X, photoMin.Y + photoHeight * 0.5f),
                Loc.T(images.Failed(content.PhotoUrl) ? L.Common.ImageFailed : L.Common.Loading), CardMutedInk,
                TextStyles.Footnote);
        }
        else
        {
            var (uv0, uv1) = ImageFit.Cover(texture.Size.X, texture.Size.Y, width, photoHeight);
            drawList.AddImageRounded(texture.Handle, photoMin, cardMax, uv0, uv1, 0xFFFFFFFFu, cardRounding,
                ImDrawFlags.RoundCornersBottom);
        }

        return new Rect(cardMin, cardMax);
    }

    private static void DrawUnavailable(ImDrawListPtr drawList, Rect stage, float width, float rounding, float scale)
    {
        var height = UnavailableHeight * scale;
        var center = new Vector2(stage.Center.X, stage.Center.Y - stage.Height * CardRaise);
        var cardMin = new Vector2(center.X - width * 0.5f, center.Y - height * 0.5f);
        var cardMax = new Vector2(center.X + width * 0.5f, center.Y + height * 0.5f);
        Squircle.Fill(drawList, cardMin, cardMax, rounding, ImGui.GetColorU32(CardFill));
        AppSkin.Icon(drawList, new Vector2(center.X, center.Y - 18f * scale), IconGlyph.Of(FontAwesomeIcon.EyeSlash),
            CardMutedInk, 1.2f);
        Typography.DrawWrappedCentered(drawList, new Vector2(center.X, center.Y + 14f * scale),
            Loc.T(L.Story.PostUnavailable), CardInk, TextStyles.Subheadline,
            MathF.Max(1f, width - UnavailableTextInset * 2f * scale));
    }
}

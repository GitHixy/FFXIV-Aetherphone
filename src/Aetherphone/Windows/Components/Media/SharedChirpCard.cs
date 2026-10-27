using Aetherphone.Core;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Lodestone;
using Aetherphone.Core.Media;
using Aetherphone.Core.Theme;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Windows.Components;

internal readonly record struct SharedChirpCardContent(string AuthorName, string Handle, string? AvatarUrl,
    string Text, string? PhotoUrl, int PhotoWidth, int PhotoHeight);

internal static class SharedChirpCard
{
    private const float CardWidthFraction = 0.80f;
    private const float CardMaxHeightFraction = 0.75f;
    private const float CardRaise = 0.04f;
    private const float Padding = 14f;
    private const float AvatarRadius = 15f;
    private const float NameGap = 9f;
    private const float BrandIconSize = 18f;
    private const float SectionGap = 10f;
    private const float PhotoRounding = 10f;
    private const float MinPhotoAspect = 0.56f;
    private const float MaxPhotoAspect = 1.0f;
    private const int MaxLinesWithPhoto = 4;
    private const int MaxLinesAlone = 10;

    private static readonly TextStyle BodyStyle = TextStyles.Callout;
    private static readonly Vector4 BackdropTop = new(0.04f, 0.20f, 0.45f, 1f);
    private static readonly Vector4 CardFill = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 CardHoverFill = new(0.94f, 0.95f, 0.97f, 1f);
    private static readonly Vector4 CardInk = new(0.07f, 0.07f, 0.09f, 1f);
    private static readonly Vector4 CardMutedInk = new(0.38f, 0.38f, 0.42f, 1f);
    private static readonly Vector4 PhotoPlaceholder = new(0.88f, 0.88f, 0.9f, 1f);

    private static string handleSource = string.Empty;
    private static string handleLabel = string.Empty;

    public static Rect Draw(ImDrawListPtr drawList, Rect stage, PhoneTheme theme, in SharedChirpCardContent content,
        RemoteImageCache images, LodestoneService lodestone, float scale, bool hovered)
    {
        var stageRounding = Metrics.Radius.Md * scale;
        Squircle.FillVerticalGradient(drawList, stage.Min, stage.Max, stageRounding, ImGui.GetColorU32(BackdropTop),
            ImGui.GetColorU32(BrandAccents.Chirper));
        var padding = Padding * scale;
        var width = stage.Width * CardWidthFraction;
        var innerWidth = width - padding * 2f;
        var avatarRadius = AvatarRadius * scale;
        var headerHeight = avatarRadius * 2f;
        var gap = SectionGap * scale;

        var textScale = BodyStyle.Scale;
        var hasPhoto = !string.IsNullOrEmpty(content.PhotoUrl);
        var maxLines = hasPhoto ? MaxLinesWithPhoto : MaxLinesAlone;
        var textHeight = 0f;
        RichTextLayout? layout = null;
        if (content.Text.Length > 0)
        {
            layout = LinkText.LayoutFor(content.Text, innerWidth / textScale);
            var naturalHeight = layout is not null
                ? layout.Size.Y * textScale
                : Typography.MeasureWrappedBlock(content.Text, BodyStyle, innerWidth).Y;
            textHeight = MathF.Min(naturalHeight, Typography.Measure("Ag", BodyStyle).Y * maxLines);
        }

        var aspect = content.PhotoWidth > 0 && content.PhotoHeight > 0
            ? Math.Clamp((float)content.PhotoHeight / content.PhotoWidth, MinPhotoAspect, MaxPhotoAspect)
            : MinPhotoAspect;
        var photoHeight = hasPhoto ? innerWidth * aspect : 0f;
        var fixedHeight = padding * 2f + headerHeight + (textHeight > 0f ? gap + textHeight : 0f)
                          + (hasPhoto ? gap : 0f);
        var maxHeight = stage.Height * CardMaxHeightFraction;
        photoHeight = MathF.Max(0f, MathF.Min(photoHeight, maxHeight - fixedHeight));
        var cardHeight = fixedHeight + photoHeight;
        var center = new Vector2(stage.Center.X, stage.Center.Y - stage.Height * CardRaise);
        var cardMin = new Vector2(center.X - width * 0.5f, center.Y - cardHeight * 0.5f);
        var cardMax = new Vector2(center.X + width * 0.5f, center.Y + cardHeight * 0.5f);
        var cardRounding = Metrics.Radius.Md * scale;
        Squircle.Fill(drawList, cardMin, cardMax, cardRounding, ImGui.GetColorU32(hovered ? CardHoverFill : CardFill));

        var avatarCenter = new Vector2(cardMin.X + padding + avatarRadius, cardMin.Y + padding + avatarRadius);
        AvatarView.DrawRemote(drawList, avatarCenter, avatarRadius, theme, content.AuthorName, string.Empty,
            content.AvatarUrl, images, lodestone, 0.8f, 24);
        var brandCenter = new Vector2(cardMax.X - padding - BrandIconSize * 0.5f * scale, avatarCenter.Y);
        PhoneIcon.Draw(drawList, brandCenter, PhoneIcons.Feather, BrandAccents.Chirper, BrandIconSize * scale);
        var nameLeft = avatarCenter.X + avatarRadius + NameGap * scale;
        var nameMaxWidth = MathF.Max(1f, brandCenter.X - BrandIconSize * 0.5f * scale - NameGap * scale - nameLeft);
        var name = Typography.FitText(content.AuthorName, nameMaxWidth, TextStyles.SubheadlineEmphasized);
        var nameSize = Typography.Measure(name, TextStyles.SubheadlineEmphasized);
        var handle = HandleLabel(content.Handle);
        if (handle.Length == 0)
        {
            Typography.Draw(drawList, new Vector2(nameLeft, avatarCenter.Y - nameSize.Y * 0.5f), name, CardInk,
                TextStyles.SubheadlineEmphasized);
        }
        else
        {
            var handleSize = Typography.Measure(handle, TextStyles.Footnote);
            var stackTop = avatarCenter.Y - (nameSize.Y + handleSize.Y) * 0.5f;
            Typography.Draw(drawList, new Vector2(nameLeft, stackTop), name, CardInk, TextStyles.SubheadlineEmphasized);
            Typography.Draw(drawList, new Vector2(nameLeft, stackTop + nameSize.Y),
                Typography.FitText(handle, nameMaxWidth, TextStyles.Footnote), CardMutedInk, TextStyles.Footnote);
        }

        var contentTop = cardMin.Y + padding + headerHeight;
        if (textHeight > 0f)
        {
            contentTop += gap;
            var textMin = new Vector2(cardMin.X + padding, contentTop);
            var textMax = new Vector2(cardMax.X - padding, contentTop + textHeight);
            drawList.PushClipRect(textMin, textMax, true);
            if (layout is not null)
            {
                LinkText.Draw(drawList, layout, textMin, textScale, CardInk, BrandAccents.Chirper, 1f, false);
            }
            else
            {
                Typography.DrawWrappedLeft(textMin, content.Text, CardInk, BodyStyle, innerWidth);
            }

            drawList.PopClipRect();
            contentTop += textHeight;
        }

        if (hasPhoto && photoHeight > 0f)
        {
            contentTop += gap;
            DrawPhoto(drawList, images, content.PhotoUrl!,
                new Vector2(cardMin.X + padding, contentTop),
                new Vector2(cardMax.X - padding, contentTop + photoHeight), PhotoRounding * scale);
        }

        return new Rect(cardMin, cardMax);
    }

    private static void DrawPhoto(ImDrawListPtr drawList, RemoteImageCache images, string url, Vector2 min, Vector2 max,
        float rounding)
    {
        var texture = images.Get(url);
        if (texture is null)
        {
            Squircle.Fill(drawList, min, max, rounding, ImGui.GetColorU32(PhotoPlaceholder));
            Typography.DrawCentered(drawList, (min + max) * 0.5f,
                Loc.T(images.Failed(url) ? L.Common.ImageFailed : L.Common.Loading), CardMutedInk, TextStyles.Footnote);
            return;
        }

        var size = max - min;
        var (uv0, uv1) = ImageFit.Cover(texture.Size.X, texture.Size.Y, size.X, size.Y);
        drawList.AddImageRounded(texture.Handle, min, max, uv0, uv1, 0xFFFFFFFFu, rounding,
            ImDrawFlags.RoundCornersAll);
    }

    private static string HandleLabel(string handle)
    {
        if (handle.Length == 0)
        {
            return string.Empty;
        }

        if (!ReferenceEquals(handle, handleSource))
        {
            handleSource = handle;
            handleLabel = "@" + handle;
        }

        return handleLabel;
    }
}

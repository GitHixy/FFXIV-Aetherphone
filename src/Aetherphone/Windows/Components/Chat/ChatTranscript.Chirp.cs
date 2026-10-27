using Aetherphone.Core;
using Aetherphone.Core.Animation;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Social;
using Aetherphone.Core.Theme;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace Aetherphone.Windows.Components;

internal sealed partial class ChatTranscript
{
    private const float ChirpCardMaxWidth = 250f;
    private const float ChirpCardWidthFraction = 0.74f;
    private const float ChirpBadgeRadius = 15f;
    private const float ChirpBadgeGap = 9f;
    private const float ChirpBadgeIconSize = 16f;
    private const float ChirpPhotoAspect = 0.56f;
    private const float ChirpPhotoRounding = 10f;
    private const int ChirpTextMaxLines = 4;
    private static readonly TextStyle ChirpTextStyle = TextStyles.Subheadline;

    private void DrawChirpBubble(TranscriptMessage message, int index, string postId, in ChatTranscriptModel model)
    {
        var scale = UiScale.Current;
        var mine = message.SenderId == model.MyUserId;
        var placeholder = (message.Flags & TranscriptFlags.Placeholder) != 0;
        var drawList = ImGui.GetWindowDrawList();
        var paddingX = 11f * scale;
        var paddingY = 9f * scale;
        var badgeRadius = ChirpBadgeRadius * scale;
        var badgeColumn = badgeRadius * 2f + ChirpBadgeGap * scale;
        var innerWidth = MathF.Min(rowWidth * ChirpCardWidthFraction, ChirpCardMaxWidth * scale) - paddingX * 2f;

        var resolution = ChirpChatBridge.Resolve(postId);
        var card = resolution.Card;
        var title = card?.AuthorName
                    ?? Loc.T(resolution.Missed ? L.Chirper.ChirpUnavailable : L.Common.Loading);
        var handle = card?.HandleLabel ?? string.Empty;
        var text = card?.Text ?? string.Empty;
        var photoUrl = card?.PhotoUrl;
        var textWidth = innerWidth - badgeColumn;
        var titleSize = Typography.Measure(title, TextStyles.SubheadlineEmphasized);
        var handleSize = handle.Length > 0 ? Typography.Measure(handle, TextStyles.Footnote) : Vector2.Zero;
        var headerTextHeight = titleSize.Y + (handleSize.Y > 0f ? 1f * scale + handleSize.Y : 0f);
        var headerHeight = MathF.Max(badgeRadius * 2f, headerTextHeight);

        var bodyScale = ChirpTextStyle.Scale;
        var bodyHeight = 0f;
        RichTextLayout? bodyLayout = null;
        if (text.Length > 0)
        {
            var lineHeight = Typography.Measure("Ag", ChirpTextStyle).Y;
            bodyLayout = LinkText.LayoutFor(text, innerWidth / bodyScale);
            var naturalHeight = bodyLayout is not null
                ? bodyLayout.Size.Y * bodyScale
                : Typography.MeasureWrappedBlock(text, ChirpTextStyle, innerWidth).Y;
            bodyHeight = MathF.Min(naturalHeight, lineHeight * ChirpTextMaxLines);
        }

        var photoHeight = photoUrl is null ? 0f : innerWidth * ChirpPhotoAspect;
        var stamp = MeasureStamp(message, mine, scale);
        var forwardLabel = MeasureForwardLabel(message, scale);
        var forwardBlock = forwardLabel.Y > 0f ? forwardLabel.Y + 3f * scale : 0f;
        var contentWidth = card is not null
            ? innerWidth
            : MathF.Min(innerWidth, MathF.Max(badgeColumn + titleSize.X, stamp.Width));
        contentWidth = MathF.Max(contentWidth, forwardLabel.X);
        var bubbleWidth = contentWidth + paddingX * 2f;
        var bubbleHeight = paddingY + forwardBlock + headerHeight
                           + (bodyHeight > 0f ? 6f * scale + bodyHeight : 0f)
                           + (photoHeight > 0f ? 7f * scale + photoHeight : 0f)
                           + 4f * scale + stamp.Height + paddingY;
        var start = ImGui.GetCursorScreenPos();
        var bubbleMin = new Vector2(mine ? start.X + rowWidth - bubbleWidth : start.X, start.Y);
        var bubbleMax = bubbleMin + new Vector2(bubbleWidth, bubbleHeight);
        ConsumeScrollTarget(message.Id, bubbleMin.Y);
        var entrance = entrances.Progress(index);
        var fx = BubblePop.For(entrance, scale, new Vector2(mine ? bubbleMax.X : bubbleMin.X, bubbleMax.Y));
        var scaledMin = fx.Apply(bubbleMin);
        var scaledMax = fx.Apply(bubbleMax);
        var fill = BubbleFill(mine, model);
        var ink = BubbleInk(mine, model);
        var mutedInk = mine ? new Vector4(1f, 1f, 1f, 0.78f) : Palette.WithAlpha(model.MutedInk, 0.95f);
        var accentInk = mine ? new Vector4(1f, 1f, 1f, 0.88f) : BrandAccents.Chirper;
        if (placeholder || resolution.Missed)
        {
            fill = Palette.WithAlpha(fill, fill.W * 0.55f);
            ink = model.MutedInk;
        }

        FillBubble(drawList, scaledMin, scaledMax, Rounding(model) * fx.Pop, mine,
            ImGui.GetColorU32(Palette.WithAlpha(fill, fill.W * fx.Alpha)));
        DrawFlash(drawList, message.Id, scaledMin, scaledMax, Rounding(model) * fx.Pop, mine, model);
        var contentTop = bubbleMin.Y + paddingY;
        if (forwardBlock > 0f)
        {
            DrawForwardLabel(drawList, new Vector2(bubbleMin.X + paddingX, contentTop), fx, mine, model, scale);
            contentTop += forwardBlock;
        }

        var badgeCenter = new Vector2(bubbleMin.X + paddingX + badgeRadius, contentTop + headerHeight * 0.5f);
        var badgeFill = mine ? new Vector4(1f, 1f, 1f, 0.20f) : Palette.WithAlpha(BrandAccents.Chirper, 0.18f);
        drawList.AddCircleFilled(fx.Apply(badgeCenter), badgeRadius * fx.Pop,
            ImGui.GetColorU32(Palette.WithAlpha(badgeFill, badgeFill.W * fx.Alpha)), 32);
        PhoneIcon.Draw(drawList, fx.Apply(badgeCenter),
            resolution.Missed ? PhoneIcons.EyeOff : PhoneIcons.Feather,
            Palette.WithAlpha(accentInk, accentInk.W * fx.Alpha), ChirpBadgeIconSize * scale * fx.Pop);

        var textLeft = bubbleMin.X + paddingX + badgeColumn;
        var titleTop = contentTop + (headerHeight - headerTextHeight) * 0.5f;
        Typography.Draw(drawList, fx.Apply(new Vector2(textLeft, titleTop)),
            Typography.FitText(title, textWidth, TextStyles.SubheadlineEmphasized),
            Palette.WithAlpha(ink, ink.W * fx.Alpha),
            TextStyles.SubheadlineEmphasized.Scale * fx.Pop, TextStyles.SubheadlineEmphasized.Weight);
        if (handleSize.Y > 0f)
        {
            Typography.Draw(drawList, fx.Apply(new Vector2(textLeft, titleTop + titleSize.Y + 1f * scale)),
                Typography.FitText(handle, textWidth, TextStyles.Footnote),
                Palette.WithAlpha(mutedInk, mutedInk.W * fx.Alpha),
                TextStyles.Footnote.Scale * fx.Pop, TextStyles.Footnote.Weight);
        }

        contentTop += headerHeight;
        if (bodyHeight > 0f)
        {
            contentTop += 6f * scale;
            var bodyMin = new Vector2(bubbleMin.X + paddingX, contentTop);
            var bodyMax = new Vector2(bubbleMin.X + paddingX + innerWidth, contentTop + bodyHeight);
            drawList.PushClipRect(fx.Apply(bodyMin), fx.Apply(bodyMax), true);
            if (bodyLayout is not null)
            {
                LinkText.Draw(drawList, bodyLayout, fx.Apply(bodyMin), bodyScale * fx.Pop, ink, ink, fx.Alpha, false);
            }
            else
            {
                Typography.DrawWrappedLeft(fx.Apply(bodyMin), text, Palette.WithAlpha(ink, ink.W * fx.Alpha),
                    ChirpTextStyle, innerWidth);
            }

            drawList.PopClipRect();
            contentTop += bodyHeight;
        }

        if (card is not null && photoUrl is not null)
        {
            contentTop += 7f * scale;
            DrawChirpPhoto(drawList, card, photoUrl,
                fx.Apply(new Vector2(bubbleMin.X + paddingX, contentTop)),
                fx.Apply(new Vector2(bubbleMin.X + paddingX + innerWidth, contentTop + photoHeight)),
                ChirpPhotoRounding * scale * fx.Pop, fx.Alpha, model);
        }

        var timeColor = mine ? new Vector4(1f, 1f, 1f, 0.72f) : Palette.WithAlpha(model.MutedInk, 0.95f);
        DrawStamp(drawList, stamp, new Vector2(bubbleMax.X - paddingX, bubbleMax.Y - paddingY), fx, timeColor);
        if (entrance >= 1f && Hovering(bubbleMin, bubbleMax))
        {
            if (card is not null)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                HoverTooltip.Show(new Rect(bubbleMin, bubbleMax), Loc.T(L.Chirper.OpenChirp), HoverLabelSide.Above);
                if (UiInteract.Click(bubbleMin, bubbleMax, true, false))
                {
                    ChirpChatBridge.Open(postId);
                }
            }

            if (model.Interactions is { } interactions && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                interactions.OnMessageContext(message.Id);
            }
        }

        var chipRow = DrawReactionChips(drawList, message, mine, bubbleMin, bubbleMax, fx.Alpha, model, scale);
        ImGui.SetCursorScreenPos(new Vector2(start.X, start.Y + bubbleHeight + chipRow + BubbleGap * scale));
    }

    private static void DrawChirpPhoto(ImDrawListPtr drawList, ChirpCard card, string photoUrl, Vector2 min,
        Vector2 max, float rounding, float alpha, in ChatTranscriptModel model)
    {
        if (SensitiveReveals.ShouldVeil(card.Sensitive, card.PostId, Plugin.Cfg.ShowSensitiveContent))
        {
            SensitiveVeil.Draw(drawList, min, max, rounding);
            return;
        }

        var texture = ChirpChatBridge.Thumbnail(photoUrl);
        if (texture is null)
        {
            Squircle.Fill(drawList, min, max, rounding, ImGui.GetColorU32(ChatInk.Wash(model.Theme, 0.08f * alpha)));
            AppSkin.Icon(drawList, (min + max) * 0.5f, IconGlyph.Of(FontAwesomeIcon.Image),
                Palette.WithAlpha(model.MutedInk, alpha), 1.2f);
            return;
        }

        var size = max - min;
        var (uv0, uv1) = ImageFit.Cover(texture.Size.X, texture.Size.Y, size.X, size.Y);
        drawList.AddImageRounded(texture.Handle, min, max, uv0, uv1,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha)), rounding, ImDrawFlags.RoundCornersAll);
    }
}

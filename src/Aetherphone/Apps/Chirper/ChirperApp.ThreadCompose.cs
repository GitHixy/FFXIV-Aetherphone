using Aetherphone.Core;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Social;
using Aetherphone.Core.Theme;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Chirper;

internal sealed partial class ChirperApp
{
    private const float ThreadBoxMinHeight = 40f;
    private const float ThreadBoxTextPad = 20f;
    private const float ThreadRemoveRadius = 11f;

    private readonly ChirperThreadDraft threadDraft = new();

    private bool ComposingThread => threadDraft.Count > 0;

    private float OpeningInputHeight(float measuredText, float desired, float available)
    {
        var scale = UiScale.Current;
        var floor = ThreadBoxMinHeight * scale;
        if (ComposingThread)
        {
            return MathF.Max(floor, measuredText + ThreadBoxTextPad * scale);
        }

        var addRow = threadDraft.CanAdd ? ThreadAddRowHeight() : 0f;
        return MathF.Max(floor, MathF.Min(desired, available - addRow));
    }

    private static float ThreadAddRowHeight()
    {
        return (RoundButton.SmallRadius * 2f + Metrics.Space.Md) * UiScale.Current;
    }

    private bool ThreadDraftReady()
    {
        return threadDraft.AllWritten(MaxPostLength);
    }

    private ref string FocusedComposeText()
    {
        var box = threadDraft.FocusedBox;
        if (box <= ChirperThreadDraft.OpeningBox || box > threadDraft.Count)
        {
            return ref draft;
        }

        return ref threadDraft.Text(box - 1);
    }

    private void DrawThreadFollowUps(ImDrawListPtr drawList, Vector2 openingAvatar, float inputX, float inputWidth,
        float openingBottom)
    {
        var scale = UiScale.Current;
        var radius = FeedAvatarRadius * scale;
        var gap = Metrics.Space.Md * scale;
        var lineInset = Metrics.Space.Xxs * scale;
        var lineColor = ImGui.GetColorU32(ThreadLine);
        var lineWidth = ThreadLineWidth * scale;
        var removeRadius = ThreadRemoveRadius * scale;
        var framePadding = ImGui.GetStyle().FramePadding.X;
        var fieldWidth = MathF.Max(1f, inputWidth - removeRadius * 2f - Metrics.Space.Xs * scale);
        var wrapWidth = fieldWidth - framePadding * 2f - Metrics.Space.Xxs * scale;
        var me = store.Me;
        if (composeFieldActive)
        {
            threadDraft.FocusedBox = ChirperThreadDraft.OpeningBox;
        }

        if (threadDraft.FocusRequest == ChirperThreadDraft.OpeningBox)
        {
            threadDraft.FocusRequest = -1;
            composeFocus = true;
        }

        var previousAvatarBottom = openingAvatar.Y + radius;
        var cursorY = MathF.Max(openingBottom, previousAvatarBottom) + gap;
        var removeIndex = -1;
        for (var index = 0; index < threadDraft.Count; index++)
        {
            var boxTop = cursorY;
            var avatarCenter = new Vector2(openingAvatar.X, boxTop + radius);
            drawList.AddLine(new Vector2(openingAvatar.X, previousAvatarBottom + lineInset),
                new Vector2(openingAvatar.X, avatarCenter.Y - radius - lineInset), lineColor, lineWidth);
            if (me is not null)
            {
                DrawAvatar(drawList, avatarCenter, radius, me.Name, me.World, me.AvatarUrl, 0.95f, 48,
                    Frames.Of(me.FrameId));
            }

            ref var text = ref threadDraft.Text(index);
            var measured = text.Length == 0
                ? Typography.Measure("Ag", ComposeInputStyle).Y
                : Typography.MeasureWrapped(text, wrapWidth, ComposeInputStyle.Scale);
            var fieldHeight = MathF.Max(ThreadBoxMinHeight * scale, measured + ThreadBoxTextPad * scale);
            var fieldId = ChirperThreadDraft.FieldId(index);
            var focusNow = threadDraft.FocusRequest == index + 1;
            ImGui.SetCursorScreenPos(new Vector2(inputX, boxTop));
            ImGui.SetNextItemWidth(fieldWidth);
            if (focusNow)
            {
                threadDraft.FocusRequest = -1;
                ImGui.SetKeyboardFocusHere();
                SoftWrapField.CaretToEnd(fieldId);
            }

            using (UiScope.PushColor(ImGuiCol.FrameBg, AppSkin.Transparent))
            using (UiScope.PushColor(ImGuiCol.Text, ChirperInk.TitleInk))
            using (Plugin.Fonts.Push(ComposeInputStyle.Scale))
            {
                SoftWrapField.Paragraphs(fieldId, ref text, MaxPostLength, new Vector2(fieldWidth, fieldHeight),
                    wrapWidth);
                if (ImGui.IsItemActive())
                {
                    threadDraft.FocusedBox = index + 1;
                }
            }

            if (focusNow)
            {
                ImGui.SetScrollHereY(1f);
            }

            if (text.Length == 0)
            {
                Typography.Draw(drawList, new Vector2(inputX + lineInset, boxTop + 2f * scale),
                    Typography.FitText(Loc.T(L.Chirper.AddAnotherChirp), fieldWidth, ComposeInputStyle),
                    ChirperInk.MutedInk, ComposeInputStyle);
            }

            var removeCenter = new Vector2(inputX + inputWidth - removeRadius, boxTop + removeRadius);
            using (UiScope.PushId(index))
            {
                if (RoundButton.Icon(drawList, removeCenter, removeRadius, PhoneIcons.X, ui.Ink, ButtonStyle.Gray,
                        Loc.T(L.Chirper.RemoveThreadChirp)))
                {
                    removeIndex = index;
                }
            }

            previousAvatarBottom = avatarCenter.Y + radius;
            cursorY = MathF.Max(boxTop + fieldHeight, previousAvatarBottom) + gap;
        }

        if (removeIndex >= 0)
        {
            threadDraft.RemoveAt(removeIndex);
        }

        if (threadDraft.CanAdd)
        {
            cursorY = DrawThreadAddRow(drawList, openingAvatar.X, inputX, inputWidth, previousAvatarBottom, cursorY);
        }

        ImGui.SetCursorScreenPos(new Vector2(inputX, cursorY));
        ImGui.Dummy(new Vector2(1f, 1f));
    }

    private float DrawThreadAddRow(ImDrawListPtr drawList, float lineX, float inputX, float inputWidth,
        float previousAvatarBottom, float top)
    {
        var scale = UiScale.Current;
        var plusRadius = RoundButton.SmallRadius * scale;
        var plusCenter = new Vector2(lineX, top + plusRadius);
        var lastText = ComposingThread ? threadDraft.Text(threadDraft.Count - 1) : draft;
        var enabled = composeScheduleAtUnix == 0
            && (!string.IsNullOrWhiteSpace(lastText) || (!ComposingThread && composeAttachments.Count > 0));
        var label = Loc.T(L.Chirper.AddAnotherChirp);
        if (enabled)
        {
            var lineInset = Metrics.Space.Xxs * scale;
            drawList.AddLine(new Vector2(lineX, previousAvatarBottom + lineInset),
                new Vector2(lineX, plusCenter.Y - plusRadius - lineInset), ImGui.GetColorU32(ThreadLine),
                ThreadLineWidth * scale);
        }

        var clicked = RoundButton.Icon(drawList, plusCenter, plusRadius, PhoneIcons.Plus, ui.Ink,
            ButtonStyle.Tinted, label, enabled: enabled);
        var labelText = Typography.FitText(label, inputWidth, MetaStyle);
        var labelSize = Typography.Measure(labelText, MetaStyle);
        var labelMin = new Vector2(inputX + Metrics.Space.Xxs * scale, plusCenter.Y - labelSize.Y * 0.5f);
        var labelMax = labelMin + labelSize;
        var labelHovered = enabled && UiInteract.Hover(labelMin, labelMax);
        Typography.Draw(drawList, labelMin, labelText,
            labelHovered ? ChirperInk.AccentLink : Palette.WithAlpha(ChirperInk.MutedInk, enabled ? 1f : 0.5f),
            MetaStyle);
        if (labelHovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if ((clicked || UiInteract.Click(labelMin, labelMax, labelHovered)) && enabled)
        {
            threadDraft.Add();
        }

        return top + ThreadAddRowHeight();
    }

    private void SubmitThread()
    {
        if (store.Posting || !ThreadDraftReady())
        {
            return;
        }

        composeStatus = string.Empty;
        composeFailure.Clear();
        if (ReportComposePollIssue())
        {
            return;
        }

        var gif = AttachedComposeGif();
        var imagePaths = gif is null ? composeAttachments.ToArray() : Array.Empty<string>();
        var opening = new ThreadOpening(draft, quoteTargetId, gif?.Item, composePoll.Active ? composePoll.ToInput() : null,
            composeSensitive, composeReplyPolicy, ComposeAltTextInputs(imagePaths));
        store.ComposeThread(opening, imagePaths, threadDraft.Snapshot(), ok =>
        {
            if (ok && gif is not null)
            {
                gifSearch.RegisterShare(gif);
            }

            composeOutcome = ok ? 1 : 2;
        }, composeFailure.Set);
    }
}

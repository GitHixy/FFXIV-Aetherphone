using System.Globalization;
using Aetherphone.Core;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Reels;
using Aetherphone.Core.Theme;
using Aetherphone.Windows.Components;
using Aetherphone.Windows.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace Aetherphone.Apps.Aethergram;

internal sealed partial class AethergramApp
{
    private const float ReelPillMinHeight = 56f;
    private const float ReelPillPad = 12f;
    private const float ReelPillIcon = 20f;
    private const float ReelPillGap = 10f;
    private const float ReelPillBar = 3f;
    private const float ReelPillButton = 16f;
    private const float ReelPillMargin = 12f;
    private const float ReelPillSpinner = 9f;
    private const int PercentKeyShift = 8;

    private CachedText reelPillStatus;
    private ReelShareState? reelPillState;
    private CultureInfo? reelPillCulture;
    private string reelPillFailure = string.Empty;

    private void DrainReelShare()
    {
        if (reelFrames.IsOpen && !router.IsTransitioning && router.Current.Screen != AethergramScreen.Compose)
        {
            ResetReelCompose();
        }

        if (store.ReelShare is not { } job)
        {
            return;
        }

        var state = job.State;
        if (state.Stage == ReelShareStage.Done)
        {
            store.AcceptCreatedReel(state.Created);
            store.DismissReelShare();
            toast.Show(Loc.T(L.Aethergram.ReelShared));
            return;
        }

        if (state.Stage == ReelShareStage.Cancelled)
        {
            store.DismissReelShare();
        }
    }

    private void DrawReelSharePill(Rect screen)
    {
        if (store.ReelShare is not { } job || router.Current.Screen == AethergramScreen.Compose)
        {
            return;
        }

        var state = job.State;
        var failed = state.Stage == ReelShareStage.Failed;
        if (!failed && !state.Busy)
        {
            return;
        }

        var scale = UiScale.Current;
        var drawList = ImGui.GetWindowDrawList();
        var pad = ReelPillPad * scale;
        var gap = ReelPillGap * scale;
        var icon = ReelPillIcon * scale;
        var button = ReelPillButton * scale;
        var margin = ReelPillMargin * scale;
        var bottom = router.Depth == 1 ? TabBar.ContentArea(screen, scale).Max.Y - margin : screen.Max.Y - margin;
        var retryLabel = Loc.T(L.Common.Retry);
        var retryWidth = failed && state.Failure.Retryable
            ? Typography.Measure(retryLabel, TextStyles.SubheadlineEmphasized).X + gap
            : 0f;
        var showsClose = failed || state.Cancellable;
        var trailing = retryWidth + (showsClose ? button + gap : 0f) + (state.Stage == ReelShareStage.Posting
            ? ReelPillSpinner * 2f * scale + gap
            : 0f);
        var left = screen.Min.X + margin;
        var right = screen.Max.X - margin;
        var textLeft = left + pad + icon + gap;
        var textWidth = MathF.Max(1f, right - pad - trailing - textLeft);
        var text = failed ? PillFailureText(state) : PillStatusText(state.Stage, job.Progress);
        var textHeight = Typography.MeasureWrappedBlock(text, TextStyles.Subheadline, textWidth).Y;
        var height = MathF.Max(ReelPillMinHeight * scale, textHeight + pad * 2f);
        var pill = new Rect(new Vector2(left, bottom - height), new Vector2(right, bottom));
        var overPill = UiInteract.HoverOverlay(pill);
        var style = ScreenToastStyle.From(ui);
        var rounding = Metrics.Radius.Grouped * scale;
        Squircle.Fill(drawList, pill.Min, pill.Max, rounding, ImGui.GetColorU32(style.Panel));
        drawList.AddRect(pill.Min, pill.Max, ImGui.GetColorU32(style.Stroke), rounding, ImDrawFlags.RoundCornersAll,
            1f);
        ProgressRing.CenterIcon(drawList, new Vector2(left + pad + icon * 0.5f, pill.Center.Y),
            failed ? FontAwesomeIcon.ExclamationTriangle : FontAwesomeIcon.Film, failed ? Ink.Danger : Accent, icon);
        Typography.DrawWrappedLeft(new Vector2(textLeft, pill.Center.Y - textHeight * 0.5f), text, style.Ink,
            TextStyles.Subheadline, textWidth);
        if (state.Stage is ReelShareStage.Converting or ReelShareStage.Uploading)
        {
            var barTop = pill.Max.Y - ReelPillBar * scale - pad * 0.5f;
            var barMin = new Vector2(textLeft, barTop);
            var barMax = new Vector2(textLeft + textWidth, barTop + ReelPillBar * scale);
            drawList.AddRectFilled(barMin, barMax, ImGui.GetColorU32(Palette.WithAlpha(style.Ink, 0.15f)),
                ReelPillBar * scale);
            drawList.AddRectFilled(barMin, new Vector2(barMin.X + textWidth * job.Progress, barMax.Y),
                ImGui.GetColorU32(Accent), ReelPillBar * scale);
        }

        var cursor = right - pad;
        if (showsClose)
        {
            var center = new Vector2(cursor - button * 0.5f, pill.Center.Y);
            if (PillButton(drawList, center, button, style.Ink, overPill))
            {
                if (failed)
                {
                    store.DismissReelShare();
                }
                else
                {
                    job.Cancel();
                }
            }

            cursor -= button + gap;
        }

        if (state.Stage == ReelShareStage.Posting)
        {
            LoadingPulse.Spinner(new Vector2(cursor - ReelPillSpinner * scale, pill.Center.Y), ReelPillSpinner * scale,
                Accent);
        }

        if (retryWidth <= 0f)
        {
            return;
        }

        var retrySize = Typography.Measure(retryLabel, TextStyles.SubheadlineEmphasized);
        var retryMin = new Vector2(cursor - retrySize.X, pill.Center.Y - retrySize.Y * 0.5f);
        var retryMax = retryMin + retrySize;
        var retryHovered = overPill && UiInteract.HoverWindowOnly(retryMin, retryMax);
        Typography.Draw(drawList, retryMin, retryLabel, retryHovered ? Ink.TitleInk : Ink.AccentLink,
            TextStyles.SubheadlineEmphasized);
        if (retryHovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if (UiInteract.Click(retryMin, retryMax, retryHovered))
        {
            job.Retry();
        }
    }

    private static bool PillButton(ImDrawListPtr drawList, Vector2 center, float size, Vector4 ink, bool overPill)
    {
        var half = new Vector2(size, size) * 0.5f;
        var hovered = overPill && UiInteract.HoverWindowOnly(center - half, center + half);
        if (hovered)
        {
            drawList.AddCircleFilled(center, size * 0.75f, ImGui.GetColorU32(Palette.WithAlpha(ink, 0.12f)));
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        PhoneIcon.Draw(drawList, center, PhoneIcons.X, ink, size);
        return UiInteract.Click(center - half, center + half, hovered);
    }

    private string PillStatusText(ReelShareStage stage, float progress)
    {
        var percent = (int)MathF.Round(Math.Clamp(progress, 0f, 1f) * 100f);
        var key = ((long)stage << PercentKeyShift) | (uint)percent;
        return reelPillStatus.IsCurrent(key)
            ? reelPillStatus.Value
            : reelPillStatus.Store(key, ReelShareText.Status(stage, progress));
    }

    private string PillFailureText(ReelShareState state)
    {
        if (ReferenceEquals(state, reelPillState) && ReferenceEquals(reelPillCulture, Loc.Culture))
        {
            return reelPillFailure;
        }

        reelPillState = state;
        reelPillCulture = Loc.Culture;
        reelPillFailure = ReelShareText.Failure(state.Failure);
        return reelPillFailure;
    }
}

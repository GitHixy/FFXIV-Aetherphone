using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Onboarding;
using Aetherphone.Windows.Components;
using Aetherphone.Windows.Widgets;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Chirper;

internal sealed partial class ChirperApp
{
    private const float ComposeReplyRowHeight = 40f;
    private const float ComposeReplyIconSize = 16f;
    private const float ComposeReplyIconGap = 6f;

    private static readonly TextStyle ComposeReplyStyle = TextStyles.SubheadlineEmphasized;
    private static readonly TextStyle ReplyLimitStyle = TextStyles.Subheadline;

    private readonly ActionSheet replyPolicySheet = new();
    private readonly ActionSheet.Item[] replyPolicyItems = new ActionSheet.Item[ChirperReplyPolicy.Count];
    private int composeReplyPolicy = ChirperReplyPolicy.Everyone;
    private string? replyPolicyPostId;
    private int replyPolicyChoice;
    private CachedText replyLimitLabel;

    private void DrawComposeReplyControl(ImDrawListPtr drawList, float left, float right, float top)
    {
        var scale = UiScale.Current;
        var centerY = top + ComposeReplyRowHeight * scale * 0.5f;
        var iconSize = ComposeReplyIconSize * scale;
        var textLeft = left + iconSize + ComposeReplyIconGap * scale;
        var label = Typography.FitText(Loc.T(ChirperReplyPolicy.Summary(composeReplyPolicy)),
            MathF.Max(1f, right - textLeft), ComposeReplyStyle);
        var labelSize = Typography.Measure(label, ComposeReplyStyle);
        var hit = new Rect(new Vector2(left, top), new Vector2(textLeft + labelSize.X, top + ComposeReplyRowHeight * scale));
        UiAnchors.Report("chirper.compose.replyPolicy", hit);
        var hovered = UiInteract.Hover(hit.Min, hit.Max);
        var ink = hovered ? ChirperInk.TitleInk : ChirperInk.AccentLink;
        PhoneIcon.Draw(drawList, new Vector2(left + iconSize * 0.5f, centerY), ChirperReplyPolicy.Glyph(composeReplyPolicy),
            ink, iconSize);
        Typography.Draw(drawList, new Vector2(textLeft, centerY - labelSize.Y * 0.5f), label, ink, ComposeReplyStyle);
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if (UiInteract.Click(hit.Min, hit.Max, hovered))
        {
            OpenReplyPolicyPicker(null, composeReplyPolicy);
        }
    }

    private void OpenReplyPolicyPicker(string? postId, int current)
    {
        actions.Reset();
        replyPolicyPostId = postId;
        replyPolicyChoice = current;
        replyPolicySheet.Open();
    }

    private void DrawReplyPolicySheet(Rect screen)
    {
        if (!replyPolicySheet.CapturesPointer)
        {
            return;
        }

        for (var policy = 0; policy < ChirperReplyPolicy.Count; policy++)
        {
            replyPolicyItems[policy] = new ActionSheet.Item(Loc.T(ChirperReplyPolicy.Option(policy)),
                ChirperReplyPolicy.Glyph(policy), false, policy == replyPolicyChoice, true);
        }

        var picked = replyPolicySheet.Draw(screen, SheetStyle, replyPolicyItems, Loc.T(L.Common.Cancel), false,
            Loc.T(L.Chirper.WhoCanReply));
        if (picked < 0)
        {
            return;
        }

        if (replyPolicyPostId is not { } postId)
        {
            composeReplyPolicy = picked;
            return;
        }

        if (picked == replyPolicyChoice)
        {
            return;
        }

        store.SetReplyPolicy(postId, picked,
            changed => toast.Show(Loc.T(changed ? L.Chirper.ReplyPolicyUpdated : L.Chirper.ReplyPolicyFailed)));
    }

    private void DrawReplyLimit(Rect bar, PostDto post)
    {
        replyFocusPending = false;
        var drawList = ImGui.GetWindowDrawList();
        PaintBarBackdrop(drawList, bar);
        drawList.AddLine(bar.Min, new Vector2(bar.Max.X, bar.Min.Y), ImGui.GetColorU32(ChirperInk.Hairline), 1f);
        UiAnchors.Report("chirper.thread.replyLimit", bar);
        Typography.DrawWrappedCentered(drawList, bar.Center, ReplyLimitText(post), ChirperInk.MutedInk, ReplyLimitStyle,
            MathF.Max(1f, bar.Width - CellPadX * 2f * UiScale.Current));
    }

    private string ReplyLimitText(PostDto post)
    {
        var key = HashCode.Combine(post.AuthorHandle, post.AuthorDisplayName, post.ReplyPolicy);
        if (replyLimitLabel.IsCurrent(key))
        {
            return replyLimitLabel.Value;
        }

        var name = post.AuthorHandle.Length > 0 ? "@" + post.AuthorHandle : post.AuthorDisplayName;
        return replyLimitLabel.Store(key, Loc.T(ChirperReplyPolicy.Limit(post.ReplyPolicy), name));
    }

    private void OpenThreadForReply(PostDto post)
    {
        OpenThread(post);
        replyFocusPending = post.CanReply;
    }
}

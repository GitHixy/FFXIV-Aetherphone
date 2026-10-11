using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Casino;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Theme;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace Aetherphone.Apps.Casino.Tables;

internal enum LedgerEntryPress : byte
{
    None,
    Confirm,
    Dispute,
}

internal readonly record struct LedgerEntryAction(LedgerEntryPress Press, string EntryId, string DisputeLabel)
{
    public static readonly LedgerEntryAction None = new(LedgerEntryPress.None, string.Empty, string.Empty);
}

internal sealed class LedgerEntryCards
{
    private CasinoLedgerEntryDto[]? source;
    private LanguageInfo? language;
    private string me = string.Empty;
    private LedgerEntryView[] views = Array.Empty<LedgerEntryView>();

    public int Count => views.Length;

    public void Refresh(CasinoLedgerEntryDto[] entries, string myUserId, int currency)
    {
        if (ReferenceEquals(entries, source) && ReferenceEquals(language, Loc.Current)
            && string.Equals(myUserId, me, StringComparison.Ordinal))
        {
            return;
        }

        source = entries;
        language = Loc.Current;
        me = myUserId;
        views = new LedgerEntryView[entries.Length];
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var side = CasinoGilLedger.SideOf(entry, myUserId);
            var confirmLabel = side == LedgerSide.Payer ? Loc.T(L.Tables.IPaid) : Loc.T(L.Tables.IReceived);
            var disputeLabel = entry.Kind is CasinoLedgerKinds.Payout or CasinoLedgerKinds.CashOut
                or CasinoLedgerKinds.Prize or CasinoLedgerKinds.Stake
                ? Loc.T(L.Tables.PayoutNotReceived)
                : Loc.T(L.Tables.GilNotReceived);
            views[index] = new LedgerEntryView(entry.EntryId, Loc.T(TableLedger.KindLabel(entry.Kind)),
                Loc.T(L.Tables.EntryParties, entry.PayerName, entry.PayeeName),
                TableLedger.AmountText(entry.Amount, currency), TableLedger.StateText(entry),
                TableLedger.ToneOf(entry), CasinoGilLedger.CanConfirm(entry, myUserId), confirmLabel,
                CasinoGilLedger.CanDispute(entry, myUserId), disputeLabel);
        }
    }

    public LedgerEntryAction Draw(AppSkin ui, bool enabled, float scale)
    {
        var width = ScrollLayout.StableContentWidth();
        var drawList = ImGui.GetWindowDrawList();
        var pad = LedgerEntryLayout.Pad * scale;
        var action = LedgerEntryAction.None;
        for (var index = 0; index < views.Length; index++)
        {
            using var entryId = ImRaii.PushId(index);
            var view = views[index];
            var origin = ImGui.GetCursorScreenPos();
            var titleLine = Typography.LineHeight(TextStyles.SubheadlineEmphasized);
            var partiesLine = Typography.LineHeight(TextStyles.Footnote);
            var stateLine = Typography.LineHeight(TextStyles.FootnoteEmphasized);
            var confirmWidth = view.CanConfirm ? Button.WidthFor(view.ConfirmLabel, ButtonSize.Large) : 0f;
            var disputeWidth = view.CanDispute ? Button.WidthFor(view.DisputeLabel, ButtonSize.Large) : 0f;
            var buttonRows = LedgerEntryLayout.ButtonRows(width - pad * 2f, confirmWidth, disputeWidth, scale);
            var height = LedgerEntryLayout.Height(titleLine, partiesLine, stateLine, buttonRows, scale);
            var max = new Vector2(origin.X + width, origin.Y + height);
            var card = new Rect(origin, max);
            var tone = view.Tone switch
            {
                LedgerTone.Waiting => TableLedger.Amber,
                LedgerTone.Disputed => ui.Accent,
                _ => TableLedger.SettledInk,
            };
            ui.Card(drawList, origin, max, Metrics.Radius.Grouped * scale);
            if (view.Tone != LedgerTone.Settled)
            {
                Squircle.Stroke(drawList, origin, max, Metrics.Radius.Grouped * scale,
                    ImGui.GetColorU32(Palette.WithAlpha(tone, 0.65f)), 1.2f * scale);
            }

            var top = origin.Y + pad;
            var amountSize = Typography.Measure(view.Amount, TextStyles.SubheadlineEmphasized);
            Typography.Draw(drawList, new Vector2(max.X - pad - amountSize.X, top), view.Amount,
                TableRow.CurrencyTint(CasinoCurrencies.Gil, ui.Accent), TextStyles.SubheadlineEmphasized);
            var textWidth = MathF.Max(1f, width - pad * 3f - amountSize.X);
            Typography.Draw(drawList, new Vector2(origin.X + pad, top),
                Typography.FitText(view.Title, textWidth, TextStyles.SubheadlineEmphasized), ui.TitleInk,
                TextStyles.SubheadlineEmphasized);
            Typography.Draw(drawList, new Vector2(origin.X + pad, top + titleLine + LedgerEntryLayout.LineGap * scale),
                Typography.FitText(view.Parties, width - pad * 2f, TextStyles.Footnote), ui.BodyInk,
                TextStyles.Footnote);
            Typography.Draw(drawList,
                new Vector2(origin.X + pad, LedgerEntryLayout.StateTop(origin.Y, titleLine, partiesLine, scale)),
                Typography.FitText(view.State, width - pad * 2f, TextStyles.FootnoteEmphasized), tone,
                TextStyles.FootnoteEmphasized);
            var press = DrawButtons(drawList, ui, view, card, buttonRows, confirmWidth, disputeWidth, enabled, scale);
            if (press != LedgerEntryPress.None)
            {
                action = new LedgerEntryAction(press, view.EntryId, view.DisputeLabel);
            }

            ImGui.SetCursorScreenPos(origin);
            ImGui.Dummy(new Vector2(width, height + Metrics.Space.Sm * scale));
        }

        return action;
    }

    private static LedgerEntryPress DrawButtons(ImDrawListPtr drawList, AppSkin ui, in LedgerEntryView view,
        in Rect card, int buttonRows, float confirmWidth, float disputeWidth, bool enabled, float scale)
    {
        if (buttonRows == 0)
        {
            return LedgerEntryPress.None;
        }

        var press = LedgerEntryPress.None;
        var stacked = buttonRows > 1;
        var right = card.Max.X - LedgerEntryLayout.Pad * scale;
        if (view.CanDispute)
        {
            var rect = stacked
                ? LedgerEntryLayout.StackedButton(card, 1, buttonRows, scale)
                : LedgerEntryLayout.RowButton(card, right, disputeWidth, scale);
            if (Button.Draw(drawList, rect, view.DisputeLabel, ui.Ink, ButtonStyle.Gray, ButtonRole.Destructive,
                    enabled, id: "ledger.dispute"))
            {
                press = LedgerEntryPress.Dispute;
            }

            right = rect.Min.X - LedgerEntryLayout.ButtonGap * scale;
        }

        if (!view.CanConfirm)
        {
            return press;
        }

        var confirmRect = stacked
            ? LedgerEntryLayout.StackedButton(card, 0, buttonRows, scale)
            : LedgerEntryLayout.RowButton(card, right, confirmWidth, scale);
        if (Button.Draw(drawList, confirmRect, view.ConfirmLabel, ui.Ink, ButtonStyle.Prominent, enabled: enabled,
                id: "ledger.confirm"))
        {
            press = LedgerEntryPress.Confirm;
        }

        return press;
    }
}

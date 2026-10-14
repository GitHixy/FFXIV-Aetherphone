using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Casino;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Theme;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace Aetherphone.Apps.Casino.Tables;

internal readonly record struct LedgerEntryView(string EntryId, string Title, string Parties, string Amount,
    string State, bool Owed, LedgerEntryOption Option, string OptionLabel, string Tag = "");

internal readonly record struct LedgerEntryAction(LedgerEntryOption Option, string EntryId)
{
    public static readonly LedgerEntryAction None = new(LedgerEntryOption.None, string.Empty);
}

internal sealed class LedgerEntryCards
{
    private const float TagPadX = 8f;
    private const float TagPadY = 2f;
    private const float TagGap = 6f;
    private const float TagFillAlpha = 0.18f;
    private const float OwedRimAlpha = 0.65f;
    private const float OwedRimWidth = 1.2f;

    private CasinoLedgerEntryDto[]? source;
    private LanguageInfo? language;
    private bool host;
    private LedgerEntryView[] views = Array.Empty<LedgerEntryView>();

    public int Count => views.Length;

    public void Refresh(CasinoLedgerEntryDto[] entries, bool hosting, int currency)
    {
        if (ReferenceEquals(entries, source) && ReferenceEquals(language, Loc.Current) && hosting == host)
        {
            return;
        }

        source = entries;
        language = Loc.Current;
        host = hosting;
        views = new LedgerEntryView[entries.Length];
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var option = CasinoGilLedger.OptionFor(entry, hosting);
            views[index] = new LedgerEntryView(entry.EntryId, Loc.T(CasinoGilLedger.KindLabel(entry.Kind)),
                Loc.T(L.Tables.EntryParties, entry.PayerName, entry.PayeeName),
                TableLedger.AmountText(entry.Amount, currency), TableLedger.StateText(entry), !entry.Settled, option,
                OptionLabel(option), entry.Guest ? Loc.T(L.Book.GuestTag) : string.Empty);
        }
    }

    internal static string OptionLabel(LedgerEntryOption option) => option switch
    {
        LedgerEntryOption.MarkPaid => Loc.T(L.Tables.MarkPaid),
        LedgerEntryOption.MarkUnpaid => Loc.T(L.Tables.MarkUnpaid),
        LedgerEntryOption.Remove => Loc.T(L.Tables.RemoveEntry),
        _ => string.Empty,
    };

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
            var hasButton = view.Option != LedgerEntryOption.None;
            var height = LedgerEntryLayout.Height(titleLine, partiesLine, stateLine, hasButton, scale);
            var max = new Vector2(origin.X + width, origin.Y + height);
            var card = new Rect(origin, max);
            var tone = view.Owed ? TableLedger.Amber : TableLedger.SettledInk;
            ui.Card(drawList, origin, max, Metrics.Radius.Grouped * scale);
            if (view.Owed)
            {
                Squircle.Stroke(drawList, origin, max, Metrics.Radius.Grouped * scale,
                    ImGui.GetColorU32(Palette.WithAlpha(tone, OwedRimAlpha)), OwedRimWidth * scale);
            }

            var top = origin.Y + pad;
            var amountSize = Typography.Measure(view.Amount, TextStyles.SubheadlineEmphasized);
            Typography.Draw(drawList, new Vector2(max.X - pad - amountSize.X, top), view.Amount,
                TableRow.CurrencyTint(CasinoCurrencies.Gil, ui.Accent), TextStyles.SubheadlineEmphasized);
            var textWidth = MathF.Max(1f, width - pad * 3f - amountSize.X);
            var tagWidth = view.Tag.Length > 0
                ? Typography.Measure(view.Tag, TextStyles.FootnoteEmphasized).X + TagPadX * 2f * scale
                : 0f;
            var titleWidth = tagWidth > 0f ? MathF.Max(1f, textWidth - tagWidth - TagGap * scale) : textWidth;
            var title = Typography.FitText(view.Title, titleWidth, TextStyles.SubheadlineEmphasized);
            Typography.Draw(drawList, new Vector2(origin.X + pad, top), title, ui.TitleInk,
                TextStyles.SubheadlineEmphasized);
            if (tagWidth > 0f)
            {
                var tagLeft = origin.X + pad + Typography.Measure(title, TextStyles.SubheadlineEmphasized).X
                    + TagGap * scale;
                DrawTag(drawList, ui, view.Tag, new Vector2(tagLeft, top + titleLine * 0.5f), tagWidth, scale);
            }

            Typography.Draw(drawList, new Vector2(origin.X + pad, top + titleLine + LedgerEntryLayout.LineGap * scale),
                Typography.FitText(view.Parties, width - pad * 2f, TextStyles.Footnote), ui.BodyInk,
                TextStyles.Footnote);
            Typography.Draw(drawList,
                new Vector2(origin.X + pad, LedgerEntryLayout.StateTop(origin.Y, titleLine, partiesLine, scale)),
                Typography.FitText(view.State, width - pad * 2f, TextStyles.FootnoteEmphasized), tone,
                TextStyles.FootnoteEmphasized);
            if (hasButton && DrawButton(drawList, ui, view, card, enabled, scale))
            {
                action = new LedgerEntryAction(view.Option, view.EntryId);
            }

            ImGui.SetCursorScreenPos(origin);
            ImGui.Dummy(new Vector2(width, height + Metrics.Space.Sm * scale));
        }

        return action;
    }

    private static void DrawTag(ImDrawListPtr drawList, AppSkin ui, string tag, Vector2 leftCenter, float width,
        float scale)
    {
        var height = Typography.LineHeight(TextStyles.FootnoteEmphasized) + TagPadY * 2f * scale;
        var min = new Vector2(leftCenter.X, leftCenter.Y - height * 0.5f);
        var max = new Vector2(leftCenter.X + width, leftCenter.Y + height * 0.5f);
        Squircle.Fill(drawList, min, max, height * 0.5f,
            ImGui.GetColorU32(Palette.WithAlpha(ui.MutedInk, TagFillAlpha)));
        Typography.DrawCentered(drawList, (min + max) * 0.5f, tag, ui.BodyInk, TextStyles.FootnoteEmphasized);
    }

    private static bool DrawButton(ImDrawListPtr drawList, AppSkin ui, in LedgerEntryView view, in Rect card,
        bool enabled, float scale)
    {
        var rect = LedgerEntryLayout.Button(card, Button.WidthFor(view.OptionLabel, ButtonSize.Large), scale);
        return view.Option switch
        {
            LedgerEntryOption.MarkPaid => Button.Draw(drawList, rect, view.OptionLabel, ui.Ink, ButtonStyle.Tinted,
                enabled: enabled, id: "ledger.markPaid"),
            LedgerEntryOption.MarkUnpaid => Button.Draw(drawList, rect, view.OptionLabel, ui.Ink, ButtonStyle.Gray,
                enabled: enabled, id: "ledger.markUnpaid"),
            _ => Button.Draw(drawList, rect, view.OptionLabel, ui.Ink, ButtonStyle.Gray, ButtonRole.Destructive,
                enabled, id: "ledger.remove"),
        };
    }
}

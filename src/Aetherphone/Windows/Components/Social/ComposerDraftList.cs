using Aetherphone.Core;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Social;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Windows.Components;

internal readonly record struct DraftListRow(string Preview, string Meta, bool Failed);

internal static class ComposerDraftList
{
    private const float RowHeight = 64f;
    private const float LineGap = 3f;
    private const float DeleteReserve = 48f;

    private static readonly TextStyle PreviewStyle = TextStyles.Body;
    private static readonly TextStyle MetaStyle = TextStyles.Footnote;

    public static int Draw(Rect body, AppSkin ui, SocialInk ink, ReadOnlySpan<DraftListRow> rows,
        string deleteTooltip, bool tappable, out int deleted)
    {
        deleted = -1;
        var tapped = -1;
        var scale = UiScale.Current;
        using (AppSurface.BeginEdgeToEdge(body))
        {
            var drawList = ImGui.GetWindowDrawList();
            var padX = FeedCell.PadX * scale;
            var radius = RoundButton.SmallRadius * scale;
            for (var index = 0; index < rows.Length; index++)
            {
                using var id = UiScope.PushId(index);
                var row = rows[index];
                var cell = FeedCell.Begin(drawList, RowHeight * scale, ink.HoverTint, tappable);
                var bounds = cell.Bounds;
                var textLeft = bounds.Min.X + padX;
                var textWidth = MathF.Max(1f, bounds.Max.X - padX - DeleteReserve * scale - textLeft);
                var blockHeight = Typography.LineHeight(PreviewStyle) + LineGap * scale + Typography.LineHeight(MetaStyle);
                var top = bounds.Center.Y - blockHeight * 0.5f;
                Typography.Draw(drawList, new Vector2(textLeft, top), Typography.FitText(row.Preview, textWidth, PreviewStyle),
                    ink.TitleInk, PreviewStyle);
                Typography.Draw(drawList,
                    new Vector2(textLeft, top + Typography.LineHeight(PreviewStyle) + LineGap * scale),
                    Typography.FitText(row.Meta, textWidth, MetaStyle), row.Failed ? ui.Theme.Danger : ink.MutedInk,
                    MetaStyle);
                var deleteCenter = new Vector2(bounds.Max.X - padX - radius, bounds.Center.Y);
                if (RoundButton.Icon(drawList, deleteCenter, radius, PhoneIcons.Trash, ui.Ink, ButtonStyle.Gray,
                        deleteTooltip))
                {
                    deleted = index;
                }
                else if (cell.Tapped)
                {
                    tapped = index;
                }

                FeedCell.End(drawList, cell, ink.Hairline);
            }
        }

        return tapped;
    }
}

internal sealed class DraftRowCache
{
    private const long RefreshSeconds = 30;

    private DraftListRow[] rows = Array.Empty<DraftListRow>();
    private List<ComposerDraft>? builtFrom;
    private int builtVersion = -1;
    private int builtFormat = -1;
    private LanguageInfo? builtLanguage;
    private long builtAt;

    public ReadOnlySpan<DraftListRow> Rows(List<ComposerDraft>? drafts)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (ReferenceEquals(drafts, builtFrom) && builtVersion == ComposerDrafts.Version
            && builtFormat == TimeText.FormatVersion && ReferenceEquals(builtLanguage, Loc.Current)
            && now - builtAt < RefreshSeconds)
        {
            return rows;
        }

        builtFrom = drafts;
        builtVersion = ComposerDrafts.Version;
        builtFormat = TimeText.FormatVersion;
        builtLanguage = Loc.Current;
        builtAt = now;
        var count = drafts?.Count ?? 0;
        if (rows.Length != count)
        {
            rows = new DraftListRow[count];
        }

        for (var index = 0; index < count; index++)
        {
            var draft = drafts![index];
            rows[index] = new DraftListRow(ComposerDrafts.Preview(draft), ComposerDrafts.Meta(draft), false);
        }

        return rows;
    }
}

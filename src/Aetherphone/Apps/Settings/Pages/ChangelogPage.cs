using Aetherphone.Core;
using Aetherphone.Core.Apps;
using Aetherphone.Core.Changelog;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Theme;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace Aetherphone.Apps.Settings.Pages;

internal sealed class ChangelogPage : ISettingsPage
{
    public static readonly Vector4 PageTint = new(0.62f, 0.42f, 0.90f, 1f);
    private const int RecentMinorLines = 2;
    private const string AllChangesRowId = "changelog.allChanges";
    private const string OlderRowId = "changelog.older";
    private const float SpotlightIcon = 38f;
    private const float PillGap = 8f;
    private const float PillPadX = 7f;
    private const float PillPadY = 3f;
    private const float PillAlpha = 0.18f;
    private readonly Configuration configuration;
    private readonly ISettingsNavigator navigator;
    private readonly ChangelogRows rows = new();
    private readonly ReleaseNotesPage releasePage;
    private readonly ChangelogArchivePage archivePage;
    private readonly int archiveStart;
    private LanguageInfo? labelsLanguage;
    private string latestLabel = string.Empty;
    private string allChangesLabel = string.Empty;
    private string olderLabel = string.Empty;

    public ChangelogPage(Configuration configuration, ISettingsNavigator navigator)
    {
        this.configuration = configuration;
        this.navigator = navigator;
        releasePage = new ReleaseNotesPage(rows);
        archiveStart = ArchiveStart();
        archivePage = new ChangelogArchivePage(rows, releasePage, navigator, archiveStart);
    }

    public string Title => Loc.T(L.Settings.Changelog);
    public string Summary => string.Empty;
    public FontAwesomeIcon Icon => FontAwesomeIcon.Gift;
    public Vector4 Tint => PageTint;
    public bool ShowsBadge => configuration.HasUnseenChangelog;

    public void Draw(in PhoneContext context, Rect body)
    {
        var scale = UiScale.Current;
        var theme = context.Theme;
        EnsureLabels();
        using (AppSurface.Begin(body))
        {
            ImGui.Dummy(new Vector2(0f, Metrics.Space.Md * scale));
            DrawLatest(context, theme, scale);
            DrawRecent(theme, scale);
            DrawArchiveLink(theme, scale);
            ImGui.Dummy(new Vector2(0f, Metrics.Space.Xl * scale));
        }
    }

    private void DrawLatest(in PhoneContext context, PhoneTheme theme, float scale)
    {
        var entry = ChangelogData.Entries[0];
        var innerWidth = ScrollLayout.StableContentWidth() - Metrics.Space.Lg * 2f * scale;
        var blockHeight = MeasureLatestBlock(entry, innerWidth, scale);
        var card = GroupCard.Begin(theme, blockHeight / scale + GroupCard.DefaultRowHeight);
        DrawLatestBlock(context, entry, card.NextRow(blockHeight / scale), theme, scale);
        if (SettingsRow.Disclosure(card.NextRow(GroupCard.DefaultRowHeight), allChangesLabel, string.Empty, theme,
                AllChangesRowId))
        {
            OpenRelease(0);
        }

        card.End();
    }

    private static float MeasureLatestBlock(in ChangelogEntry entry, float width, float scale)
    {
        var height = Metrics.Space.Md * scale * 2f + Typography.LineHeight(TextStyles.Title3) +
                     Metrics.Space.Xxs * scale + Typography.LineHeight(TextStyles.Footnote);
        var spotlight = entry.Spotlight;
        for (var index = 0; index < spotlight.Count; index++)
        {
            height += Metrics.Space.Md * scale + SpotlightRowHeight(spotlight[index], width, scale);
        }

        return height;
    }

    private void DrawLatestBlock(in PhoneContext context, in ChangelogEntry entry, Rect block, PhoneTheme theme,
        float scale)
    {
        var drawList = ImGui.GetWindowDrawList();
        var y = block.Min.Y + Metrics.Space.Md * scale;
        var version = rows.VersionLabel(0);
        var versionSize = Typography.Measure(version, TextStyles.Title3);
        Typography.Draw(drawList, new Vector2(block.Min.X, y), version, theme.TextStrong, TextStyles.Title3);
        DrawLatestPill(drawList, theme, scale, block.Min.X + versionSize.X + PillGap * scale, y, versionSize.Y);
        y += Typography.LineHeight(TextStyles.Title3) + Metrics.Space.Xxs * scale;
        Typography.Draw(drawList, new Vector2(block.Min.X, y), rows.DateLabel(0), theme.TextMuted,
            TextStyles.Footnote);
        y += Typography.LineHeight(TextStyles.Footnote);
        var spotlight = entry.Spotlight;
        for (var index = 0; index < spotlight.Count; index++)
        {
            y += Metrics.Space.Md * scale;
            y += DrawSpotlightRow(context, spotlight[index], block.Min.X, y, block.Width, theme, scale);
        }
    }

    private static float SpotlightRowHeight(in ChangelogSpotlight item, float width, float scale)
    {
        var textWidth = SpotlightTextWidth(width, scale);
        var trim = ImGui.GetStyle().ItemSpacing.Y;
        var text = Typography.MeasureWrappedBlock(Loc.T(item.Title), TextStyles.Headline, textWidth).Y - trim +
                   Metrics.Space.Xxs * scale +
                   Typography.MeasureWrappedBlock(Loc.T(item.Body), TextStyles.Subheadline, textWidth).Y - trim;
        return MathF.Max(SpotlightIcon * scale, text);
    }

    private static float SpotlightTextWidth(float width, float scale) =>
        MathF.Max(1f, width - (SpotlightIcon + Metrics.Space.Md) * scale - SettingsRow.ChevronReserve(scale));

    private static float DrawSpotlightRow(in PhoneContext context, in ChangelogSpotlight item, float left, float top,
        float width, PhoneTheme theme, float scale)
    {
        var drawList = ImGui.GetWindowDrawList();
        var height = SpotlightRowHeight(item, width, scale);
        var row = new Rect(new Vector2(left, top), new Vector2(left + width, top + height));
        var opens = context.Navigation.CanOpen(item.AppId);
        var hovered = opens && UiInteract.Hover(row.Min, row.Max);
        if (hovered)
        {
            SettingsRow.DrawRowHighlight(row, theme);
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        var icon = SpotlightIcon * scale;
        IconTile.DrawApp(drawList, item.AppId, new Vector2(left + icon * 0.5f, row.Center.Y), icon,
            IconTile.Surface(AppAccents.For(item.AppId)));
        var trim = ImGui.GetStyle().ItemSpacing.Y;
        var textWidth = SpotlightTextWidth(width, scale);
        var textLeft = left + icon + Metrics.Space.Md * scale;
        var title = Loc.T(item.Title);
        var body = Loc.T(item.Body);
        var titleHeight = Typography.MeasureWrappedBlock(title, TextStyles.Headline, textWidth).Y - trim;
        var textHeight = titleHeight + Metrics.Space.Xxs * scale +
                         Typography.MeasureWrappedBlock(body, TextStyles.Subheadline, textWidth).Y - trim;
        var textTop = row.Center.Y - textHeight * 0.5f;
        Typography.DrawWrappedLeft(new Vector2(textLeft, textTop), title, theme.TextStrong, TextStyles.Headline,
            textWidth);
        Typography.DrawWrappedLeft(new Vector2(textLeft, textTop + titleHeight + Metrics.Space.Xxs * scale), body,
            theme.TextMuted, TextStyles.Subheadline, textWidth);
        if (!opens)
        {
            return height;
        }

        SettingsRow.DrawChevron(drawList, new Vector2(row.Max.X, row.Center.Y), scale, theme.TextMuted);
        if (UiInteract.Click(row.Min, row.Max, hovered))
        {
            context.Navigation.Open(item.AppId);
        }

        return height;
    }

    private void DrawLatestPill(ImDrawListPtr drawList, PhoneTheme theme, float scale, float x, float rowTop,
        float rowHeight)
    {
        var textSize = Typography.Measure(latestLabel, TextStyles.Caption2);
        var padX = PillPadX * scale;
        var padY = PillPadY * scale;
        var pillMin = new Vector2(x, rowTop + (rowHeight - textSize.Y - padY * 2f) * 0.5f);
        var pillMax = new Vector2(x + textSize.X + padX * 2f, pillMin.Y + textSize.Y + padY * 2f);
        Squircle.Fill(drawList, pillMin, pillMax, (pillMax.Y - pillMin.Y) * 0.5f,
            ImGui.GetColorU32(Palette.WithAlpha(theme.Accent, PillAlpha)));
        Typography.Draw(drawList, new Vector2(pillMin.X + padX, pillMin.Y + padY), latestLabel, theme.Accent,
            TextStyles.Caption2);
    }

    private void DrawRecent(PhoneTheme theme, float scale)
    {
        if (archiveStart <= 1)
        {
            return;
        }

        SettingsSection.Header(Loc.T(L.Changelog.PreviousVersions), theme);
        var rowUnits = ChangelogRows.RowHeightUnits(scale);
        var card = GroupCard.Begin(theme, archiveStart - 1, rowUnits);
        for (var entryIndex = 1; entryIndex < archiveStart; entryIndex++)
        {
            if (rows.Draw(card.NextRow(), entryIndex, theme))
            {
                OpenRelease(entryIndex);
            }
        }

        card.End();
    }

    private void DrawArchiveLink(PhoneTheme theme, float scale)
    {
        if (archiveStart >= ChangelogData.Entries.Count)
        {
            return;
        }

        ImGui.Dummy(new Vector2(0f, Metrics.Space.Xl * scale));
        var card = GroupCard.Begin(theme, 1);
        if (SettingsRow.Disclosure(card.NextRow(), olderLabel, string.Empty, theme, OlderRowId))
        {
            navigator.Open(archivePage);
        }

        card.End();
    }

    private void OpenRelease(int entryIndex)
    {
        releasePage.Show(entryIndex);
        navigator.Open(releasePage);
    }

    private void EnsureLabels()
    {
        if (ReferenceEquals(labelsLanguage, Loc.Current))
        {
            return;
        }

        labelsLanguage = Loc.Current;
        var entries = ChangelogData.Entries;
        latestLabel = Loc.Culture.TextInfo.ToUpper(Loc.T(L.Settings.ChangelogLatest));
        allChangesLabel = Loc.Plural(L.Changelog.AllChanges, entries[0].Tally.Total);
        olderLabel = archiveStart < entries.Count
            ? Loc.T(L.Changelog.OlderVersions, entries[archiveStart].MinorLine)
            : string.Empty;
    }

    private static int ArchiveStart()
    {
        var entries = ChangelogData.Entries;
        var lines = 1;
        for (var index = 1; index < entries.Count; index++)
        {
            if (entries[index].MinorLine == entries[index - 1].MinorLine)
            {
                continue;
            }

            lines++;
            if (lines > RecentMinorLines)
            {
                return index;
            }
        }

        return entries.Count;
    }
}

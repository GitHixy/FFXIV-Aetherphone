using Aetherphone.Core;
using Aetherphone.Core.Apps;
using Aetherphone.Core.Changelog;
using Aetherphone.Core.Localization;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace Aetherphone.Apps.Settings.Pages;

internal sealed class ChangelogArchivePage : ISettingsPage
{
    private readonly ChangelogRows rows;
    private readonly ReleaseNotesPage releasePage;
    private readonly ISettingsNavigator navigator;
    private readonly int[] groupStarts;
    private LanguageInfo? titleLanguage;
    private string title = string.Empty;

    public ChangelogArchivePage(ChangelogRows rows, ReleaseNotesPage releasePage, ISettingsNavigator navigator,
        int firstEntry)
    {
        this.rows = rows;
        this.releasePage = releasePage;
        this.navigator = navigator;
        groupStarts = GroupStarts(firstEntry);
    }

    public string Title
    {
        get
        {
            if (ReferenceEquals(titleLanguage, Loc.Current))
            {
                return title;
            }

            titleLanguage = Loc.Current;
            title = Loc.T(L.Changelog.OlderVersions, ChangelogData.Entries[groupStarts[0]].MinorLine);
            return title;
        }
    }

    public string Summary => string.Empty;
    public FontAwesomeIcon Icon => FontAwesomeIcon.Gift;
    public Vector4 Tint => ChangelogPage.PageTint;

    public void Draw(in PhoneContext context, Rect body)
    {
        var scale = UiScale.Current;
        var theme = context.Theme;
        using (AppSurface.Begin(body))
        {
            var width = ScrollLayout.StableContentWidth();
            var rowUnits = ChangelogRows.RowHeightUnits(scale);
            var entries = ChangelogData.Entries;
            for (var groupIndex = 0; groupIndex < groupStarts.Length - 1; groupIndex++)
            {
                var first = groupStarts[groupIndex];
                var count = groupStarts[groupIndex + 1] - first;
                SettingsSection.Header(entries[first].MinorLine, theme);
                var size = new Vector2(width, count * rowUnits * scale);
                if (!ImGui.IsRectVisible(size))
                {
                    ImGui.Dummy(size);
                    continue;
                }

                var card = GroupCard.Begin(theme, count, rowUnits);
                for (var entryIndex = first; entryIndex < first + count; entryIndex++)
                {
                    if (rows.Draw(card.NextRow(), entryIndex, theme))
                    {
                        releasePage.Show(entryIndex);
                        navigator.Open(releasePage);
                    }
                }

                card.End();
            }

            ImGui.Dummy(new Vector2(0f, Metrics.Space.Xl * scale));
        }
    }

    private static int[] GroupStarts(int firstEntry)
    {
        var entries = ChangelogData.Entries;
        var starts = new List<int> { firstEntry };
        for (var index = firstEntry + 1; index < entries.Count; index++)
        {
            if (entries[index].MinorLine != entries[index - 1].MinorLine)
            {
                starts.Add(index);
            }
        }

        starts.Add(entries.Count);
        return starts.ToArray();
    }
}

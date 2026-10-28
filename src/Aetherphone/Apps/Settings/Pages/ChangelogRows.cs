using System.Globalization;
using System.Text;
using Aetherphone.Core;
using Aetherphone.Core.Changelog;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Theme;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Settings.Pages;

internal sealed class ChangelogRows
{
    private const string SummarySeparator = "  ·  ";
    private const string DateFormat = "d MMM yyyy";
    private const float RowPadding = Metrics.Space.Sm;
    private const float LineGap = 2f;
    private readonly string[] versionLabels;
    private readonly string[] dateLabels;
    private readonly string[] summaries;
    private LanguageInfo? stringsLanguage;

    public ChangelogRows()
    {
        var count = ChangelogData.Entries.Count;
        versionLabels = new string[count];
        dateLabels = new string[count];
        summaries = new string[count];
    }

    public static float RowHeightUnits(float scale)
    {
        var text = Typography.LineHeight(TextStyles.BodyEmphasized) + Typography.LineHeight(TextStyles.Footnote);
        return MathF.Max(GroupCard.DefaultRowHeight, (RowPadding * 2f + LineGap) + text / scale);
    }

    public string VersionLabel(int entryIndex)
    {
        EnsureStrings();
        return versionLabels[entryIndex];
    }

    public string DateLabel(int entryIndex)
    {
        EnsureStrings();
        return dateLabels[entryIndex];
    }

    public string Summary(int entryIndex)
    {
        EnsureStrings();
        return summaries[entryIndex];
    }

    public bool Draw(Rect row, int entryIndex, PhoneTheme theme)
    {
        EnsureStrings();
        var scale = UiScale.Current;
        var drawList = ImGui.GetWindowDrawList();
        var hovered = UiInteract.Hover(row.Min, row.Max);
        if (hovered)
        {
            SettingsRow.DrawRowHighlight(row, theme);
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        var chevronTip = new Vector2(row.Max.X, row.Center.Y);
        var textRight = chevronTip.X - SettingsRow.ChevronReserve(scale);
        var titleHeight = Typography.LineHeight(TextStyles.BodyEmphasized);
        var summaryHeight = Typography.LineHeight(TextStyles.Footnote);
        var top = row.Center.Y - (titleHeight + LineGap * scale + summaryHeight) * 0.5f;
        var date = dateLabels[entryIndex];
        var dateWidth = Typography.Measure(date, TextStyles.Footnote).X;
        var dateLeft = textRight - dateWidth;
        var title = Typography.FitText(ChangelogData.Entries[entryIndex].ShortVersion,
            MathF.Max(1f, dateLeft - Metrics.Space.Sm * scale - row.Min.X), TextStyles.BodyEmphasized);
        Typography.Draw(drawList, new Vector2(row.Min.X, top), title, theme.TextStrong, TextStyles.BodyEmphasized);
        var dateTop = top + (titleHeight - Typography.LineHeight(TextStyles.Footnote)) * 0.5f;
        Typography.Draw(drawList, new Vector2(dateLeft, dateTop), date, theme.TextMuted, TextStyles.Footnote);
        var summary = Typography.FitText(summaries[entryIndex], MathF.Max(1f, textRight - row.Min.X),
            TextStyles.Footnote);
        Typography.Draw(drawList, new Vector2(row.Min.X, top + titleHeight + LineGap * scale), summary,
            theme.TextMuted, TextStyles.Footnote);
        SettingsRow.DrawChevron(drawList, chevronTip, scale, theme.TextMuted);
        return UiInteract.Click(row.Min, row.Max, hovered);
    }

    private void EnsureStrings()
    {
        if (ReferenceEquals(stringsLanguage, Loc.Current))
        {
            return;
        }

        stringsLanguage = Loc.Current;
        var entries = ChangelogData.Entries;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            versionLabels[index] = Loc.T(L.Changelog.VersionTitle, entry.ShortVersion);
            dateLabels[index] = FormatDate(entry.Date);
            summaries[index] = BuildSummary(entry.Tally);
        }
    }

    private static string BuildSummary(in ChangeTally tally)
    {
        var builder = new StringBuilder();
        Append(builder, L.Changelog.CountNew, tally.New);
        Append(builder, L.Changelog.CountImproved, tally.Improved);
        Append(builder, L.Changelog.CountFixed, tally.Fixed);
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, LocPlural label, int count)
    {
        if (count == 0)
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.Append(SummarySeparator);
        }

        builder.Append(Loc.Plural(label, count));
    }

    private static string FormatDate(string isoDate)
    {
        if (DateTime.TryParse(isoDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed.ToString(DateFormat, Loc.Culture);
        }

        return isoDate;
    }
}

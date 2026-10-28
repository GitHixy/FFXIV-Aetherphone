using Aetherphone.Core;
using Aetherphone.Core.Animation;
using Aetherphone.Core.Apps;
using Aetherphone.Core.Changelog;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Theme;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace Aetherphone.Apps.Settings.Pages;

internal sealed class ReleaseNotesPage : ISettingsPage
{
    private const int FilterSlots = 4;
    private const int AllKinds = -1;
    private const int ExpandAllLimit = 8;
    private const string FilterId = "changelog.filter";
    private const float TileSize = 30f;
    private const float StripHeight = 36f;
    private const float BulletRadius = 2.5f;
    private const float BulletGap = Metrics.Space.Xs;
    private const float BodyBottom = Metrics.Space.Md;
    private const float ChevronHalfHeight = 6f;
    private const float ChevronDepth = 3.5f;
    private const float ChevronThickness = 2.2f;
    private const float QuarterTurn = MathF.PI * 0.5f;
    private const float RestingHeight = 0.5f;

    private readonly ChangelogRows rows;
    private readonly List<string> wrappedLines = new();
    private readonly List<BulletLayout> bullets = new();
    private readonly List<SectionLayout> sections = new();
    private readonly string[] threeFilters = new string[3];
    private readonly string[] fourFilters = new string[FilterSlots];
    private readonly int[] filterKinds = new int[FilterSlots];
    private readonly bool[] open;
    private readonly Spring[] bodyHeights;
    private readonly Spring[] turns;
    private readonly int[] counts;
    private readonly string[] countLabels;
    private readonly float[] bodyTargets;
    private string[] filterOptions;
    private int filterCount;
    private int filter;
    private int entryIndex;
    private bool snapPending;
    private float lineHeight;
    private float textHeight;
    private int layoutEntry = -1;
    private float layoutWidth = -1f;
    private float layoutScale;
    private int layoutFontGeneration = -1;
    private LanguageInfo? layoutLanguage;

    public ReleaseNotesPage(ChangelogRows rows)
    {
        this.rows = rows;
        var maxSections = 1;
        var entries = ChangelogData.Entries;
        for (var index = 0; index < entries.Count; index++)
        {
            maxSections = Math.Max(maxSections, entries[index].Sections.Count);
        }

        open = new bool[maxSections];
        bodyHeights = new Spring[maxSections];
        turns = new Spring[maxSections];
        counts = new int[maxSections * FilterSlots];
        countLabels = new string[maxSections * FilterSlots];
        bodyTargets = new float[maxSections * FilterSlots];
        filterOptions = fourFilters;
    }

    public string Title => rows.VersionLabel(entryIndex);
    public string Summary => string.Empty;
    public FontAwesomeIcon Icon => FontAwesomeIcon.Gift;
    public Vector4 Tint => ChangelogPage.PageTint;

    public void Show(int index)
    {
        entryIndex = index;
        filter = 0;
        var entry = ChangelogData.Entries[index];
        var sectionCount = Math.Max(1, entry.Sections.Count);
        var expandAll = sectionCount == 1 || entry.Tally.Total <= ExpandAllLimit;
        for (var sectionIndex = 0; sectionIndex < open.Length; sectionIndex++)
        {
            open[sectionIndex] = expandAll && sectionIndex < sectionCount;
        }

        snapPending = true;
    }

    public void Draw(in PhoneContext context, Rect body)
    {
        var scale = UiScale.Current;
        var theme = context.Theme;
        using (AppSurface.Begin(body))
        {
            var width = ScrollLayout.StableContentWidth();
            EnsureLayout(width, scale);
            ImGui.Dummy(new Vector2(0f, Metrics.Space.Xs * scale));
            DrawCaption(theme, width, scale);
            if (filterCount > 2)
            {
                DrawFilter(theme, width, scale);
            }

            DrawSections(theme, width, scale);
            ImGui.Dummy(new Vector2(0f, Metrics.Space.Xl * scale));
        }
    }

    private void DrawCaption(PhoneTheme theme, float width, float scale)
    {
        var origin = ImGui.GetCursorScreenPos();
        var left = origin.X + Metrics.Space.Lg * scale;
        var height = Typography.DrawWrappedLeft(new Vector2(left, origin.Y), rows.DateLabel(entryIndex),
            theme.TextMuted, TextStyles.Subheadline, MathF.Max(1f, width - Metrics.Space.Lg * 2f * scale));
        ImGui.Dummy(new Vector2(width, height + Metrics.Space.Sm * scale));
    }

    private void DrawFilter(PhoneTheme theme, float width, float scale)
    {
        var origin = ImGui.GetCursorScreenPos();
        var height = StripHeight * scale;
        var row = new Rect(origin, origin + new Vector2(width, height));
        filter = SegmentStrip.Draw(FilterId, row, filterOptions, filter, theme);
        ImGui.Dummy(new Vector2(width, height + Metrics.Space.Md * scale));
    }

    private void DrawSections(PhoneTheme theme, float width, float scale)
    {
        var delta = MathF.Min(ImGui.GetIO().DeltaTime, TransitionTiming.MaxFrameSeconds);
        var headerHeight = GroupCard.DefaultRowHeight * scale;
        var total = 0f;
        var visible = 0;
        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            var slot = sectionIndex * FilterSlots + filter;
            var target = open[sectionIndex] ? bodyTargets[slot] : 0f;
            if (counts[slot] == 0)
            {
                bodyHeights[sectionIndex].SnapTo(0f);
                continue;
            }

            if (snapPending)
            {
                bodyHeights[sectionIndex].SnapTo(target);
                turns[sectionIndex].SnapTo(open[sectionIndex] ? 1f : 0f);
            }
            else
            {
                bodyHeights[sectionIndex].Step(target, Motion.Sheet, delta);
                turns[sectionIndex].Step(open[sectionIndex] ? 1f : 0f, Motion.Sheet, delta);
            }

            total += headerHeight + MathF.Max(0f, bodyHeights[sectionIndex].Value);
            visible++;
        }

        snapPending = false;
        if (visible == 0)
        {
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var right = origin.X + width;
        GroupCard.DrawSurface(drawList, origin, new Vector2(right, origin.Y + total), theme, scale);
        var padding = Metrics.Space.Lg * scale;
        var y = origin.Y;
        var drawn = 0;
        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            if (counts[sectionIndex * FilterSlots + filter] == 0)
            {
                continue;
            }

            if (drawn > 0)
            {
                drawList.AddLine(new Vector2(origin.X + padding + SettingsRow.AppTileTextInset * scale, y),
                    new Vector2(right, y), ImGui.GetColorU32(theme.Separator), Metrics.Stroke.Hairline);
            }

            var header = new Rect(new Vector2(origin.X + padding, y), new Vector2(right - padding, y + headerHeight));
            DrawHeader(drawList, sectionIndex, header, theme, scale);
            y += headerHeight;
            var bodyHeight = MathF.Max(0f, bodyHeights[sectionIndex].Value);
            if (bodyHeight > RestingHeight)
            {
                drawList.PushClipRect(new Vector2(origin.X, y), new Vector2(right, y + bodyHeight), true);
                DrawBullets(drawList, sections[sectionIndex], header, y, theme, scale);
                drawList.PopClipRect();
            }

            y += bodyHeight;
            drawn++;
        }

        ImGui.Dummy(new Vector2(width, total));
    }

    private void DrawHeader(ImDrawListPtr drawList, int sectionIndex, Rect row, PhoneTheme theme, float scale)
    {
        var section = sections[sectionIndex];
        var hovered = UiInteract.Hover(row.Min, row.Max);
        if (hovered)
        {
            SettingsRow.DrawRowHighlight(row, theme);
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        var tile = TileSize * scale;
        DrawTopicTile(drawList, section.Topic, new Vector2(row.Min.X + tile * 0.5f, row.Center.Y), tile);
        var chevronCenter = new Vector2(row.Max.X - ChevronHalfHeight * scale, row.Center.Y);
        var countLabel = countLabels[sectionIndex * FilterSlots + filter];
        var countSize = Typography.Measure(countLabel, TextStyles.Body);
        var countRight = chevronCenter.X - (ChevronHalfHeight + Metrics.Space.Md) * scale;
        Typography.Draw(drawList, new Vector2(countRight - countSize.X, row.Center.Y - countSize.Y * 0.5f), countLabel,
            theme.TextMuted, TextStyles.Body);
        var titleLeft = row.Min.X + SettingsRow.AppTileTextInset * scale;
        var title = Typography.FitText(section.Title,
            MathF.Max(1f, countRight - countSize.X - Metrics.Space.Sm * scale - titleLeft), TextStyles.BodyEmphasized);
        var titleHeight = Typography.Measure(title, TextStyles.BodyEmphasized).Y;
        Typography.Draw(drawList, new Vector2(titleLeft, row.Center.Y - titleHeight * 0.5f), title, theme.TextStrong,
            TextStyles.BodyEmphasized);
        DrawChevron(drawList, chevronCenter, turns[sectionIndex].Value * QuarterTurn, scale, theme.TextMuted);
        if (UiInteract.Click(row.Min, row.Max, hovered))
        {
            open[sectionIndex] = !open[sectionIndex];
        }
    }

    private void DrawBullets(ImDrawListPtr drawList, in SectionLayout section, Rect header, float top,
        PhoneTheme theme, float scale)
    {
        var kind = filterKinds[filter];
        var dotX = header.Min.X + TileSize * scale * 0.5f;
        var textLeft = header.Min.X + SettingsRow.AppTileTextInset * scale;
        var dotColor = ImGui.GetColorU32(theme.Accent);
        var y = top;
        var drawnBullets = 0;
        for (var bulletIndex = section.FirstBullet; bulletIndex < section.FirstBullet + section.BulletCount;
             bulletIndex++)
        {
            var bullet = bullets[bulletIndex];
            if (kind != AllKinds && (int)bullet.Kind != kind)
            {
                continue;
            }

            if (drawnBullets > 0)
            {
                y += BulletGap * scale;
            }

            var height = bullet.LineCount * lineHeight;
            if (ImGui.IsRectVisible(new Vector2(header.Min.X, y), new Vector2(header.Max.X, y + height)))
            {
                drawList.AddCircleFilled(new Vector2(dotX, y + textHeight * 0.5f), BulletRadius * scale, dotColor);
                for (var lineIndex = 0; lineIndex < bullet.LineCount; lineIndex++)
                {
                    Typography.Draw(drawList, new Vector2(textLeft, y + lineIndex * lineHeight),
                        wrappedLines[bullet.FirstLine + lineIndex], theme.TextStrong, TextStyles.Callout);
                }
            }

            y += height;
            drawnBullets++;
        }
    }

    private static void DrawTopicTile(ImDrawListPtr drawList, in ChangelogTopic topic, Vector2 center, float size)
    {
        if (topic.AppId is { } appId)
        {
            IconTile.DrawApp(drawList, appId, center, size, IconTile.Surface(AppAccents.For(appId)));
            return;
        }

        var half = new Vector2(size * 0.5f, size * 0.5f);
        IconTile.FillShaded(drawList, center - half, center + half, size * Metrics.Radius.TileFactor,
            IconTile.Surface(topic.Tint));
        ProgressRing.CenterIcon(drawList, center, topic.Icon, AccentRing.Ink, size * 0.5f);
    }

    private static void DrawChevron(ImDrawListPtr drawList, Vector2 center, float angle, float scale, Vector4 color)
    {
        var cosine = MathF.Cos(angle);
        var sine = MathF.Sin(angle);
        var depth = ChevronDepth * scale;
        var half = ChevronHalfHeight * scale;
        var tip = center + Rotate(new Vector2(depth, 0f), cosine, sine);
        var upper = center + Rotate(new Vector2(-depth, -half), cosine, sine);
        var lower = center + Rotate(new Vector2(-depth, half), cosine, sine);
        var packed = ImGui.GetColorU32(color);
        var thickness = ChevronThickness * scale;
        drawList.AddLine(upper, tip, packed, thickness);
        drawList.AddLine(tip, lower, packed, thickness);
    }

    private static Vector2 Rotate(Vector2 point, float cosine, float sine) =>
        new(point.X * cosine - point.Y * sine, point.X * sine + point.Y * cosine);

    private void EnsureLayout(float width, float scale)
    {
        var fontGeneration = Plugin.Fonts.Generation;
        if (layoutEntry == entryIndex
            && MathF.Abs(width - layoutWidth) < 0.5f
            && scale == layoutScale
            && fontGeneration == layoutFontGeneration
            && ReferenceEquals(layoutLanguage, Loc.Current))
        {
            return;
        }

        BuildLayout(width, scale);
        layoutEntry = entryIndex;
        layoutWidth = width;
        layoutScale = scale;
        layoutFontGeneration = fontGeneration;
        layoutLanguage = Loc.Current;
    }

    private void BuildLayout(float width, float scale)
    {
        wrappedLines.Clear();
        bullets.Clear();
        sections.Clear();
        lineHeight = Typography.LineHeight(TextStyles.Callout);
        textHeight = Typography.Measure("Ag", TextStyles.Callout).Y;
        var textWidth = MathF.Max(1f, width - (Metrics.Space.Lg * 2f + SettingsRow.AppTileTextInset) * scale);
        var entry = ChangelogData.Entries[entryIndex];
        if (entry.Sections.Count == 0)
        {
            AddSection(AepConstants.Name, ChangelogTopics.General, entry.Highlights, textWidth);
        }

        for (var index = 0; index < entry.Sections.Count; index++)
        {
            var section = entry.Sections[index];
            AddSection(Loc.T(section.Title), ChangelogTopics.For(section.Title), section.Highlights, textWidth);
        }

        BuildFilters(entry.Tally);
        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            for (var slot = 0; slot < filterCount; slot++)
            {
                MeasureSlot(sectionIndex, slot, scale);
            }
        }
    }

    private void AddSection(string title, in ChangelogTopic topic, IReadOnlyList<LocString> highlights,
        float textWidth)
    {
        var firstBullet = bullets.Count;
        for (var index = 0; index < highlights.Count; index++)
        {
            var lines = Typography.WrapText(Loc.T(highlights[index]), TextStyles.Callout, textWidth);
            bullets.Add(new BulletLayout(ChangeKinds.Of(highlights[index]), wrappedLines.Count, lines.Length));
            wrappedLines.AddRange(lines);
        }

        sections.Add(new SectionLayout(title, topic, firstBullet, highlights.Count));
    }

    private void BuildFilters(in ChangeTally tally)
    {
        filterCount = 0;
        AddFilter(L.Changelog.FilterAll, AllKinds);
        AddKindFilter(tally, ChangeKind.New, L.Changelog.FilterNew);
        AddKindFilter(tally, ChangeKind.Improved, L.Changelog.FilterImproved);
        AddKindFilter(tally, ChangeKind.Fixed, L.Changelog.FilterFixed);
        filter = Math.Min(filter, filterCount - 1);
        if (filterCount != threeFilters.Length)
        {
            filterOptions = fourFilters;
            return;
        }

        Array.Copy(fourFilters, threeFilters, threeFilters.Length);
        filterOptions = threeFilters;
    }

    private void AddKindFilter(in ChangeTally tally, ChangeKind kind, LocString label)
    {
        if (tally.For(kind) == 0)
        {
            return;
        }

        AddFilter(label, (int)kind);
    }

    private void AddFilter(LocString label, int kind)
    {
        fourFilters[filterCount] = Loc.T(label);
        filterKinds[filterCount] = kind;
        filterCount++;
    }

    private void MeasureSlot(int sectionIndex, int slot, float scale)
    {
        var section = sections[sectionIndex];
        var kind = filterKinds[slot];
        var count = 0;
        var height = 0f;
        for (var bulletIndex = section.FirstBullet; bulletIndex < section.FirstBullet + section.BulletCount;
             bulletIndex++)
        {
            var bullet = bullets[bulletIndex];
            if (kind != AllKinds && (int)bullet.Kind != kind)
            {
                continue;
            }

            if (count > 0)
            {
                height += BulletGap * scale;
            }

            height += bullet.LineCount * lineHeight;
            count++;
        }

        var index = sectionIndex * FilterSlots + slot;
        counts[index] = count;
        countLabels[index] = count.ToString(Loc.Culture);
        bodyTargets[index] = count == 0 ? 0f : height + BodyBottom * scale;
    }

    private readonly struct SectionLayout
    {
        public readonly string Title;
        public readonly ChangelogTopic Topic;
        public readonly int FirstBullet;
        public readonly int BulletCount;

        public SectionLayout(string title, ChangelogTopic topic, int firstBullet, int bulletCount)
        {
            Title = title;
            Topic = topic;
            FirstBullet = firstBullet;
            BulletCount = bulletCount;
        }
    }

    private readonly struct BulletLayout
    {
        public readonly ChangeKind Kind;
        public readonly int FirstLine;
        public readonly int LineCount;

        public BulletLayout(ChangeKind kind, int firstLine, int lineCount)
        {
            Kind = kind;
            FirstLine = firstLine;
            LineCount = lineCount;
        }
    }
}

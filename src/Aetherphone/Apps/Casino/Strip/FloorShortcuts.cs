using Aetherphone.Apps.Casino.Stage;
using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Onboarding;
using Aetherphone.Core.Theme;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace Aetherphone.Apps.Casino.Strip;

internal enum FloorShortcutKind : byte
{
    Missions,
    Club,
    Fame,
    History,
}

internal readonly record struct FloorShortcut(FloorShortcutKind Kind, FontAwesomeIcon Icon, Vector4 Tint,
    string Label, string Badge, Vector4 BadgeFill);

internal readonly record struct MissionsTally(int Done, int Total, int Ready);

internal sealed class FloorShortcuts
{
    public const int Capacity = 4;
    public const float TileHeight = 84f;
    public const float Icon = 36f;
    public const float Pad = 6f;
    public const float IconGap = 7f;
    public const float LabelPad = 2f;
    public const float BadgeInset = 5f;
    public const float BadgePadX = 5f;
    public const float BadgePadY = 1.5f;
    public const float BadgeRing = 1.5f;
    public const int LabelLines = 2;

    private const string AnchorKey = "casino.shortcuts";

    private static readonly TextStyle LabelStyle = TextStyles.FootnoteEmphasized;

    private readonly TileLabelFit[] fits = new TileLabelFit[Capacity];

    public static MissionsTally Tally(CasinoMissionsDto? missions)
    {
        var total = MissionsCard.Count(missions);
        var done = 0;
        var ready = 0;
        for (var index = 0; index < total; index++)
        {
            var mission = missions!.Missions![index];
            if (!mission.Complete)
            {
                continue;
            }

            done++;
            ready += mission.Claimed ? 0 : 1;
        }

        return new MissionsTally(done, total, ready);
    }

    public static float Height(float labelBlock, float scale) =>
        MathF.Max(TileHeight * scale, (Pad * 2f + Icon + IconGap) * scale + labelBlock);

    public static float LabelWidth(float tileWidth, float scale) => MathF.Max(1f, tileWidth - LabelPad * 2f * scale);

    public int Draw(ImDrawListPtr drawList, AppSkin ui, ReadOnlySpan<FloorShortcut> items, Vector2 origin,
        float width, float scale, out float bottom)
    {
        var count = Math.Min(items.Length, Capacity);
        bottom = origin.Y;
        if (count == 0)
        {
            return -1;
        }

        var gap = StripShelves.GridGap * scale;
        var tileWidth = StripGrid.CellWidth(width, count, gap);
        var labelWidth = LabelWidth(tileWidth, scale);
        var labelBlock = 0f;
        for (var index = 0; index < count; index++)
        {
            fits[index] = TileLabel.Fit(items[index].Label, labelWidth, LabelLines, LabelStyle, LabelStyle);
            labelBlock = MathF.Max(labelBlock, TileLabel.Height(fits[index]));
        }

        var height = Height(labelBlock, scale);
        bottom = origin.Y + height;
        UiAnchors.Report(AnchorKey, new Rect(origin, new Vector2(origin.X + width, bottom)));
        var tapped = -1;
        for (var index = 0; index < count; index++)
        {
            var left = origin.X + index * (tileWidth + gap);
            var tile = new Rect(new Vector2(left, origin.Y), new Vector2(left + tileWidth, bottom));
            using (ImRaii.PushId(index))
            {
                if (DrawTile(drawList, ui, items[index], fits[index], tile, labelBlock, scale))
                {
                    tapped = index;
                }
            }
        }

        return tapped;
    }

    private static bool DrawTile(ImDrawListPtr drawList, AppSkin ui, in FloorShortcut item, in TileLabelFit fit,
        Rect tile, float labelBlock, float scale)
    {
        var hovered = CasinoArt.PressCard(ImGui.GetID("shortcut"), tile.Min, tile.Max, out var min, out var max);
        var radius = Metrics.Radius.Lg * scale;
        ui.Card(drawList, min, max, radius);
        var icon = Icon * scale;
        var blockTop = (min.Y + max.Y) * 0.5f - (icon + IconGap * scale + labelBlock) * 0.5f;
        var iconCenter = new Vector2((min.X + max.X) * 0.5f, blockTop + icon * 0.5f);
        CasinoArt.IconTileAt(drawList, iconCenter, icon, item.Tint, item.Icon);
        var lineHeight = Typography.LineHeight(fit.Style);
        var top = blockTop + icon + IconGap * scale;
        for (var line = 0; line < fit.Lines.Length; line++)
        {
            var text = fit.Lines[line];
            var size = Typography.Measure(text, fit.Style);
            Typography.Draw(drawList, new Vector2(iconCenter.X - size.X * 0.5f, top), text, ui.TitleInk, fit.Style);
            top += lineHeight;
        }

        DrawBadge(drawList, ui, item, new Rect(min, max), scale);
        return UiInteract.Click(tile.Min, tile.Max, hovered);
    }

    private static void DrawBadge(ImDrawListPtr drawList, AppSkin ui, in FloorShortcut item, Rect tile, float scale)
    {
        if (item.Badge.Length == 0)
        {
            return;
        }

        var inset = BadgeInset * scale;
        var padX = BadgePadX * scale;
        var label = Typography.FitText(item.Badge, MathF.Max(1f, tile.Width - inset * 2f - padX * 2f), LabelStyle);
        var size = Typography.Measure(label, LabelStyle);
        var height = size.Y + BadgePadY * 2f * scale;
        var max = new Vector2(tile.Max.X - inset, tile.Min.Y + inset + height);
        var min = new Vector2(max.X - size.X - padX * 2f, tile.Min.Y + inset);
        var ring = BadgeRing * scale;
        Squircle.Fill(drawList, min - new Vector2(ring, ring), max + new Vector2(ring, ring), height * 0.5f + ring,
            ImGui.GetColorU32(ui.Palette.BackdropTop with { W = 1f }));
        Squircle.Fill(drawList, min, max, height * 0.5f, ImGui.GetColorU32(item.BadgeFill));
        Typography.Draw(drawList, (min + max) * 0.5f - size * 0.5f, label, CasinoColors.InkOnLight, LabelStyle);
    }
}

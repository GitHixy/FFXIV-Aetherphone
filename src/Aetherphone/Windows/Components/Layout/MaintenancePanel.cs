using Aetherphone.Core;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Net;
using Aetherphone.Core.Theme;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace Aetherphone.Windows.Components;

internal sealed class MaintenancePanel
{
    private const float MaxWidthUnits = 320f;
    private const float SidePadUnits = 28f;
    private const float IconRadiusUnits = 30f;
    private const float GlyphUnits = 26f;
    private const float IconWashAlpha = 0.16f;
    private const float TileHeightUnits = 64f;
    private const float CompactTileHeightUnits = 46f;
    private const float TileRadiusUnits = 14f;
    private const float CompactTileRadiusUnits = 10f;
    private const float TileGapUnits = 8f;
    private const float CompactTileGapUnits = 6f;
    private const float TileAccentMix = 0.14f;
    private const long MillisecondsPerSecond = 1000L;

    private readonly CountdownTiles countdown = new();

    public void Draw(Rect area, PhoneTheme theme, MaintenanceView view)
    {
        var scale = UiScale.Current;
        var width = MathF.Max(1f, MathF.Min(area.Width - 2f * SidePadUnits * scale, MaxWidthUnits * scale));
        var nowMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var remaining = view.SecondsLeft(nowMilliseconds / MillisecondsPerSecond);
        var counting = remaining > 0;
        var title = Loc.T(L.Maintenance.Title);
        var message = view.Message ?? string.Empty;
        var status = Loc.T(counting ? L.Maintenance.ExpectedBackIn : L.Maintenance.FinishingUp);
        var footnote = Loc.T(L.Maintenance.OfflineApps);
        var titleHeight = Typography.MeasureWrappedBlock(title, TextStyles.Title3, width).Y;
        var messageHeight = message.Length == 0
            ? 0f
            : Typography.MeasureWrappedBlock(message, TextStyles.Subheadline, width).Y;
        var statusStyle = counting ? TextStyles.Footnote : TextStyles.Headline;
        var statusHeight = Typography.MeasureWrappedBlock(status, statusStyle, width).Y;
        var footnoteHeight = Typography.MeasureWrappedBlock(footnote, TextStyles.Footnote, width).Y;
        var iconDiameter = 2f * IconRadiusUnits * scale;
        var messageBlock = message.Length == 0 ? 0f : Metrics.Space.Sm * scale + messageHeight;
        var tilesBlock = counting ? Metrics.Space.Sm * scale + TileHeightUnits * scale : 0f;
        var fullHeight = iconDiameter + Metrics.Space.Lg * scale + titleHeight + messageBlock
                         + Metrics.Space.Xl * scale + statusHeight + tilesBlock
                         + Metrics.Space.Xl * scale + footnoteHeight;
        var compact = fullHeight > area.Height;
        var tileHeight = (compact ? CompactTileHeightUnits : TileHeightUnits) * scale;
        var height = compact
            ? titleHeight + messageBlock + Metrics.Space.Md * scale + statusHeight
              + (counting ? Metrics.Space.Xs * scale + tileHeight : 0f)
            : fullHeight;
        var drawList = ImGui.GetWindowDrawList();
        var centerX = area.Center.X;
        var cursorY = MathF.Max(area.Min.Y, area.Center.Y - height * 0.5f);
        drawList.PushClipRect(area.Min, area.Max, true);
        if (!compact)
        {
            var iconCenter = new Vector2(centerX, cursorY + iconDiameter * 0.5f);
            drawList.AddCircleFilled(iconCenter, IconRadiusUnits * scale,
                ImGui.GetColorU32(Palette.WithAlpha(theme.Accent, IconWashAlpha)), 32);
            ProgressRing.CenterIcon(drawList, iconCenter, FontAwesomeIcon.Wrench, theme.Accent, GlyphUnits * scale);
            cursorY += iconDiameter + Metrics.Space.Lg * scale;
        }

        cursorY += Typography.DrawWrappedCentered(new Vector2(centerX, cursorY), title, theme.TextStrong,
            TextStyles.Title3, width);
        if (message.Length > 0)
        {
            cursorY += Metrics.Space.Sm * scale;
            cursorY += Typography.DrawWrappedCentered(new Vector2(centerX, cursorY), message, theme.TextMuted,
                TextStyles.Subheadline, width);
        }

        cursorY += (compact ? Metrics.Space.Md : Metrics.Space.Xl) * scale;
        cursorY += Typography.DrawWrappedCentered(new Vector2(centerX, cursorY), status,
            counting ? theme.TextMuted : theme.TextStrong, statusStyle, width);
        if (counting)
        {
            cursorY += (compact ? Metrics.Space.Xs : Metrics.Space.Sm) * scale;
            var row = new Rect(new Vector2(centerX - width * 0.5f, cursorY),
                new Vector2(centerX + width * 0.5f, cursorY + tileHeight));
            countdown.Draw(drawList, row, remaining, nowMilliseconds / (double)MillisecondsPerSecond,
                TileLook(theme, compact), scale);
            cursorY += tileHeight;
        }

        if (!compact)
        {
            cursorY += Metrics.Space.Xl * scale;
            Typography.DrawWrappedCentered(new Vector2(centerX, cursorY), footnote, theme.TextMuted,
                TextStyles.Footnote, width);
        }

        drawList.PopClipRect();
    }

    private static CountdownStyle TileLook(PhoneTheme theme, bool compact) => new(
        Palette.Mix(theme.Surface, theme.Accent, TileAccentMix), theme.Surface, theme.Hairline, theme.TextStrong,
        theme.TextMuted, compact ? TextStyles.Title2 : TextStyles.Title1, TextStyles.Caption2,
        compact ? CompactTileRadiusUnits : TileRadiusUnits, compact ? CompactTileGapUnits : TileGapUnits);
}

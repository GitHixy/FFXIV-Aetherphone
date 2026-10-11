using Aetherphone.Apps.Coin;
using Aetherphone.Core;
using Aetherphone.Core.Theme;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace Aetherphone.Apps.Casino.Strip;

internal static class FloorCapsule
{
    public const float Height = 64f;
    public const float Pad = 14f;
    public const float Tile = 36f;
    public const float BarHeight = 5f;

    private const float SurfaceMix = 0.16f;
    private const float StrokeAlpha = 0.55f;
    private const float TrackAlpha = 0.25f;
    private const float ChevronInset = 4f;

    public static bool Draw(ImDrawListPtr drawList, AppSkin ui, string id, Vector2 origin, float width, float scale,
        Vector4 tint, FontAwesomeIcon icon, string title, float fraction, string line, Vector4 lineInk,
        out float bottom)
    {
        var height = Height * scale;
        var max = new Vector2(origin.X + width, origin.Y + height);
        bottom = max.Y;
        var hovered = CasinoArt.PressCard(ImGui.GetID(id), origin, max, out var min, out var shown);
        var radius = height * 0.5f;
        Squircle.Fill(drawList, min, shown, radius,
            ImGui.GetColorU32(Palette.Mix(ui.Palette.BackdropTop, tint, SurfaceMix)));
        Squircle.Stroke(drawList, min, shown, radius, ImGui.GetColorU32(tint with { W = StrokeAlpha }),
            MathF.Max(1f, scale));
        var pad = Pad * scale;
        var tile = Tile * scale;
        var tileCenter = new Vector2(min.X + pad + tile * 0.5f, (min.Y + shown.Y) * 0.5f);
        CasinoArt.IconTileAt(drawList, tileCenter, tile, tint, icon);
        var chevron = new Vector2(shown.X - pad - ChevronInset * scale, tileCenter.Y);
        CasinoArt.Chevron(drawList, chevron, ui.BodyInk);
        var textLeft = tileCenter.X + tile * 0.5f + CoinArt.TextGap * scale;
        var textWidth = MathF.Max(1f, chevron.X - CoinArt.ValueGap * scale - textLeft);
        var headline = Typography.LineHeight(TextStyles.Headline);
        var footnote = Typography.LineHeight(TextStyles.Footnote);
        var bar = BarHeight * scale;
        var top = tileCenter.Y - (headline + bar + Metrics.Space.Xs * scale + footnote) * 0.5f;
        Typography.Draw(drawList, new Vector2(textLeft, top), Typography.FitText(title, textWidth, TextStyles.Headline),
            ui.TitleInk, TextStyles.Headline);
        top += headline;
        CoinArt.Bar(drawList, new Vector2(textLeft, top), new Vector2(textLeft + textWidth, top + bar), fraction,
            Palette.WithAlpha(ui.MutedInk, TrackAlpha), tint);
        top += bar + Metrics.Space.Xs * scale;
        Typography.Draw(drawList, new Vector2(textLeft, top), Typography.FitText(line, textWidth, TextStyles.Footnote),
            lineInk, TextStyles.Footnote);
        return UiInteract.Click(origin, max, hovered);
    }
}

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
        var pad = Pad * scale;
        var tile = Tile * scale;
        var textWidth = MathF.Max(1f, width - pad * 2f - ChevronInset * scale - CoinArt.ValueGap * scale - tile
                                      - CoinArt.TextGap * scale);
        var bar = BarHeight * scale;
        var block = Tables.PitText.Height(title, TextStyles.Headline, textWidth) + bar + Metrics.Space.Xs * scale
                    + Tables.PitText.Height(line, TextStyles.Footnote, textWidth);
        var height = MathF.Max(Height * scale, block + Metrics.Space.Sm * 2f * scale);
        var max = new Vector2(origin.X + width, origin.Y + height);
        bottom = max.Y;
        var hovered = CasinoArt.PressCard(ImGui.GetID(id), origin, max, out var min, out var shown);
        var radius = Height * scale * 0.5f;
        Squircle.Fill(drawList, min, shown, radius,
            ImGui.GetColorU32(Palette.Mix(ui.Palette.BackdropTop, tint, SurfaceMix)));
        Squircle.Stroke(drawList, min, shown, radius, ImGui.GetColorU32(tint with { W = StrokeAlpha }),
            MathF.Max(1f, scale));
        var tileCenter = new Vector2(min.X + pad + tile * 0.5f, (min.Y + shown.Y) * 0.5f);
        CasinoArt.IconTileAt(drawList, tileCenter, tile, tint, icon);
        var chevron = new Vector2(shown.X - pad - ChevronInset * scale, tileCenter.Y);
        CasinoArt.Chevron(drawList, chevron, ui.BodyInk);
        var textLeft = tileCenter.X + tile * 0.5f + CoinArt.TextGap * scale;
        var top = tileCenter.Y - block * 0.5f;
        top = Tables.PitText.Draw(drawList, title, TextStyles.Headline, ui.TitleInk, textLeft, top, textWidth, false);
        CoinArt.Bar(drawList, new Vector2(textLeft, top), new Vector2(textLeft + textWidth, top + bar), fraction,
            Palette.WithAlpha(ui.MutedInk, TrackAlpha), tint);
        top += bar + Metrics.Space.Xs * scale;
        Tables.PitText.Draw(drawList, line, TextStyles.Footnote, lineInk, textLeft, top, textWidth, false);
        return UiInteract.Click(origin, max, hovered);
    }
}

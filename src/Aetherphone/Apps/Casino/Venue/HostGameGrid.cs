using Aetherphone.Apps.Casino.Strip;
using Aetherphone.Apps.Casino.Tables;
using Aetherphone.Core;
using Aetherphone.Core.Casino;
using Aetherphone.Core.Localization;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace Aetherphone.Apps.Casino.Venue;

internal sealed class HostGameGrid
{
    private const float HeaderGap = 10f;
    private const float SublineGap = 8f;

    private readonly string[] metas = new string[VenuePosters.Games.Length];
    private string badge = string.Empty;
    private LanguageInfo? language;
    private bool metasGilOpen;

    public float Draw(ImDrawListPtr drawList, AppSkin ui, Vector2 origin, float width, CasinoFeatureSet features,
        bool gilOpen, bool bookOpen, bool interactive, float scale, out int tapped)
    {
        RefreshTexts(gilOpen);
        tapped = -1;
        var top = DrawHeader(drawList, ui, origin, width, scale) + HeaderGap * scale;
        var tileWidth = HostHomeLayout.GridTileWidth(width, scale);
        var tileHeight = PosterTile.Height(tileWidth, PosterTile.TextBlock(scale));
        var count = VenuePosters.Count(bookOpen);
        var phase = (float)ImGui.GetTime();
        var gridOrigin = new Vector2(origin.X, top);
        for (var index = 0; index < count; index++)
        {
            var game = VenuePosters.Games[index];
            var tile = HostHomeLayout.GridCell(gridOrigin, tileWidth, tileHeight, index, scale);
            var open = CasinoGameGate.IsOpen(features, HostGames.Of(game).GameId);
            var shownBadge = VenuePosters.ShowsGilBadge(game, gilOpen) ? badge : string.Empty;
            using (ImRaii.PushId(index))
            {
                if (DrawPoster(drawList, game, tile, metas[index], shownBadge, open, phase, interactive && open, scale))
                {
                    tapped = index;
                }
            }
        }

        return top + HostHomeLayout.GridHeight(count, tileHeight, scale);
    }

    private static bool DrawPoster(ImDrawListPtr drawList, HostGame game, Rect tile, string meta, string badge,
        bool open, float phase, bool interactive, float scale)
    {
        ref readonly var info = ref HostGames.Of(game);
        var hovered = CasinoArt.PressCard(ImGui.GetID("poster"), tile.Min, tile.Max, out var pressedMin,
            out var pressedMax, interactive);
        var pressed = new Rect(pressedMin, pressedMax);
        PosterTile.DrawFrame(drawList, pressed, info.Tint, scale);
        if (info.HasGlyph)
        {
            PosterTile.DrawGlyph(drawList, pressed, info.GameId, scale);
        }
        else
        {
            PosterTile.DrawIcon(drawList, pressed, info.Icon, scale);
        }

        PosterTile.DrawSign(drawList, pressed, info.Sign, phase, scale, PosterTile.SignBandTop(scale));
        var poster = new PosterInfo(open, 0, meta, string.Empty, badge);
        PosterTile.DrawText(drawList, pressed, Loc.T(info.Title), poster, scale);
        PosterTile.DrawBadge(drawList, pressed, poster.Badge, scale);
        if (!open)
        {
            PosterTile.DrawClosed(drawList, pressed, scale);
        }

        PosterTile.DrawRim(drawList, pressed, info.Tint, hovered, scale);
        return interactive && UiInteract.Click(tile.Min, tile.Max, hovered);
    }

    private static float DrawHeader(ImDrawListPtr drawList, AppSkin ui, Vector2 origin, float width, float scale)
    {
        var title = Loc.T(L.HostDesk.PickGame);
        var subline = Loc.T(L.HostDesk.PickGameLine);
        var titleSize = Typography.Measure(title, TextStyles.Title3);
        var sublineSize = Typography.Measure(subline, TextStyles.Footnote);
        var titleHeight = Typography.LineHeight(TextStyles.Title3);
        var sideBySide = titleSize.X + SublineGap * scale + sublineSize.X <= width;
        Typography.Draw(drawList, origin, Typography.FitText(title, width, TextStyles.Title3), ui.TitleInk,
            TextStyles.Title3);
        if (sideBySide)
        {
            var baseline = origin.Y + titleHeight - Typography.LineHeight(TextStyles.Footnote);
            Typography.Draw(drawList, new Vector2(origin.X + width - sublineSize.X, baseline), subline, ui.MutedInk,
                TextStyles.Footnote);
            return origin.Y + titleHeight;
        }

        return PitText.Draw(drawList, subline, TextStyles.Footnote, ui.MutedInk, origin.X, origin.Y + titleHeight,
            width, false);
    }

    private void RefreshTexts(bool gilOpen)
    {
        if (ReferenceEquals(language, Loc.Current) && gilOpen == metasGilOpen && badge.Length > 0)
        {
            return;
        }

        language = Loc.Current;
        metasGilOpen = gilOpen;
        badge = Loc.T(L.VenueHub.GilBadge);
        for (var index = 0; index < metas.Length; index++)
        {
            metas[index] = Loc.T(VenuePosters.MetaOf(VenuePosters.Games[index], gilOpen));
        }
    }
}

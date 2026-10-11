using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Casino;
using Aetherphone.Core.Localization;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace Aetherphone.Apps.Casino.Strip;

internal sealed class ClubCapsule
{
    public const float Height = FloorCapsule.Height;

    private readonly CasinoTextCache texts = new();

    public bool Draw(ImDrawListPtr drawList, AppSkin ui, CasinoClubDto club, Vector2 origin, float width, float scale,
        out float bottom)
    {
        var tier = CasinoClubTiers.Clamp(club.Tier);
        var tint = ClubTierArt.TierTint(tier);
        var title = texts.Named(L.Strip.ClubTierTitle, Loc.T(ClubTierArt.TierName(tier)));
        var line = tier >= CasinoClubTiers.Obsidian || club.NextTierPoints <= club.Points
            ? Loc.T(L.Strip.ClubTop)
            : texts.NamedNumber(L.Strip.ClubPointsTo, Loc.T(ClubTierArt.TierName(tier + 1)),
                Math.Max(0, club.NextTierPoints - club.Points));
        return FloorCapsule.Draw(drawList, ui, "casino.club.capsule", origin, width, scale, tint, FontAwesomeIcon.Crown,
            title, CasinoClubTiers.Progress(club), line, ui.BodyInk, out bottom);
    }
}

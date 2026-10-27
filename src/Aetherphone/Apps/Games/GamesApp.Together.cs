using Aetherphone.Core;
using Aetherphone.Core.Apps;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Net;
using Aetherphone.Core.Theme;
using Aetherphone.Windows.Components;

namespace Aetherphone.Apps.Games;

internal sealed partial class GamesApp
{
    private const string TogetherNavId = "games.together.nav";
    private const string RoomMaintenanceNavId = "games.room.maintenance.nav";

    private readonly MaintenancePanel maintenancePanel = new();
    private Action? leaveRoom;

    private void DrawTogether(in PhoneContext context)
    {
        var navBar = AppHeader.BeginLargeTitle(context, false);
        DrawTogetherBody(navBar.Body, context.Theme);
        AppHeader.EndLargeTitle(in navBar, context, TogetherNavId, Loc.T(L.Games.OnlineTitle),
            NavBarStyle.From(ui), ReadOnlySpan<NavBarButton>.Empty);
    }

    private void DrawTogetherBody(Rect body, PhoneTheme surfaceTheme)
    {
        if (maintenance.Holds)
        {
            maintenancePanel.Draw(body, surfaceTheme, maintenance.View);
            return;
        }

        using (var surface = AppSurface.Begin(body))
        {
            onlineHub.Draw(body, surface.Pull, surface.Dragging, ui, navigation);
        }
    }

    private void DrawRoomMaintenance(in PhoneContext context)
    {
        AppLandscape.Release(Id);
        leaveRoom ??= LeaveOnlineRoom;
        var navBar = AppHeader.BeginLargeTitle(context, true);
        maintenancePanel.Draw(navBar.Body, context.Theme, maintenance.View);
        AppHeader.EndLargeTitle(in navBar, context, RoomMaintenanceNavId, Loc.T(L.Games.OnlineTitle),
            NavBarStyle.From(ui), ReadOnlySpan<NavBarButton>.Empty, TabTitle(tab), leaveRoom);
    }
}

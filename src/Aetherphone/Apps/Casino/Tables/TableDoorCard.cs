using Aetherphone.Apps.Casino.Stage;
using Aetherphone.Core;
using Aetherphone.Core.Casino;
using Aetherphone.Core.Localization;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Casino.Tables;

internal enum TableDoorAction : byte
{
    None,
    Knock,
    Leave,
}

internal static class TableDoorCard
{
    public static TableDoorAction Draw(ImDrawListPtr drawList, AppSkin ui, string reason, LocString closedTitle,
        LocString closedHint, Rect safe, float scale)
    {
        var asks = AsksToJoin(reason);
        var title = Loc.T(TitleFor(reason, closedTitle));
        var hint = Loc.T(CasinoReasons.TryMessage(reason, out var known) ? known : closedHint);
        var action = Loc.T(asks ? L.Casino.BlackjackAskToJoin : L.Casino.WheelBackToFloor);
        CasinoNotice.DrawWithAction(drawList, ui, CasinoNoticeKind.Card, title, hint, action, safe.Min.X,
            safe.Min.Y + Metrics.Space.Lg * scale, safe.Width, scale, out var pressed);
        if (!pressed)
        {
            return TableDoorAction.None;
        }

        return asks ? TableDoorAction.Knock : TableDoorAction.Leave;
    }

    internal static bool AsksToJoin(string reason)
    {
        return string.Equals(reason, CasinoReasons.InviteOnly, StringComparison.Ordinal)
            || string.Equals(reason, CasinoReasons.NotMember, StringComparison.Ordinal)
            || string.Equals(reason, CasinoReasons.Denied, StringComparison.Ordinal);
    }

    internal static bool Removed(string reason)
    {
        return string.Equals(reason, CasinoReasons.Kicked, StringComparison.Ordinal)
            || string.Equals(reason, CasinoReasons.BannedFromTable, StringComparison.Ordinal);
    }

    internal static LocString TitleFor(string reason, LocString closedTitle)
    {
        if (Removed(reason))
        {
            return L.Tables.RemovedTitle;
        }

        return AsksToJoin(reason) ? L.Casino.BlackjackDoorTitle : closedTitle;
    }
}

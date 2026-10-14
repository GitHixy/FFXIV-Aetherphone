using Aetherphone.Apps.Casino.Stage;
using Aetherphone.Apps.Casino.Strip;
using Aetherphone.Apps.Casino.Tables;
using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Casino;
using Aetherphone.Core.Localization;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace Aetherphone.Apps.Casino.Live;

internal sealed class HoldemRoomsSheet
{
    private const float MinShare = 0.3f;
    private const float MaxShare = 0.9f;
    private const float HeaderTile = 44f;
    private const float HeaderGap = 12f;
    private const float ListGap = 14f;
    private const float RowGap = 8f;
    private const float RowPadX = 14f;
    private const float RowPadY = 12f;
    private const float RowRadius = 16f;
    private const float LineGap = 3f;
    private const float ColumnGap = 12f;
    private const float RowStrokeAlpha = 0.06f;
    private const float EmptySeatAlpha = 0.16f;

    private static readonly string[] SitIds = BuildIds();

    private readonly SheetSurface sheet = new("casino.live.holdem");
    private readonly Action<Rect> drawBody;
    private readonly HoldemRoomView[] rooms = new HoldemRoomView[HoldemRules.TierCount];

    private CasinoTableRowDto[]? roomsSource;
    private LanguageInfo? roomsLanguage;
    private bool roomsLocked;
    private AppSkin skin = null!;
    private string picked = string.Empty;

    public HoldemRoomsSheet()
    {
        drawBody = DrawBody;
    }

    public bool CapturesPointer => sheet.CapturesPointer;

    public void Open() => sheet.Open();

    public void Close() => sheet.Close();

    public string TakePick()
    {
        var room = picked;
        picked = string.Empty;
        return room;
    }

    public void Draw(Rect screen, AppSkin ui, CasinoTableRowDto[] tables, bool royalLocked)
    {
        if (!sheet.CapturesPointer)
        {
            return;
        }

        skin = ui;
        Refresh(tables, royalLocked);
        var scale = UiScale.Current;
        var width = screen.Width - Metrics.Space.Lg * 2f * scale;
        var share = Math.Clamp(
            (SheetSurface.ChromeHeight() + ContentHeight(width, scale) + Metrics.Space.Lg * 2f * scale) /
            screen.Height, MinShare, MaxShare);
        sheet.Draw(screen, CasinoArt.Sheet(ui), Loc.T(L.LivePage.RoomsTitle), share, drawBody);
    }

    internal static bool Seats(in HoldemRoomView room) => !room.Locked && room.Seated < room.MaxSeats;

    private static string[] BuildIds()
    {
        var ids = new string[HoldemRules.TierCount];
        for (var tier = 0; tier < ids.Length; tier++)
        {
            ids[tier] = "casino.live.holdem." + tier.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return ids;
    }

    private void Refresh(CasinoTableRowDto[] tables, bool royalLocked)
    {
        if (ReferenceEquals(tables, roomsSource) && ReferenceEquals(roomsLanguage, Loc.Current)
            && royalLocked == roomsLocked)
        {
            return;
        }

        roomsSource = tables;
        roomsLanguage = Loc.Current;
        roomsLocked = royalLocked;
        for (var tier = 0; tier < rooms.Length; tier++)
        {
            rooms[tier] = HoldemPit.HouseView(tier, LiveBoard.Find(tables, HoldemRules.HouseRooms[tier]), royalLocked);
        }
    }

    private float HeaderHeight(float width, float scale) =>
        MathF.Max(HeaderTile * scale, PitText.Height(Loc.T(L.LivePage.RoomsLine), TextStyles.Subheadline,
            HeaderTextWidth(width, scale)));

    private static float HeaderTextWidth(float width, float scale) =>
        MathF.Max(1f, width - (HeaderTile + HeaderGap) * scale);

    private float ContentHeight(float width, float scale)
    {
        var height = HeaderHeight(width, scale) + ListGap * scale;
        for (var tier = 0; tier < rooms.Length; tier++)
        {
            height += RowHeight(rooms[tier], width, scale) + (tier > 0 ? RowGap * scale : 0f);
        }

        return height;
    }

    private static string ActionOf(in HoldemRoomView room) => Loc.T(Seats(room) ? L.LivePage.Sit : L.LivePage.Watch);

    private static string DetailOf(in HoldemRoomView room) => room.Locked ? Loc.T(L.Holdem.RoyalLocked) : room.BuyIn;

    private static float ActionWidth(in HoldemRoomView room) => Button.WidthFor(ActionOf(room), ButtonSize.Regular);

    private static float TextWidth(in HoldemRoomView room, float width, float scale) =>
        MathF.Max(1f, width - RowPadX * 2f * scale - CasinoArt.SeatDotsWidth(room.MaxSeats, scale)
                      - ActionWidth(room) - ColumnGap * 2f * scale);

    private static float RowHeight(in HoldemRoomView room, float width, float scale)
    {
        var textWidth = TextWidth(room, width, scale);
        var text = Typography.LineHeight(TextStyles.Headline) + LineGap * scale
                   + PitText.Height(DetailOf(room), TextStyles.Footnote, textWidth);
        return RowPadY * 2f * scale + MathF.Max(Button.RegularHeight * scale, text);
    }

    private void DrawBody(Rect content)
    {
        ImGui.SetCursorScreenPos(content.Min);
        using (ImRaii.Child("##liveHoldemRooms", content.Size, false, ImGuiWindowFlags.NoBackground))
        {
            var scale = UiScale.Current;
            var drawList = ImGui.GetWindowDrawList();
            var origin = ImGui.GetCursorScreenPos();
            var width = ScrollLayout.NativeScrollContentWidth();
            var top = DrawHeader(drawList, origin, width, scale) + ListGap * scale;
            for (var tier = 0; tier < rooms.Length; tier++)
            {
                top = DrawRoom(drawList, rooms[tier], tier, new Vector2(origin.X, top), width, scale) + RowGap * scale;
            }

            ImGui.SetCursorScreenPos(origin);
            ImGui.Dummy(new Vector2(width, MathF.Max(1f, top - RowGap * scale - origin.Y)));
        }
    }

    private float DrawHeader(ImDrawListPtr drawList, Vector2 origin, float width, float scale)
    {
        var height = HeaderHeight(width, scale);
        var tile = HeaderTile * scale;
        CasinoArt.GameTile(drawList, CasinoGames.Holdem, new Vector2(origin.X + tile * 0.5f, origin.Y + height * 0.5f),
            tile);
        var line = Loc.T(L.LivePage.RoomsLine);
        var textWidth = HeaderTextWidth(width, scale);
        var lineHeight = PitText.Height(line, TextStyles.Subheadline, textWidth);
        PitText.Draw(drawList, line, TextStyles.Subheadline, skin.BodyInk, origin.X + tile + HeaderGap * scale,
            origin.Y + (height - lineHeight) * 0.5f, textWidth, false);
        return origin.Y + height;
    }

    private float DrawRoom(ImDrawListPtr drawList, in HoldemRoomView room, int tier, Vector2 origin, float width,
        float scale)
    {
        var height = RowHeight(room, width, scale);
        var max = new Vector2(origin.X + width, origin.Y + height);
        var radius = RowRadius * scale;
        Squircle.Fill(drawList, origin, max, radius, ImGui.GetColorU32(StageText.CapsuleFill));
        Squircle.Stroke(drawList, origin, max, radius,
            ImGui.GetColorU32(skin.TitleInk with { W = RowStrokeAlpha }), MathF.Max(1f, scale));
        if (tier == HoldemRules.RoyalTier)
        {
            Squircle.Stroke(drawList, origin, max, radius, ImGui.GetColorU32(CasinoColors.Money with { W = 0.45f }),
                MathF.Max(1f, scale));
        }

        var centerY = origin.Y + height * 0.5f;
        var padX = RowPadX * scale;
        var actionWidth = ActionWidth(room);
        var actionHeight = Button.RegularHeight * scale;
        var action = new Rect(new Vector2(max.X - padX - actionWidth, centerY - actionHeight * 0.5f),
            new Vector2(max.X - padX, centerY + actionHeight * 0.5f));
        var dotsWidth = CasinoArt.SeatDotsWidth(room.MaxSeats, scale);
        var dotsLeft = action.Min.X - ColumnGap * scale - dotsWidth;
        CasinoArt.SeatDots(drawList, new Vector2(dotsLeft, centerY), room.Seated, room.MaxSeats, skin.Accent,
            skin.TitleInk with { W = EmptySeatAlpha }, scale);
        var left = origin.X + padX;
        var textWidth = TextWidth(room, width, scale);
        var detail = DetailOf(room);
        var nameHeight = Typography.LineHeight(TextStyles.Headline);
        var detailHeight = PitText.Height(detail, TextStyles.Footnote, textWidth);
        var top = centerY - (nameHeight + LineGap * scale + detailHeight) * 0.5f;
        Typography.Draw(drawList, new Vector2(left, top), Typography.FitText(room.Name, textWidth, TextStyles.Headline),
            CasinoColors.Money, TextStyles.Headline);
        PitText.Draw(drawList, detail, TextStyles.Footnote, skin.BodyInk, left, top + nameHeight + LineGap * scale,
            textWidth, false);
        var seats = Seats(room);
        if (Button.Draw(drawList, action, ActionOf(room), skin.Ink, seats ? ButtonStyle.Prominent : ButtonStyle.Gray,
                enabled: sheet.IsOpen, overlay: true, id: SitIds[tier]))
        {
            picked = room.RoomId;
            sheet.Close();
        }

        return max.Y;
    }
}

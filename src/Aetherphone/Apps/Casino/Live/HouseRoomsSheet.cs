using Aetherphone.Apps.Casino.Stage;
using Aetherphone.Apps.Casino.Strip;
using Aetherphone.Apps.Casino.Tables;
using Aetherphone.Core;
using Aetherphone.Core.Localization;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace Aetherphone.Apps.Casino.Live;

internal readonly record struct HouseRoomRow(
    string RoomId,
    string Name,
    string Detail,
    int Seated,
    int MaxSeats,
    string Action,
    bool Seats,
    bool Rim);

internal sealed class HouseRoomsSheet
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
    private const float DotsGap = 7f;
    private const float RimAlpha = 0.45f;

    private readonly SheetSurface sheet;
    private readonly string gameId;
    private readonly string childId;
    private readonly string quickId;
    private readonly string[] actionIds;
    private readonly Action<Rect> drawBody;

    private HouseRoomRow[] rows = Array.Empty<HouseRoomRow>();
    private string line = string.Empty;
    private string quickLabel = string.Empty;
    private bool quickEnabled;
    private AppSkin skin = null!;
    private string picked = string.Empty;
    private bool quickPicked;

    public HouseRoomsSheet(string id, string gameId, int capacity)
    {
        sheet = new SheetSurface(id);
        this.gameId = gameId;
        childId = "##" + id;
        quickId = id + ".quick";
        actionIds = new string[capacity];
        for (var index = 0; index < capacity; index++)
        {
            actionIds[index] = id + "." + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        drawBody = DrawBody;
    }

    public bool CapturesPointer => sheet.CapturesPointer;

    public void Open() => sheet.Open();

    public void Close() => sheet.Close();

    public void Gate()
    {
        if (sheet.CapturesPointer)
        {
            UiInteract.BlockThisFrame();
        }
    }

    public string TakePick()
    {
        var room = picked;
        picked = string.Empty;
        return room;
    }

    public bool TakeQuickSeat()
    {
        var quick = quickPicked;
        quickPicked = false;
        return quick;
    }

    public void Draw(Rect screen, AppSkin ui, string title, string headerLine, HouseRoomRow[] roomRows,
        string quickAction = "", bool quickActionEnabled = false)
    {
        if (!sheet.CapturesPointer)
        {
            return;
        }

        skin = ui;
        rows = roomRows;
        line = headerLine;
        quickLabel = quickAction;
        quickEnabled = quickActionEnabled;
        var scale = UiScale.Current;
        var width = screen.Width - Metrics.Space.Lg * 2f * scale;
        var share = Math.Clamp(
            (SheetSurface.ChromeHeight() + ContentHeight(width, scale) + Metrics.Space.Lg * 2f * scale) /
            screen.Height, MinShare, MaxShare);
        sheet.Draw(screen, CasinoArt.Sheet(ui), title, share, drawBody);
    }

    private float HeaderHeight(float width, float scale) =>
        MathF.Max(HeaderTile * scale, PitText.Height(line, TextStyles.Subheadline, HeaderTextWidth(width, scale)));

    private static float HeaderTextWidth(float width, float scale) =>
        MathF.Max(1f, width - (HeaderTile + HeaderGap) * scale);

    private float QuickHeight(float scale) => quickLabel.Length > 0 ? (Button.RegularHeight + ListGap) * scale : 0f;

    private float ContentHeight(float width, float scale)
    {
        var height = HeaderHeight(width, scale) + ListGap * scale + QuickHeight(scale);
        for (var index = 0; index < rows.Length; index++)
        {
            height += RowHeight(rows[index], width, scale) + (index > 0 ? RowGap * scale : 0f);
        }

        return height;
    }

    private static float ActionWidth(in HouseRoomRow row) => Button.WidthFor(row.Action, ButtonSize.Regular);

    private static float TextWidth(in HouseRoomRow row, float width, float scale) =>
        MathF.Max(1f, width - RowPadX * 2f * scale - ActionWidth(row) - ColumnGap * scale);

    private static float DotsHeight(float scale) => CasinoArt.SeatDotRadius * 2f * scale;

    private static float TextHeight(in HouseRoomRow row, float textWidth, float scale) =>
        PitText.Height(row.Name, TextStyles.Headline, textWidth) + LineGap * scale
        + PitText.Height(row.Detail, TextStyles.Footnote, textWidth) + DotsGap * scale + DotsHeight(scale);

    private static float RowHeight(in HouseRoomRow row, float width, float scale) =>
        RowPadY * 2f * scale + MathF.Max(Button.RegularHeight * scale,
            TextHeight(row, TextWidth(row, width, scale), scale));

    private void DrawBody(Rect content)
    {
        ImGui.SetCursorScreenPos(content.Min);
        using (ImRaii.Child(childId, content.Size, false, ImGuiWindowFlags.NoBackground))
        {
            var scale = UiScale.Current;
            var drawList = ImGui.GetWindowDrawList();
            var origin = ImGui.GetCursorScreenPos();
            var width = ScrollLayout.NativeScrollContentWidth();
            var top = DrawHeader(drawList, origin, width, scale) + ListGap * scale;
            top = DrawQuickSeat(drawList, new Vector2(origin.X, top), width, scale);
            for (var index = 0; index < rows.Length; index++)
            {
                top = DrawRoom(drawList, rows[index], index, new Vector2(origin.X, top), width, scale)
                    + RowGap * scale;
            }

            ImGui.SetCursorScreenPos(origin);
            ImGui.Dummy(new Vector2(width, MathF.Max(1f, top - RowGap * scale - origin.Y)));
        }
    }

    private float DrawHeader(ImDrawListPtr drawList, Vector2 origin, float width, float scale)
    {
        var height = HeaderHeight(width, scale);
        var tile = HeaderTile * scale;
        CasinoArt.GameTile(drawList, gameId, new Vector2(origin.X + tile * 0.5f, origin.Y + height * 0.5f), tile);
        var textWidth = HeaderTextWidth(width, scale);
        var lineHeight = PitText.Height(line, TextStyles.Subheadline, textWidth);
        PitText.Draw(drawList, line, TextStyles.Subheadline, skin.BodyInk, origin.X + tile + HeaderGap * scale,
            origin.Y + (height - lineHeight) * 0.5f, textWidth, false);
        return origin.Y + height;
    }

    private float DrawQuickSeat(ImDrawListPtr drawList, Vector2 origin, float width, float scale)
    {
        if (quickLabel.Length == 0)
        {
            return origin.Y;
        }

        var button = new Rect(origin, new Vector2(origin.X + width, origin.Y + Button.RegularHeight * scale));
        if (Button.Draw(drawList, button, quickLabel, skin.Ink, ButtonStyle.Prominent,
                enabled: sheet.IsOpen && quickEnabled, overlay: true, id: quickId))
        {
            quickPicked = true;
            sheet.Close();
        }

        return origin.Y + QuickHeight(scale);
    }

    private float DrawRoom(ImDrawListPtr drawList, in HouseRoomRow row, int index, Vector2 origin, float width,
        float scale)
    {
        var height = RowHeight(row, width, scale);
        var max = new Vector2(origin.X + width, origin.Y + height);
        var radius = RowRadius * scale;
        Squircle.Fill(drawList, origin, max, radius, ImGui.GetColorU32(StageText.CapsuleFill));
        Squircle.Stroke(drawList, origin, max, radius,
            ImGui.GetColorU32(skin.TitleInk with { W = RowStrokeAlpha }), MathF.Max(1f, scale));
        if (row.Rim)
        {
            Squircle.Stroke(drawList, origin, max, radius,
                ImGui.GetColorU32(CasinoColors.Money with { W = RimAlpha }), MathF.Max(1f, scale));
        }

        var centerY = origin.Y + height * 0.5f;
        var padX = RowPadX * scale;
        var actionWidth = ActionWidth(row);
        var actionHeight = Button.RegularHeight * scale;
        var action = new Rect(new Vector2(max.X - padX - actionWidth, centerY - actionHeight * 0.5f),
            new Vector2(max.X - padX, centerY + actionHeight * 0.5f));
        var left = origin.X + padX;
        var textWidth = TextWidth(row, width, scale);
        var top = centerY - TextHeight(row, textWidth, scale) * 0.5f;
        top = PitText.Draw(drawList, row.Name, TextStyles.Headline, CasinoColors.Money, left, top, textWidth, false);
        top = PitText.Draw(drawList, row.Detail, TextStyles.Footnote, skin.BodyInk, left, top + LineGap * scale,
            textWidth, false);
        CasinoArt.SeatDots(drawList, new Vector2(left, top + DotsGap * scale + DotsHeight(scale) * 0.5f), row.Seated,
            row.MaxSeats, skin.Accent, skin.TitleInk with { W = EmptySeatAlpha }, scale);
        if (Button.Draw(drawList, action, row.Action, skin.Ink, row.Seats ? ButtonStyle.Prominent : ButtonStyle.Gray,
                enabled: sheet.IsOpen, overlay: true, id: actionIds[index]))
        {
            picked = row.RoomId;
            sheet.Close();
        }

        return max.Y;
    }
}

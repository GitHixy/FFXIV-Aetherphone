using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Lodestone;
using Aetherphone.Core.Media;
using Aetherphone.Core.Theme;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Aethergram;

internal readonly record struct HighlightRowResult(int Opened, int Menu, bool NewTapped)
{
    public static readonly HighlightRowResult None = new(-1, -1, false);
}

internal sealed class HighlightRow
{
    private readonly record struct TileArt(PhoneTheme Theme, SocialInk Ink, RemoteImageCache Images,
        LodestoneService Lodestone);

    public const float Height = 100f;

    private const float TileWidth = 78f;
    private const float Radius = 29f;
    private const float RingGap = 3.5f;
    private const float AvatarLift = 9f;
    private const float LabelGap = 10f;
    private const float EdgeInset = 6f;
    private const float DragSlop = 5f;
    private const float PlusSize = 22f;
    private const double LongPressSeconds = 0.5d;
    private const int NoTile = -2;
    private const int NewTile = -1;

    private float offset;
    private float maxOffset;
    private bool dragging;
    private float dragTravel;
    private float lastMouseX;
    private int pressedTile = NoTile;
    private double pressedAt;
    private bool longPressFired;

    public HighlightRowResult Draw(ImDrawListPtr drawList, Rect row, StoryHighlightDto[] items, bool showNew,
        string newLabel, bool interactive, bool overlay, PhoneTheme theme, SocialInk ink, RemoteImageCache images,
        LodestoneService lodestone)
    {
        var art = new TileArt(theme, ink, images, lodestone);
        var scale = UiScale.Current;
        var tile = TileWidth * scale;
        var slots = items.Length + (showNew ? 1 : 0);
        maxOffset = MathF.Max(0f, tile * slots + EdgeInset * 2f * scale - row.Width);
        var rowHovered = interactive && Hover(row.Min, row.Max, overlay);
        HandleDrag(rowHovered);
        drawList.PushClipRect(row.Min, row.Max, true);
        var x = row.Min.X + EdgeInset * scale - offset;
        var result = HighlightRowResult.None;
        if (showNew)
        {
            if (DrawTile(drawList, new Vector2(x + tile * 0.5f, row.Center.Y), null, newLabel, NewTile, interactive,
                    overlay, in art, scale, ref result))
            {
                result = result with { NewTapped = true };
            }

            x += tile;
        }

        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            if (DrawTile(drawList, new Vector2(x + tile * 0.5f, row.Center.Y), item.CoverUrl, item.Title, index,
                    interactive, overlay, in art, scale, ref result))
            {
                result = result with { Opened = index };
            }

            x += tile;
        }

        drawList.PopClipRect();
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            pressedTile = NoTile;
            longPressFired = false;
        }

        return result;
    }

    private static bool Hover(Vector2 min, Vector2 max, bool overlay) =>
        overlay ? UiInteract.HoverWindowOnly(min, max) : UiInteract.Hover(min, max);

    private void HandleDrag(bool rowHovered)
    {
        if (rowHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            dragging = true;
            dragTravel = 0f;
            lastMouseX = ImGui.GetIO().MousePos.X;
        }

        if (dragging && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var mouseX = ImGui.GetIO().MousePos.X;
            var travel = mouseX - lastMouseX;
            lastMouseX = mouseX;
            dragTravel += MathF.Abs(travel);
            offset = Math.Clamp(offset - travel, 0f, maxOffset);
        }

        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            dragging = false;
        }

        offset = Math.Clamp(offset, 0f, maxOffset);
    }

    private bool DrawTile(ImDrawListPtr drawList, Vector2 slotCenter, string? coverUrl, string label, int tileIndex,
        bool interactive, bool overlay, in TileArt art, float scale, ref HighlightRowResult result)
    {
        var ink = art.Ink;
        var radius = Radius * scale;
        var center = new Vector2(slotCenter.X, slotCenter.Y - AvatarLift * scale);
        var ringRadius = radius + RingGap * scale;
        var min = center - new Vector2(ringRadius, ringRadius);
        var max = center + new Vector2(ringRadius, ringRadius);
        var hovered = interactive && Hover(min, max, overlay);
        drawList.AddCircle(center, ringRadius, ImGui.GetColorU32(hovered ? ink.MutedInk : ink.Hairline), 48,
            1.3f * scale);
        if (tileIndex == NewTile)
        {
            drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(ink.FieldFill), 48);
            PhoneIcon.Draw(drawList, center, PhoneIcons.Plus, ink.TitleInk, PlusSize * scale);
        }
        else
        {
            AvatarView.DrawRemote(drawList, center, radius, art.Theme, label, string.Empty, coverUrl, art.Images,
                art.Lodestone, 0.8f, 48);
        }

        var fitted = Typography.FitText(label, TileWidth * scale - 8f * scale, TextStyles.Footnote);
        var size = Typography.Measure(fitted, TextStyles.Footnote);
        Typography.Draw(drawList,
            new Vector2(center.X - size.X * 0.5f, center.Y + ringRadius + LabelGap * scale - size.Y * 0.5f), fitted,
            ink.BodyInk, TextStyles.Footnote);
        if (!hovered)
        {
            return false;
        }

        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            pressedTile = tileIndex;
            pressedAt = ImGui.GetTime();
            longPressFired = false;
        }

        if (tileIndex >= 0 && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            result = result with { Menu = tileIndex };
        }

        if (tileIndex >= 0 && pressedTile == tileIndex && !longPressFired && dragTravel <= DragSlop
            && ImGui.IsMouseDown(ImGuiMouseButton.Left) && ImGui.GetTime() - pressedAt >= LongPressSeconds)
        {
            longPressFired = true;
            result = result with { Menu = tileIndex };
        }

        return UiInteract.Click(min, max, hovered) && dragTravel <= DragSlop && !longPressFired;
    }
}

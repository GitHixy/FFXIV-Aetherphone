using Aetherphone.Core;
using Aetherphone.Core.Theme;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Windows.Components;

internal static class AltBadge
{
    public const float FeedInset = 10f;
    public const float ComposeInset = 6f;

    private const string Label = "ALT";
    private const string AddLabel = "+ALT";
    private const float PadX = 7f;
    private const float PadY = 3f;

    private static readonly TextStyle LabelStyle = TextStyles.FootnoteEmphasized;
    private static readonly Vector4 Surface = new(0f, 0f, 0f, 0.55f);
    private static readonly Vector4 HoverSurface = new(0f, 0f, 0f, 0.75f);
    private static readonly Vector4 LabelInk = new(1f, 1f, 1f, 0.95f);

    public static bool Draw(ImDrawListPtr drawList, Rect rect) =>
        DrawPill(drawList, rect, Label, Surface, HoverSurface, FeedInset, null);

    public static bool DrawEditable(ImDrawListPtr drawList, Rect rect, bool described, Vector4 accent, string tooltip)
    {
        return described
            ? DrawPill(drawList, rect, Label, accent, Palette.Lighten(accent, 0.12f), ComposeInset, tooltip)
            : DrawPill(drawList, rect, AddLabel, Surface, HoverSurface, ComposeInset, tooltip);
    }

    // The pill sits on top of a tappable photo, so it reserves its rect as an overlay only while the pointer is
    // over it: the photo underneath then ignores the tap, and a feed full of badges never floods the reservations.
    private static bool DrawPill(ImDrawListPtr drawList, Rect rect, string label, Vector4 surface,
        Vector4 hoverSurface, float inset, string? tooltip)
    {
        var scale = UiScale.Current;
        var size = Typography.Measure(label, LabelStyle);
        var padding = new Vector2(PadX * scale, PadY * scale);
        var max = new Vector2(rect.Max.X - inset * scale, rect.Max.Y - inset * scale);
        var min = max - size - padding * 2f;
        var pill = new Rect(min, max);
        var hovered = !UiInteract.InputBlocked && ImGui.IsMouseHoveringRect(min, max)
            && UiInteract.HoverOverlay(pill);
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(hovered ? hoverSurface : surface), (max.Y - min.Y) * 0.5f);
        Typography.Draw(drawList, min + padding, label, LabelInk, LabelStyle);
        if (tooltip is not null)
        {
            HoverTooltip.Show(pill, tooltip, HoverLabelSide.Above);
        }

        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        return UiInteract.Click(min, max, hovered);
    }
}

using Aetherphone.Core;
using Aetherphone.Core.Animation;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Windows.Components;

internal sealed class StridePager
{
    private const float SwipeSlop = 10f;

    private readonly Pager pager = new();
    private bool pressed;
    private Vector2 pressPosition;
    private float wheelCarry;

    public float Value => pager.Value;
    public int Page => pager.Page;
    public bool Dragging => pager.Dragging;
    public bool Owning => pressed || pager.Dragging;

    public void Reset()
    {
        pager.SnapTo(0, 1);
        pressed = false;
        wheelCarry = 0f;
    }

    public void Step(float delta, int count) => pager.Step(delta, count);

    public void AnimateTo(int page, int count) => pager.AnimateTo(page, count);

    public void Drive(string id, Rect row, float stride, int count, float delta, float scale)
    {
        var activated = SwipeClaim.Claim(id, row);
        Wheel(row, count);
        var mouse = ImGui.GetMousePos();
        if (count > 1 && !pressed && activated && !UiInteract.InputBlocked)
        {
            pressed = true;
            pressPosition = mouse;
        }

        if (!pressed)
        {
            return;
        }

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (pager.Dragging)
            {
                pager.Release(stride, count);
                UiInteract.BlockThisFrame();
            }

            pressed = false;
            return;
        }

        var move = mouse - pressPosition;
        if (!pager.Dragging && MathF.Abs(move.X) > SwipeSlop * scale && MathF.Abs(move.X) > MathF.Abs(move.Y))
        {
            pager.Begin(pressPosition.X);
            UiInteract.CancelPendingTap();
        }

        if (!pager.Dragging)
        {
            return;
        }

        pager.Drag(mouse.X, stride, count, delta);
        UiInteract.BlockThisFrame();
    }

    private void Wheel(Rect row, int count)
    {
        if (count <= 1 || pager.Dragging || !UiInteract.Hover(row.Min, row.Max))
        {
            wheelCarry = 0f;
            return;
        }

        var io = ImGui.GetIO();
        wheelCarry += io.MouseWheelH != 0f ? -io.MouseWheelH : io.KeyShift ? -io.MouseWheel : 0f;
        if (MathF.Abs(wheelCarry) < 1f)
        {
            return;
        }

        var step = MathF.Sign(wheelCarry);
        wheelCarry -= step;
        pager.AnimateTo(pager.Page + step, count);
    }
}

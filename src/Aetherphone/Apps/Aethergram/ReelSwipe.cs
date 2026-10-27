using Aetherphone.Core;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Aethergram;

internal enum ReelTouch : byte
{
    None,
    Tap,
    DoubleTap,
}

// A tap waits out the double-tap window, so a double tap likes without also toggling the sound.
internal sealed class ReelSwipe
{
    private const float DragSlop = 8f;
    private const double HoldSeconds = 0.28d;
    private const double DoubleTapSeconds = 0.3d;
    private const float CommitShare = 0.18f;
    private const float FlingSpeed = 650f;
    private const float EdgeResistance = 0.3f;
    private const float VelocitySmoothing = 0.35f;
    private const double WheelCooldownSeconds = 0.35d;

    private bool pressed;
    private bool dragging;
    private bool holding;
    private Vector2 pressOrigin;
    private double pressedAt;
    private float drag;
    private float lastMouseY;
    private float velocity;
    private bool tapPending;
    private double tapAt;
    private double wheelAt = double.MinValue;

    internal bool Dragging => dragging;

    internal bool Holding => holding;

    internal float Drag => drag;

    internal bool Released { get; private set; }

    internal float ReleasedDrag { get; private set; }

    internal void Cancel()
    {
        pressed = false;
        dragging = false;
        holding = false;
        tapPending = false;
        drag = 0f;
        Released = false;
    }

    // A press only starts on bare video, so the rail and the caption keep their own taps.
    internal int Update(Rect surface, bool enabled, bool pressAllowed, bool canNext, bool canPrevious, double now,
        float deltaSeconds, out ReelTouch touch)
    {
        touch = ReelTouch.None;
        Released = false;
        if (!enabled)
        {
            Cancel();
            return 0;
        }

        var scale = UiScale.Current;
        var mouse = ImGui.GetMousePos();
        var hovered = UiInteract.Hover(surface.Min, surface.Max);
        if (!pressed && hovered && pressAllowed && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            pressed = true;
            dragging = false;
            holding = false;
            pressOrigin = mouse;
            pressedAt = now;
            lastMouseY = mouse.Y;
            velocity = 0f;
            drag = 0f;
        }

        var step = pressed ? TrackPress(surface, mouse, scale, canNext, canPrevious, now, deltaSeconds, ref touch) : 0;
        if (tapPending && !pressed && now - tapAt > DoubleTapSeconds)
        {
            tapPending = false;
            touch = ReelTouch.Tap;
        }

        if (step != 0 || pressed || !hovered)
        {
            return step;
        }

        return Wheel(canNext, canPrevious, now);
    }

    private int TrackPress(Rect surface, Vector2 mouse, float scale, bool canNext, bool canPrevious, double now,
        float deltaSeconds, ref ReelTouch touch)
    {
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var travel = mouse.Y - pressOrigin.Y;
            if (!dragging && !holding && MathF.Abs(travel) >= DragSlop * scale)
            {
                dragging = true;
                tapPending = false;
            }

            if (dragging)
            {
                var blocked = (travel < 0f && !canNext) || (travel > 0f && !canPrevious);
                drag = blocked ? travel * EdgeResistance : travel;
                if (deltaSeconds > 0f)
                {
                    var instant = (mouse.Y - lastMouseY) / deltaSeconds;
                    velocity += (instant - velocity) * VelocitySmoothing;
                }

                UiInteract.ReportDragSurface();
            }
            else if (!holding && now - pressedAt >= HoldSeconds)
            {
                holding = true;
                tapPending = false;
            }

            lastMouseY = mouse.Y;
            return 0;
        }

        var step = 0;
        if (dragging)
        {
            var threshold = surface.Height * CommitShare;
            var fling = FlingSpeed * scale;
            if (canNext && (drag < -threshold || velocity < -fling))
            {
                step = 1;
            }
            else if (canPrevious && (drag > threshold || velocity > fling))
            {
                step = -1;
            }

            Released = true;
            ReleasedDrag = drag;
        }
        else if (!holding)
        {
            if (tapPending && now - tapAt <= DoubleTapSeconds)
            {
                tapPending = false;
                touch = ReelTouch.DoubleTap;
            }
            else
            {
                tapPending = true;
                tapAt = now;
            }
        }

        pressed = false;
        dragging = false;
        holding = false;
        drag = 0f;
        return step;
    }

    private int Wheel(bool canNext, bool canPrevious, double now)
    {
        var wheel = ImGui.GetIO().MouseWheel;
        if (wheel == 0f || now - wheelAt < WheelCooldownSeconds)
        {
            return 0;
        }

        var step = wheel < 0f ? (canNext ? 1 : 0) : (canPrevious ? -1 : 0);
        if (step == 0)
        {
            return 0;
        }

        wheelAt = now;
        tapPending = false;
        Released = true;
        ReleasedDrag = 0f;
        return step;
    }
}

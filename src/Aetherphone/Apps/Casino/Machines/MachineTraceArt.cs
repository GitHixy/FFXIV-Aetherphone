using Aetherphone.Apps.Casino.Stage;
using Aetherphone.Apps.Games.Framework;
using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Casino;
using Aetherphone.Core.Localization;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Casino.Machines;

internal static class MachineTraceArt
{
    public const float RevealShare = 0.22f;
    public const float OverviewAlpha = 0.55f;

    private const float GlowWidth = 9f;
    private const float CoreWidth = 3f;
    private const float RestWidth = 1.6f;
    private const float RestAlpha = 0.35f;
    private const float JointRadius = 4f;
    private const float BadgeRadius = 12f;
    private const float BadgeInset = 3f;
    private const float BadgeGap = 5f;
    private const float BadgePadX = 9f;
    private const float BadgeStroke = 1.2f;

    private static readonly Vector4 LineInk = new(1f, 0.80f, 0.30f, 1f);
    private static readonly Vector4 BadgePlate = new(0.05f, 0.03f, 0.08f, 0.9f);
    private static readonly Vector4 BadgeDigit = new(0.10f, 0.06f, 0.02f, 1f);

    public static void Line(ImDrawListPtr drawList, Rect window, float cellWidth, float cellHeight,
        CasinoSlotsWinDto win, float alpha, float reveal, bool badge, float scale)
    {
        var rows = SlotsRules.Paylines[win.Line];
        var winning = Math.Clamp(win.Count, 2, SlotsRules.ReelCount);
        Span<Vector2> points = stackalloc Vector2[SlotsRules.ReelCount];
        var total = 0f;
        for (var reel = 0; reel < SlotsRules.ReelCount; reel++)
        {
            points[reel] = new Vector2(window.Min.X + (reel + 0.5f) * cellWidth,
                window.Min.Y + (rows[reel] + 0.5f) * cellHeight);
            if (reel > 0)
            {
                total += Vector2.Distance(points[reel - 1], points[reel]);
            }
        }

        var remaining = total * Math.Clamp(reveal, 0f, 1f);
        var glow = ImGui.GetColorU32(LineInk with { W = 0.28f * alpha });
        var core = ImGui.GetColorU32(LineInk with { W = alpha });
        var rest = ImGui.GetColorU32(LineInk with { W = RestAlpha * alpha });
        for (var segment = 0; segment < SlotsRules.ReelCount - 1 && remaining > 0f; segment++)
        {
            var from = points[segment];
            var to = points[segment + 1];
            var length = Vector2.Distance(from, to);
            var end = length <= remaining ? to : Vector2.Lerp(from, to, remaining / MathF.Max(0.0001f, length));
            remaining -= length;
            if (segment < winning - 1)
            {
                drawList.AddLine(from, end, glow, GlowWidth * scale);
                drawList.AddLine(from, end, core, CoreWidth * scale);
                continue;
            }

            drawList.AddLine(from, end, rest, RestWidth * scale);
        }

        for (var reel = 0; reel < winning; reel++)
        {
            drawList.AddCircleFilled(points[reel], JointRadius * scale, core, 12);
        }

        if (!badge)
        {
            return;
        }

        var badgeAlpha = alpha * Math.Clamp(reveal / RevealShare, 0f, 1f);
        Badge(drawList, new Vector2(window.Min.X + BadgeInset * scale, points[0].Y), GameNumber.Label(win.Line + 1),
            NumberText.Compact(win.Pay), LineInk, badgeAlpha, scale);
    }

    public static void Badge(ImDrawListPtr drawList, Vector2 leftCenter, string number, string amount, Vector4 tone,
        float alpha, float scale)
    {
        if (alpha <= 0.01f)
        {
            return;
        }

        var radius = BadgeRadius * scale;
        var amountStyle = TextStyles.SubheadlineEmphasized;
        var amountSize = Typography.Measure(amount, amountStyle);
        var discCenter = new Vector2(leftCenter.X + radius, leftCenter.Y);
        var plateMin = new Vector2(discCenter.X, leftCenter.Y - radius);
        var plateMax = new Vector2(discCenter.X + radius + BadgeGap * scale + amountSize.X + BadgePadX * scale,
            leftCenter.Y + radius);
        Squircle.Fill(drawList, plateMin, plateMax, radius, ImGui.GetColorU32(BadgePlate with { W = BadgePlate.W * alpha }));
        Squircle.Stroke(drawList, plateMin, plateMax, radius, ImGui.GetColorU32(tone with { W = 0.7f * alpha }),
            BadgeStroke * scale);
        drawList.AddCircleFilled(discCenter, radius, ImGui.GetColorU32(tone with { W = alpha }), 24);
        Typography.DrawCentered(drawList, discCenter, number, BadgeDigit with { W = alpha },
            TextStyles.FootnoteEmphasized);
        Typography.Draw(drawList, new Vector2(discCenter.X + radius + BadgeGap * scale, leftCenter.Y - amountSize.Y * 0.5f),
            amount, CasinoColors.Money with { W = alpha }, amountStyle);
    }
}

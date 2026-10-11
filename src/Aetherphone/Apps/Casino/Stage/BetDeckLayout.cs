using Aetherphone.Core;

namespace Aetherphone.Apps.Casino.Stage;

internal readonly struct BetDeckLayout
{
    public const float Pad = DeckActions.Pad;
    public const float Gap = DeckActions.Gap;
    public const float RowHeight = DeckActions.SecondaryHeight;
    public const float AmountHeight = RowHeight;
    public const float KnobHeight = RowHeight;
    public const float ActionHeight = DeckActions.PrimaryHeight;
    public const float FieldMinWidth = DeckAmountRow.FieldMinWidth;
    public const float ModeWidth = 112f;
    public const float ModeMinWidth = 84f;
    public const float ModeTrackHeight = 40f;
    public const float GearSize = 40f;
    public const float GearMaxShare = 0.35f;
    public const float PrimaryMinWidth = 120f;
    public const float ClusterMaxShare = 0.6f;

    public readonly Rect Knob;
    public readonly DeckAmountRow Amount;
    public readonly Rect Mode;
    public readonly Rect Gear;
    public readonly Rect Action;
    public readonly bool HasKnob;
    public readonly bool HasAmount;
    public readonly bool HasMode;

    private BetDeckLayout(Rect knob, in DeckAmountRow amount, Rect mode, Rect gear, Rect action, bool hasKnob,
        bool hasAmount, bool hasMode)
    {
        Knob = knob;
        Amount = amount;
        Mode = mode;
        Gear = gear;
        Action = action;
        HasKnob = hasKnob;
        HasAmount = hasAmount;
        HasMode = hasMode;
    }

    public Rect Field => Amount.Field;

    public Rect Half => Amount.Half;

    public Rect Double => Amount.Double;

    public Rect Max => Amount.Max;

    public static float DeckHeightFor(bool knob, bool fixedAmount)
    {
        var height = Pad * 2f + ActionHeight;
        if (!fixedAmount)
        {
            height += AmountHeight + Gap;
        }

        if (knob)
        {
            height += KnobHeight + Gap;
        }

        return height;
    }

    public static BetDeckLayout Compute(Rect deck, bool knob, bool fixedAmount, bool auto, in DeckQuickLabels quick,
        float scale)
    {
        var left = deck.Min.X + Pad * scale;
        var right = MathF.Max(left, deck.Max.X - Pad * scale);
        var top = deck.Min.Y + Pad * scale;
        var gap = Gap * scale;
        var knobRect = new Rect(new Vector2(left, top), new Vector2(left, top));
        if (knob)
        {
            knobRect = new Rect(new Vector2(left, top), new Vector2(right, top + KnobHeight * scale));
            top = knobRect.Max.Y + gap;
        }

        var amount = DeckAmountRow.Empty(new Vector2(left, top));
        if (!fixedAmount)
        {
            var row = new Rect(new Vector2(left, top), new Vector2(right, top + AmountHeight * scale));
            amount = DeckAmountRow.Compute(row, quick, scale);
            top = row.Max.Y + gap;
        }

        var actionTop = MathF.Max(top, deck.Max.Y - (Pad + ActionHeight) * scale);
        var actionRow = new Rect(new Vector2(left, actionTop), new Vector2(right, actionTop + ActionHeight * scale));
        var empty = new Rect(actionRow.Min, actionRow.Min);
        if (!auto)
        {
            return new BetDeckLayout(knobRect, amount, empty, empty, actionRow, knob, !fixedAmount, false);
        }

        ModeCluster(actionRow, scale, out var mode, out var gear, out var action);
        return new BetDeckLayout(knobRect, amount, mode, gear, action, knob, !fixedAmount, true);
    }

    private static void ModeCluster(Rect row, float scale, out Rect mode, out Rect gear, out Rect action)
    {
        var gap = Gap * scale;
        var gearSize = GearSize * scale;
        var desired = ModeWidth * scale + gap + gearSize;
        var minimum = ModeMinWidth * scale + gap + gearSize;
        var room = row.Width - gap - PrimaryMinWidth * scale;
        var cluster = MathF.Min(Math.Clamp(room, minimum, desired), row.Width * ClusterMaxShare);
        cluster = MathF.Max(0f, cluster);
        gearSize = MathF.Min(gearSize, cluster * GearMaxShare);
        var modeWidth = MathF.Max(0f, cluster - gap - gearSize);
        var trackHeight = MathF.Min(ModeTrackHeight * scale, row.Height);
        var center = row.Center.Y;
        mode = new Rect(new Vector2(row.Min.X, center - trackHeight * 0.5f),
            new Vector2(row.Min.X + modeWidth, center + trackHeight * 0.5f));
        var gearLeft = mode.Max.X + gap;
        gear = new Rect(new Vector2(gearLeft, center - gearSize * 0.5f),
            new Vector2(gearLeft + gearSize, center + gearSize * 0.5f));
        var actionLeft = MathF.Min(row.Max.X, gear.Max.X + gap);
        action = new Rect(new Vector2(actionLeft, row.Min.Y), row.Max);
    }
}

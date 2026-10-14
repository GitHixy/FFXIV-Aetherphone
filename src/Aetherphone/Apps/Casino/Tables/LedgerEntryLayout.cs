using Aetherphone.Core;

namespace Aetherphone.Apps.Casino.Tables;

internal static class LedgerEntryLayout
{
    public const float Pad = 14f;
    public const float LineGap = 2f;
    public const float StateGap = 4f;
    public const float ButtonGap = 8f;
    public const float Touch = 44f;

    public static float Height(float titleLine, float partiesLine, float stateLine, bool hasButton, float scale)
    {
        return Pad * 2f * scale + titleLine + LineGap * scale + partiesLine + StateGap * scale + stateLine
            + (hasButton ? ButtonGap * scale + Touch * scale : 0f);
    }

    public static float StateTop(float cardTop, float titleLine, float partiesLine, float scale) =>
        cardTop + Pad * scale + titleLine + LineGap * scale + partiesLine + StateGap * scale;

    public static Rect Button(in Rect card, float width, float scale)
    {
        var pad = Pad * scale;
        var fitted = MathF.Min(width, MathF.Max(0f, card.Width - pad * 2f));
        var bottom = card.Max.Y - pad;
        return new Rect(new Vector2(card.Max.X - pad - fitted, bottom - Touch * scale),
            new Vector2(card.Max.X - pad, bottom));
    }
}

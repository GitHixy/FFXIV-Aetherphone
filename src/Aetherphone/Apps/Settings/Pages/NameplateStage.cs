using Aetherphone.Core;
using Aetherphone.Core.Honorific;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Theme;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Settings.Pages;

internal sealed class NameplateStage
{
    private const float Height = 132f;
    private const float Inset = 18f;
    private const float NameTitleGap = 4f;
    private const float HaloRadius = 1.4f;
    private const string OpenQuote = "《";
    private const string CloseQuote = "》";

    private static readonly Vector4 Top = new(0.05f, 0.09f, 0.13f, 1f);
    private static readonly Vector4 Bottom = new(0.08f, 0.19f, 0.18f, 1f);
    private static readonly Vector4 NameInk = new(0.95f, 0.96f, 1f, 1f);
    private static readonly Vector4 NameHalo = new(0.04f, 0.16f, 0.29f, 1f);

    private string quotedSource = string.Empty;
    private string quotedTitle = string.Empty;

    public void Draw(PhoneTheme theme, float scale, string name, in NameplateTitle title)
    {
        var stage = Paint(theme, scale);
        var maxWidth = stage.Width - Inset * 2f * scale;
        var nameHeight = Typography.LineHeight(TextStyles.Title3);
        var titleHeight = Typography.LineHeight(TextStyles.Subheadline);
        var gap = NameTitleGap * scale;
        var blockTop = stage.Center.Y - (nameHeight + gap + titleHeight) * 0.5f;
        var nameCenterY = title.Prefix ? blockTop + titleHeight + gap + nameHeight * 0.5f : blockTop + nameHeight * 0.5f;
        var titleCenterY = title.Prefix
            ? blockTop + titleHeight * 0.5f
            : blockTop + nameHeight + gap + titleHeight * 0.5f;
        Typography.DrawCenteredHalo(new Vector2(stage.Center.X, nameCenterY), name, NameInk, NameHalo,
            HaloRadius * scale, maxWidth, TextStyles.Title3);
        if (title.IsNone)
        {
            return;
        }

        var look = title.Look;
        var center = new Vector2(stage.Center.X, titleCenterY);
        var quoted = Quoted(title.Text);
        var ink = new Vector4(look.Color, 1f);
        Span<Vector4> halos = stackalloc Vector4[quoted.Length];
        if (TryGradientHalos(halos, look, title.Text.Length))
        {
            Typography.DrawCenteredHalo(center, quoted, ink, halos, HaloRadius * scale, maxWidth,
                TextStyles.SubheadlineEmphasized);
            return;
        }

        var glow = look.Glow ?? look.Color3 ?? NameplatePalette.DarkGlow;
        Typography.DrawCenteredHalo(center, quoted, ink, new Vector4(glow, 1f), HaloRadius * scale, maxWidth,
            TextStyles.SubheadlineEmphasized);
    }

    public static string NameOf(NameplateTitleService titles) =>
        titles.CharacterName.Length > 0 ? titles.CharacterName : Loc.T(L.Nameplate.Example);

    private static bool TryGradientHalos(Span<Vector4> halos, in TitleLook look, int length)
    {
        var tick = NameplateGradients.Tick(Environment.TickCount64);
        var edge = new Vector4(NameplatePalette.DarkGlow, 1f);
        halos.Fill(edge);
        for (var glyph = 0; glyph < length; glyph++)
        {
            if (!NameplateGradients.TryGlyphGlow(look, glyph, length, tick, out var glow))
            {
                return false;
            }

            halos[OpenQuote.Length + glyph] = new Vector4(glow, 1f);
        }

        return length > 0;
    }

    public static void DrawMessage(PhoneTheme theme, float scale, string message)
    {
        var stage = Paint(theme, scale);
        Typography.DrawCenteredHalo(stage.Center, message, NameInk, NameHalo, HaloRadius * scale,
            stage.Width - Inset * 2f * scale, TextStyles.Headline);
    }

    private static Rect Paint(PhoneTheme theme, float scale)
    {
        var card = GroupCard.Begin(theme, Height);
        var stage = card.Bounds;
        card.End();
        Squircle.FillVerticalGradient(ImGui.GetWindowDrawList(), stage.Min, stage.Max,
            Metrics.Radius.Grouped * scale, ImGui.GetColorU32(Top), ImGui.GetColorU32(Bottom));
        return stage;
    }

    private string Quoted(string text)
    {
        if (!ReferenceEquals(text, quotedSource))
        {
            quotedSource = text;
            quotedTitle = string.Concat(OpenQuote, text, CloseQuote);
        }

        return quotedTitle;
    }
}

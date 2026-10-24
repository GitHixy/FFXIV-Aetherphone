using Aetherphone.Core;
using Aetherphone.Core.Apps;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Theme;
using Aetherphone.Core.Wallpapers;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace Aetherphone.Apps.Settings.Pages;

internal sealed class LiquidGlassPage : ISettingsPage
{
    private const float CardGap = Metrics.Space.Xl;
    private const float SliderRowHeight = 38f;
    private const float CaptionBottom = Metrics.Space.Md;
    private const float CaptionWidthFraction = 0.45f;
    private const float AmountEpsilon = 0.001f;
    private readonly Configuration configuration;
    private readonly WallpaperLibrary wallpapers;

    public LiquidGlassPage(Configuration configuration, WallpaperLibrary wallpapers)
    {
        this.configuration = configuration;
        this.wallpapers = wallpapers;
    }

    public string Title => Loc.T(L.Settings.LiquidGlass);
    public string Summary => string.Empty;
    public FontAwesomeIcon Icon => FontAwesomeIcon.Tint;
    public Vector4 Tint => DisplayPage.PageTint;

    public void Draw(in PhoneContext context, Rect body)
    {
        var scale = UiScale.Current;
        var theme = context.Theme;
        using (AppSurface.Begin(body))
        {
            ImGui.Dummy(new Vector2(0f, Metrics.Space.Md * scale));
            DrawPreview(theme, scale);
            ImGui.Dummy(new Vector2(0f, CardGap * scale));
            DrawRealGlassCard(theme);
            ImGui.Dummy(new Vector2(0f, CardGap * scale));
            DrawAmountCard(theme, scale);
            ImGui.Dummy(new Vector2(0f, Metrics.Space.Md * scale));
        }
    }

    private void DrawPreview(PhoneTheme theme, float scale)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var size = new Vector2(width, width * LiquidGlassPreview.HeightFraction);
        ImGui.Dummy(size);
        LiquidGlassPreview.Draw(ImGui.GetWindowDrawList(), new Rect(origin, origin + size), theme, wallpapers, scale);
    }

    private void DrawAmountCard(PhoneTheme theme, float scale)
    {
        var cardHeight = SliderRowHeight + Typography.LineHeight(TextStyles.Footnote) / scale + CaptionBottom;
        var card = GroupCard.Begin(theme, cardHeight);
        var row = card.NextRow(cardHeight);
        var sliderRow = new Rect(row.Min, new Vector2(row.Max.X, row.Min.Y + SliderRowHeight * scale));
        var slider = Slider.Draw("settings.glassAmount", sliderRow, configuration.GlassAmount, theme, 0f, 0f);
        DrawCaptions(sliderRow, theme);
        card.End();
        SettingsSection.Hint(Loc.T(L.Settings.LiquidGlassHint), theme);
        if ((slider.Dragging || slider.Released) && MathF.Abs(slider.Value - configuration.GlassAmount) > AmountEpsilon)
        {
            configuration.GlassAmount = slider.Value;
        }

        if (slider.Released)
        {
            configuration.Save();
        }
    }

    private static void DrawCaptions(Rect sliderRow, PhoneTheme theme)
    {
        var drawList = ImGui.GetWindowDrawList();
        var maxWidth = sliderRow.Width * CaptionWidthFraction;
        var clear = Typography.FitText(Loc.T(L.Settings.LiquidGlassClear), maxWidth, TextStyles.Footnote);
        var tinted = Typography.FitText(Loc.T(L.Settings.LiquidGlassTinted), maxWidth, TextStyles.Footnote);
        var tintedWidth = Typography.Measure(tinted, TextStyles.Footnote).X;
        Typography.Draw(drawList, new Vector2(sliderRow.Min.X, sliderRow.Max.Y), clear, theme.TextMuted,
            TextStyles.Footnote);
        Typography.Draw(drawList, new Vector2(sliderRow.Max.X - tintedWidth, sliderRow.Max.Y), tinted,
            theme.TextMuted, TextStyles.Footnote);
    }

    private void DrawRealGlassCard(PhoneTheme theme)
    {
        var card = GroupCard.Begin(theme, 1);
        var realGlass = SettingsRow.Bool(card.NextRow(), Loc.T(L.Settings.RealGlass), configuration.RealGlass, theme);
        card.End();
        SettingsSection.Hint(Loc.T(L.Settings.RealGlassHint), theme);
        if (realGlass == configuration.RealGlass)
        {
            return;
        }

        configuration.RealGlass = realGlass;
        configuration.Save();
    }
}

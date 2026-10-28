using Aetherphone.Core;
using Aetherphone.Core.Animation;
using Aetherphone.Core.Apps;
using Aetherphone.Core.Changelog;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Notifications;
using Aetherphone.Core.Theme;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace Aetherphone.Windows.Components;

internal sealed class WhatsNewOverlay
{
    private const ImGuiWindowFlags OverlayFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
                                                  ImGuiWindowFlags.NoBackground;

    private const string SettingsAppId = "settings";
    private const float MaxDim = 0.5f;
    private const float PanelRounding = 28f;
    private const float PanelTopGap = 8f;
    private const float ContentClipInset = 6f;
    private const float WashHeight = 300f;
    private const float WashAlpha = 0.16f;
    private const float HeroTop = 46f;
    private const float SideGutter = 28f;
    private const float IconSize = 46f;
    private const float IconGap = 14f;
    private const float RowGap = 22f;
    private const float RowWashOutset = 8f;
    private const float RowWashRadius = 16f;
    private const float RowWashAlpha = 0.06f;
    private const float ChevronAlpha = 0.45f;
    private const float EdgeFade = 28f;
    private const float FooterBottomInset = 22f;
    private const float FooterGap = 6f;
    private const float RevealDelay = 0.12f;
    private const float RevealStagger = 0.07f;
    private const float RevealRise = 14f;
    private const int RevealStaggerCap = 8;
    private const int HeroBeats = 2;

    private readonly WhatsNewService service;
    private readonly INavigator navigation;
    private readonly SettingsLauncher settingsLauncher;
    private Spring reveal;
    private bool wasActive;
    private bool scrollTopPending;
    private int openedFrame;
    private float elapsed;
    private LanguageInfo? eyebrowLanguage;
    private string eyebrow = string.Empty;

    public WhatsNewOverlay(WhatsNewService service, INavigator navigation, SettingsLauncher settingsLauncher)
    {
        this.service = service;
        this.navigation = navigation;
        this.settingsLauncher = settingsLauncher;
    }

    public bool Captures => service.Active;

    public bool Due => service.Due;

    public void Present() => service.Present();

    public void Dismiss() => service.Dismiss();

    public void Draw(Rect screen, PhoneTheme theme)
    {
        var active = service.Active;
        if (active && !wasActive)
        {
            BeginReveal();
        }
        else if (!active && wasActive)
        {
            UiFeedback.Play(UiSound.SheetDismiss);
        }

        wasActive = active;
        var delta = MathF.Min(ImGui.GetIO().DeltaTime, TransitionTiming.MaxFrameSeconds);
        reveal.Step(active ? 1f : 0f, Motion.Sheet, delta);
        if (!active && reveal.IsResting(0f, 0.001f, 0.005f))
        {
            reveal.SnapTo(0f);
            return;
        }

        if (active)
        {
            elapsed += delta;
        }

        var presence = Math.Clamp(reveal.Value, 0f, 1f);
        var interactive = active && presence > 0.5f && ImGui.GetFrameCount() != openedFrame;
        ImGui.SetCursorScreenPos(screen.Min);
        using (ImRaii.Child("##whatsNewOverlay", screen.Size, false, OverlayFlags))
        {
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(screen.Min, screen.Max,
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, MaxDim * presence)));
            DrawPanel(PanelRect(screen, theme, presence), theme, interactive);
        }
    }

    private void BeginReveal()
    {
        openedFrame = ImGui.GetFrameCount();
        elapsed = 0f;
        scrollTopPending = true;
        UiFeedback.Play(UiSound.SheetPresent);
    }

    private static Rect PanelRect(Rect screen, PhoneTheme theme, float presence)
    {
        var scale = UiScale.Current;
        var restTop = screen.Min.Y + (theme.TopZoneHeight + PanelTopGap) * scale;
        var height = screen.Max.Y - restTop;
        var top = restTop + height * (1f - presence);
        return new Rect(new Vector2(screen.Min.X, top), new Vector2(screen.Max.X, top + height));
    }

    private void DrawPanel(Rect panel, PhoneTheme theme, bool interactive)
    {
        var scale = UiScale.Current;
        var drawList = ImGui.GetWindowDrawList();
        var rounding = PanelRounding * scale;
        var surface = theme.AppBackground with { W = 1f };
        var accent = theme.Accent with { W = 1f };
        Squircle.Fill(drawList, panel.Min, panel.Max, rounding, ImGui.GetColorU32(surface));
        Squircle.FillVerticalGradient(drawList, panel.Min,
            new Vector2(panel.Max.X, panel.Min.Y + WashHeight * scale), rounding,
            ImGui.GetColorU32(accent with { W = WashAlpha }), ImGui.GetColorU32(accent with { W = 0f }));
        Squircle.Stroke(drawList, panel.Min, panel.Max, rounding, ImGui.GetColorU32(theme.Hairline),
            Metrics.Stroke.Hairline);

        var footerHeight = (Button.LargeHeight + FooterGap + Button.RegularHeight + FooterBottomInset) * scale;
        var footerTop = panel.Max.Y - footerHeight;
        var listRect = new Rect(new Vector2(panel.Min.X, panel.Min.Y + ContentClipInset * scale),
            new Vector2(panel.Max.X, footerTop - Metrics.Space.Md * scale));
        DrawList(listRect, theme, accent, surface, interactive);
        DrawFooter(drawList, panel, footerTop, theme, accent, interactive);
    }

    private void DrawList(Rect listRect, PhoneTheme theme, Vector4 accent, Vector4 surface, bool interactive)
    {
        if (listRect.Height <= 0f)
        {
            return;
        }

        var scale = UiScale.Current;
        var listKey = ImGui.GetID("##whatsNewList");
        ImGui.SetCursorScreenPos(listRect.Min);
        using (ImRaii.Child("##whatsNewList", listRect.Size, false,
                   DragScrollHost.ScrollFlags(ImGuiWindowFlags.NoBackground)))
        {
            var scrollSurface = DragScrollHost.Begin(listKey);
            if (scrollTopPending)
            {
                scrollSurface.JumpToTop();
                scrollTopPending = false;
            }

            var drawList = ImGui.GetWindowDrawList();
            var origin = ImGui.GetCursorScreenPos();
            var fullWidth = ScrollLayout.StableContentWidth();
            var gutter = SideGutter * scale;
            var left = origin.X + gutter;
            var width = fullWidth - gutter * 2f;
            var y = DrawHero(theme, accent, origin.X + fullWidth * 0.5f,
                origin.Y + (HeroTop - ContentClipInset) * scale, width);
            y += Metrics.Space.Xxl * scale;
            var items = service.Items;
            for (var index = 0; index < items.Length; index++)
            {
                if (index > 0)
                {
                    y += RowGap * scale;
                }

                y += DrawRow(drawList, items[index], index, left, y, width, theme, interactive);
            }

            y += Metrics.Space.Lg * scale;
            ImGui.SetCursorScreenPos(origin);
            ImGui.Dummy(new Vector2(fullWidth, y - origin.Y));
            DrawBottomFade(drawList, listRect, surface);
        }
    }

    private float DrawHero(PhoneTheme theme, Vector4 accent, float centerX, float top, float width)
    {
        var scale = UiScale.Current;
        var trim = ImGui.GetStyle().ItemSpacing.Y;
        var eyebrowReveal = Reveal(0);
        var eyebrowInk = ControlInk.From(theme).WithAccent(accent).AccentInk;
        var y = top;
        y += Typography.DrawWrappedCentered(new Vector2(centerX, y + Rise(eyebrowReveal)), Eyebrow(),
            eyebrowInk with { W = eyebrowReveal }, TextStyles.FootnoteEmphasized, width) - trim;
        y += Metrics.Space.Xxs * scale;
        var titleReveal = Reveal(1);
        y += Typography.DrawWrappedCentered(new Vector2(centerX, y + Rise(titleReveal)),
            Loc.T(L.Settings.ChangelogHero), theme.TextStrong with { W = titleReveal }, TextStyles.LargeTitle,
            width) - trim;
        return y;
    }

    private float DrawRow(ImDrawListPtr drawList, in ChangelogSpotlight item, int order, float left, float top,
        float width, PhoneTheme theme, bool interactive)
    {
        var scale = UiScale.Current;
        var trim = ImGui.GetStyle().ItemSpacing.Y;
        var alpha = Reveal(order + HeroBeats);
        var rowTop = top + Rise(alpha);
        var icon = IconSize * scale;
        var opens = navigation.CanOpen(item.AppId);
        var textLeft = left + icon + IconGap * scale;
        var textRight = left + width - (opens ? SettingsRow.ChevronReserve(scale) : 0f);
        var textWidth = MathF.Max(1f, textRight - textLeft);
        var title = Loc.T(item.Title);
        var body = Loc.T(item.Body);
        var titleHeight = Typography.MeasureWrappedBlock(title, TextStyles.Headline, textWidth).Y - trim;
        var bodyGap = Metrics.Space.Xxs * scale;
        var bodyHeight = Typography.MeasureWrappedBlock(body, TextStyles.Subheadline, textWidth).Y - trim;
        var textHeight = titleHeight + bodyGap + bodyHeight;
        var height = MathF.Max(icon, textHeight);

        var outset = RowWashOutset * scale;
        var hitMin = new Vector2(left - outset, rowTop - outset);
        var hitMax = new Vector2(left + width + outset, rowTop + height + outset);
        var hovered = interactive && opens && UiInteract.Hover(hitMin, hitMax);
        if (hovered)
        {
            var pressed = ImGui.IsMouseDown(ImGuiMouseButton.Left);
            Squircle.Fill(drawList, hitMin, hitMax, RowWashRadius * scale,
                ImGui.GetColorU32(theme.TextStrong with { W = RowWashAlpha * (pressed ? 2f : 1f) }));
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        var middle = rowTop + height * 0.5f;
        DrawIcon(drawList, item.AppId, new Vector2(left + icon * 0.5f, middle), icon, alpha, scale);
        var textTop = rowTop + (height - textHeight) * 0.5f;
        Typography.DrawWrappedLeft(new Vector2(textLeft, textTop), title, theme.TextStrong with { W = alpha },
            TextStyles.Headline, textWidth);
        Typography.DrawWrappedLeft(new Vector2(textLeft, textTop + titleHeight + bodyGap), body,
            theme.TextMuted with { W = alpha }, TextStyles.Subheadline, textWidth);
        if (!opens)
        {
            return height;
        }

        SettingsRow.DrawChevron(drawList, new Vector2(left + width, middle), scale,
            theme.TextMuted with { W = ChevronAlpha * alpha });
        if (UiInteract.Click(hitMin, hitMax, hovered))
        {
            service.Dismiss();
            navigation.Open(item.AppId);
        }

        return height;
    }

    private static void DrawIcon(ImDrawListPtr drawList, string appId, Vector2 center, float size, float alpha,
        float scale)
    {
        var half = new Vector2(size * 0.5f, size * 0.5f);
        var accent = AppAccents.For(appId);
        if (AppIconTile.TryDraw(drawList, appId, accent, center - half, center + half,
                size * Metrics.Radius.HomeTileFactor, alpha, true, scale))
        {
            return;
        }

        IconTile.DrawApp(drawList, appId, center, size, IconTile.Surface(accent), alpha);
    }

    private static void DrawBottomFade(ImDrawListPtr drawList, Rect listRect, Vector4 surface)
    {
        var fade = EdgeFade * UiScale.Current;
        var remaining = ImGui.GetScrollMaxY() - ImGui.GetScrollY();
        var strength = Math.Clamp(remaining / fade, 0f, 1f);
        if (strength <= 0.001f)
        {
            return;
        }

        var clear = ImGui.GetColorU32(surface with { W = 0f });
        var solid = ImGui.GetColorU32(surface with { W = strength });
        drawList.AddRectFilledMultiColor(new Vector2(listRect.Min.X, listRect.Max.Y - fade), listRect.Max, clear,
            clear, solid, solid);
    }

    private void DrawFooter(ImDrawListPtr drawList, Rect panel, float footerTop, PhoneTheme theme, Vector4 accent,
        bool interactive)
    {
        var scale = UiScale.Current;
        var gutter = SideGutter * scale;
        var opacity = Reveal(HeroBeats + service.Items.Length);
        var ink = ControlInk.From(theme).WithAccent(accent);
        var continueRect = new Rect(new Vector2(panel.Min.X + gutter, footerTop),
            new Vector2(panel.Max.X - gutter, footerTop + Button.LargeHeight * scale));
        if (Button.Draw(drawList, continueRect, Loc.T(L.Changelog.WhatsNewContinue), ink,
                id: "whatsNew.continue", opacity: opacity) && interactive)
        {
            service.Dismiss();
        }

        var seeAllTop = continueRect.Max.Y + FooterGap * scale;
        var seeAllRect = new Rect(new Vector2(panel.Min.X + gutter, seeAllTop),
            new Vector2(panel.Max.X - gutter, seeAllTop + Button.RegularHeight * scale));
        if (!Button.Draw(drawList, seeAllRect, Loc.T(L.Changelog.WhatsNewSeeAll), ink, ButtonStyle.Plain,
                id: "whatsNew.seeAll", opacity: opacity) || !interactive)
        {
            return;
        }

        service.Dismiss();
        settingsLauncher.Request(SettingsPageKind.Changelog);
        navigation.Open(SettingsAppId);
    }

    private string Eyebrow()
    {
        if (ReferenceEquals(eyebrowLanguage, Loc.Current))
        {
            return eyebrow;
        }

        eyebrowLanguage = Loc.Current;
        eyebrow = Loc.Culture.TextInfo.ToUpper(Loc.T(L.Changelog.VersionTitle,
            ChangelogData.Entries[0].ShortVersion));
        return eyebrow;
    }

    private float Reveal(int order) =>
        Spring.Settle(elapsed - RevealDelay - Math.Min(order, RevealStaggerCap) * RevealStagger, Motion.Sheet);

    private static float Rise(float revealAmount) => (1f - revealAmount) * RevealRise * UiScale.Current;
}

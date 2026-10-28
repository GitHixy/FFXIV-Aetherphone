using Aetherphone.Core.Animation;
using Aetherphone.Core.Apps;
using Aetherphone.Core.Onboarding;
using Aetherphone.Core.Theme;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Core.Shell;

internal readonly struct ShellOverlayState
{
    public readonly bool SetupActive;
    public readonly bool Confirming;
    public readonly bool IslandCaptures;
    public readonly bool Busy;
    public readonly bool ShieldBase;

    public ShellOverlayState(bool setupActive, bool confirming, bool islandCaptures, bool busy, bool shieldBase)
    {
        SetupActive = setupActive;
        Confirming = confirming;
        IslandCaptures = islandCaptures;
        Busy = busy;
        ShieldBase = shieldBase;
    }
}

internal sealed class ShellOverlayCoordinator
{
    private const float WhatsNewSettleSeconds = 0.45f;
    private readonly Configuration configuration;
    private readonly LoadingScreen loading;
    private readonly NavigationStack navigation;
    private readonly ControlCenter controlCenter;
    private readonly AppSwitcher appSwitcher;
    private readonly NotificationBanner banner;
    private readonly DynamicIsland island;
    private readonly RateLimitPill rateLimitPill;
    private readonly ShortcutRunPill shortcutPill;
    private readonly CoinEarnPill coinPill;
    private readonly CoinEarnFloats coinFloats;
    private readonly IncomingCallOverlay incomingOverlay;
    private readonly AlarmOverlay alarmOverlay;
    private readonly BanOverlay banOverlay;
    private readonly ConfirmOverlay confirmOverlay;
    private readonly ReportOverlay reportOverlay;
    private readonly ShareSheet shareSheet;
    private readonly ConductGateOverlay conductOverlay;
    private readonly EncryptionHelpOverlay encryptionHelpOverlay;
    private readonly WhatsNewOverlay whatsNewOverlay;
    private readonly OnboardingDirector director;
    private readonly SetupOverlay setup;
    private bool bannerWasVisible;
    private float whatsNewSettle;

    public ShellOverlayCoordinator(Configuration configuration, LoadingScreen loading, NavigationStack navigation,
        ControlCenter controlCenter, AppSwitcher appSwitcher, NotificationBanner banner, DynamicIsland island,
        RateLimitPill rateLimitPill, ShortcutRunPill shortcutPill, CoinEarnPill coinPill, CoinEarnFloats coinFloats,
        IncomingCallOverlay incomingOverlay, AlarmOverlay alarmOverlay, BanOverlay banOverlay,
        ConfirmOverlay confirmOverlay, ReportOverlay reportOverlay, ShareSheet shareSheet,
        ConductGateOverlay conductOverlay, EncryptionHelpOverlay encryptionHelpOverlay,
        WhatsNewOverlay whatsNewOverlay, OnboardingDirector director, SetupOverlay setup)
    {
        this.coinPill = coinPill;
        this.coinFloats = coinFloats;
        this.configuration = configuration;
        this.loading = loading;
        this.navigation = navigation;
        this.controlCenter = controlCenter;
        this.appSwitcher = appSwitcher;
        this.banner = banner;
        this.island = island;
        this.rateLimitPill = rateLimitPill;
        this.shortcutPill = shortcutPill;
        this.incomingOverlay = incomingOverlay;
        this.alarmOverlay = alarmOverlay;
        this.banOverlay = banOverlay;
        this.confirmOverlay = confirmOverlay;
        this.reportOverlay = reportOverlay;
        this.shareSheet = shareSheet;
        this.conductOverlay = conductOverlay;
        this.encryptionHelpOverlay = encryptionHelpOverlay;
        this.whatsNewOverlay = whatsNewOverlay;
        this.director = director;
        this.setup = setup;
    }

    public ShellOverlayState Assess(Rect screen)
    {
        var banNotice = !loading.IsActive && banOverlay.IsActive;
        var conductActive = !loading.IsActive && !banNotice && conductOverlay.Captures;
        var helpActive = !loading.IsActive && !banNotice && encryptionHelpOverlay.Captures;
        var setupActive = setup.IsActive;
        var confirming = !loading.IsActive &&
                         (confirmOverlay.CapturesPointer || reportOverlay.CapturesPointer ||
                          shareSheet.CapturesPointer);
        var controlCenterCaptures = !loading.IsActive && controlCenter.CapturesPointer;
        var switcherCaptures = !loading.IsActive && appSwitcher.CapturesPointer;
        var overlaysCapture = (controlCenterCaptures && !director.WantsControlCenter) || switcherCaptures;
        var ringing = !loading.IsActive && (incomingOverlay.IsRinging || alarmOverlay.IsRinging);
        var calm = !loading.IsActive && !setupActive && !banNotice && !conductActive && !helpActive && !confirming &&
                   !ringing && !controlCenter.IsActive && !appSwitcher.IsActive && !director.IsRunning &&
                   navigation.AtHome && !navigation.IsTransitioning;
        PresentWhatsNewWhenSettled(calm);
        var whatsNewActive = !loading.IsActive && whatsNewOverlay.Captures;
        var islandCaptures = !loading.IsActive && !controlCenterCaptures && !switcherCaptures && !ringing &&
                             !confirming && !setupActive && !conductActive && !helpActive && !whatsNewActive &&
                             !banNotice &&
                             !DragScrollHost.AnyDragging &&
                             (island.CapturesPointer() ||
                              (!director.CapturesPointer &&
                               (banner.CapturesPointer(screen) || shortcutPill.CapturesPointer())));
        var busy = loading.IsActive || overlaysCapture || ringing || confirming || navigation.IsTransitioning ||
                   setupActive || banNotice || conductActive || helpActive || whatsNewActive;
        var shieldBase = loading.IsActive || islandCaptures || controlCenterCaptures || switcherCaptures || ringing ||
                         confirming || setupActive || banNotice || conductActive || helpActive || whatsNewActive;
        return new ShellOverlayState(setupActive, confirming, islandCaptures, busy, shieldBase);
    }

    private void PresentWhatsNewWhenSettled(bool calm)
    {
        if (!calm || !whatsNewOverlay.Due)
        {
            whatsNewSettle = 0f;
            return;
        }

        whatsNewSettle += MathF.Min(ImGui.GetIO().DeltaTime, TransitionTiming.MaxFrameSeconds);
        if (whatsNewSettle < WhatsNewSettleSeconds)
        {
            return;
        }

        whatsNewSettle = 0f;
        whatsNewOverlay.Present();
    }

    private void HandleEscape()
    {
        if (banOverlay.IsActive || conductOverlay.Captures || setup.IsActive || director.CapturesPointer)
        {
            return;
        }

        var helpActive = encryptionHelpOverlay.Captures;
        var whatsNewActive = whatsNewOverlay.Captures;
        if (!helpActive && !whatsNewActive && !confirmOverlay.CapturesPointer && !reportOverlay.CapturesPointer &&
            !shareSheet.CapturesPointer && !controlCenter.IsActive && !appSwitcher.IsActive)
        {
            return;
        }

        if (UiInteract.WindowFocused)
        {
            ImGui.SetNextFrameWantCaptureKeyboard(true);
        }

        if (!ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            return;
        }

        if (confirmOverlay.CapturesPointer)
        {
            confirmOverlay.CancelActive();
            return;
        }

        if (reportOverlay.CapturesPointer)
        {
            reportOverlay.Dismiss();
            return;
        }

        if (shareSheet.CapturesPointer)
        {
            shareSheet.Dismiss();
            return;
        }

        if (helpActive)
        {
            encryptionHelpOverlay.Dismiss();
            return;
        }

        if (whatsNewActive)
        {
            whatsNewOverlay.Dismiss();
            return;
        }

        if (controlCenter.IsActive)
        {
            controlCenter.Dismiss();
            return;
        }

        appSwitcher.Dismiss();
    }

    public void DrawOverlays(in ChassisGeometry chassis, PhoneTheme theme, float delta, in ShellOverlayState state,
        bool seals, bool homeEditing)
    {
        var screen = chassis.Screen;
        if (state.SetupActive)
        {
            setup.Draw(screen, delta, !loading.IsActive && !state.Confirming && !banOverlay.IsActive);
        }

        if (loading.IsActive)
        {
            loading.Draw(screen, theme);
            SealScreen(chassis, theme, seals);
            return;
        }

        if (state.SetupActive)
        {
            ShellToast.Draw(screen, theme);
            banOverlay.Draw(screen, theme);
            confirmOverlay.Draw(screen, theme);
            SealScreen(chassis, theme, seals);
            return;
        }

        if (banner.IsVisible && !bannerWasVisible)
        {
            island.Pulse();
        }

        bannerWasVisible = banner.IsVisible;
        if (!director.CapturesPointer)
        {
            if (!controlCenter.IsActive && !appSwitcher.IsActive)
            {
                banner.Draw(screen, theme);
                island.Draw(screen, theme, navigation, navigation.Current?.Id);
                shortcutPill.Draw(screen, theme, delta, banner.IsVisible);
                coinPill.Draw(screen, theme, delta, banner.IsVisible || shortcutPill.IsVisible);
                if (!banner.IsVisible && !shortcutPill.IsVisible && !coinPill.IsVisible)
                {
                    rateLimitPill.Draw(screen, theme, delta);
                }
            }

            alarmOverlay.Draw(screen, theme);
            incomingOverlay.Draw(screen, theme);
        }

        if (GuideIntents.Consume(TourRegistry.ControlCenterOpenIntent))
        {
            controlCenter.Open();
        }

        if (GuideIntents.Consume(TourRegistry.ControlCenterCloseIntent))
        {
            controlCenter.Dismiss();
        }

        var landscapeHeld = navigation.Current is { } landscapeApp && AppLandscape.Held(landscapeApp.Id);
        if (landscapeHeld && controlCenter.IsActive)
        {
            controlCenter.Dismiss();
        }

        if (landscapeHeld && appSwitcher.IsActive)
        {
            appSwitcher.Dismiss();
        }

        HandleEscape();

        var whatsNewActive = whatsNewOverlay.Captures;
        appSwitcher.DrawOverlay(screen, theme, delta,
            !director.CapturesPointer && !state.Confirming && !whatsNewActive);
        controlCenter.Draw(screen, theme, delta,
            !navigation.IsTransitioning && !director.CapturesPointer && !state.IslandCaptures && !whatsNewActive &&
            !banOverlay.IsActive && navigation.Current?.Id != "camera" && !landscapeHeld &&
            !appSwitcher.IsActive && !homeEditing,
            !director.CapturesPointer);

        ShellToast.Draw(screen, theme);
        shareSheet.Draw(screen, theme);
        reportOverlay.Draw(screen, theme);
        confirmOverlay.Draw(screen, theme);
        director.Draw(screen, theme);
        whatsNewOverlay.Draw(screen, theme);
        conductOverlay.Draw(screen, theme);
        encryptionHelpOverlay.Draw(screen, theme);
        banOverlay.Draw(screen, theme);
        coinFloats.Draw(screen, theme, delta);
        SealScreen(chassis, theme, seals);
    }

    private void SealScreen(in ChassisGeometry chassis, PhoneTheme theme, bool seals)
    {
        if (!seals)
        {
            return;
        }

        DeviceChrome.SealScreen(chassis, theme, configuration.ScreenBrightness);
    }
}

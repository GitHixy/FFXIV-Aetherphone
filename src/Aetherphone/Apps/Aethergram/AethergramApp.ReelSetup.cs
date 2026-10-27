using Aetherphone.Core;
using Aetherphone.Core.Aethernet;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Theme;
using Aetherphone.Core.Video;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace Aetherphone.Apps.Aethergram;

internal sealed partial class AethergramApp
{
    private const float ReelSetupTile = 64f;
    private const float ReelSetupCardHeight = 74f;

    private readonly StoreWork reelSetupWork = new("Aethergram reels setup");
    private bool reelSetupDismissed;
    private bool reelSetupChecked;
    private long reelInstallLabelBytes = -1;
    private string reelInstallLabel = string.Empty;

    private bool ReelSetupNeeded => reelPlayback.LibraryMissing && !reelSetupDismissed;

    private void DrawReelSetup(Rect area)
    {
        var scale = UiScale.Current;
        var dependencies = reelPlayback.Dependencies;
        if (!reelSetupChecked)
        {
            reelSetupChecked = true;
            reelSetupWork.Run("check reel components",
                async token => await dependencies.CheckReelSizesAsync(token).ConfigureAwait(false));
        }

        var margin = Metrics.Space.Xl * scale;
        var content = new Rect(new Vector2(area.Min.X + margin, area.Min.Y + margin),
            new Vector2(area.Max.X - margin, area.Max.Y - margin));
        var drawList = ImGui.GetWindowDrawList();
        var tileSize = ReelSetupTile * scale;
        var tileMin = new Vector2(content.Center.X - tileSize * 0.5f, content.Min.Y + Metrics.Space.Xl * scale);
        IconTile.FillShaded(drawList, tileMin, tileMin + new Vector2(tileSize, tileSize),
            tileSize * Metrics.Radius.TileFactor, IconTile.Surface(ui.Accent));
        ProgressRing.CenterIcon(drawList, tileMin + new Vector2(tileSize * 0.5f, tileSize * 0.5f),
            FontAwesomeIcon.Film, AccentRing.Ink, tileSize * 0.5f);

        var titleY = tileMin.Y + tileSize + Metrics.Space.Lg * scale;
        var titleHeight = Typography.LineHeight(TextStyles.Title2);
        Typography.DrawCentered(drawList, new Vector2(content.Center.X, titleY + titleHeight * 0.5f),
            Loc.T(L.Aethergram.ReelSetupTitle), ui.TitleInk, TextStyles.Title2);
        var bodyY = titleY + titleHeight + Metrics.Space.Sm * scale;
        var bodyHeight = Typography.DrawWrappedCentered(new Vector2(content.Center.X, bodyY),
            Loc.T(L.Aethergram.ReelSetupBody), ui.MutedInk, TextStyles.Subheadline, content.Width);
        var cardTop = bodyY + bodyHeight + Metrics.Space.Xl * scale;
        DependencySetup.Card(ui, theme, new Rect(new Vector2(content.Min.X, cardTop),
                new Vector2(content.Max.X, cardTop + ReelSetupCardHeight * scale)), dependencies.VideoLibrary,
            L.AetherStream.SetupVideoEngine, L.AetherStream.SetupVideoEngineDetail, scale);

        var buttonHeight = Button.LargeHeight * scale;
        var buttonTop = content.Max.Y - buttonHeight - Typography.LineHeight(TextStyles.Subheadline)
            - Metrics.Space.Lg * scale;
        DrawReelSetupAction(new Rect(new Vector2(content.Min.X, buttonTop),
            new Vector2(content.Max.X, buttonTop + buttonHeight)), dependencies, scale);
    }

    private void DrawReelSetupAction(Rect button, MediaDependencies dependencies, float scale)
    {
        var library = dependencies.VideoLibrary.Snapshot();
        var label = DependencySetup.IsInstalling(library)
            ? Loc.T(L.AetherStream.SetupInstalling)
            : DependencySetup.IsBusy(library)
                ? Loc.T(L.AetherStream.SetupChecking)
                : library.State == DependencyState.Failed
                    ? Loc.T(L.AetherStream.SetupRetry)
                    : ReelInstallLabel(dependencies.PendingReelBytes);
        if (Button.Draw(button, label, ui.Ink, ButtonStyle.Prominent, enabled: !DependencySetup.IsBusy(library)))
        {
            InstallReelPlayer();
        }

        var linkTop = button.Max.Y + Metrics.Space.Sm * scale;
        var linkRect = new Rect(new Vector2(button.Min.X, linkTop),
            new Vector2(button.Max.X, linkTop + Typography.LineHeight(TextStyles.Subheadline)));
        var hovered = UiInteract.Hover(linkRect.Min, linkRect.Max);
        Typography.DrawCentered(ImGui.GetWindowDrawList(), linkRect.Center, Loc.T(L.AetherStream.SetupNotNow),
            hovered ? ui.TitleInk : ui.MutedInk, TextStyles.Subheadline);
        if (UiInteract.Click(linkRect.Min, linkRect.Max, hovered))
        {
            reelSetupDismissed = true;
        }
    }

    private string ReelInstallLabel(long pendingBytes)
    {
        if (pendingBytes <= 0)
        {
            return Loc.T(L.AetherStream.SetupInstall);
        }

        if (pendingBytes != reelInstallLabelBytes)
        {
            reelInstallLabelBytes = pendingBytes;
            reelInstallLabel = string.Format(Loc.T(L.AetherStream.SetupInstallSized),
                DependencySetup.FormatMegabytes(pendingBytes));
        }

        return reelInstallLabel;
    }

    private void InstallReelPlayer()
    {
        var dependencies = reelPlayback.Dependencies;
        if (DependencySetup.IsBusy(dependencies.VideoLibrary.Snapshot()))
        {
            return;
        }

        reelSetupWork.Run("install reel components",
            async token => await dependencies.EnsureReelsReadyAsync(token).ConfigureAwait(false));
    }

    private static float ReelChipY(Rect overlay) => overlay.Min.Y + overlay.Height * 0.3f;

    private void DrawReelPlayerChip(ImDrawListPtr drawList, Rect overlay)
    {
        var center = new Vector2(overlay.Center.X, ReelChipY(overlay));
        if (!reelPlayback.LibraryMissing)
        {
            DrawReelChip(drawList, center, Loc.T(L.Aethergram.ReelVideoUnavailable));
            return;
        }

        var installing = DependencySetup.IsBusy(reelPlayback.Dependencies.VideoLibrary.Snapshot());
        var chip = DrawReelChip(drawList, center,
            Loc.T(installing ? L.Aethergram.ReelInstallingPlayer : L.Aethergram.ReelInstallPlayer));
        if (installing)
        {
            return;
        }

        var hovered = UiInteract.Hover(chip.Min, chip.Max);
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if (UiInteract.Click(chip.Min, chip.Max, hovered))
        {
            InstallReelPlayer();
        }
    }
}

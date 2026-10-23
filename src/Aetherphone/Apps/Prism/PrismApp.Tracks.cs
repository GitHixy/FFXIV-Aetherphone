using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Animation;
using Aetherphone.Core.Localization;
using Aetherphone.Windows.Components;
using Aetherphone.Windows.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace Aetherphone.Apps.Prism;

internal readonly record struct TrackCardPhoto(string Url, PrismPlacingDto? Winner, GlamDto? Showcase, bool LastWeek);

internal sealed partial class PrismApp
{
    private const int MaxTracks = 3;
    private const float CardHeightPerWidth = 4f / 3f;
    private const float CardTallestShare = 0.62f;
    private const float CardMarkShare = 0.4f;
    private const float CardMarkHeightShare = 0.38f;
    private const float CardEdgeRest = 0.35f;
    private const int PageTileUnitsShown = 3;
    private const float PageTileWidthUnits = 56f;
    private const float PageTileHeightUnits = 44f;

    private static readonly string ResultsGlyph = IconGlyph.Of(FontAwesomeIcon.Trophy);
    private static readonly string GalleryGlyph = IconGlyph.Of(FontAwesomeIcon.Th);
    private static readonly string[] CardIds = { "prism.trackCard.0", "prism.trackCard.1", "prism.trackCard.2" };
    private static readonly TrackCardPhoto NoTrackPhoto = new(string.Empty, null, null, false);

    private readonly CachedText[] trackActionTexts = new CachedText[MaxTracks];
    private readonly CachedText[] trackCreditTexts = new CachedText[MaxTracks];
    private readonly CachedText[] trackEntryTexts = new CachedText[MaxTracks];
    private readonly Spring[] cardHover = new Spring[MaxTracks];
    private readonly PrismCountdown pageCountdown = new();
    private CachedText phaseText;
    private CachedText phaseCaptionText;
    private PrismWeekDto? lastWeekAsked;
    private string resultsWaitTrack = string.Empty;

    private bool HasTracks => store.Tracks.Length > 1;

    private bool VanillaEntry => HasTracks && store.Track == PrismTracks.Vanilla;

    private void DrawContest(ImDrawListPtr drawList, Rect full, Rect content, PrismWeekDto week, float scale)
    {
        if (!ReferenceEquals(week, lastWeekAsked))
        {
            lastWeekAsked = week;
            store.LoadLastWeekResults();
        }

        FollowResultsWait();
        var open = week.Id.Length > 0 && week.Phase.Length > 0;
        var top = DrawContestHeader(drawList, content, week, open, scale);
        var body = new Rect(new Vector2(full.Min.X, top), new Vector2(full.Max.X, content.Max.Y));
        using var surface = BeginBody(body);
        var list = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = content.Width;
        var left = origin.X + content.Min.X - full.Min.X;
        var cursor = DrawContestIntro(list, left, origin.Y + Metrics.Space.Xs * scale, width, week, open, scale);
        if (open)
        {
            cursor = DrawTrackCards(list, left, cursor + Metrics.Space.Lg * scale, width,
                body.Height - bottomChrome, week, scale);
        }

        cursor += PrismArt.BottomMargin(scale);
        ImGui.Dummy(new Vector2(content.Width, cursor - origin.Y));
    }

    private float DrawContestHeader(ImDrawListPtr drawList, Rect content, PrismWeekDto week, bool open, float scale)
    {
        var tap = Metrics.Size.TapTarget * scale;
        var radius = tap * 0.5f;
        var top = content.Min.Y + Metrics.Space.Xs * scale;
        var guide = new Vector2(content.Max.X - radius, top + radius);
        var rewards = guide - new Vector2(tap, 0f);
        var results = rewards - new Vector2(tap, 0f);
        var left = content.Min.X;
        var style = PrismType.Title;
        Typography.Draw(drawList, new Vector2(left, top + (tap - Typography.LineHeight(style)) * 0.5f),
            Typography.FitText(Loc.T(L.Prism.TabWeek), MathF.Max(1f, results.X - radius - left), style),
            PrismArt.Fore, style);
        if (IconButton(drawList, guide, PhoneIcons.HelpCircle, Loc.T(L.Prism.HowItWorks), scale,
                HoverLabelSide.Below))
        {
            StartGuide(tab);
        }

        if (IconButton(drawList, rewards, RewardsGlyph, Loc.T(L.Prism.Rewards), scale, HoverLabelSide.Below))
        {
            OpenRewards();
        }

        DrawResultsButton(drawList, results, week, open, scale);
        return top + tap;
    }

    private void DrawResultsButton(ImDrawListPtr drawList, Vector2 center, PrismWeekDto week, bool open, float scale)
    {
        if (ShownResults() is not { } results)
        {
            return;
        }

        if (open && week.Phase == PrismPhases.Results)
        {
            if (IconButton(drawList, center, GalleryGlyph, Loc.T(L.Prism.AllGlams), scale, HoverLabelSide.Below))
            {
                OpenGallery(results.WeekId, store.Track, results.Title);
            }

            return;
        }

        if (IconButton(drawList, center, ResultsGlyph, Loc.T(L.Prism.LastWeekWinners), scale, HoverLabelSide.Below))
        {
            OpenResults();
        }
    }

    private float DrawContestIntro(ImDrawListPtr drawList, float left, float top, float width, PrismWeekDto week,
        bool open, float scale)
    {
        var title = open ? week.Title : Loc.T(L.Prism.NoWeekTitle);
        top += PrismType.DrawHero(drawList, new Vector2(left, top), title, width, PrismArt.Fore);
        var phase = open ? PhaseLine(week) : string.Empty;
        if (phase.Length > 0)
        {
            top = DrawPhaseLine(drawList, new Vector2(left, top + Metrics.Space.Xs * scale), width, week, phase, scale);
        }

        var brief = open ? week.Brief : Loc.T(L.Prism.NoWeekHint);
        if (brief.Length == 0)
        {
            return top;
        }

        top += Metrics.Space.Sm * scale;
        return top + Typography.DrawWrappedLeft(new Vector2(left, top), brief, PrismArt.Muted, PrismType.Body, width);
    }

    private float DrawTrackCards(ImDrawListPtr drawList, float left, float top, float width, float room,
        PrismWeekDto week, float scale)
    {
        var tracks = store.Tracks;
        var count = tracks.Length > 1 ? Math.Min(tracks.Length, MaxTracks) : 1;
        var gap = Metrics.Space.Sm * scale;
        var cardWidth = (width - gap * (count - 1)) / count;
        var cardHeight = MathF.Min(cardWidth * CardHeightPerWidth, MathF.Max(cardWidth, room * CardTallestShare));
        var cardBottom = top + cardHeight;
        var bottom = cardBottom;
        for (var index = 0; index < count; index++)
        {
            var track = count > 1 ? tracks[index] : null;
            var x = left + index * (cardWidth + gap);
            var photo = TrackPhoto(track, week);
            DrawTrackCard(drawList, new Rect(new Vector2(x, top), new Vector2(x + cardWidth, cardBottom)), index, track,
                week, photo, scale);
            bottom = MathF.Max(bottom, DrawTrackNotes(drawList, x, cardBottom + Metrics.Space.Sm * scale, cardWidth,
                index, track, week, photo, scale));
        }

        return bottom;
    }

    private TrackCardPhoto TrackPhoto(PrismTrackDto? track, PrismWeekDto week)
    {
        if (PageEntry(track, week) is { ImageUrl.Length: > 0 } entry)
        {
            return new TrackCardPhoto(entry.ImageUrl, null, null, false);
        }

        var results = track is null || track.Id == store.Track ? ShownResults() : store.LastWeekResults(track.Id);
        if (CoverPlacing(results) is { ImageUrl.Length: > 0 } winner)
        {
            return new TrackCardPhoto(winner.ImageUrl, winner, null, results!.WeekId == week.PreviousWeekId);
        }

        if (track is not null && glams.Showcase(track.Id == PrismTracks.Vanilla) is { } glam)
        {
            return new TrackCardPhoto(Cover(glam), null, glam, false);
        }

        return NoTrackPhoto;
    }

    private static PrismEntryDto? PageEntry(PrismTrackDto? track, PrismWeekDto week) =>
        track is null ? week.MyEntry : track.MyEntry;

    private void DrawTrackCard(ImDrawListPtr drawList, Rect card, int index, PrismTrackDto? track, PrismWeekDto week,
        in TrackCardPhoto photo, float scale)
    {
        var entry = PageEntry(track, week);
        var action = ContestAction(index, week.Phase, entry is not null);
        var enabled = action.Length > 0 && (week.Phase == PrismPhases.Results || !store.Busy);
        var hovered = enabled && UiInteract.Hover(card.Min, card.Max);
        var delta = MathF.Min(ImGui.GetIO().DeltaTime, TransitionTiming.MaxFrameSeconds);
        var hover = Math.Clamp(cardHover[index].Step(hovered ? 1f : 0f, Motion.HoverLift, delta), 0f, 1f);
        if (ImGui.IsRectVisible(card.Min, card.Max))
        {
            DrawTrackCardFace(drawList, card, index, track, week, photo, entry, action, enabled, hovered, scale);
            var glow = track?.Id == PrismTracks.Vanilla ? PrismGlow.Pearl : PrismGlow.Spectrum;
            PrismLight.Edge(drawList, card, 0f, glow, PrismLight.EdgeAlpha * (CardEdgeRest + (1f - CardEdgeRest) * hover),
                scale);
        }

        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if (UiInteract.Click(card.Min, card.Max, hovered))
        {
            RunTrackCard(track, week.Phase, entry);
        }
    }

    private void DrawTrackCardFace(ImDrawListPtr drawList, Rect card, int index, PrismTrackDto? track,
        PrismWeekDto week, in TrackCardPhoto photo, PrismEntryDto? entry, string action, bool enabled, bool hovered,
        float scale)
    {
        var onCardPhoto = photo.Url.Length > 0;
        if (onCardPhoto)
        {
            DrawLook(drawList, card, photo.Url, false);
        }
        else
        {
            PrismLight.Week(drawList, card, week.Id, PrismArt.Page, PrismArt.Fore, card.Width * CardMarkShare,
                CardMarkHeightShare);
        }

        var inset = Metrics.Space.Md * scale;
        var pillHeight = Button.RegularHeight * scale;
        var nameLine = track is null ? 0f : Typography.LineHeight(PrismType.Headline);
        var pillBlock = action.Length > 0 ? pillHeight : 0f;
        var block = nameLine + (nameLine > 0f && pillBlock > 0f ? Metrics.Space.Sm * scale : 0f) + pillBlock;
        if (block <= 0f)
        {
            return;
        }

        if (onCardPhoto)
        {
            PrismArt.Scrim(drawList, card, block + inset, scale);
        }

        PrismArt.OverPhoto(onCardPhoto);
        var room = card.Width - inset * 2f;
        var y = card.Max.Y - inset - block;
        if (track is not null)
        {
            DrawTrackName(drawList, new Vector2(card.Min.X + inset, y), room, track.Id, scale);
        }

        if (pillBlock > 0f)
        {
            var busy = store.Busy && week.Phase == PrismPhases.Entering && entry is null &&
                       (track is null || track.Id == store.Track);
            var label = busy ? Loc.T(L.Prism.Uploading) : action;
            var pillWidth = MathF.Min(Typography.Measure(label, Button.LabelStyle(pillHeight)).X + pillHeight, room);
            var pill = new Rect(new Vector2(card.Min.X + inset, card.Max.Y - inset - pillHeight),
                new Vector2(card.Min.X + inset + pillWidth, card.Max.Y - inset));
            Button.DrawLabel(drawList, Button.Surface(drawList, pill, onCardPhoto ? photoControls : controls,
                entry is not null ? ButtonStyle.Gray : ButtonStyle.Solid, ButtonRole.Normal, enabled, hovered,
                ImGui.GetID(CardIds[index])), label);
        }

        PrismArt.OverPhoto(false);
    }

    private static void DrawTrackName(ImDrawListPtr drawList, Vector2 topLeft, float room, string trackId, float scale)
    {
        var line = Typography.LineHeight(PrismType.Headline);
        var glyph = PrismArt.InlineIconUnits * scale;
        PrismArt.FactIcon(drawList, new Vector2(topLeft.X + glyph * 0.5f, topLeft.Y + line * 0.5f), glyph,
            trackId == PrismTracks.Vanilla ? PrismFact.Vanilla : PrismFact.Modded, PrismArt.Fore);
        var textLeft = topLeft.X + glyph + Metrics.Space.Xs * scale;
        Typography.Draw(drawList, new Vector2(textLeft, topLeft.Y),
            Typography.FitText(PrismTracks.Name(trackId), MathF.Max(1f, room - glyph - Metrics.Space.Xs * scale),
                PrismType.Headline), PrismArt.Fore, PrismType.Headline);
    }

    private float DrawTrackNotes(ImDrawListPtr drawList, float left, float top, float width, int index,
        PrismTrackDto? track, PrismWeekDto week, in TrackCardPhoto photo, float scale)
    {
        var line = Typography.LineHeight(PrismType.Caption);
        var reach = Metrics.Space.Xs * scale;
        var rowGap = Metrics.Space.Sm * scale;
        var entry = PageEntry(track, week);
        if (entry is not null)
        {
            var end = DrawGlyphCaption(drawList, new Vector2(left, top), left + width, StatusGlyph(entry.Status),
                EntryLine(index, entry), PrismArt.Muted, scale);
            if (UiInteract.HoverClick(new Vector2(left, top - reach), new Vector2(end, top + line + reach)))
            {
                SelectPage(track);
                Go(PrismScreen.Entry);
            }

            top += line + rowGap;
        }

        if (photo.Winner is { } winner)
        {
            var end = DrawGlyphCaption(drawList, new Vector2(left, top), left + width,
                photo.LastWeek ? PhoneIcons.Crown : PhoneIcons.Photo, CreditLine(index, winner, photo.LastWeek),
                PrismArt.Muted, scale);
            TapForOutfit(new Vector2(left, top - reach), new Vector2(end, top + line + reach), winner);
            top += line + rowGap;
        }
        else if (photo.Showcase is { } glam)
        {
            var end = DrawGlyphCaption(drawList, new Vector2(left, top), left + width, PhoneIcons.Photo,
                ShowcaseLine(index, glam), PrismArt.Muted, scale);
            if (UiInteract.HoverClick(new Vector2(left, top - reach), new Vector2(end, top + line + reach)))
            {
                OpenGlam(glam);
            }

            top += line + rowGap;
        }

        var rule = track is null ? string.Empty : TrackRule(track.Id);
        return rule.Length == 0
            ? top
            : top + Typography.DrawWrappedLeft(new Vector2(left, top), rule, PrismArt.Muted, PrismType.Caption, width);
    }

    private void RunTrackCard(PrismTrackDto? track, string phase, PrismEntryDto? entry)
    {
        SelectPage(track);
        if (phase == PrismPhases.Results)
        {
            if (ShownResults() is not null)
            {
                OpenResults();
                return;
            }

            resultsWaitTrack = store.Track;
            return;
        }

        if (phase == PrismPhases.Entering && entry is not null)
        {
            Go(PrismScreen.Entry);
        }
        else if (phase == PrismPhases.Entering)
        {
            StartPick();
        }
        else
        {
            StartJudging();
        }
    }

    private void FollowResultsWait()
    {
        if (resultsWaitTrack.Length == 0)
        {
            return;
        }

        if (resultsWaitTrack != store.Track)
        {
            resultsWaitTrack = string.Empty;
            return;
        }

        if (ShownResults() is null)
        {
            return;
        }

        resultsWaitTrack = string.Empty;
        OpenResults();
    }

    private float DrawPhaseLine(ImDrawListPtr drawList, Vector2 topLeft, float width, PrismWeekDto week,
        string phase, float scale)
    {
        var deadline = PrismCountdown.Deadline(week);
        if (PrismCountdown.Remaining(deadline) <= 0)
        {
            Typography.Draw(drawList, topLeft, Typography.FitText(phase, width, PrismType.Body), PrismArt.Muted,
                PrismType.Body);
            return topLeft.Y + Typography.LineHeight(PrismType.Body);
        }

        Typography.Draw(drawList, topLeft, Typography.FitText(PhaseCaption(week), width, PrismType.Caption),
            PrismArt.Muted, PrismType.Caption);
        var top = topLeft.Y + Typography.LineHeight(PrismType.Caption) + Metrics.Space.Xs * scale;
        var tilesWidth = MathF.Min(width,
            PageTileUnitsShown * PageTileWidthUnits * scale + (PageTileUnitsShown - 1) * Metrics.Space.Xs * scale);
        var tiles = new Rect(new Vector2(topLeft.X, top),
            new Vector2(topLeft.X + tilesWidth, top + PageTileHeightUnits * scale));
        pageCountdown.DrawCompact(drawList, tiles, deadline, scale);
        return tiles.Max.Y;
    }

    private string PhaseCaption(PrismWeekDto week)
    {
        var label = Loc.T(DeadlineLabel(week.Phase));
        if (!PrismPhases.Judgeable(week.Phase))
        {
            return label;
        }

        var judged = JudgedLine(week.BallotsCast);
        var key = HashCode.Combine(label, judged);
        return phaseCaptionText.IsCurrent(key)
            ? phaseCaptionText.Value
            : phaseCaptionText.Store(key, Loc.T(L.Prism.WithTrack, label, judged));
    }

    private void SelectPage(PrismTrackDto? track)
    {
        if (track is not null)
        {
            store.SelectTrack(track.Id);
        }
    }

    private static void DrivePager(Pager pager, Rect area, int count, ref bool pressed, ref Vector2 press,
        float scale)
    {
        var delta = ImGui.GetIO().DeltaTime;
        pager.Step(delta, count);
        var mouse = ImGui.GetMousePos();
        if (pager.Dragging)
        {
            pager.Drag(mouse.X, area.Width, count, delta);
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                pager.Release(area.Width, count);
            }

            return;
        }

        if (UiInteract.Hover(area.Min, area.Max) && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            pressed = true;
            press = mouse;
        }

        if (!pressed || !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            pressed = false;
            return;
        }

        var move = mouse - press;
        if (MathF.Abs(move.X) <= PagerSlopUnits * scale || MathF.Abs(move.X) <= MathF.Abs(move.Y))
        {
            return;
        }

        pressed = false;
        UiInteract.CancelPendingTap();
        pager.Begin(press.X);
    }

    private static void DrawPagerDots(ImDrawListPtr drawList, Pager pager, Rect photo, int count, int page, Vector4 ink,
        float scale)
    {
        var tap = Metrics.Size.TapTarget * scale;
        var center = new Vector2(photo.Center.X, photo.Max.Y - tap * 0.5f);
        PhotoCarousel.DrawDots(drawList, center, count, page, photo.Width, ink);
        var hit = new Rect(new Vector2(center.X - tap * count * 0.5f, photo.Max.Y - tap),
            new Vector2(center.X + tap * count * 0.5f, photo.Max.Y));
        if (UiInteract.HoverClick(hit.Min, hit.Max))
        {
            pager.AnimateTo(ImGui.GetMousePos().X >= center.X ? page + 1 : page - 1, count);
        }
    }

    private static string TrackRule(string id) => id switch
    {
        PrismTracks.Vanilla => Loc.T(L.Prism.TrackVanillaRule),
        PrismTracks.Modded => Loc.T(L.Prism.TrackModdedRule),
        _ => string.Empty,
    };

    private string ContestAction(int index, string phase, bool entered)
    {
        var key = HashCode.Combine(phase, entered, Loc.Current.Code);
        if (trackActionTexts[index].IsCurrent(key))
        {
            return trackActionTexts[index].Value;
        }

        return trackActionTexts[index].Store(key, phase switch
        {
            PrismPhases.Entering when entered => Loc.T(L.Prism.SeeYourGlam),
            PrismPhases.Entering => Loc.T(L.Prism.Enter),
            PrismPhases.Judging => Loc.T(L.Prism.JudgeHeat),
            PrismPhases.Final => Loc.T(L.Prism.JudgeFinal),
            PrismPhases.Results => Loc.T(L.Prism.SeeResults),
            _ => string.Empty,
        });
    }

    private string PhaseLine(PrismWeekDto week)
    {
        var label = Loc.T(DeadlineLabel(week.Phase));
        var value = week.Phase == PrismPhases.Tallying ? TimeText.Clock(week.ResultsAtUnix) : DeadlineValue(week);
        var judged = PrismPhases.Judgeable(week.Phase) ? JudgedLine(week.BallotsCast) : string.Empty;
        var key = HashCode.Combine(label, value, judged);
        if (phaseText.IsCurrent(key))
        {
            return phaseText.Value;
        }

        var line = value.Length > 0 ? Loc.T(L.Prism.WithTrack, label, value) : string.Empty;
        return phaseText.Store(key,
            judged.Length > 0 && line.Length > 0 ? Loc.T(L.Prism.WithTrack, line, judged) : line);
    }

    private string EntryLine(int index, PrismEntryDto entry)
    {
        var key = HashCode.Combine(entry.Status, Loc.Current.Code);
        return trackEntryTexts[index].IsCurrent(key)
            ? trackEntryTexts[index].Value
            : trackEntryTexts[index].Store(key,
                Loc.T(L.Prism.WithTrack, Loc.T(L.Prism.YourLook), Loc.T(StatusName(entry.Status))));
    }

    private string CreditLine(int index, PrismPlacingDto winner, bool lastWeek)
    {
        var key = HashCode.Combine(winner.Handle, winner.DisplayName, lastWeek, Loc.Current.Code);
        return trackCreditTexts[index].IsCurrent(key)
            ? trackCreditTexts[index].Value
            : trackCreditTexts[index].Store(key, Loc.T(lastWeek ? L.Prism.LastWeekWinner : L.Prism.CoverBy,
                AtHandle(winner.Handle, winner.DisplayName)));
    }

    private string ShowcaseLine(int index, GlamDto glam)
    {
        var handle = glam.Author?.Handle ?? string.Empty;
        var name = glam.Author?.DisplayName ?? string.Empty;
        var key = HashCode.Combine(glam.Id, handle, name, Loc.Current.Code);
        return trackCreditTexts[index].IsCurrent(key)
            ? trackCreditTexts[index].Value
            : trackCreditTexts[index].Store(key, Loc.T(L.Prism.TrackGlamBy, AtHandle(handle, name)));
    }
}

using Aetherphone.Core;
using Aetherphone.Core.Animation;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Theme;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Games.Framework;

internal enum GuideOrigin : byte
{
    BeforePlay,
    Intro,
    Pause,
}

internal enum GuideExit : byte
{
    None,
    Play,
    Closed,
}

internal sealed class StageGuide
{
    private const float VeilAlpha = 0.9f;
    private const float FadeSeconds = 0.12f;
    private const float ActiveThreshold = 0.9f;
    private const float PageSpeed = 3.2f;
    private const float ArtHeightShare = 0.52f;
    private const float ArtWidthRatio = 0.82f;
    private const float ArtInset = 12f;
    private const float LandscapeArtShare = 0.56f;
    private const float TextMaxWidth = 320f;
    private const float BodyAlpha = 0.82f;
    private const float ButtonHeight = 46f;
    private const float ButtonMaxWidth = 168f;
    private const float DotRadius = 3.5f;
    private const float DotActiveWidth = 18f;
    private const float DotGap = 8f;
    private const float LiftDistance = 10f;
    private static readonly TextStyle CaptionStyle = TextStyles.FootnoteEmphasized;
    private static readonly TextStyle TitleStyle = TextStyles.Title2;
    private static readonly TextStyle BodyStyle = TextStyles.Callout;

    private GameGuide? guide;
    private Spring veil;
    private GuideOrigin origin;
    private int page;
    private float seconds;
    private float pageIn;
    private bool open;

    public bool IsOpen => open;

    public GuideOrigin Origin => origin;

    public void Open(GameGuide next, GuideOrigin from)
    {
        guide = next;
        origin = from;
        open = true;
        ShowPage(0);
    }

    public void Close()
    {
        open = false;
    }

    public void Reset()
    {
        open = false;
        guide = null;
        veil.SnapTo(0f);
    }

    public GuideExit Draw(ImDrawListPtr drawList, in GameContext context, Vector4 accent, float scale)
    {
        var raw = context.RawDeltaSeconds;
        veil.Step(open ? 1f : 0f, FadeSeconds, raw);
        var alpha = Math.Clamp(veil.Value, 0f, 1f);
        if (alpha <= 0.01f || guide is null || guide.Count == 0)
        {
            return GuideExit.None;
        }

        seconds += raw;
        pageIn = GameJuice.Advance(pageIn, raw, PageSpeed);
        var full = context.Full;
        Material.Veil(drawList, full.Min, full.Max, VeilAlpha * alpha);
        var content = StageLayout.Safe(full, HudStyle.Standard, scale);
        var landscape = full.IsLandscape();
        var footerHeight = ButtonHeight * scale + Metrics.Space.Lg * scale + DotRadius * 2f * scale;
        var body = new Rect(content.Min, new Vector2(content.Max.X, content.Max.Y - footerHeight));
        var art = landscape ? LandscapeArt(body, scale) : PortraitArt(body, scale);
        var textColumn = landscape
            ? new Rect(new Vector2(art.Max.X + Metrics.Space.Xl * scale, body.Min.Y), body.Max)
            : new Rect(new Vector2(body.Min.X, art.Max.Y + Metrics.Space.Lg * scale), body.Max);
        ref readonly var current = ref guide.Page(page);
        var phase = Easing.EaseOutCubic(pageIn) * alpha;
        DrawArt(drawList, art, current.Art, accent, phase, scale);
        DrawText(drawList, textColumn, in current, landscape, phase, scale);
        var interactive = open && alpha >= ActiveThreshold;
        var exit = DrawFooter(drawList, content, accent, context.Theme, alpha, interactive, scale);
        if (exit != GuideExit.None || !interactive)
        {
            return exit;
        }

        if (UiInteract.HoverClick(art.Min, art.Max) && page < guide.Count - 1)
        {
            ShowPage(page + 1);
        }

        return ReadKeys();
    }

    private void ShowPage(int next)
    {
        page = next;
        seconds = 0f;
        pageIn = 0f;
    }

    private static Rect PortraitArt(Rect body, float scale)
    {
        var height = MathF.Min(body.Height * ArtHeightShare, body.Width * ArtWidthRatio);
        var width = MathF.Min(body.Width, height / ArtWidthRatio);
        var left = body.Center.X - width * 0.5f;
        return new Rect(new Vector2(left, body.Min.Y + Metrics.Space.Lg * scale),
            new Vector2(left + width, body.Min.Y + Metrics.Space.Lg * scale + height));
    }

    private static Rect LandscapeArt(Rect body, float scale)
    {
        var width = body.Width * LandscapeArtShare;
        var height = MathF.Min(body.Height, width * ArtWidthRatio);
        var top = body.Center.Y - height * 0.5f;
        return new Rect(new Vector2(body.Min.X, top), new Vector2(body.Min.X + width, top + height));
    }

    private void DrawArt(ImDrawListPtr drawList, Rect art, GuideArt draw, Vector4 accent, float phase, float scale)
    {
        var firstVertex = drawList.VtxBuffer.Size;
        BoardPlate.Draw(drawList, art, BoardPlate.Radius * scale, scale, accent, StageInk.Light);
        var inner = art.Inset(ArtInset * scale);
        drawList.PushClipRect(inner.Min, inner.Max, true);
        draw(drawList, inner, seconds, accent, scale);
        drawList.PopClipRect();
        LayerCompositor.Fade(drawList, firstVertex, phase);
    }

    private void DrawText(ImDrawListPtr drawList, Rect column, in GuidePage current, bool landscape, float phase,
        float scale)
    {
        var width = MathF.Min(column.Width, TextMaxWidth * scale);
        var title = Loc.T(current.Title);
        var bodyText = Loc.T(current.Body);
        var captionHeight = Typography.LineHeight(CaptionStyle);
        var titleHeight = Typography.MeasureWrappedBlock(title, TitleStyle, width).Y;
        var bodyHeight = Typography.MeasureWrappedBlock(bodyText, BodyStyle, width).Y;
        var gap = Metrics.Space.Sm * scale;
        var block = captionHeight + gap + titleHeight + gap + bodyHeight;
        var top = landscape ? column.Center.Y - block * 0.5f : column.Min.Y;
        var lift = (1f - phase) * LiftDistance * scale;
        var centerX = column.Center.X;
        Typography.DrawCentered(drawList, new Vector2(centerX, top + captionHeight * 0.5f + lift),
            Loc.T(L.Stage.HowToPlay), StageInks.Muted with { W = phase }, CaptionStyle);
        top += captionHeight + gap;
        Typography.DrawWrappedCentered(drawList, new Vector2(centerX, top + titleHeight * 0.5f + lift), title,
            StageInks.Strong with { W = phase }, TitleStyle, width);
        top += titleHeight + gap;
        Typography.DrawWrappedCentered(drawList, new Vector2(centerX, top + bodyHeight * 0.5f + lift), bodyText,
            StageInks.Strong with { W = BodyAlpha * phase }, BodyStyle, width);
    }

    private GuideExit DrawFooter(ImDrawListPtr drawList, Rect content, Vector4 accent, PhoneTheme theme,
        float alpha, bool interactive, float scale)
    {
        var count = guide!.Count;
        var buttonHeight = ButtonHeight * scale;
        var buttonsCenterY = content.Max.Y - buttonHeight * 0.5f;
        var dotsY = content.Max.Y - buttonHeight - Metrics.Space.Lg * scale;
        DrawDots(drawList, new Vector2(content.Center.X, dotsY), count, accent, alpha, scale);
        if (!interactive)
        {
            return GuideExit.None;
        }

        var gap = Metrics.Space.Md * scale;
        var width = MathF.Min(ButtonMaxWidth * scale, (content.Width - gap) * 0.5f);
        var size = new Vector2(width, buttonHeight);
        var secondary = Palette.Mix(StageInks.Surface, accent, 0.18f);
        var leftCenter = new Vector2(content.Center.X - gap * 0.5f - width * 0.5f, buttonsCenterY);
        var rightCenter = new Vector2(content.Center.X + gap * 0.5f + width * 0.5f, buttonsCenterY);
        var last = page == count - 1;
        if (GameHud.Button(leftCenter, size, Loc.T(page == 0 ? SkipLabel() : L.Stage.GuideBack), secondary, theme))
        {
            return page == 0 ? Leave() : Back();
        }

        if (GameHud.Button(rightCenter, size, Loc.T(last ? FinishLabel() : L.Common.Next), accent, theme))
        {
            return last ? Finish() : Next();
        }

        return GuideExit.None;
    }

    private void DrawDots(ImDrawListPtr drawList, Vector2 center, int count, Vector4 accent, float alpha, float scale)
    {
        var radius = DotRadius * scale;
        var active = DotActiveWidth * scale;
        var gap = DotGap * scale;
        var x = center.X - (active + (count - 1) * (radius * 2f + gap)) * 0.5f;
        for (var index = 0; index < count; index++)
        {
            var current = index == page;
            var width = current ? active : radius * 2f;
            var ink = current ? accent : StageInks.Muted;
            Squircle.Fill(drawList, new Vector2(x, center.Y - radius), new Vector2(x + width, center.Y + radius),
                radius, ImGui.GetColorU32(ink with { W = ink.W * alpha }));
            x += width + gap;
        }
    }

    private LocString SkipLabel() => origin == GuideOrigin.BeforePlay ? L.Stage.GuideSkip : L.Common.Close;

    private LocString FinishLabel() => origin == GuideOrigin.Pause ? L.Stage.GuideDone : L.Games.Play;

    private GuideExit ReadKeys()
    {
        if (GameInput.Pressed(ImGuiKey.Escape))
        {
            open = false;
            return GuideExit.Closed;
        }

        if (GameInput.Pressed(ImGuiKey.LeftArrow, ImGuiKey.A) && page > 0)
        {
            return Back();
        }

        if (!GameInput.Pressed(ImGuiKey.RightArrow, ImGuiKey.D) && !GameInput.Pressed(ImGuiKey.Enter, ImGuiKey.Space))
        {
            return GuideExit.None;
        }

        return page == guide!.Count - 1 ? Finish() : Next();
    }

    private GuideExit Next()
    {
        ShowPage(Math.Min(page + 1, guide!.Count - 1));
        return GuideExit.None;
    }

    private GuideExit Back()
    {
        ShowPage(Math.Max(page - 1, 0));
        return GuideExit.None;
    }

    private GuideExit Leave()
    {
        open = false;
        return origin == GuideOrigin.BeforePlay ? GuideExit.Play : GuideExit.Closed;
    }

    private GuideExit Finish()
    {
        open = false;
        return origin == GuideOrigin.Pause ? GuideExit.Closed : GuideExit.Play;
    }
}

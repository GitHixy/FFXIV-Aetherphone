using Aetherphone.Apps.Casino.Stage;
using Aetherphone.Apps.Games;
using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Animation;
using Aetherphone.Core.Casino;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Onboarding;
using Aetherphone.Core.Theme;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Casino.Strip;

internal enum StripHeroAction : byte
{
    None,
    GetChips,
    Cashier,
    ClaimTimed,
    ClaimStreak,
    FreeSpin,
}

[Flags]
internal enum StripHeroClaims : byte
{
    None = 0,
    Timed = 1,
    Streak = 2,
    Spin = 4,
}

internal enum StripHeroRow : byte
{
    Inline,
    Stacked,
    Rail,
}

internal readonly record struct StripHeroModel(
    long Balance,
    bool Seated,
    int Level,
    float LevelProgress,
    long LevelCap,
    bool ShowsLevel,
    CasinoBonusDto? Timed,
    CasinoBonusDto? Streak,
    bool SpinReady,
    bool Claiming,
    long NowUnix,
    long Rate = CasinoChipLots.ChipPerCoin);

internal sealed class StripHero
{
    public const float Pad = 18f;
    public const float GlyphSize = 30f;
    public const float RowGap = 12f;
    public const float BonusRing = 30f;
    public const float RingThickness = 3.5f;
    public const float StreakDot = 26f;
    public const float StreakGap = 6f;
    public const int StreakDays = CasinoFaucets.StreakDays;
    public const long TimedPeriodSeconds = 3 * 3600;
    public const float StreakLabelGap = 6f;

    private const float SurfaceLuminance = 0.07f;
    private const float GlassOpacity = 0.94f;
    private const float SubAlpha = 0.86f;
    private const float IdleAlpha = 0.6f;
    private const float PillRailPad = 4f;
    private const float FitSlack = 0.5f;
    private const string PillRailId = "##casino.hero.pills";

    private static readonly TextStyle AmountStyle = TextStyles.LargeTitle;

    private readonly CasinoTextCache texts = new();
    private readonly string[] dayLabels = new string[StreakDays];
    private readonly ChipValueText worth = new();
    private readonly TileRail pillRail = new();
    private RollingAmount balance;
    private LevelCapsule level;
    private Vector2 timedCenter;
    private Vector2 streakCenter;
    private Rect spinSource;

    public Vector2 TimedCenter => timedCenter;

    public Vector2 StreakCenter => streakCenter;

    public Rect SpinSource => spinSource;

    public void Snap(long value) => balance.Snap(value);

    public void Reset() => pillRail.Reset();

    public static StripHeroClaims ClaimsOf(in StripHeroModel model)
    {
        var claims = StripHeroClaims.None;
        if (model.Timed is not null)
        {
            claims |= StripHeroClaims.Timed;
        }

        if (model.Streak is not null)
        {
            claims |= StripHeroClaims.Streak;
        }

        if (model.SpinReady)
        {
            claims |= StripHeroClaims.Spin;
        }

        return claims;
    }

    public static int RowPills(StripHeroClaims claims) =>
        1 + ((claims & StripHeroClaims.Timed) != 0 ? 1 : 0) + ((claims & StripHeroClaims.Spin) != 0 ? 1 : 0);

    public static StripHeroRow RowLayout(int pills, float rowWidth, float pillsWidth)
    {
        if (pills <= 1 || pillsWidth <= rowWidth + FitSlack)
        {
            return StripHeroRow.Inline;
        }

        return pills >= 3 ? StripHeroRow.Rail : StripHeroRow.Stacked;
    }

    public static float ButtonsHeight(StripHeroRow row, float buttonHeight, float gap) =>
        row == StripHeroRow.Stacked ? buttonHeight * 2f + gap : buttonHeight;

    public static float StreakHeight(float labelHeight, float dot, float labelGap) => labelHeight + labelGap + dot;

    public float Height(in StripHeroModel model, float width, float scale)
    {
        var pad = Pad * scale;
        var gap = RowGap * scale;
        var buttonGap = Metrics.Space.Sm * scale;
        var row = PlanRow(model, width - pad * 2f, buttonGap, scale, out _, out _, out _);
        var height = pad * 2f + MathF.Max(GlyphSize * scale, Typography.LineHeight(TextStyles.SubheadlineEmphasized))
                     + Typography.LineHeight(AmountStyle) + Typography.LineHeight(TextStyles.Footnote) + gap
                     + ButtonsHeight(row, Button.LargeHeight * scale, buttonGap);
        if (model.ShowsLevel)
        {
            height += gap + LevelCapsule.Height * scale;
        }

        if (model.Streak is not null)
        {
            height += gap + StreakHeight(Typography.LineHeight(TextStyles.FootnoteEmphasized), StreakDot * scale,
                StreakLabelGap * scale);
        }

        return height;
    }

    private static float GetChipsWidth(float scale) =>
        MathF.Max(CasinoStageLayout.TouchTarget * scale, Button.WidthFor(Loc.T(L.Strip.GetChips), ButtonSize.Large));

    private static float SpinWidth(float scale) =>
        MathF.Max(CasinoStageLayout.TouchTarget * scale, Button.WidthFor(Loc.T(L.Floor.FreeSpin), ButtonSize.Large));

    private StripHeroRow PlanRow(in StripHeroModel model, float rowWidth, float buttonGap, float scale,
        out float getWidth, out float timedWidth, out float spinWidth)
    {
        var claims = ClaimsOf(model);
        getWidth = GetChipsWidth(scale);
        timedWidth = (claims & StripHeroClaims.Timed) != 0 ? TimedWidth(model, scale) : 0f;
        spinWidth = (claims & StripHeroClaims.Spin) != 0 ? SpinWidth(scale) : 0f;
        var pillsWidth = getWidth + (timedWidth > 0f ? buttonGap + timedWidth : 0f)
                         + (spinWidth > 0f ? buttonGap + spinWidth : 0f);
        return RowLayout(RowPills(claims), rowWidth, pillsWidth);
    }

    private float TimedWidth(in StripHeroModel model, float scale)
    {
        if (model.Timed is not { } timed)
        {
            return 0f;
        }

        var style = TextStyles.SubheadlineEmphasized;
        var claim = Typography.Measure(texts.Compact(L.Strip.BonusClaim, timed.Amount), style).X;
        var wait = Typography.Measure(texts.Duration(L.Strip.BonusReadyIn, (int)TimedPeriodSeconds), style).X;
        var height = Button.LargeHeight * scale;
        return height * 0.5f + BonusRing * 0.5f * scale + Metrics.Space.Xs * scale + MathF.Max(claim, wait)
               + Metrics.Space.Sm * scale;
    }

    public StripHeroAction Draw(ImDrawListPtr drawList, AppSkin ui, in StripHeroModel model, Vector2 origin,
        float width, float deltaSeconds, float scale, out float bottom)
    {
        var height = Height(model, width, scale);
        var min = origin;
        var max = new Vector2(origin.X + width, origin.Y + height);
        bottom = max.Y;
        UiAnchors.Report("casino.chipbar", new Rect(min, max));
        var surface = Palette.ShadeToLuminance(ui.Accent with { W = 1f }, SurfaceLuminance);
        Material.AccentGlass(drawList, min, max, Metrics.Radius.Widget * scale, scale, surface, GlassOpacity);

        var pad = Pad * scale;
        var gap = RowGap * scale;
        var left = min.X + pad;
        var right = max.X - pad;
        var action = StripHeroAction.None;
        var top = min.Y + pad;
        var headRow = MathF.Max(GlyphSize * scale, Typography.LineHeight(TextStyles.SubheadlineEmphasized));
        var glyphCenter = new Vector2(left + GlyphSize * scale * 0.5f, top + headRow * 0.5f);
        CurrencyGlyph.Draw(drawList, CurrencyKind.Chips, glyphCenter, GlyphSize * scale);
        var title = StatusTitle.For(model.Balance);
        var titleWidth = StatusTitle.Width(title, width * 0.4f, scale);
        if (titleWidth > 0f)
        {
            StatusTitle.Draw(drawList, new Vector2(right - titleWidth * 0.5f, glyphCenter.Y), title, width * 0.4f,
                scale);
        }

        var labelLeft = glyphCenter.X + GlyphSize * scale * 0.5f + Metrics.Space.Sm * scale;
        var labelRight = right - (titleWidth > 0f ? titleWidth + Metrics.Space.Sm * scale : 0f);
        var label = Typography.FitText(Loc.T(L.Strip.YourChips), MathF.Max(1f, labelRight - labelLeft),
            TextStyles.SubheadlineEmphasized);
        Typography.Draw(drawList,
            new Vector2(labelLeft, glyphCenter.Y - Typography.LineHeight(TextStyles.SubheadlineEmphasized) * 0.5f),
            label, Palette.WithAlpha(CasinoArt.White, SubAlpha), TextStyles.SubheadlineEmphasized);
        top += headRow;

        balance.Update(model.Balance, deltaSeconds);
        var amount = NumberText.Compact(balance.Display);
        var amountHeight = Typography.LineHeight(AmountStyle);
        var amountScale = Typography.FitScale(amount, right - left, AmountStyle.Scale * balance.PopScale,
            TextStyles.Title2.Scale, AmountStyle.Weight);
        var amountSize = Typography.Measure(amount, amountScale, AmountStyle.Weight);
        var amountRect = new Rect(new Vector2(left, top), new Vector2(right, top + amountHeight));
        Typography.Draw(drawList, new Vector2(left, top + (amountHeight - amountSize.Y) * 0.5f), amount,
            model.Balance > 0 ? CasinoColors.Money : Palette.WithAlpha(CasinoArt.White, IdleAlpha), amountScale,
            AmountStyle.Weight);
        var overAmount = UiInteract.Hover(amountRect.Min, amountRect.Max);
        if (overAmount)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            HoverTooltip.Show(amountRect, NumberText.Group(model.Balance), HoverLabelSide.Below);
        }

        if (UiInteract.Click(amountRect.Min, amountRect.Max, overAmount))
        {
            action = StripHeroAction.Cashier;
        }

        top += amountHeight;
        var coins = worth.Full(model.Balance, model.Rate);
        var coinsText = Typography.Measure(coins, TextStyles.Footnote).X <= right - left
            ? coins
            : worth.Compact(model.Balance, model.Rate);
        Typography.Draw(drawList, new Vector2(left, top), Typography.FitText(coinsText, right - left, TextStyles.Footnote),
            Palette.WithAlpha(CasinoArt.White, SubAlpha), TextStyles.Footnote);
        top += Typography.LineHeight(TextStyles.Footnote);
        if (model.ShowsLevel)
        {
            top += gap;
            var capsule = new Rect(new Vector2(left, top), new Vector2(right, top + LevelCapsule.Height * scale));
            level.Draw(drawList, capsule, Math.Max(1, model.Level), model.LevelProgress, model.LevelCap,
                CasinoColors.Money, scale);
            top = capsule.Max.Y;
        }

        top += gap;
        var buttonHeight = Button.LargeHeight * scale;
        var buttonGap = Metrics.Space.Sm * scale;
        var row = PlanRow(model, right - left, buttonGap, scale, out var getWidth, out var timedWidth,
            out var spinWidth);
        var rowRect = new Rect(new Vector2(left, top), new Vector2(right, top + buttonHeight));
        var rowAction = row == StripHeroRow.Rail
            ? DrawPillRail(drawList, ui, model, rowRect, getWidth, timedWidth, spinWidth, buttonGap, surface, scale)
            : DrawPillRow(drawList, ui, model, row, rowRect, getWidth, timedWidth, spinWidth, buttonGap, surface,
                scale);
        if (rowAction != StripHeroAction.None)
        {
            action = rowAction;
        }

        top += ButtonsHeight(row, buttonHeight, buttonGap);
        if (model.Streak is { } streak)
        {
            top += gap;
            if (DrawStreak(drawList, streak, model.Claiming, new Vector2(left, top), right - left, scale))
            {
                action = StripHeroAction.ClaimStreak;
            }
        }

        return action;
    }

    private StripHeroAction DrawPillRow(ImDrawListPtr drawList, AppSkin ui, in StripHeroModel model, StripHeroRow row,
        Rect rowRect, float getWidth, float timedWidth, float spinWidth, float gap, Vector4 surface, float scale)
    {
        var action = StripHeroAction.None;
        var hasTimed = timedWidth > 0f;
        var hasSpin = spinWidth > 0f;
        if (row == StripHeroRow.Stacked)
        {
            if (DrawWhitePill(drawList, ui, rowRect, Loc.T(L.Strip.GetChips), surface, true))
            {
                action = StripHeroAction.GetChips;
            }

            var second = new Rect(new Vector2(rowRect.Min.X, rowRect.Max.Y + gap),
                new Vector2(rowRect.Max.X, rowRect.Max.Y + gap + rowRect.Height));
            if (hasTimed)
            {
                UiAnchors.Report("casino.bonus", second);
                if (DrawTimed(drawList, ui, second, model.Timed!, model.Claiming, model.NowUnix, scale, true))
                {
                    action = StripHeroAction.ClaimTimed;
                }
            }
            else if (DrawSpin(drawList, ui, second, true))
            {
                action = StripHeroAction.FreeSpin;
            }

            return action;
        }

        var getRight = hasTimed || hasSpin ? rowRect.Min.X + getWidth : rowRect.Max.X;
        var getRect = new Rect(rowRect.Min, new Vector2(getRight, rowRect.Max.Y));
        if (DrawWhitePill(drawList, ui, getRect, Loc.T(L.Strip.GetChips), surface, true))
        {
            action = StripHeroAction.GetChips;
        }

        var cursor = getRect.Max.X + gap;
        if (hasTimed)
        {
            var timedRect = new Rect(new Vector2(cursor, rowRect.Min.Y),
                new Vector2(hasSpin ? cursor + timedWidth : rowRect.Max.X, rowRect.Max.Y));
            UiAnchors.Report("casino.bonus", timedRect);
            if (DrawTimed(drawList, ui, timedRect, model.Timed!, model.Claiming, model.NowUnix, scale, true))
            {
                action = StripHeroAction.ClaimTimed;
            }

            cursor = timedRect.Max.X + gap;
        }

        if (hasSpin && DrawSpin(drawList, ui, new Rect(new Vector2(cursor, rowRect.Min.Y), rowRect.Max), true))
        {
            action = StripHeroAction.FreeSpin;
        }

        return action;
    }

    private StripHeroAction DrawPillRail(ImDrawListPtr drawList, AppSkin ui, in StripHeroModel model, Rect rowRect,
        float getWidth, float timedWidth, float spinWidth, float gap, Vector4 surface, float scale)
    {
        var action = StripHeroAction.None;
        var contentWidth = getWidth + gap + timedWidth + gap + spinWidth;
        var pad = new Vector2(PillRailPad * scale, PillRailPad * scale);
        var rail = new Rect(rowRect.Min - pad, rowRect.Max + pad);
        pillRail.Begin(drawList, PillRailId, rail, rail, contentWidth);
        var interactive = pillRail.TapAllowed;
        var left = rowRect.Min.X - pillRail.Offset;
        var getRect = new Rect(new Vector2(left, rowRect.Min.Y), new Vector2(left + getWidth, rowRect.Max.Y));
        if (DrawWhitePill(drawList, ui, getRect, Loc.T(L.Strip.GetChips), surface, interactive))
        {
            action = StripHeroAction.GetChips;
        }

        left = getRect.Max.X + gap;
        var timedRect = new Rect(new Vector2(left, rowRect.Min.Y), new Vector2(left + timedWidth, rowRect.Max.Y));
        UiAnchors.Report("casino.bonus", timedRect);
        if (DrawTimed(drawList, ui, timedRect, model.Timed!, model.Claiming, model.NowUnix, scale, interactive))
        {
            action = StripHeroAction.ClaimTimed;
        }

        left = timedRect.Max.X + gap;
        var spinRect = new Rect(new Vector2(left, rowRect.Min.Y), new Vector2(left + spinWidth, rowRect.Max.Y));
        if (DrawSpin(drawList, ui, spinRect, interactive))
        {
            action = StripHeroAction.FreeSpin;
        }

        pillRail.End(drawList, rail, contentWidth, ui);
        return action;
    }

    private static bool DrawWhitePill(ImDrawListPtr drawList, AppSkin ui, Rect rect, string label, Vector4 surface,
        bool interactive)
    {
        var hovered = interactive && UiInteract.Hover(rect.Min, rect.Max);
        var face = Button.Surface(drawList, rect, ui.Ink.WithAccent(CasinoArt.White), ButtonStyle.Prominent,
            ButtonRole.Normal, true, hovered, ImGui.GetID("casino.hero.get"));
        Button.DrawLabel(drawList, face with { LabelInk = surface with { W = face.LabelInk.W } }, label);
        return interactive && UiInteract.Click(rect.Min, rect.Max, hovered);
    }

    private bool DrawSpin(ImDrawListPtr drawList, AppSkin ui, Rect rect, bool interactive)
    {
        spinSource = rect;
        var hovered = interactive && UiInteract.Hover(rect.Min, rect.Max);
        var face = Button.Surface(drawList, rect, ui.Ink.WithAccent(CasinoColors.Money), ButtonStyle.Tinted,
            ButtonRole.Normal, true, hovered, ImGui.GetID("casino.hero.spin"));
        Button.DrawLabel(drawList, face with { LabelInk = CasinoColors.MoneyHighlight with { W = face.LabelInk.W } },
            Loc.T(L.Floor.FreeSpin));
        return interactive && UiInteract.Click(rect.Min, rect.Max, hovered);
    }

    private bool DrawTimed(ImDrawListPtr drawList, AppSkin ui, Rect rect, CasinoBonusDto timed, bool claiming,
        long nowUnix, float scale, bool interactive)
    {
        timedCenter = rect.Center;
        var ready = timed.Ready && timed.Eligible;
        var remaining = CashierBonusShelf.SecondsUntil(timed, nowUnix);
        var label = ready
            ? texts.Compact(L.Strip.BonusClaim, timed.Amount)
            : texts.Duration(L.Strip.BonusReadyIn, (int)Math.Min(remaining, int.MaxValue));
        var hovered = interactive && ready && !claiming && UiInteract.Hover(rect.Min, rect.Max);
        var face = Button.Surface(drawList, rect, ui.Ink.WithAccent(CasinoColors.Money),
            ready ? ButtonStyle.Tinted : ButtonStyle.Gray, ButtonRole.Normal, ready && !claiming, hovered,
            ImGui.GetID("casino.hero.timed"));
        var ringRadius = BonusRing * 0.5f * scale;
        var ringCenter = new Vector2(rect.Min.X + rect.Height * 0.5f, rect.Center.Y);
        var thickness = RingThickness * scale;
        ProgressRing.Track(drawList, ringCenter, ringRadius - thickness, thickness,
            Palette.WithAlpha(CasinoColors.InkMuted, 0.4f));
        var fraction = ready ? 1f : 1f - Math.Clamp((float)remaining / TimedPeriodSeconds, 0f, 1f);
        if (fraction > 0f)
        {
            ProgressRing.Fill(drawList, ringCenter, ringRadius - thickness, thickness, fraction, CasinoColors.Money);
        }

        if (ready)
        {
            var breath = Pulse.Wave(Pulse.Breath);
            drawList.AddCircleFilled(ringCenter, (ringRadius - thickness * 2f) * (0.55f + 0.2f * breath),
                ImGui.GetColorU32(CasinoColors.Money), 20);
        }

        var textLeft = ringCenter.X + ringRadius + Metrics.Space.Xs * scale;
        var style = TextStyles.SubheadlineEmphasized;
        var shown = Typography.FitText(label, MathF.Max(1f, rect.Max.X - Metrics.Space.Sm * scale - textLeft), style);
        var size = Typography.Measure(shown, style);
        Typography.Draw(drawList, new Vector2(textLeft, rect.Center.Y - size.Y * 0.5f), shown,
            ready ? CasinoColors.MoneyHighlight with { W = face.LabelInk.W } : CasinoColors.InkBody, style);
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        return interactive && ready && !claiming && UiInteract.Click(rect.Min, rect.Max, hovered);
    }

    private bool DrawStreak(ImDrawListPtr drawList, CasinoBonusDto streak, bool claiming, Vector2 origin, float width,
        float scale)
    {
        var dot = StreakDot * scale;
        var gap = StreakGap * scale;
        var labelStyle = TextStyles.FootnoteEmphasized;
        var dotsWidth = StreakDays * dot + (StreakDays - 1) * gap;
        var label = Typography.FitText(Loc.T(L.Strip.BonusStreak), width, labelStyle);
        Typography.Draw(drawList, origin, label, Palette.WithAlpha(CasinoArt.White, SubAlpha), labelStyle);
        origin = new Vector2(origin.X, origin.Y + Typography.LineHeight(labelStyle) + StreakLabelGap * scale);
        var ready = streak.Ready && streak.Eligible;
        var paid = Math.Clamp(ready ? streak.StreakDay - 1 : streak.StreakDay, 0, StreakDays);
        var stripLeft = origin.X;
        var stripRect = new Rect(new Vector2(stripLeft, origin.Y),
            new Vector2(stripLeft + MathF.Min(dotsWidth, width), origin.Y + dot));
        streakCenter = new Vector2(stripLeft + paid * (dot + gap) + dot * 0.5f, origin.Y + dot * 0.5f);
        for (var day = 0; day < StreakDays; day++)
        {
            var center = new Vector2(stripLeft + day * (dot + gap) + dot * 0.5f, origin.Y + dot * 0.5f);
            var radius = dot * 0.5f;
            var dayLabel = DayLabel(day);
            if (day < paid)
            {
                drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(CasinoColors.Money), 20);
                Typography.DrawCentered(drawList, center, dayLabel, CasinoColors.InkOnLight, TextStyles.FootnoteEmphasized);
                continue;
            }

            if (day == paid && ready)
            {
                var glow = 0.55f + 0.45f * Pulse.Wave(Pulse.Breath);
                drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(CasinoColors.Money with { W = glow }), 20);
                Typography.DrawCentered(drawList, center, dayLabel, CasinoColors.InkOnLight, TextStyles.FootnoteEmphasized);
                continue;
            }

            drawList.AddCircle(center, radius - scale, ImGui.GetColorU32(Palette.WithAlpha(CasinoArt.White, 0.5f)),
                20, 1.5f * scale);
            Typography.DrawCentered(drawList, center, dayLabel, Palette.WithAlpha(CasinoArt.White, SubAlpha),
                TextStyles.FootnoteEmphasized);
        }

        var hovered = ready && !claiming && UiInteract.Hover(stripRect.Min, stripRect.Max);
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            HoverTooltip.Show(stripRect, texts.Compact(L.Strip.BonusClaim, streak.Amount), HoverLabelSide.Below);
        }

        return hovered && UiInteract.Click(stripRect.Min, stripRect.Max, hovered);
    }

    private string DayLabel(int day)
    {
        return dayLabels[day] ??= Games.Framework.GameNumber.Label(day + 1);
    }
}

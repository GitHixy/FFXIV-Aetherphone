using Aetherphone.Core;
using Aetherphone.Core.Animation;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Notifications;
using Aetherphone.Core.Theme;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility.Raii;

namespace Aetherphone.Windows.Components;

internal sealed class AltTextEditor
{
    public const int MaxLength = 1000;

    private const ImGuiWindowFlags OverlayFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
                                                  ImGuiWindowFlags.NoBackground;

    private const string OverlayId = "##altTextOverlay";
    private const string FieldId = "##altTextField";
    private const float MaxDim = 0.55f;
    private const float CardRounding = 24f;
    private const float CardPadding = 20f;
    private const float CardMaxWidth = 360f;
    private const float CardSideMargin = 24f;
    private const float PreviewSize = 96f;
    private const float PreviewRounding = 12f;
    private const float FieldHeight = 132f;
    private const float FieldRounding = 14f;
    private const float FieldInset = 8f;
    private const float SectionGap = 12f;
    private const float CounterGap = 6f;
    private const float ButtonsTopGap = 18f;
    private const float ButtonGap = 10f;

    private static readonly TextStyle TitleStyle = TextStyles.Title3;
    private static readonly TextStyle HintStyle = TextStyles.Body;
    private static readonly TextStyle CounterStyle = TextStyles.Caption1;
    private static readonly Vector4 Transparent = new(0f, 0f, 0f, 0f);

    private Spring reveal;
    private bool open;
    private bool focusPending;
    private string key = string.Empty;
    private string draft = string.Empty;
    private string counter = string.Empty;
    private int counterLength = -1;

    public bool CapturesPointer => open || !reveal.IsResting(0f, 0.001f, 0.005f);

    public string Key => key;

    public void Open(string target, string text)
    {
        if (!open)
        {
            UiFeedback.Play(UiSound.SheetPresent);
        }

        key = target;
        draft = text;
        open = true;
        focusPending = true;
    }

    public void Close()
    {
        if (!open)
        {
            return;
        }

        open = false;
        UiFeedback.Play(UiSound.SheetDismiss);
    }

    public bool Draw(Rect screen, PhoneTheme theme, IDalamudTextureWrap? preview, out string saved)
    {
        saved = string.Empty;
        var delta = MathF.Min(ImGui.GetIO().DeltaTime, TransitionTiming.MaxFrameSeconds);
        reveal.Step(open ? 1f : 0f, Motion.Sheet, delta);
        if (!open && reveal.IsResting(0f, 0.001f, 0.005f))
        {
            reveal.SnapTo(0f);
            return false;
        }

        var opacity = Math.Clamp(reveal.Value, 0f, 1f);
        ImGui.SetCursorScreenPos(screen.Min);
        using (ImRaii.Child(OverlayId, screen.Size, false, OverlayFlags))
        {
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(screen.Min, screen.Max,
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, MaxDim * opacity)));
            return DrawCard(drawList, screen, theme, preview, opacity, out saved);
        }
    }

    private bool DrawCard(ImDrawListPtr drawList, Rect screen, PhoneTheme theme, IDalamudTextureWrap? preview,
        float opacity, out string saved)
    {
        saved = string.Empty;
        var scale = UiScale.Current;
        var pad = CardPadding * scale;
        var cardWidth = MathF.Min(CardMaxWidth * scale, screen.Width - CardSideMargin * 2f * scale);
        var innerWidth = cardWidth - pad * 2f;
        var title = Loc.T(L.Chirper.AltTextTitle);
        var titleHeight = Typography.MeasureWrappedBlock(title, TitleStyle, innerWidth).Y;
        var previewSize = PreviewSize * scale;
        var fieldHeight = FieldHeight * scale;
        var counterHeight = Typography.LineHeight(CounterStyle);
        var buttonHeight = Button.LargeHeight * scale;
        var cardHeight = pad + titleHeight + SectionGap * scale + previewSize + SectionGap * scale + fieldHeight
            + CounterGap * scale + counterHeight + ButtonsTopGap * scale + buttonHeight + pad;
        var cardMin = new Vector2(screen.Center.X - cardWidth * 0.5f, screen.Center.Y - cardHeight * 0.5f);
        var cardMax = cardMin + new Vector2(cardWidth, cardHeight);
        Squircle.Fill(drawList, cardMin, cardMax, CardRounding * scale,
            ImGui.GetColorU32(Palette.WithAlpha(theme.Surface, opacity)));
        Squircle.Stroke(drawList, cardMin, cardMax, CardRounding * scale,
            ImGui.GetColorU32(Palette.WithAlpha(theme.TextStrong, 0.08f * opacity)), 1f);

        var left = cardMin.X + pad;
        var right = cardMax.X - pad;
        var y = cardMin.Y + pad;
        Typography.DrawWrappedCentered(new Vector2(screen.Center.X, y), title,
            Palette.WithAlpha(theme.TextStrong, opacity), TitleStyle, innerWidth);
        y += titleHeight + SectionGap * scale;

        DrawPreview(drawList, new Vector2(screen.Center.X - previewSize * 0.5f, y), previewSize, theme, preview,
            opacity);
        y += previewSize + SectionGap * scale;

        var field = new Rect(new Vector2(left, y), new Vector2(right, y + fieldHeight));
        DrawField(drawList, field, theme, opacity);
        y += fieldHeight + CounterGap * scale;

        SyncCounter();
        var counterWidth = Typography.Measure(counter, CounterStyle).X;
        Typography.Draw(drawList, new Vector2(right - counterWidth, y), counter,
            Palette.WithAlpha(theme.TextMuted, opacity), CounterStyle);
        y += counterHeight + ButtonsTopGap * scale;

        var buttonWidth = (innerWidth - ButtonGap * scale) * 0.5f;
        var cancelRect = new Rect(new Vector2(left, y), new Vector2(left + buttonWidth, y + buttonHeight));
        var saveRect = new Rect(new Vector2(cancelRect.Max.X + ButtonGap * scale, y), new Vector2(right, y + buttonHeight));
        if (ConfirmDialog.DrawPillButton(cancelRect, Loc.T(L.Common.Cancel), open, theme, 1f, opacity,
                ConfirmButtonTone.Neutral, "alttext.cancel"))
        {
            Close();
            return false;
        }

        if (!ConfirmDialog.DrawPillButton(saveRect, Loc.T(L.Chirper.AltTextSave), open, theme, 1f, opacity,
                ConfirmButtonTone.Primary, "alttext.save"))
        {
            return false;
        }

        saved = draft.ReplaceLineEndings(" ").Trim();
        Close();
        return true;
    }

    private static void DrawPreview(ImDrawListPtr drawList, Vector2 min, float size, PhoneTheme theme,
        IDalamudTextureWrap? preview, float opacity)
    {
        var max = min + new Vector2(size, size);
        var rounding = PreviewRounding * UiScale.Current;
        if (preview is null)
        {
            Squircle.Fill(drawList, min, max, rounding, ImGui.GetColorU32(Palette.WithAlpha(theme.SurfaceMuted, opacity)));
            return;
        }

        var (uv0, uv1) = ImageFit.CoverSquare(preview.Size);
        drawList.AddImageRounded(preview.Handle, min, max, uv0, uv1,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, opacity)), rounding, ImDrawFlags.RoundCornersAll);
    }

    private void DrawField(ImDrawListPtr drawList, Rect field, PhoneTheme theme, float opacity)
    {
        var scale = UiScale.Current;
        var fill = Surfaces.Fill(ControlInk.From(theme), FillLevel.Tertiary);
        Squircle.Fill(drawList, field.Min, field.Max, FieldRounding * scale,
            ImGui.GetColorU32(fill with { W = fill.W * opacity }));
        var inset = FieldInset * scale;
        var inner = new Rect(field.Min + new Vector2(inset, inset), field.Max - new Vector2(inset, inset));
        var framePadding = ImGui.GetStyle().FramePadding;
        if (!open)
        {
            drawList.PushClipRect(inner.Min, inner.Max, true);
            Typography.DrawWrappedLeft(inner.Min + framePadding, draft, Palette.WithAlpha(theme.TextStrong, opacity),
                HintStyle, inner.Width - framePadding.X * 2f);
            drawList.PopClipRect();
            return;
        }

        ImGui.SetCursorScreenPos(inner.Min);
        if (focusPending)
        {
            ImGui.SetKeyboardFocusHere();
            SoftWrapField.CaretToEnd(FieldId);
            focusPending = false;
        }

        var wrapWidth = inner.Width - framePadding.X * 2f - 4f * scale;
        using (UiScope.PushColor(ImGuiCol.FrameBg, Transparent))
        using (UiScope.PushColor(ImGuiCol.Text, theme.TextStrong))
        {
            SoftWrapField.Multiline(FieldId, ref draft, MaxLength, inner.Size, wrapWidth);
        }

        if (draft.Length > 0)
        {
            return;
        }

        Typography.DrawWrappedLeft(inner.Min + framePadding, Loc.T(L.Chirper.AltTextHint),
            Palette.WithAlpha(theme.TextMuted, opacity), HintStyle, inner.Width - framePadding.X * 2f);
    }

    private void SyncCounter()
    {
        if (counterLength == draft.Length)
        {
            return;
        }

        counterLength = draft.Length;
        counter = string.Concat(counterLength.ToString(Loc.Culture), "/", MaxLength.ToString(Loc.Culture));
    }
}

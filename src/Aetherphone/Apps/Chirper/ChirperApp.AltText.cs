using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Media;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Chirper;

internal sealed partial class ChirperApp
{
    private const float AltTextPreviewPixels = 96f;

    private readonly InfoSheet altTextViewer = new();
    private readonly string[] altTextViewerParagraphs = new string[1];
    private readonly AltTextEditor altTextEditor = new();
    private readonly Dictionary<string, string> composeAltTexts = new(StringComparer.Ordinal);

    private bool AltTextCapturesPointer => altTextEditor.CapturesPointer;

    private void GateAltText() => altTextViewer.Gate();

    private void CloseAltText()
    {
        altTextEditor.Close();
        altTextViewer.Close();
    }

    private void DrawAltText(Rect screen)
    {
        if (altTextEditor.CapturesPointer)
        {
            var path = altTextEditor.Key;
            var preview = ComposeThumbTexture(path, AltTextPreviewPixels * UiScale.Current);
            if (altTextEditor.Draw(screen, theme, preview, out var saved))
            {
                PostAltTexts.Set(composeAltTexts, path, saved);
            }
        }

        if (!altTextViewer.CapturesPointer)
        {
            return;
        }

        altTextViewer.Draw(screen, ChirperInk.Shared, Loc.T(L.Chirper.AltTextTitle), altTextViewerParagraphs,
            Loc.T(L.Chirper.Done));
    }

    private void DrawPostAltBadge(ImDrawListPtr drawList, PostDto post, int photoIndex, Rect tile)
    {
        var text = PostAltTexts.At(post, photoIndex);
        if (text.Length == 0 || !AltBadge.Draw(drawList, tile))
        {
            return;
        }

        altTextViewerParagraphs[0] = text;
        altTextViewer.Open();
    }

    private void DrawComposeAltChip(ImDrawListPtr drawList, string path, Vector2 min, Vector2 max)
    {
        if (AttachedComposeGif() is not null)
        {
            return;
        }

        var text = PostAltTexts.For(composeAltTexts, path);
        var described = text.Length > 0;
        if (AltBadge.DrawEditable(drawList, new Rect(min, max), described, ChirperInk.Accent,
                Loc.T(described ? L.Chirper.AltTextEdit : L.Chirper.AltTextAdd)))
        {
            altTextEditor.Open(path, text);
        }
    }

    private string[]? ComposeAltTextInputs(string[] attachments) => PostAltTexts.Aligned(attachments, composeAltTexts);
}

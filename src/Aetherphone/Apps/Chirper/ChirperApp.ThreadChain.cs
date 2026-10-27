using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Localization;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Chirper;

internal sealed partial class ChirperApp
{
    private static readonly TextStyle ThreadLinkStyle = TextStyles.Subheadline;

    private static float ThreadLinkHeight(PostDto post, bool isThreadHead)
    {
        if (isThreadHead || post.ThreadCount <= 1)
        {
            return 0f;
        }

        return Metrics.Space.Sm * UiScale.Current + Typography.LineHeight(ThreadLinkStyle);
    }

    private void DrawThreadLink(ImDrawListPtr drawList, PostDto post, float left, float top, float width)
    {
        var scale = UiScale.Current;
        var label = Typography.FitText(Loc.T(L.Chirper.ShowThread), width, ThreadLinkStyle);
        var size = Typography.Measure(label, ThreadLinkStyle);
        var min = new Vector2(left, top + Metrics.Space.Sm * scale);
        var max = min + size;
        var hovered = UiInteract.Hover(min, max);
        Typography.Draw(drawList, min, label, ChirperInk.AccentLink, ThreadLinkStyle);
        if (hovered)
        {
            drawList.AddLine(new Vector2(min.X, max.Y - scale), new Vector2(max.X, max.Y - scale),
                ImGui.GetColorU32(ChirperInk.AccentLink), Metrics.Stroke.Hairline);
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if (UiInteract.Click(min, max, hovered))
        {
            OpenThread(post);
        }
    }

    private void DrawThreadChain(PostDto root)
    {
        store.EnsureThreadChain(root);
        var chain = store.ThreadChainFor(root.Id);
        var last = chain.Length - 1;
        while (last > 0 && HiddenByMediaPreference(chain[last]))
        {
            last--;
        }

        if (last < 1)
        {
            return;
        }

        var scale = UiScale.Current;
        var drawList = ImGui.GetWindowDrawList();
        var radius = FeedAvatarRadius * scale;
        var lineX = ImGui.GetCursorScreenPos().X + CellPadX * scale + radius;
        var lineInset = Metrics.Space.Xxs * scale;
        var lineColor = ImGui.GetColorU32(ThreadLine);
        var lineWidth = ThreadLineWidth * scale;
        for (var index = 1; index <= last; index++)
        {
            var item = chain[index];
            if (HiddenByMediaPreference(item))
            {
                continue;
            }

            var rowTop = ImGui.GetCursorScreenPos().Y;
            DrawPost(item);
            var rowBottom = ImGui.GetCursorScreenPos().Y;
            var avatarCenterY = rowTop + CellPadTop * scale + radius;
            drawList.AddLine(new Vector2(lineX, rowTop), new Vector2(lineX, avatarCenterY - radius - lineInset),
                lineColor, lineWidth);
            if (index < last)
            {
                drawList.AddLine(new Vector2(lineX, avatarCenterY + radius + lineInset), new Vector2(lineX, rowBottom),
                    lineColor, lineWidth);
            }
        }
    }
}

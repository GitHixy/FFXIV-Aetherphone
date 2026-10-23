using Aetherphone.Core;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Prism;

internal enum PrismPicture : byte
{
    None,
    TrackModded,
    TrackVanilla,
}

internal static class PrismStock
{
    private static readonly string[] Files = { string.Empty, "track-modded.png", "track-vanilla.png" };
    private static readonly string[] Paths = BuildPaths();

    public static bool DrawRounded(ImDrawListPtr drawList, Rect rect, PrismPicture picture, float alpha,
        float rounding)
    {
        var path = Paths[(int)picture];
        if (path.Length == 0)
        {
            return false;
        }

        var texture = Plugin.TextureProvider.GetFromFile(path).GetWrapOrDefault();
        if (texture is null || texture.Handle == nint.Zero)
        {
            return false;
        }

        var (uv0, uv1) = ImageFit.Cover(texture.Size.X, texture.Size.Y, rect.Width, rect.Height);
        drawList.AddImageRounded(texture.Handle, rect.Min, rect.Max, uv0, uv1,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha)), rounding);
        return true;
    }

    private static string[] BuildPaths()
    {
        var folder = Path.Combine(Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty, "Images",
            "Prism");
        var paths = new string[Files.Length];
        for (var index = 0; index < Files.Length; index++)
        {
            paths[index] = Files[index].Length == 0 ? string.Empty : Path.Combine(folder, Files[index]);
        }

        return paths;
    }
}

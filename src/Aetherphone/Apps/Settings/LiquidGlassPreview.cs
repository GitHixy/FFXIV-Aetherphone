using Aetherphone.Core;
using Aetherphone.Core.Shell;
using Aetherphone.Core.Theme;
using Aetherphone.Core.Wallpapers;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Settings;

internal static class LiquidGlassPreview
{
    public const float HeightFraction = 0.62f;

    private const int ActiveTab = 0;
    private const float VisibleDarkness = 0.001f;
    private static readonly string[] TabGlyphs = { PhoneIcons.Home, PhoneIcons.Photo, PhoneIcons.Settings };

    public static void Draw(ImDrawListPtr drawList, Rect picture, PhoneTheme theme, WallpaperLibrary wallpapers,
        float scale)
    {
        var radius = Metrics.Radius.Grouped * scale;
        var aspect = picture.Width / MathF.Max(picture.Height, 1f);
        var light = wallpapers.Resolve(theme.LightWallpaperId);
        var dark = wallpapers.Resolve(theme.DarkWallpaperId);
        var darkness = wallpapers.ThemeDarkness;
        WallpaperRenderer.DrawSingleRounded(drawList, picture, radius, light, aspect, 1f, theme.SurfaceMuted);
        if (darkness > VisibleDarkness)
        {
            WallpaperRenderer.DrawSingleRounded(drawList, picture, radius, dark, aspect, darkness, null);
        }

        var backdrop = WallpaperBackdrop.Snapshot();
        if (!RealGlass.Ready)
        {
            WallpaperRenderer.RecordBackdrop(picture, light, dark, aspect, darkness);
        }

        DrawTabBar(drawList, picture, theme, scale);
        WallpaperBackdrop.Restore(backdrop);
        Squircle.Stroke(drawList, picture.Min, picture.Max, radius, ImGui.GetColorU32(theme.Separator),
            Metrics.Stroke.Hairline * scale);
    }

    private static void DrawTabBar(ImDrawListPtr drawList, Rect picture, PhoneTheme theme, float scale)
    {
        var capsule = TabBarLayout.FullCapsule(picture, scale, true);
        var action = TabBarLayout.ActionCircle(picture, scale);
        Material.ThemedGlass(drawList, capsule.Min, capsule.Max, capsule.Height * 0.5f, scale, theme,
            TabBar.GlassOpacity);
        Material.ThemedGlass(drawList, action.Min, action.Max, action.Height * 0.5f, scale, theme,
            TabBar.GlassOpacity);
        TabBar.DrawHighlight(drawList, TabBarLayout.Highlight(capsule, TabGlyphs.Length, ActiveTab, scale),
            theme.Accent, 0f, scale);
        var iconSize = TabBarLayout.IconSize * scale;
        for (var index = 0; index < TabGlyphs.Length; index++)
        {
            var cell = TabBarLayout.Cell(capsule, TabGlyphs.Length, index, scale);
            PhoneIcon.Draw(drawList, TabBarLayout.IconCenter(cell), TabGlyphs[index],
                index == ActiveTab ? theme.Accent : theme.TextMuted, iconSize, optical: true);
        }

        PhoneIcon.Draw(drawList, action.Center, PhoneIcons.Search, theme.TextStrong, iconSize, optical: true);
    }
}

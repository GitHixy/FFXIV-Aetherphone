using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Glasswork.Backend;

namespace Aetherphone.Core.Shell;

internal static class RealGlass
{
    private const string LogPrefix = "[RealGlass] ";

    private static IDalamudPluginInterface? pluginInterface;
    private static IPluginLog? pluginLog;
    private static Configuration? configuration;
    private static GlassLayer? layer;
    private static bool active;

    public static void Initialize(IDalamudPluginInterface host, IPluginLog log, Configuration settings)
    {
        Dispose();
        pluginInterface = host;
        pluginLog = log;
        configuration = settings;
        layer = new GlassLayer(() => host.UiBuilder.DeviceHandle, Log);
        host.UiBuilder.ResizeBuffers += OnResizeBuffers;
    }

    public static void BeginFrame()
    {
        if (layer is null || pluginInterface is null)
        {
            return;
        }

        if (configuration is not { RealGlass: true })
        {
            if (active)
            {
                active = false;
                layer.ReleaseResources();
            }

            return;
        }

        active = true;
        layer.ReducedMotion = pluginInterface.UiBuilder.ShouldUseReducedMotion;
        layer.BeginFrame();
    }

    public static float Amount => configuration?.GlassAmount ?? GlassAmountCurve.Neutral;

    public static GlassGroupScope Group() => active && layer is { } glass ? glass.Group() : default;

    public static GlassSuspendScope Suspend(bool transformed = true) =>
        transformed && active && layer is { } glass ? glass.Suspend() : default;

    public static bool TryDraw(ImDrawListPtr drawList, Vector2 min, Vector2 max, float radius, RealGlassKind kind,
        GlassTone tone, Vector4 accent, float opacity)
    {
        if (!active || configuration is not { } settings || layer is not { } glass || !glass.CanRecord())
        {
            return false;
        }

        var shape = RealGlassMaterials.Shape(min, max, radius);
        if (shape.HalfSize.X <= 0f || shape.HalfSize.Y <= 0f)
        {
            return false;
        }

        var material = RealGlassMaterials.Build(kind, tone, accent, opacity, settings.GlassAmount, shape.Radius,
            shape.HalfSize * 2f).Scaled(UiScale.Current);
        return glass.TryDraw(drawList, in shape, in material);
    }

    public static void Dispose()
    {
        if (pluginInterface is not null)
        {
            pluginInterface.UiBuilder.ResizeBuffers -= OnResizeBuffers;
            pluginInterface = null;
        }

        layer?.Dispose();
        layer = null;
        active = false;
        configuration = null;
        pluginLog = null;
    }

    private static void OnResizeBuffers() => layer?.ReleaseResources();

    private static void Log(string message, Exception? exception)
    {
        if (exception is null)
        {
            pluginLog?.Information(string.Concat(LogPrefix, message));
            return;
        }

        pluginLog?.Warning(exception, string.Concat(LogPrefix, message));
    }
}

using Aetherphone.Core.Game;
using Dalamud.Game.Gui.NamePlate;

namespace Aetherphone.Core.Video;

internal sealed class ScreenController : IDisposable
{
    private readonly Func<bool> hideNameplates;
    private const float NameplateHideRadius = 3.0f;
    private bool hiding;

    internal VideoEngine Engine { get; }

    public ScreenController(Func<bool> hideNameplates)
    {
        this.hideNameplates = hideNameplates;
        Engine = new VideoEngine();
        Plugin.NamePlateGui.OnNamePlateUpdate += OnNamePlateUpdate;
    }

    private void OnNamePlateUpdate(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        var hide = hideNameplates() && Engine.IsActive && Engine.ScreenVisible;
        if (hide != hiding)
        {
            hiding = hide;
            if (!hide)
            {
                Plugin.NamePlateGui.RequestRedraw();
            }
        }

        if (!hide)
        {
            return;
        }

        foreach (var handler in handlers)
        {
            var gameObject = handler.GameObject;
            if (gameObject is null)
            {
                continue;
            }

            if (Vector3.Distance(gameObject.Position, Engine.ScreenPosition) <= NameplateHideRadius)
            {
                NamePlateStripper.Strip(handler);
                handler.VisibilityFlags = 0;
            }
        }
    }

    public void Dispose()
    {
        Plugin.NamePlateGui.OnNamePlateUpdate -= OnNamePlateUpdate;
        if (hiding)
        {
            Plugin.NamePlateGui.RequestRedraw();
        }

        Engine.Dispose();
    }
}

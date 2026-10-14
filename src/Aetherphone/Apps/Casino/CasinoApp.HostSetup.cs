using Aetherphone.Core;

namespace Aetherphone.Apps.Casino;

internal sealed partial class CasinoApp
{
    private void DrawHostRules(Rect body) => hostSheet.DrawRules(body, ui);
}

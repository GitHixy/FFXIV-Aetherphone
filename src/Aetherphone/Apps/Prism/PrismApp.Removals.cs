using Aetherphone.Core;
using Aetherphone.Core.Aethernet;

namespace Aetherphone.Apps.Prism;

internal sealed partial class PrismApp
{
    private void DrainRemovals()
    {
        while (glams.TryTakeRemoval(out var removal))
        {
            if (string.Equals(removal.Kind, ContentRemovalKinds.Look, StringComparison.Ordinal))
            {
                store.PurgeLook(removal.ContentId, removal.ParentId);
                continue;
            }

            PurgeGlam(removal.ContentId);
        }
    }

    private void PurgeGlam(string glamId)
    {
        glams.Purge(glamId);
        DropShowcase(glamId);
        DropHistory(glamId);
        tagGlams = CopyOnWrite.RemoveById(tagGlams, glamId);
        if (profileCover?.Id == glamId)
        {
            profileCover = null;
            coverItems = null;
        }

        if (editBannerGlam?.Id == glamId)
        {
            editBannerGlam = null;
        }
    }

    private void DropHistory(string glamId)
    {
        var kept = 0;
        for (var index = 0; index < historyDepth; index++)
        {
            if (history[index].Screen == PrismScreen.GlamDetail && history[index].Glam?.Id == glamId)
            {
                continue;
            }

            history[kept++] = history[index];
        }

        for (var index = kept; index < historyDepth; index++)
        {
            history[index] = default;
        }

        historyDepth = kept;
    }
}

using Aetherphone.Core;
using Aetherphone.Core.Localization;

namespace Aetherphone.Windows.Components;

internal enum DraftExitChoice : byte
{
    None,
    Save,
    Discard,
}

internal sealed class DraftExitSheet
{
    private readonly ActionSheet sheet = new();
    private readonly ActionSheet.Item[] items = new ActionSheet.Item[2];

    public bool CapturesPointer => sheet.CapturesPointer;

    public void Open() => sheet.Open();

    public void Close() => sheet.Close();

    public void Gate() => sheet.Gate();

    public DraftExitChoice Draw(Rect screen, in ActionSheetStyle style)
    {
        if (!sheet.CapturesPointer)
        {
            return DraftExitChoice.None;
        }

        items[0] = new ActionSheet.Item(Loc.T(L.Social.DraftSave));
        items[1] = new ActionSheet.Item(Loc.T(L.Social.DraftDiscard), Danger: true);
        var picked = sheet.Draw(screen, style, items, Loc.T(L.Common.Cancel), false, Loc.T(L.Social.DraftSavePrompt));
        return picked switch
        {
            0 => DraftExitChoice.Save,
            1 => DraftExitChoice.Discard,
            _ => DraftExitChoice.None,
        };
    }
}

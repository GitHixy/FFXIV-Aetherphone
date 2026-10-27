using Aetherphone.Core.Gifs;

namespace Aetherphone.Apps.Chirper;

internal sealed class ChirperThreadDraft
{
    public const int MaxChirps = 25;
    public const int MaxFollowUps = MaxChirps - 1;
    public const int OpeningBox = 0;

    private static readonly string[] FieldIds = BuildFieldIds();

    private readonly string[] texts = new string[MaxFollowUps];
    private readonly List<string>[] attachments = BuildAttachmentLists();
    private readonly GifPick?[] gifs = new GifPick?[MaxFollowUps];

    public ChirperThreadDraft()
    {
        Clear();
    }

    public int Count { get; private set; }

    public bool CanAdd => Count < MaxFollowUps;

    public int FocusedBox { get; set; } = OpeningBox;

    public int FocusRequest { get; set; } = -1;

    public bool FocusesFollowUp => FocusedBox > OpeningBox && FocusedBox <= Count;

    public ref string Text(int index) => ref texts[index];

    public List<string> Attachments(int index) => attachments[index];

    public static string FieldId(int index) => FieldIds[index];

    public GifPick? AttachedGif(int index)
    {
        var list = attachments[index];
        return gifs[index] is { } gif && list.Count == 1
            && string.Equals(list[0], gif.Item.Url, StringComparison.Ordinal)
                ? gif
                : null;
    }

    public void AttachGif(int index, GifPick pick)
    {
        attachments[index].Clear();
        attachments[index].Add(pick.Item.Url);
        gifs[index] = pick;
    }

    public GifPick? GifFor(string url)
    {
        for (var index = 0; index < Count; index++)
        {
            if (AttachedGif(index) is { } gif && string.Equals(gif.Item.Url, url, StringComparison.Ordinal))
            {
                return gif;
            }
        }

        return null;
    }

    public bool HasPhotos()
    {
        for (var index = 0; index < Count; index++)
        {
            if (attachments[index].Count > 0 && AttachedGif(index) is null)
            {
                return true;
            }
        }

        return false;
    }

    public bool Holds(string path)
    {
        for (var index = 0; index < Count; index++)
        {
            var list = attachments[index];
            for (var pathIndex = 0; pathIndex < list.Count; pathIndex++)
            {
                if (string.Equals(list[pathIndex], path, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public bool IsBlank(int index) => string.IsNullOrWhiteSpace(texts[index]) && attachments[index].Count == 0;

    public void Add()
    {
        if (!CanAdd)
        {
            return;
        }

        texts[Count] = string.Empty;
        attachments[Count].Clear();
        gifs[Count] = null;
        Count++;
        FocusRequest = Count;
    }

    public void RemoveAt(int index)
    {
        var removedList = attachments[index];
        for (var shift = index; shift < Count - 1; shift++)
        {
            texts[shift] = texts[shift + 1];
            attachments[shift] = attachments[shift + 1];
            gifs[shift] = gifs[shift + 1];
        }

        Count--;
        texts[Count] = string.Empty;
        removedList.Clear();
        attachments[Count] = removedList;
        gifs[Count] = null;
        FocusedBox = index;
        FocusRequest = index;
    }

    public void Clear()
    {
        for (var index = 0; index < texts.Length; index++)
        {
            texts[index] = string.Empty;
            attachments[index].Clear();
            gifs[index] = null;
        }

        Count = 0;
        FocusedBox = OpeningBox;
        FocusRequest = -1;
    }

    public bool AllWritten(int maxLength)
    {
        for (var index = 0; index < Count; index++)
        {
            if (IsBlank(index) || texts[index].Length > maxLength)
            {
                return false;
            }
        }

        return true;
    }

    private static string[] BuildFieldIds()
    {
        var ids = new string[MaxFollowUps];
        for (var index = 0; index < ids.Length; index++)
        {
            ids[index] = "##chirpThreadBody" + index;
        }

        return ids;
    }

    private static List<string>[] BuildAttachmentLists()
    {
        var lists = new List<string>[MaxFollowUps];
        for (var index = 0; index < lists.Length; index++)
        {
            lists[index] = new List<string>(ChirperStore.MaxImages);
        }

        return lists;
    }
}

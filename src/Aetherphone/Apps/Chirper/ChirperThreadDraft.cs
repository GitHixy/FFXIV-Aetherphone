namespace Aetherphone.Apps.Chirper;

internal sealed class ChirperThreadDraft
{
    public const int MaxChirps = 25;
    public const int MaxFollowUps = MaxChirps - 1;
    public const int OpeningBox = 0;

    private static readonly string[] FieldIds = BuildFieldIds();

    private readonly string[] texts = new string[MaxFollowUps];

    public ChirperThreadDraft()
    {
        Clear();
    }

    public int Count { get; private set; }

    public bool CanAdd => Count < MaxFollowUps;

    public int FocusedBox { get; set; } = OpeningBox;

    public int FocusRequest { get; set; } = -1;

    public ref string Text(int index) => ref texts[index];

    public static string FieldId(int index) => FieldIds[index];

    public void Add()
    {
        if (!CanAdd)
        {
            return;
        }

        texts[Count] = string.Empty;
        Count++;
        FocusRequest = Count;
    }

    public void RemoveAt(int index)
    {
        for (var shift = index; shift < Count - 1; shift++)
        {
            texts[shift] = texts[shift + 1];
        }

        Count--;
        texts[Count] = string.Empty;
        FocusedBox = index;
        FocusRequest = index;
    }

    public void Clear()
    {
        for (var index = 0; index < texts.Length; index++)
        {
            texts[index] = string.Empty;
        }

        Count = 0;
        FocusedBox = OpeningBox;
        FocusRequest = -1;
    }

    public bool AllWritten(int maxLength)
    {
        for (var index = 0; index < Count; index++)
        {
            if (string.IsNullOrWhiteSpace(texts[index]) || texts[index].Length > maxLength)
            {
                return false;
            }
        }

        return true;
    }

    public string[] Snapshot()
    {
        var copy = new string[Count];
        Array.Copy(texts, copy, Count);
        return copy;
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
}

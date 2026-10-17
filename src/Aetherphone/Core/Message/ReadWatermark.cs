namespace Aetherphone.Core.Message;

internal readonly record struct ReadThrough(long LastMessageAt, int Unread);

internal enum ReadWatermarkVerdict : byte
{
    Hold = 0,
    Settled = 1,
    Superseded = 2,
}

internal static class ReadWatermark
{
    public static ReadWatermarkVerdict Judge(ReadThrough mark, long lastMessageAt, int serverUnread)
    {
        if (lastMessageAt > mark.LastMessageAt || serverUnread > mark.Unread)
        {
            return ReadWatermarkVerdict.Superseded;
        }

        return serverUnread > 0 ? ReadWatermarkVerdict.Hold : ReadWatermarkVerdict.Settled;
    }
}

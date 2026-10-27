namespace Aetherphone.Core.Reels;

// The Reels feed is a server session served a page at a time. The next page is asked for while two reels remain
// ahead. A page that adds nothing means the session ran dry: one fresh session follows, which the server fills with
// older reels, and only when that is empty too does the feed stop until a refresh.
internal struct ReelPaging
{
    internal const int PrefetchRemaining = 2;
    internal const long RetryDelayMilliseconds = 5000;

    private bool freshSessionTried;
    private long retryAtMilliseconds;

    internal string? Session { get; private set; }

    internal string? Cursor { get; private set; }

    internal bool Loading { get; private set; }

    internal bool Exhausted { get; private set; }

    internal readonly bool Wants(int position, int count, long nowMilliseconds) =>
        !Loading && !Exhausted && nowMilliseconds >= retryAtMilliseconds && count - position - 1 <= PrefetchRemaining;

    internal void Begin()
    {
        Loading = true;
    }

    internal void Accept(string session, string? nextCursor, int added)
    {
        Loading = false;
        Session = session;
        Cursor = nextCursor;
        if (added > 0)
        {
            freshSessionTried = false;
            return;
        }

        if (freshSessionTried)
        {
            Exhausted = true;
            return;
        }

        freshSessionTried = true;
        Session = null;
        Cursor = null;
    }

    internal void Fail(long nowMilliseconds)
    {
        Loading = false;
        retryAtMilliseconds = nowMilliseconds + RetryDelayMilliseconds;
    }

    internal void Reset()
    {
        this = default;
    }
}

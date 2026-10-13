namespace Aetherphone.Core.Casino;

internal sealed record TradeMatchAnswer(TradeMatchResult Result, string AccountId, string TableId, long Net,
    string PartnerName);

internal sealed class TradeMatchState
{
    private TradeMatchAnswer? answer;
    private int running;

    public bool Busy => Volatile.Read(ref running) != 0;

    public bool TryBegin()
    {
        return Interlocked.CompareExchange(ref running, 1, 0) == 0;
    }

    public async Task RunAsync(ITradeMatchSource source, TradeCompletion completion, TradeTableContext context,
        CancellationToken token)
    {
        var result = TradeMatchResult.Of(TradeMatchRoute.Unreachable);
        try
        {
            result = await TradeMatchFlow.ServerAsync(source, completion, context, token).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref answer, new TradeMatchAnswer(result, context.MyUserId, context.TableId,
                completion.Net, GuestLedger.PartnerNameOf(completion)));
        }
    }

    public TradeMatchAnswer? TakeAnswer()
    {
        var taken = Interlocked.Exchange(ref answer, null);
        if (taken is not null)
        {
            Interlocked.Exchange(ref running, 0);
        }

        return taken;
    }
}

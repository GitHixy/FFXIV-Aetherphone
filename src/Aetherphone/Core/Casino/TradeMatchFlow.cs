using System.Globalization;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;

namespace Aetherphone.Core.Casino;

internal interface ITradeMatchSource
{
    Task<CasinoLedgerTradeMatchDto?> MatchTradeAsync(string roomId, CasinoLedgerTradeMatchRequest request,
        CancellationToken token, Action<AepFailure>? onFailure = null);
}

internal enum TradeMatchRoute : byte
{
    None,
    NoTable,
    NoNames,
    Local,
    Server,
    NoMatch,
    Refused,
    Unreachable,
    Mismatch,
    Unrecordable,
}

internal readonly record struct TradeMatchResult(TradeProposal Proposal, TradeMatchRoute Route, string Reason)
{
    public static TradeMatchResult Of(TradeMatchRoute route) => new(TradeProposal.None, route, string.Empty);
}

internal sealed class TradeProposalQueue
{
    public const int Capacity = 8;

    private readonly object gate = new();
    private volatile TradeProposal[] held = Array.Empty<TradeProposal>();

    public TradeProposal Head
    {
        get
        {
            var snapshot = held;
            return snapshot.Length > 0 ? snapshot[0] : TradeProposal.None;
        }
    }

    public int Count => held.Length;

    public bool Add(in TradeProposal proposal)
    {
        lock (gate)
        {
            var snapshot = held;
            var kept = Math.Min(snapshot.Length, Capacity - 1);
            var next = new TradeProposal[kept + 1];
            Array.Copy(snapshot, snapshot.Length - kept, next, 0, kept);
            next[kept] = proposal;
            held = next;
            return kept == snapshot.Length;
        }
    }

    public bool TryTake(out TradeProposal proposal)
    {
        lock (gate)
        {
            var snapshot = held;
            if (snapshot.Length == 0)
            {
                proposal = TradeProposal.None;
                return false;
            }

            proposal = snapshot[0];
            var next = new TradeProposal[snapshot.Length - 1];
            Array.Copy(snapshot, 1, next, 0, next.Length);
            held = next;
            return true;
        }
    }
}

internal sealed record TradeMiss(string TableId, long Amount, long AtTick)
{
    public const long ShownMilliseconds = 10 * 60 * 1000;

    public bool ShownAt(string tableId, long nowTick) =>
        string.Equals(TableId, tableId, StringComparison.Ordinal) && nowTick - AtTick <= ShownMilliseconds;
}

internal static class TradeMatchFlow
{
    public static TradeMatchResult Local(in TradeCompletion completion, in TradeTableContext context)
    {
        if (!context.Known)
        {
            return TradeMatchResult.Of(TradeMatchRoute.NoTable);
        }

        var proposal = TradeLedgerMatcher.Match(completion, context);
        return proposal.Kind == TradeProposalKind.None
            ? TradeMatchResult.Of(TradeMatchRoute.NoMatch)
            : new TradeMatchResult(proposal, TradeMatchRoute.Local, string.Empty);
    }

    public static async Task<TradeMatchResult> ServerAsync(ITradeMatchSource source, TradeCompletion completion,
        TradeTableContext context, CancellationToken token)
    {
        if (!context.Known)
        {
            return TradeMatchResult.Of(TradeMatchRoute.NoTable);
        }

        var names = TradeNames.ForServer(completion.NameCandidates);
        if (names.Length == 0)
        {
            return TradeMatchResult.Of(TradeMatchRoute.NoNames);
        }

        var failure = AepFailure.None;
        var answer = await source.MatchTradeAsync(context.TableId,
            new CasinoLedgerTradeMatchRequest(names, completion.Received, completion.Sent), token,
            failed => failure = failed).ConfigureAwait(false);
        var result = TradeLedgerMatcher.FromServer(completion, context, answer);
        if (result.Route != TradeMatchRoute.Unreachable || !failure.Failed)
        {
            return result;
        }

        return result with
        {
            Reason = failure.StatusCode > 0
                ? failure.StatusCode.ToString(CultureInfo.InvariantCulture)
                : failure.Kind.ToString(),
        };
    }
}

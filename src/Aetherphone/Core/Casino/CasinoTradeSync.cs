using Aetherphone.Core.Aethernet;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Notifications;
using Dalamud.Plugin.Services;

namespace Aetherphone.Core.Casino;

internal sealed class CasinoTradeSync : IDisposable
{
    private const long LocateEveryMilliseconds = 1_000;

    private readonly Configuration configuration;
    private readonly AethernetSession session;
    private readonly ITradeWindowReader reader;
    private readonly CasinoRoomsStore rooms;
    private readonly CasinoTablesStore tables;
    private readonly CasinoVenueStore venue;
    private readonly NotificationService notifications;
    private readonly Vector4 accent;
    private readonly TradeSyncTracker tracker = new();
    private readonly TradeTableMemory memory = new();
    private readonly ITradeMatchSource source;
    private readonly StoreWork work = new("CasinoTradeSync");
    private readonly Func<string, CasinoTableRowDto?> cardFor;
    private readonly Func<string, CasinoTableLedgerDto?> ledgerFor;

    private volatile TradeProposalHolder? pending;
    private volatile TradeMatchHolder? serverAnswer;
    private volatile TradeMiss? lastMiss;
    private int matching;
    private bool wasOpen;
    private long locatedAtTick;

    public CasinoTradeSync(Configuration configuration, AethernetSession session, ITradeWindowReader reader,
        ITradeMatchSource source, CasinoRoomsStore rooms, CasinoTablesStore tables, CasinoVenueStore venue,
        NotificationService notifications, Vector4 accent)
    {
        this.configuration = configuration;
        this.session = session;
        this.reader = reader;
        this.source = source;
        this.rooms = rooms;
        this.tables = tables;
        this.venue = venue;
        this.notifications = notifications;
        this.accent = accent;
        cardFor = tables.CardFor;
        ledgerFor = tables.LedgerFor;
        Plugin.Framework.Update += OnFrameworkUpdate;
    }

    public bool Enabled
    {
        get => configuration.CasinoTradeSync;
        set
        {
            if (configuration.CasinoTradeSync == value)
            {
                return;
            }

            configuration.CasinoTradeSync = value;
            configuration.Save();
            tracker.Reset();
        }
    }

    public bool AutoConfirm
    {
        get => configuration.CasinoTradeAutoConfirm;
        set
        {
            if (configuration.CasinoTradeAutoConfirm == value)
            {
                return;
            }

            configuration.CasinoTradeAutoConfirm = value;
            configuration.Save();
        }
    }

    public TradeProposal Pending => pending?.Proposal ?? TradeProposal.None;

    public TradeMiss? LastMiss => lastMiss;

    public bool Busy => venue.HostInFlight;

    public void Accept()
    {
        var held = Interlocked.Exchange(ref pending, null);
        if (held is not null)
        {
            venue.ProposeTrade(held.Proposal);
        }
    }

    public void Dismiss()
    {
        Interlocked.Exchange(ref pending, null);
    }

    public TradeTableContext Context()
    {
        var view = View();
        return TradeContexts.Build(memory.Resolve(view, Environment.TickCount64), view);
    }

    private TradeTableView View()
    {
        return new TradeTableView(session.CurrentUser?.Id ?? string.Empty, rooms.Room.RoomId,
            rooms.Room.State?.Blackjack, venue.View, tables.Door, tables.Listed, cardFor, ledgerFor);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!configuration.CasinoTradeSync || !session.IsSignedIn)
        {
            return;
        }

        var now = Environment.TickCount64;
        var sample = reader.Sample();
        if (sample.Open && !wasOpen)
        {
            Opened(memory.Resolve(View(), now));
        }
        else if (now - locatedAtTick >= LocateEveryMilliseconds)
        {
            locatedAtTick = now;
            memory.Resolve(View(), now);
        }

        wasOpen = sample.Open;
        var answered = Interlocked.Exchange(ref serverAnswer, null);
        if (answered is not null)
        {
            Answered(answered);
        }

        var completion = tracker.Observe(sample, now);
        if (completion is { } done)
        {
            Completed(done);
        }
    }

    private void Completed(TradeCompletion completion)
    {
        AepLog.Info($"[TradeSync] Trade completed: net {completion.Net} gil, "
            + $"{completion.NameCandidates.Length} name candidates, partner read {completion.PartnerRead}");
        var context = Context();
        var local = TradeMatchFlow.Local(completion, context);
        if (local.Route == TradeMatchRoute.NoTable)
        {
            AepLog.Info("[TradeSync] No gil table is known for this trade, nothing to match");
            return;
        }

        if (local.Route == TradeMatchRoute.Local)
        {
            AepLog.Info($"[TradeSync] Matched by display name at table {context.TableId}");
            Offer(local.Proposal);
            return;
        }

        if (Interlocked.Exchange(ref matching, 1) != 0)
        {
            AepLog.Info("[TradeSync] A server match is still in flight, this trade is left to the ledger");
            lastMiss = new TradeMiss(context.TableId, Math.Abs(completion.Net), Environment.TickCount64);
            return;
        }

        AepLog.Info($"[TradeSync] No display name matched at table {context.TableId}, asking the server");
        work.Run("trade match", async token =>
        {
            var result = await TradeMatchFlow.ServerAsync(source, completion, context, token).ConfigureAwait(false);
            Interlocked.Exchange(ref serverAnswer,
                new TradeMatchHolder(result, context.MyUserId, context.TableId, completion.Net));
        }, () => Interlocked.Exchange(ref matching, 0));
    }

    private void Answered(TradeMatchHolder answered)
    {
        var result = answered.Result;
        if (result.Route != TradeMatchRoute.Server)
        {
            AepLog.Info(MissNote(result));
            lastMiss = new TradeMiss(answered.TableId, Math.Abs(answered.Net), Environment.TickCount64);
            return;
        }

        if (!string.Equals(answered.AccountId, session.CurrentUser?.Id, StringComparison.Ordinal))
        {
            AepLog.Info("[TradeSync] Server match arrived after the account changed, dropped");
            return;
        }

        AepLog.Info("[TradeSync] Matched by character name on the server");
        Offer(result.Proposal);
    }

    private static string MissNote(in TradeMatchResult result) => result.Route switch
    {
        TradeMatchRoute.Refused => $"[TradeSync] Server refused the match: {result.Reason}",
        TradeMatchRoute.Unreachable => $"[TradeSync] Server match unavailable ({result.Reason}), treated as no match",
        TradeMatchRoute.Mismatch => "[TradeSync] Server match does not fit this table's side, dropped",
        TradeMatchRoute.Unrecordable =>
            "[TradeSync] Server matched a counterparty, but no ledger entry can take this trade",
        TradeMatchRoute.NoNames => "[TradeSync] No names were read from the trade window",
        _ => "[TradeSync] Server found no counterparty for this trade",
    };

    private void Offer(in TradeProposal proposal)
    {
        lastMiss = null;
        var automatic = !proposal.HostSide && configuration.CasinoTradeAutoConfirm;
        AepLog.Info($"[TradeSync] {proposal.Kind} {proposal.LedgerKind} for {proposal.Amount} gil, "
            + $"host side {proposal.HostSide}, incoming {proposal.Incoming}, automatic {automatic}");
        if (automatic)
        {
            venue.ProposeTrade(proposal);
            return;
        }

        pending = new TradeProposalHolder(proposal);
        notifications.Notify(new PhoneNotification(CasinoTurnNotifier.AppId, Loc.T(L.Venue.TradeNotifyTitle),
            Loc.T(proposal.Incoming ? L.Venue.TradeReceivedFrom : L.Venue.TradeSentTo, proposal.CounterpartyName,
                NumberText.Group(proposal.Amount)), DateTime.Now, accent,
            string.Concat(CasinoTurnNotifier.GroupPrefix, proposal.TableId))
        {
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
    }

    private void Opened(in TradeTableRef table)
    {
        if (!table.Located)
        {
            return;
        }

        tables.RefreshLedgerNow(table.TableId);
        if (!table.Known)
        {
            tables.RefreshCard(table.TableId);
        }
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnFrameworkUpdate;
        work.Dispose();
        reader.Dispose();
    }

    private sealed record TradeProposalHolder(TradeProposal Proposal);

    private sealed record TradeMatchHolder(TradeMatchResult Result, string AccountId, string TableId, long Net);
}

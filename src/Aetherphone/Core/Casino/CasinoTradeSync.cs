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
    private readonly Func<string, CasinoTableRowDto?> cardFor;
    private readonly Func<string, CasinoTableLedgerDto?> ledgerFor;

    private volatile TradeProposalHolder? pending;
    private bool wasOpen;
    private long locatedAtTick;

    public CasinoTradeSync(Configuration configuration, AethernetSession session, ITradeWindowReader reader,
        CasinoRoomsStore rooms, CasinoTablesStore tables, CasinoVenueStore venue, NotificationService notifications,
        Vector4 accent)
    {
        this.configuration = configuration;
        this.session = session;
        this.reader = reader;
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
        var completion = tracker.Observe(sample, now);
        if (completion is not { } done)
        {
            return;
        }

        var proposal = TradeLedgerMatcher.Match(done, Context());
        if (proposal.Kind == TradeProposalKind.None)
        {
            return;
        }

        if (!proposal.HostSide && configuration.CasinoTradeAutoConfirm)
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
        reader.Dispose();
    }

    private sealed record TradeProposalHolder(TradeProposal Proposal);
}

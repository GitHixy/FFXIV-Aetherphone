using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Localization;

namespace Aetherphone.Core.Casino;

internal enum GuestTableKind : byte
{
    Seats,
    Room,
    Book,
}

internal sealed record GuestOffer(string TableId, string GuestName, string LedgerKind, long Amount, bool Incoming,
    bool Unchecked = false, GuestTableKind Table = GuestTableKind.Seats);

internal static class GuestLedger
{
    public static GuestTableKind TableOf(in TradeTableContext context)
    {
        if (context.Book)
        {
            return GuestTableKind.Book;
        }

        return context.Venue ? GuestTableKind.Room : GuestTableKind.Seats;
    }

    public static LocString HintFor(GuestOffer offer)
    {
        if (offer.Unchecked)
        {
            return L.Book.GuestHintUnchecked;
        }

        return offer.Table switch
        {
            GuestTableKind.Book => L.Book.GuestHintBook,
            GuestTableKind.Room => L.Venue.GuestHintRoom,
            _ => L.Book.GuestHint,
        };
    }

    public static string PartnerNameOf(in TradeCompletion completion)
    {
        if (!completion.PartnerRead || completion.NameCandidates.Length == 0)
        {
            return string.Empty;
        }

        var name = CasinoBookRules.NormalizeGuestName(completion.NameCandidates[0]);
        return CasinoBookRules.IsGuestName(name) ? name : string.Empty;
    }

    public static GuestOffer? Offer(TradeMiss? miss, bool hosting, CasinoLedgerEntryDto[] entries,
        GuestTableKind table = GuestTableKind.Seats)
    {
        if (miss is null || !hosting || !miss.GuestEligible || miss.Amount <= 0
            || !CasinoBookRules.IsGuestName(miss.PartnerName))
        {
            return null;
        }

        return new GuestOffer(miss.TableId, miss.PartnerName, KindOf(miss, entries), miss.Amount, miss.Incoming,
            miss.Kind != TradeMissKind.NotAtTable, table);
    }

    public static GuestOffer? Shown(GuestOffer? offer, bool open) => open ? offer : null;

    public static bool SameTrade(GuestOffer handled, TradeMiss miss)
    {
        return string.Equals(handled.TableId, miss.TableId, StringComparison.Ordinal)
            && string.Equals(handled.GuestName, miss.PartnerName, StringComparison.Ordinal)
            && handled.Amount == miss.Amount
            && handled.Incoming == miss.Incoming;
    }

    public static string KindOf(TradeMiss miss, CasinoLedgerEntryDto[] entries) =>
        miss.Incoming ? BuyInKindFor(entries, miss.PartnerName) : CasinoLedgerKinds.Payout;

    public static LocString RecordLabel(string ledgerKind) => ledgerKind switch
    {
        CasinoLedgerKinds.Rebuy => L.Book.RecordGuestRebuy,
        CasinoLedgerKinds.Payout => L.Book.RecordGuestPayout,
        _ => L.Book.RecordGuestBuyIn,
    };

    public static GuestOffer? Typed(string tableId, string rawName, bool payout, long amount,
        CasinoLedgerEntryDto[] entries)
    {
        var name = CasinoBookRules.NormalizeGuestName(rawName);
        if (tableId.Length == 0 || !CasinoBookRules.IsGuestName(name) || amount <= 0
            || amount > CasinoHostingRules.MaxGil)
        {
            return null;
        }

        var kind = payout ? CasinoLedgerKinds.Payout : BuyInKindFor(entries, name);
        return new GuestOffer(tableId, name, kind, amount, !payout);
    }

    public static string BuyInKindFor(CasinoLedgerEntryDto[] entries, string guestName)
    {
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            if (!entry.Guest || !string.Equals(entry.GuestName, guestName, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(entry.Kind, CasinoLedgerKinds.BuyIn, StringComparison.Ordinal)
                || string.Equals(entry.Kind, CasinoLedgerKinds.Rebuy, StringComparison.Ordinal))
            {
                return CasinoLedgerKinds.Rebuy;
            }
        }

        return CasinoLedgerKinds.BuyIn;
    }
}

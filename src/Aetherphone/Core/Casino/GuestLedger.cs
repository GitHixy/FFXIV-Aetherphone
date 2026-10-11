using Aetherphone.Core.Aethernet.Contracts;

namespace Aetherphone.Core.Casino;

internal sealed record GuestOffer(string TableId, string GuestName, string LedgerKind, long Amount, bool Incoming);

internal static class GuestLedger
{
    public static string PartnerNameOf(in TradeCompletion completion)
    {
        if (!completion.PartnerRead || completion.NameCandidates.Length == 0)
        {
            return string.Empty;
        }

        var name = CasinoBookRules.NormalizeGuestName(completion.NameCandidates[0]);
        return CasinoBookRules.IsGuestName(name) ? name : string.Empty;
    }

    public static GuestOffer? Offer(TradeMiss? miss, bool hosting, bool open, CasinoLedgerEntryDto[] entries)
    {
        if (miss is null || !open || !hosting || miss.Amount <= 0 || !CasinoBookRules.IsGuestName(miss.PartnerName))
        {
            return null;
        }

        var kind = miss.Incoming ? BuyInKindFor(entries, miss.PartnerName) : CasinoLedgerKinds.Payout;
        return new GuestOffer(miss.TableId, miss.PartnerName, kind, miss.Amount, miss.Incoming);
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

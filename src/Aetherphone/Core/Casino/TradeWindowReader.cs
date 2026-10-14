using System.Text;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Text.ReadOnly;

namespace Aetherphone.Core.Casino;

internal interface ITradeWindowReader : IDisposable
{
    TradeWindowSample Sample();
}

internal sealed unsafe class TradeWindowReader : ITradeWindowReader
{
    public const string TradeAddonName = "Trade";

    private const int MaxNameCandidates = 12;
    private const int MaxNodeDepth = 4;
    private const int MinNameLength = 3;
    private const char MacroBreak = '';

    private readonly List<string> names = new(MaxNameCandidates);
    private string[] candidates = Array.Empty<string>();
    private string[] nodeNames = Array.Empty<string>();
    private string partnerName = string.Empty;
    private bool open;
    private bool partnerRead;
    private long leftOffer;
    private long rightOffer;

    public TradeWindowReader()
    {
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, TradeAddonName, OnOpened);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, TradeAddonName, OnChanged);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, TradeAddonName, OnChanged);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, TradeAddonName, OnClosed);
    }

    public TradeWindowSample Sample()
    {
        var visible = WindowVisible();
        if (visible && !open)
        {
            Begin();
        }

        open = visible;
        if (open)
        {
            ReadArrays();
        }

        return new TradeWindowSample(open, candidates, leftOffer, rightOffer, WalletGil(), partnerRead);
    }

    private void OnOpened(AddonEvent type, AddonArgs args)
    {
        Begin();
        open = true;
        Read(args);
    }

    private void OnChanged(AddonEvent type, AddonArgs args)
    {
        open = true;
        Read(args);
    }

    private void OnClosed(AddonEvent type, AddonArgs args)
    {
        Read(args);
        open = false;
    }

    private void Begin()
    {
        candidates = Array.Empty<string>();
        nodeNames = Array.Empty<string>();
        partnerName = string.Empty;
        partnerRead = false;
        leftOffer = 0;
        rightOffer = 0;
    }

    private void Read(AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null)
        {
            return;
        }

        ReadArrays();
        names.Clear();
        var left = 0L;
        var right = 0L;
        var middle = addon->X + addon->RootNode->Width * addon->Scale * 0.5f;
        Walk(&addon->UldManager, middle, ref left, ref right, 0);
        if (names.Count > 0)
        {
            nodeNames = names.ToArray();
            Merge();
        }

        if (!TryOfferedGil(out _, out _) && (left > 0 || right > 0))
        {
            leftOffer = left;
            rightOffer = right;
        }
    }

    private void ReadArrays()
    {
        var partner = PartnerName();
        if (partner.Length > 0 && !string.Equals(partner, partnerName, StringComparison.Ordinal))
        {
            partnerName = partner;
            partnerRead = true;
            Merge();
        }

        if (TryOfferedGil(out var sent, out var received))
        {
            leftOffer = sent;
            rightOffer = received;
        }
    }

    private void Merge()
    {
        if (partnerName.Length == 0)
        {
            candidates = nodeNames;
            return;
        }

        var merged = new string[nodeNames.Length + 1];
        merged[0] = partnerName;
        Array.Copy(nodeNames, 0, merged, 1, nodeNames.Length);
        candidates = merged;
    }

    private void Walk(AtkUldManager* manager, float middle, ref long left, ref long right, int depth)
    {
        if (manager == null || depth > MaxNodeDepth)
        {
            return;
        }

        for (var index = 0; index < manager->NodeListCount; index++)
        {
            var node = manager->NodeList[index];
            if (node == null || !node->IsVisible())
            {
                continue;
            }

            if (node->Type == NodeType.Text)
            {
                Absorb(((AtkTextNode*)node)->NodeText.ToString(), node->ScreenX < middle, ref left, ref right);
                continue;
            }

            if ((ushort)node->Type < 1000)
            {
                continue;
            }

            var component = ((AtkComponentNode*)node)->Component;
            if (component != null)
            {
                Walk(&component->UldManager, middle, ref left, ref right, depth + 1);
            }
        }
    }

    private void Absorb(string text, bool leftSide, ref long left, ref long right)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (TradeNames.TryAmount(text, out var amount))
        {
            if (leftSide)
            {
                left = Math.Max(left, amount);
            }
            else
            {
                right = Math.Max(right, amount);
            }

            return;
        }

        if (text.Length >= MinNameLength && names.Count < MaxNameCandidates && text.IndexOf(' ') > 0)
        {
            names.Add(text);
        }
    }

    private static bool WindowVisible()
    {
        var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName(TradeAddonName).Address;
        return addon != null && addon->IsVisible;
    }

    private static string PartnerName()
    {
        var strings = TradeStringArray.Instance();
        if (strings == null || !strings->TradePartnerName.HasValue)
        {
            return string.Empty;
        }

        return PlainText(new ReadOnlySeStringSpan(strings->TradePartnerName.AsSpan()));
    }

    private static string PlainText(ReadOnlySeStringSpan text)
    {
        var builder = new StringBuilder();
        foreach (var payload in text)
        {
            if (payload.Type == ReadOnlySePayloadType.Text)
            {
                builder.Append(Encoding.UTF8.GetString(payload.Body));
                continue;
            }

            if (builder.Length > 0 && builder[^1] != MacroBreak)
            {
                builder.Append(MacroBreak);
            }
        }

        return builder.ToString();
    }

    private static bool TryOfferedGil(out long sent, out long received)
    {
        var numbers = TradeNumberArray.Instance();
        if (numbers == null)
        {
            sent = 0;
            received = 0;
            return false;
        }

        sent = numbers->SendGilCount;
        received = numbers->ReceiveGilCount;
        return sent > 0 || received > 0;
    }

    private static long WalletGil()
    {
        var manager = InventoryManager.Instance();
        return manager == null ? 0 : manager->GetGil();
    }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, TradeAddonName, OnOpened);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostRefresh, TradeAddonName, OnChanged);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostRequestedUpdate, TradeAddonName, OnChanged);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, TradeAddonName, OnClosed);
    }
}

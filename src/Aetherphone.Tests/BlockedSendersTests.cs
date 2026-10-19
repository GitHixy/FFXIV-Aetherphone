using Aetherphone.Core.Aethernet;
using Aetherphone.Core.Message;
using Xunit;

namespace Aetherphone.Tests;

public sealed class BlockedSendersTests
{
    private const string Me = "me";
    private const string Troll = "troll";
    private const string Friend = "friend";
    private static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    private static BlockList BlockingTroll()
    {
        var blocks = new BlockList();
        blocks.Replace(new[] { Troll }, blocks.Epoch, Now);
        return blocks;
    }

    [Fact]
    public void CollapsesABlockedSendersMessageInAGroup()
    {
        Assert.True(BlockedSenders.Collapses(BlockingTroll(), true, Troll, Me, false));
    }

    [Fact]
    public void ShowsTheMessageOnceItWasRevealed()
    {
        Assert.False(BlockedSenders.Collapses(BlockingTroll(), true, Troll, Me, true));
    }

    [Fact]
    public void LeavesDirectChatsAndOtherSendersAlone()
    {
        var blocks = BlockingTroll();

        Assert.False(BlockedSenders.Collapses(blocks, false, Troll, Me, false));
        Assert.False(BlockedSenders.Collapses(blocks, true, Friend, Me, false));
        Assert.False(BlockedSenders.Collapses(blocks, true, Me, Me, false));
    }

    [Fact]
    public void TypingCountsOnlyWhenSomeoneUnblockedIsTyping()
    {
        var blocks = BlockingTroll();

        Assert.False(BlockedSenders.AnyVisible(blocks, new[] { Troll }));
        Assert.True(BlockedSenders.AnyVisible(blocks, new[] { Troll, Friend }));
        Assert.False(BlockedSenders.AnyVisible(blocks, Array.Empty<string>()));
    }

    [Fact]
    public void BlockAndUnblockUpdateTheListAndItsVersion()
    {
        var blocks = new BlockList();
        var before = blocks.Version;

        blocks.Add(Troll);
        Assert.True(blocks.Contains(Troll));
        Assert.True(blocks.Version > before);

        blocks.Remove(Troll);
        Assert.False(blocks.Contains(Troll));
    }

    [Fact]
    public void ALoadStartedBeforeAnAccountSwitchIsDropped()
    {
        var blocks = new BlockList();
        var epoch = blocks.Epoch;

        blocks.Clear();

        Assert.False(blocks.Replace(new[] { Troll }, epoch, Now));
        Assert.False(blocks.Contains(Troll));
        Assert.True(blocks.IsStale(Now, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void AFreshLoadIsNotStaleUntilItAges()
    {
        var blocks = BlockingTroll();

        Assert.False(blocks.IsStale(Now.AddMinutes(5), TimeSpan.FromMinutes(10)));
        Assert.True(blocks.IsStale(Now.AddMinutes(10), TimeSpan.FromMinutes(10)));
    }
}

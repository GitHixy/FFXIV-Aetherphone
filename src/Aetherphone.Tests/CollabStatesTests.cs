using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Social;
using Xunit;

namespace Aetherphone.Tests;

public sealed class CollabStatesTests
{
    [Fact]
    public void AcceptedCollaboratorsAreCountedAndIndexedInInviteOrderPastPendingOnes()
    {
        var post = Post(Person("first", CollabStates.Pending), Person("second", CollabStates.Accepted),
            Person("third", CollabStates.Accepted));

        Assert.Equal(3, CollabStates.Count(post));
        Assert.Equal(2, CollabStates.AcceptedCount(post));
        Assert.Equal("second", CollabStates.AcceptedAt(post, 0)!.UserId);
        Assert.Equal("third", CollabStates.AcceptedAt(post, 1)!.UserId);
        Assert.Null(CollabStates.AcceptedAt(post, 2));
    }

    [Fact]
    public void TheViewersOwnRowDecidesInviteAndMembership()
    {
        var post = Post(Person("pending", CollabStates.Pending), Person("joined", CollabStates.Accepted));

        Assert.True(CollabStates.InvitePendingFor(post, "pending"));
        Assert.False(CollabStates.InvitePendingFor(post, "joined"));
        Assert.True(CollabStates.IsCollaborator(post, "joined"));
        Assert.False(CollabStates.IsCollaborator(post, "stranger"));
        Assert.False(CollabStates.IsCollaborator(post, null));
    }

    [Fact]
    public void RemovingOneCollaboratorKeepsTheRestAndTheLastLeavesNone()
    {
        var post = Post(Person("kept", CollabStates.Accepted), Person("gone", CollabStates.Pending));

        var trimmed = CollabStates.Without(post, "gone");
        var emptied = CollabStates.Without(trimmed, "kept");

        Assert.Equal("kept", Assert.Single(trimmed.Collaborators!).UserId);
        Assert.Null(emptied.Collaborators);
        Assert.Same(post, CollabStates.Without(post, "stranger"));
        Assert.Equal(0, CollabStates.AcceptedCount(emptied));
    }

    private static CollaboratorDto Person(string userId, int state)
    {
        return new CollaboratorDto(userId, userId, userId, null, state);
    }

    private static PostDto Post(params CollaboratorDto[] collaborators)
    {
        return new PostDto("post", "author", string.Empty, string.Empty, string.Empty, "author", string.Empty, 0,
            System.Array.Empty<int>(), 0, -1, 1, null, 0, 0, null, 0, false) with { Collaborators = collaborators };
    }
}

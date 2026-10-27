using Aetherphone.Core.Aethernet.Contracts;

namespace Aetherphone.Core.Social;

internal static class CollabStates
{
    public const int Pending = 0;
    public const int Accepted = 1;

    public static CollaboratorDto? AcceptedFor(PostDto post) =>
        post.Collaborator is { State: Accepted } collaborator ? collaborator : null;

    public static bool InvitePendingFor(PostDto post, string? userId) =>
        post.Collaborator is { State: Pending } collaborator
        && string.Equals(collaborator.UserId, userId, StringComparison.Ordinal);

    public static bool IsCollaborator(PostDto post, string? userId) =>
        post.Collaborator is { } collaborator && string.Equals(collaborator.UserId, userId, StringComparison.Ordinal);
}

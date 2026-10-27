using Aetherphone.Core.Aethernet.Contracts;

namespace Aetherphone.Core.Social;

internal static class CollabStates
{
    public const int Pending = 0;
    public const int Accepted = 1;
    public const int MaxCollaborators = 5;

    public static int Count(PostDto post) => post.Collaborators?.Length ?? 0;

    public static int AcceptedCount(PostDto post)
    {
        if (post.Collaborators is not { } collaborators)
        {
            return 0;
        }

        var count = 0;
        for (var index = 0; index < collaborators.Length; index++)
        {
            if (collaborators[index].State == Accepted)
            {
                count++;
            }
        }

        return count;
    }

    public static CollaboratorDto? AcceptedAt(PostDto post, int acceptedIndex)
    {
        if (post.Collaborators is not { } collaborators)
        {
            return null;
        }

        var seen = 0;
        for (var index = 0; index < collaborators.Length; index++)
        {
            if (collaborators[index].State != Accepted)
            {
                continue;
            }

            if (seen == acceptedIndex)
            {
                return collaborators[index];
            }

            seen++;
        }

        return null;
    }

    public static CollaboratorDto? For(PostDto post, string? userId)
    {
        if (post.Collaborators is not { } collaborators || userId is null)
        {
            return null;
        }

        for (var index = 0; index < collaborators.Length; index++)
        {
            if (string.Equals(collaborators[index].UserId, userId, StringComparison.Ordinal))
            {
                return collaborators[index];
            }
        }

        return null;
    }

    public static bool InvitePendingFor(PostDto post, string? userId) => For(post, userId) is { State: Pending };

    public static bool IsCollaborator(PostDto post, string? userId) => For(post, userId) is not null;

    public static PostDto Without(PostDto post, string userId)
    {
        if (post.Collaborators is not { } collaborators)
        {
            return post;
        }

        var kept = new List<CollaboratorDto>(collaborators.Length);
        for (var index = 0; index < collaborators.Length; index++)
        {
            if (!string.Equals(collaborators[index].UserId, userId, StringComparison.Ordinal))
            {
                kept.Add(collaborators[index]);
            }
        }

        if (kept.Count == collaborators.Length)
        {
            return post;
        }

        return post with { Collaborators = kept.Count == 0 ? null : kept.ToArray() };
    }
}

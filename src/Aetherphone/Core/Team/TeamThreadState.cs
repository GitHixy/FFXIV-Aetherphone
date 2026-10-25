using Aetherphone.Core.Aethernet.Contracts;

namespace Aetherphone.Core.Team;

internal sealed class TeamThreadState
{
    private readonly object gate = new();
    private TeamMessageDto[] messages = Array.Empty<TeamMessageDto>();
    private int unread;
    private bool canReply;
    private bool hasMoreOlder;
    private bool loaded;
    private int version;

    public TeamMessageDto[] Messages => Volatile.Read(ref messages);

    public int Unread => Volatile.Read(ref unread);

    public bool CanReply => Volatile.Read(ref canReply);

    public bool HasMoreOlder => Volatile.Read(ref hasMoreOlder);

    public bool Loaded => Volatile.Read(ref loaded);

    public int Version => Volatile.Read(ref version);

    public bool HasThread => Messages.Length > 0;

    public long OldestId
    {
        get
        {
            var snapshot = Messages;
            return snapshot.Length == 0 ? 0 : snapshot[0].Id;
        }
    }

    public long NewestId
    {
        get
        {
            var snapshot = Messages;
            return snapshot.Length == 0 ? 0 : snapshot[snapshot.Length - 1].Id;
        }
    }

    public TeamMessageDto? Newest
    {
        get
        {
            var snapshot = Messages;
            return snapshot.Length == 0 ? null : snapshot[snapshot.Length - 1];
        }
    }

    public void ApplyLatest(TeamMessagePage page, int pageSize)
    {
        var incoming = Ascending(page.Messages);
        lock (gate)
        {
            var current = messages;
            var detached = current.Length > 0 && incoming.Length >= pageSize && incoming[0].Id > NewestOf(current);
            if (!loaded || detached)
            {
                hasMoreOlder = incoming.Length >= pageSize;
                messages = incoming;
            }
            else
            {
                messages = Merge(current, incoming);
            }

            unread = Math.Max(0, page.Unread);
            canReply = page.CanReply;
            loaded = true;
            version++;
        }
    }

    public void ApplyOlder(TeamMessagePage page, int pageSize)
    {
        var incoming = Ascending(page.Messages);
        lock (gate)
        {
            messages = Merge(messages, incoming);
            hasMoreOlder = incoming.Length >= pageSize;
            version++;
        }
    }

    public bool ApplyIncoming(TeamMessageDto message)
    {
        lock (gate)
        {
            var current = messages;
            var position = IndexOf(current, message.Id);
            if (position >= 0)
            {
                var updated = new TeamMessageDto[current.Length];
                Array.Copy(current, updated, current.Length);
                updated[position] = message;
                messages = updated;
                version++;
                return false;
            }

            messages = Merge(current, new[] { message });
            if (message.FromTeam)
            {
                canReply = true;
                if (!message.Read)
                {
                    unread++;
                }
            }

            version++;
            return true;
        }
    }

    public bool MarkAllRead()
    {
        lock (gate)
        {
            var current = messages;
            var changed = unread > 0;
            TeamMessageDto[]? updated = null;
            for (var index = 0; index < current.Length; index++)
            {
                var message = current[index];
                if (!message.FromTeam || message.Read)
                {
                    continue;
                }

                if (updated is null)
                {
                    updated = new TeamMessageDto[current.Length];
                    Array.Copy(current, updated, current.Length);
                }

                updated[index] = message with { Read = true };
                changed = true;
            }

            if (!changed)
            {
                return false;
            }

            if (updated is not null)
            {
                messages = updated;
            }

            unread = 0;
            version++;
            return true;
        }
    }

    public void CloseReplies()
    {
        lock (gate)
        {
            if (!canReply)
            {
                return;
            }

            canReply = false;
            version++;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            messages = Array.Empty<TeamMessageDto>();
            unread = 0;
            canReply = false;
            hasMoreOlder = false;
            loaded = false;
            version++;
        }
    }

    private static long NewestOf(TeamMessageDto[] items) => items.Length == 0 ? 0 : items[items.Length - 1].Id;

    private static int IndexOf(TeamMessageDto[] items, long id)
    {
        for (var index = 0; index < items.Length; index++)
        {
            if (items[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }

    private static TeamMessageDto[] Ascending(TeamMessageDto[]? source)
    {
        if (source is null || source.Length == 0)
        {
            return Array.Empty<TeamMessageDto>();
        }

        var copy = new TeamMessageDto[source.Length];
        Array.Copy(source, copy, source.Length);
        Array.Sort(copy, CompareById);
        return Distinct(copy);
    }

    private static TeamMessageDto[] Merge(TeamMessageDto[] existing, TeamMessageDto[] incoming)
    {
        if (incoming.Length == 0)
        {
            return existing;
        }

        var combined = new TeamMessageDto[existing.Length + incoming.Length];
        var count = 0;
        var existingIndex = 0;
        var incomingIndex = 0;
        while (existingIndex < existing.Length || incomingIndex < incoming.Length)
        {
            if (incomingIndex >= incoming.Length)
            {
                combined[count++] = existing[existingIndex++];
                continue;
            }

            if (existingIndex >= existing.Length)
            {
                combined[count++] = incoming[incomingIndex++];
                continue;
            }

            var existingId = existing[existingIndex].Id;
            var incomingId = incoming[incomingIndex].Id;
            if (existingId < incomingId)
            {
                combined[count++] = existing[existingIndex++];
                continue;
            }

            if (existingId == incomingId)
            {
                existingIndex++;
            }

            combined[count++] = incoming[incomingIndex++];
        }

        if (count == combined.Length)
        {
            return combined;
        }

        var trimmed = new TeamMessageDto[count];
        Array.Copy(combined, trimmed, count);
        return trimmed;
    }

    private static int CompareById(TeamMessageDto left, TeamMessageDto right) => left.Id.CompareTo(right.Id);

    private static TeamMessageDto[] Distinct(TeamMessageDto[] sorted)
    {
        var count = 0;
        for (var index = 0; index < sorted.Length; index++)
        {
            if (count > 0 && sorted[count - 1].Id == sorted[index].Id)
            {
                continue;
            }

            sorted[count++] = sorted[index];
        }

        if (count == sorted.Length)
        {
            return sorted;
        }

        var trimmed = new TeamMessageDto[count];
        Array.Copy(sorted, trimmed, count);
        return trimmed;
    }
}

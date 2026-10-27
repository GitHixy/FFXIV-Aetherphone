using Aetherphone.Core.Aethernet.Contracts;

namespace Aetherphone.Core.Message;

internal interface IChatShareTarget
{
    ConversationDto[] Conversations { get; }

    ContactDto[] Friends { get; }

    bool ConversationsLoaded { get; }

    bool LoadingConversations { get; }

    void RefreshShareTargets();

    string DisplayTitle(ConversationDto conversation);

    void SendShare(string conversationId, string body, Action<bool> onComplete);

    void SendShareToUser(string userId, string body, Action<bool> onComplete);
}

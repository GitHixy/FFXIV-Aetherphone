using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Game;
using Aetherphone.Core.Social;

namespace Aetherphone.Windows.Components;

internal sealed class UserRowSubtitles
{
    private UserDto[]? source;
    private string[] lines = Array.Empty<string>();

    public string[] For(UserDto[] users, GameData gameData)
    {
        if (ReferenceEquals(users, source))
        {
            return lines;
        }

        var built = new string[users.Length];
        for (var index = 0; index < users.Length; index++)
        {
            var user = users[index];
            built[index] = SocialIdentity.ProfileMeta(user.Handle, SocialRegion.Resolve(user.Region, user.World, gameData));
        }

        source = users;
        lines = built;
        return built;
    }
}

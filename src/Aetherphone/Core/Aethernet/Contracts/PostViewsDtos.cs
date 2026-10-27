namespace Aetherphone.Core.Aethernet.Contracts;

internal sealed record PostViewsDto(
    int Total,
    int Followers,
    int NonFollowers,
    int Home,
    int Explore,
    int Profile,
    int HashtagsAndSearch,
    int Stories,
    int Other,
    int Earlier);

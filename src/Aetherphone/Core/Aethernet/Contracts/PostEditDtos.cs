namespace Aetherphone.Core.Aethernet.Contracts;

internal sealed record EditChirpRequest(string Text);

internal sealed record PostEditDto(string Id, string Text, long PostedAtUnix, long EditedAtUnix);

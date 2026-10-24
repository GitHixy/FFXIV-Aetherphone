namespace Aetherphone.Core.Gifs;

internal static class KlipyMedia
{
    private const string Scheme = "https://";
    private const string RootDomain = "klipy.com";

    public static bool IsHosted(string? url)
    {
        if (url is null || !url.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = url.AsSpan(Scheme.Length);
        var end = rest.IndexOfAny('/', ':', '?');
        return IsKlipyHost(end < 0 ? rest : rest[..end]);
    }

    public static bool IsKlipyHost(ReadOnlySpan<char> host)
    {
        if (host.Equals(RootDomain, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return host.Length > RootDomain.Length + 1
               && host.EndsWith(RootDomain, StringComparison.OrdinalIgnoreCase)
               && host[host.Length - RootDomain.Length - 1] == '.';
    }
}

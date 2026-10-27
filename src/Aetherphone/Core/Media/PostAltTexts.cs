using Aetherphone.Core.Aethernet.Contracts;

namespace Aetherphone.Core.Media;

internal static class PostAltTexts
{
    public static string At(PostDto post, int photoIndex) => At(post.AltTexts, photoIndex);

    public static string At(string[]? altTexts, int photoIndex) =>
        altTexts is not null && photoIndex >= 0 && photoIndex < altTexts.Length
            ? altTexts[photoIndex]
            : string.Empty;

    public static string For(Dictionary<string, string> drafts, string path) =>
        drafts.TryGetValue(path, out var text) ? text : string.Empty;

    public static void Set(Dictionary<string, string> drafts, string path, string text)
    {
        if (text.Length == 0)
        {
            drafts.Remove(path);
            return;
        }

        drafts[path] = text;
    }

    public static string[]? Aligned(IReadOnlyList<string> paths, Dictionary<string, string> drafts)
    {
        if (drafts.Count == 0 || paths.Count == 0)
        {
            return null;
        }

        var aligned = new string[paths.Count];
        var described = false;
        for (var index = 0; index < paths.Count; index++)
        {
            aligned[index] = For(drafts, paths[index]);
            described |= aligned[index].Length > 0;
        }

        return described ? aligned : null;
    }

    public static void Prune(Dictionary<string, string> drafts, IReadOnlyList<string> paths)
    {
        if (drafts.Count == 0)
        {
            return;
        }

        foreach (var path in drafts.Keys)
        {
            if (!Contains(paths, path))
            {
                drafts.Remove(path);
            }
        }
    }

    public static bool Same(string[]? left, string[]? right, int photoCount)
    {
        for (var index = 0; index < photoCount; index++)
        {
            if (!string.Equals(At(left, index), At(right, index), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Contains(IReadOnlyList<string> paths, string path)
    {
        for (var index = 0; index < paths.Count; index++)
        {
            if (string.Equals(paths[index], path, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

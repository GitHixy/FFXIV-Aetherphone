namespace Aetherphone.Core.Reels;

internal static class ReelWorkFiles
{
    // A crash or a closed game mid-share leaves its converted file behind; the next share clears what is old.
    internal static void SweepStale(string folder, TimeSpan age)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            var cutoff = DateTime.UtcNow - age;
            var files = Directory.GetFiles(folder);
            for (var index = 0; index < files.Length; index++)
            {
                if (File.GetLastWriteTimeUtc(files[index]) < cutoff)
                {
                    File.Delete(files[index]);
                }
            }
        }
        catch (IOException exception)
        {
            AepLog.Warning(exception, "[Reels] could not clear old converted reels");
        }
        catch (UnauthorizedAccessException exception)
        {
            AepLog.Warning(exception, "[Reels] could not clear old converted reels");
        }
    }
}
